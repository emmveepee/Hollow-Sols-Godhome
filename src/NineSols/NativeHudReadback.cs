using System;
using System.Diagnostics;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using YiGodhome.Bridge;

namespace YiGodhome;

// One immutable captured image, at most two GPU regions. Native content keeps
// its camera/materials; only the CPU wait moves off the gameplay frame.
internal sealed class NativeHudReadback : IDisposable
{
    private readonly VisualEndpoint endpoint;
    private readonly Action<string> log;
    private readonly string channel;
    private readonly Func<long,bool>? admitFrame;
    private readonly AsyncGPUReadbackRequest[] requests = new AsyncGPUReadbackRequest[2];
    private VisualFrame? frame;
    private VisualFrame? whiteMatte;
    private byte[]? expected;
    private int remaining, issued, regionBytes;
    private bool disposed, failed;
    private long geometry;
    private readonly NativeWorkCost.Meter completionCost = new NativeWorkCost.Meter();
    internal bool Supported => !disposed && !failed && SystemInfo.supportsAsyncGPUReadback;
    internal bool Busy => frame != null;
    internal bool Verified { get; private set; }
    private readonly HashSet<(int,int)> verifiedSizes=new();
    internal bool VerifiedSize(int width,int height)=>verifiedSizes.Contains((width,height));
    internal RenderTexture? Source {get;private set;}
    internal RenderTexture? MatteSource {get;private set;}
    private int actorSamples;
    private double actorCopyMs,actorMaxCopyMs,actorAgeMs,actorMaxAgeMs;
    internal void Observe(long currentGeometry) { geometry = currentGeometry; }
    internal NativeHudReadback(VisualEndpoint endpoint, Action<string> log, string channel,Func<long,bool>? admitFrame=null)
    { this.endpoint = endpoint; this.log = log; this.channel = channel;this.admitFrame=admitFrame; }
    internal void Request(RenderTexture source, VisualFrame captured, int bottom, int bandHeight, bool atlas, byte[]? reference)
    {
        if (Busy || !Supported) throw new InvalidOperationException("HUD readback admission changed.");
        frame = captured; Source=source; expected = reference; issued = 0; remaining = atlas ? 2 : 1;
        regionBytes = captured.Width * bandHeight * 4;
        try {
            requests[0] = AsyncGPUReadback.Request(source, 0, 0, captured.Width, bottom, bandHeight, 0, 1, TextureFormat.RGBA32, r => Complete(r, 0));
            issued = 1;
            if (atlas) {
                requests[1] = AsyncGPUReadback.Request(source, 0, 0, captured.Width, bandHeight * 2, bandHeight, 0, 1, TextureFormat.RGBA32,
                    r => Complete(r, captured.Width * bandHeight * 4));
                issued = 2;
            }
        } catch { failed = true; DisposePending(); throw; }
    }
    internal void RequestMattes(RenderTexture black,RenderTexture white,VisualFrame captured,byte[]? reference)
    {
        if(Busy || !Supported) throw new InvalidOperationException("HUD matte admission changed.");
        frame=captured; Source=black; MatteSource=white; expected=reference; whiteMatte=endpoint.RentFrame(captured.Rgba.Length);
        issued=0; remaining=2; regionBytes=captured.Rgba.Length;
        try {
            requests[0]=AsyncGPUReadback.Request(black,0,TextureFormat.RGBA32,r=>Complete(r,0)); issued=1;
            requests[1]=AsyncGPUReadback.Request(white,0,TextureFormat.RGBA32,r=>Complete(r,-1)); issued=2;
        } catch { failed=true; DisposePending(); throw; }
    }
    private void Complete(AsyncGPUReadbackRequest request, int offset)
    {
        if (disposed || frame == null) return;
        long start = Stopwatch.GetTimestamp();
        if (request.hasError) failed = true;
        else {
            var data = request.GetData<byte>();
            var destination=offset<0?whiteMatte?.Rgba:frame.Rgba;
            int startOffset=offset<0?0:offset;
            if (destination==null || data.Length != regionBytes || data.Length + startOffset > destination.Length) failed = true;
            else NativeArray<byte>.Copy(data, 0, destination, startOffset, data.Length);
        }
        if (--remaining != 0) return;
        var captured = frame; frame = null;Source=null;MatteSource=null;
        if (expected != null && !failed) {
            bool exact = expected.Length == captured.Rgba.Length;
            for (int i = 0; exact && i < expected.Length; i++) exact = expected[i] == captured.Rgba[i];
            failed = !exact; Verified = exact;
            if(exact && verifiedSizes.Count<16)verifiedSizes.Add((captured.Width,captured.Height));
            log("NS_HUD_ASYNC_VERIFIED channel=" + channel + " exact=" + exact + " bytes=" + captured.Rgba.Length
                + " geometry=" + captured.GeometryFrame + " nativeFrame=" + captured.NativeFrame);
        }
        expected = null;
        if (failed || captured.GeometryFrame != geometry || admitFrame!=null && !admitFrame(captured.NativeFrame)) {
            captured.ReleasePixels();
            whiteMatte?.ReleasePixels();
            if (failed) log("NS_HUD_ASYNC_FAILED channel=" + channel + " synchronousFallback=True");
        } else if(whiteMatte!=null) endpoint.PublishMattePixels(captured,whiteMatte);
        else if(channel=="player_hud" || channel=="actor") endpoint.PublishPremultipliedPixels(captured);
        else endpoint.PublishPixels(captured);
        whiteMatte=null;
        if(channel=="actor") {
            double copyMs=(Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency;
            double ageMs=(Stopwatch.GetTimestamp()-captured.CaptureTick)*1000d/Stopwatch.Frequency;
            actorCopyMs+=copyMs;actorMaxCopyMs=Math.Max(actorMaxCopyMs,copyMs);actorAgeMs+=ageMs;actorMaxAgeMs=Math.Max(actorMaxAgeMs,ageMs);
            if(++actorSamples==90) {
                log(FormattableString.Invariant($"NS_ACTOR_ASYNC_COST samples={actorSamples} meanCopyMs={actorCopyMs/actorSamples:F3} maxCopyMs={actorMaxCopyMs:F3} meanAgeMs={actorAgeMs/actorSamples:F3} maxAgeMs={actorMaxAgeMs:F3}"));
                actorSamples=0;actorCopyMs=actorMaxCopyMs=actorAgeMs=actorMaxAgeMs=0;
            }
        }
        completionCost.Record(start, channel + "_complete", log);
    }
    private void DisposePending()
    {
        // Teardown only: source texture cannot be destroyed while owned GPU
        // requests reference it. No gameplay-frame WaitForCompletion.
        for (int i = 0; i < issued; i++) if (!requests[i].done) requests[i].WaitForCompletion();
        frame?.ReleasePixels(); frame = null; expected = null;Source=null;MatteSource=null;
        whiteMatte?.ReleasePixels(); whiteMatte=null;
    }
    public void Dispose() { disposed = true; DisposePending(); }
}
