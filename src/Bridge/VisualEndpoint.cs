using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace YiGodhome.Bridge;

public sealed class VisualEndpoint : IDisposable
{
    private sealed class Received
    {
        public readonly VisualFrame Frame;
        public readonly long Tick = Stopwatch.GetTimestamp();
        public Received(VisualFrame frame) { Frame = frame; }
    }
    private readonly Guid session;
    private readonly Role role;
    private readonly bool hud;
    private readonly bool menu;
    private readonly bool boss;
    private readonly bool rest;
    private readonly bool prompt;
    private readonly bool palette;
    private readonly bool summons;
    private readonly bool walls;
    private readonly bool mask;
    private readonly bool outline;
    private readonly bool arrows;
    private readonly bool hudEffects;
    private bool Publishing => mask ? role == Role.HollowKnight : role == Role.NineSols;
    private string Tag => hudEffects ? "HUD_EFFECTS" : arrows ? "ARROW_VISUAL" : outline ? "RAGE_OUTLINE" : mask ? "RAGE_MASK" : walls ? "WALL_ART" : summons ? "SUMMON_UI" : palette ? "ATTACK_PALETTE" : prompt ? "PROMPT" : rest ? "REST" : boss ? "BOSS_HUD" : menu ? "MENU" : hud ? "HUD" : "VISUAL";
    private readonly ManualResetEvent stopping = new ManualResetEvent(false);
    private readonly ConcurrentQueue<string> events = new ConcurrentQueue<string>();
    private VisualFrame? outgoing;
    private Received? incoming;
    private PipeStream? pipe;
    private int stopped, leaseWaitReports;
    public bool Stopped => Volatile.Read(ref stopped) != 0;
    private readonly PixelBuffers buffers;
    private readonly object incomingGate = new object();
    internal int PixelAllocations => Volatile.Read(ref buffers.Created);
    internal int OutstandingPixels => Volatile.Read(ref buffers.Outstanding);
    internal long AllocatedPixelBytes => buffers.AllocatedBytes;
    internal long EvictedPixelBytes => buffers.EvictedBytes;
    internal long RetainedPixelBytes=>buffers.RetainedBytes;
    internal bool CanQueuePixels=>!Stopped && Volatile.Read(ref outgoing)==null;
    internal void PreparePixels(int bytes,int leases=3) {
        // Distinct free leases cover the bounded producer/worker pipeline.
        // Preparation never publishes an image or changes its session metadata.
        if(leases<1 || leases>6)throw new ArgumentOutOfRangeException(nameof(leases));
        var held=new byte[]?[leases];
        try {for(int i=0;i<held.Length;i++)held[i]=buffers.Rent(bytes);}
        finally {foreach(var value in held)if(value!=null)buffers.Return(value);}
    }
    public VisualFrame RentFrame(int bytes) => new VisualFrame { Rgba = buffers.Rent(bytes), BufferOwner = buffers };
    // FreshFrame is metadata-only outside this lease: buffer reuse can occur
    // concurrently. Hold the lease until texture upload/pixel inspection ends.
    public readonly struct FrameLease : IDisposable
    {
        private readonly object gate;
        public readonly VisualFrame? Frame;
        internal FrameLease(object gate, VisualFrame? frame) { this.gate = gate; Frame = frame; }
        public void Dispose() { Monitor.Exit(gate); }
    }
    public FrameLease AcquireFrame()
    {
        long begin = Stopwatch.GetTimestamp();
        Monitor.Enter(incomingGate);
        double waitMs = (Stopwatch.GetTimestamp()-begin)*1000d/Stopwatch.Frequency;
        if (waitMs >= 25 && Interlocked.Increment(ref leaseWaitReports) <= 8)
            events.Enqueue(Tag+"_LEASE_WAIT role="+role+" ms="+waitMs.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+" beginTick="+begin);
        return new FrameLease(incomingGate, FreshFrame);
    }
    public VisualEndpoint(Guid session, Role role, bool hud = false, bool menu = false, bool boss = false, bool rest = false, bool prompt = false, bool palette = false, bool summons = false, bool walls = false, bool mask = false, bool outline = false, bool arrows = false, bool hudEffects = false)
    {
        // HUD has one dual-matte readback; world effects have two overlapping
        // readbacks (four buffers), plus two pending and two worker-owned.
        // Keep the same total free-byte budget, including at maximum resolution.
        buffers=new PixelBuffers(128L*1024*1024,arrows?8:hud||hudEffects?6:3);
        if(hudEffects && (hud||menu||boss||rest||prompt||palette||summons||walls||mask||outline||arrows))throw new ArgumentException("HUD effects require their own channel.");
        if (session == Guid.Empty || role != Role.HollowKnight && role != Role.NineSols) throw new ArgumentException("Invalid visual identity.");
        if (arrows && (outline || mask || walls || summons || palette || prompt || rest || boss || menu || hud)) throw new ArgumentException("Arrow channel cannot own another category.");
        if (outline && (mask || hud || menu || boss || rest || prompt || palette || summons || walls)) throw new ArgumentException("Outline channel cannot own another visual category.");
        if (mask && (hud || menu || boss || rest || prompt || palette || summons || walls)) throw new ArgumentException("Mask channel cannot own another visual category.");
        if (walls && (summons || palette || prompt || menu || boss || rest || hud) || summons && (palette || prompt || menu || boss || rest || hud) || palette && (prompt || menu || boss || rest || hud) || prompt && (menu || boss || rest || hud) || menu && boss || rest && (menu || boss || hud)) throw new ArgumentException("Conflicting visual channel types.");
        this.session = session; this.role = role; this.hud = hud || menu || boss || hudEffects; this.menu = menu; this.boss = boss;this.hudEffects=hudEffects;
        this.rest = rest; this.prompt = prompt;
        this.arrows = arrows;
        this.palette = palette; this.summons = summons; this.walls = walls; this.mask = mask; this.outline = outline;
        new Thread(Run) { IsBackground = true, Name = "YiGodhome visuals" }.Start();
    }
    public void Publish(VisualFrame frame)
    {
        if(frame.Regions.Length>0 && !summons){frame.ReleasePixels();throw new InvalidOperationException("Atlas regions require summon channel.");}
        if (!Publishing || frame.Session != session) { frame.ReleasePixels(); throw new InvalidOperationException("Invalid visual publisher."); }
        if (Volatile.Read(ref stopped) != 0) { frame.ReleasePixels(); return; }
        Interlocked.Exchange(ref outgoing, frame)?.ReleasePixels();
        if (Volatile.Read(ref stopped) != 0) Interlocked.Exchange(ref outgoing, null)?.ReleasePixels();
    }
    // Captures hand off an owned byte array. Hashing belongs on this channel's
    // worker, not on Unity's animation/render thread; wire validation is unchanged.
    public void PublishPixels(VisualFrame frame)
    {
        frame.ComputeHashBeforeSend = true;
        Publish(frame);
    }
    public void PublishPremultipliedPixels(VisualFrame frame)
    {
        frame.StraightAlphaBeforeSend=true;
        PublishPixels(frame);
    }
    public void PublishMattePixels(VisualFrame black,VisualFrame white)
    {
        if(ReferenceEquals(black,white) || black.Rgba.Length!=white.Rgba.Length) throw new ArgumentException("Distinct matching native HUD mattes required.");
        black.WhiteMatte=white; PublishPixels(black);
    }
    public VisualFrame? FreshFrame
    {
        get
        {
            var sample = Volatile.Read(ref incoming);
            if (Volatile.Read(ref stopped) != 0 || sample == null) return null;
            var f = sample.Frame;
            double age = (f.SentTick - f.CaptureTick) * 1000.0 / f.Frequency + f.PreviousRoundTripMs
                + (Stopwatch.GetTimestamp() - sample.Tick) * 1000.0 / Stopwatch.Frequency;
            return age <= 250 ? f : null;
        }
    }
    public bool TryGetEvent(out string message) => events.TryDequeue(out message!);
    public void Dispose()
    {
        if (Interlocked.Exchange(ref stopped, 1) != 0) return;
        lock (incomingGate) { var prior = incoming; Volatile.Write(ref incoming, null); prior?.Frame.ReleasePixels(); }
        Interlocked.Exchange(ref outgoing, null)?.ReleasePixels(); stopping.Set();
        try { PipeShutdown.Close(Volatile.Read(ref pipe)); } catch (IOException) { }
        events.Enqueue(Tag + "_DISCONNECTED role=" + role);
    }
    private void Run()
    {
        try
        {
            string name = "YiGodhome." + session.ToString("N") + (hudEffects ? ".hud-effects" : arrows ? ".arrows" : outline ? ".rage-outline" : mask ? ".rage-mask" : walls ? ".walls" : summons ? ".summons" : palette ? ".attack-palette" : prompt ? ".prompt" : rest ? ".rest" : boss ? ".boss" : menu ? ".menu" : hud ? ".hud" : ".visual");
            using PipeStream stream = role == Role.HollowKnight
                ? new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.None)
                : new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.None);
            Volatile.Write(ref pipe, stream); if (Volatile.Read(ref stopped) != 0) return;
            if (stream is NamedPipeServerStream server) server.WaitForConnection(); else ((NamedPipeClientStream)stream).Connect(45000);
            bool actorControl = !Publishing && !arrows && !outline && !mask && !walls && !summons && !palette && !prompt && !rest && !boss && !menu && !hud
                && SessionConfiguration.Current.RunFixtures && SessionConfiguration.Current.Has("trace-probe")
                && EncounterProfile.Selected.Family == EncounterProfile.NightmareGrimm;
            using var input = new PipeReadBuffer(stream,actorControl?events:null);
            using var output = PipeBuffers.Outgoing(stream);
            using var reader = new BinaryReader(input, System.Text.Encoding.UTF8, true);
            using var writer = new WireWriter(output);
            long sequence = 0, nativeFrame = -1; double rtt = 0;
            while (!stopping.WaitOne(0))
            {
                if (Publishing)
                {
                    var frame = Interlocked.Exchange(ref outgoing, null);
                    if (frame == null) { stopping.WaitOne(2); continue; }
                    try
                    {
                    frame.Sequence = ++sequence; frame.SentTick = Stopwatch.GetTimestamp(); frame.PreviousRoundTripMs = rtt;
                    VisualCodec.Write(output, frame, hud);
                    long ackSequence = reader.ReadInt64(), ackTick = reader.ReadInt64();
                    if (ackSequence != sequence || ackTick != frame.SentTick) throw new InvalidDataException("Visual acknowledgement mismatch.");
                    rtt = (Stopwatch.GetTimestamp() - frame.SentTick) * 1000.0 / Stopwatch.Frequency;
                    writer.Write(rtt); writer.Flush();
                    if (sequence == 1 || sequence % 30 == 0) events.Enqueue(Tag + "_SENT " + Describe(frame)
                        +" resolveMs="+frame.ResolveMilliseconds.ToString("F3",System.Globalization.CultureInfo.InvariantCulture));
                    }
                    finally { frame.ReleasePixels(); }
                }
                else
                {
                    var frame = VisualCodec.Read(input, hud, buffers);
                    bool retained = false;
                    try
                    {
                    if (frame.Session != session || frame.Sequence != sequence + 1 || frame.NativeFrame < nativeFrame || frame.Regions.Length>0 && !summons)
                        throw new InvalidDataException("Visual session/sequence/frame mismatch.");
                    sequence = frame.Sequence; nativeFrame = frame.NativeFrame;
                    writer.Write(sequence); writer.Write(frame.SentTick); writer.Flush();
                    double transferRoundTrip = reader.ReadDouble();
                    if (double.IsNaN(transferRoundTrip) || transferRoundTrip < 0 || transferRoundTrip > 60000)
                        throw new InvalidDataException("Invalid visual transfer age.");
                    frame.PreviousRoundTripMs = Math.Max(frame.PreviousRoundTripMs, transferRoundTrip);
                    input.Complete(sequence);
                    if (Volatile.Read(ref stopped) != 0) return;
                    lock (incomingGate)
                    {
                        if (Volatile.Read(ref stopped) != 0) return;
                        var prior = incoming; Volatile.Write(ref incoming, new Received(frame)); retained = true;
                        prior?.Frame.ReleasePixels();
                    }
                    if (sequence == 1 || sequence % 30 == 0) events.Enqueue(Tag + "_RECEIVED " + Describe(frame));
                    }
                    finally { if (!retained) frame.ReleasePixels(); }
                }
            }
        }
        catch (Exception error) { events.Enqueue(Tag + "_STOP reason=" + error.GetType().Name + ":" + error.Message); }
        finally { Dispose(); }
    }
    private static string Describe(VisualFrame f) => "sequence=" + f.Sequence + " frame=" + f.NativeFrame + " hash=" + f.Hash
        + " bytes=" + f.Rgba.Length + " geometryFrame=" + f.GeometryFrame + " transferMs=" + f.PreviousRoundTripMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
}
