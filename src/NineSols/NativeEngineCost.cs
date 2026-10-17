using System;
using UnityEngine;
using UnityEngine.Profiling;
using Unity.Profiling;

namespace YiGodhome;

// Fixed engine markers only. No profiler/scene inventory and no gameplay edits.
internal sealed class NativeEngineCost : IDisposable
{
    private static readonly string[] Names = { "Gfx.WaitForPresentOnGfxThread", "WaitForTargetFPS", "Camera.Render", "Physics2D.Simulate", "GC.Collect", "Gfx.WaitForCommands", "BehaviourUpdate", "LateBehaviourUpdate", "Gfx.WaitForRenderThread", "GfxDeviceD3D11.WaitForLastPresent", "PlayerLoop" };
    private readonly Recorder[] recorders = new Recorder[Names.Length];
    private readonly double[] total = new double[Names.Length], maximum = new double[Names.Length];
    private readonly int[] blocks = new int[Names.Length];
    private int samples;
    private readonly NativeLoopCost loop;
    private static readonly string[] TimingNames = { "CPU Main Thread Frame Time", "CPU Render Thread Frame Time", "CPU Main Thread Present Wait Time", "GPU Frame Time" };
    private readonly ProfilerRecorder[] timings = new ProfilerRecorder[TimingNames.Length];
    private readonly double[] timingTotal = new double[TimingNames.Length], timingMaximum = new double[TimingNames.Length];
    private readonly int[] timingSamples = new int[TimingNames.Length];
    private float next;
    internal NativeEngineCost(Action<string> log)
    {
        loop = new NativeLoopCost(log);
        for (int i = 0; i < Names.Length; i++) {
            recorders[i] = Recorder.Get(Names[i]);
            if (recorders[i].isValid) recorders[i].enabled = true;
            log("NS_ENGINE_MARKER name=" + Names[i] + " valid=" + recorders[i].isValid);
        }
        for (int i = 0; i < TimingNames.Length; i++) {
            timings[i] = ProfilerRecorder.StartNew(ProfilerCategory.Internal, TimingNames[i], 1);
            log("NS_ENGINE_TIMING name=" + TimingNames[i] + " valid=" + timings[i].Valid);
        }
    }
    internal void Sample(Action<string> log)
    {
        samples++;
        for (int i = 0; i < timings.Length; i++) if (timings[i].Valid && timings[i].LastValue > 0) {
            double ms = timings[i].LastValue / 1000000d;
            timingTotal[i] += ms; timingMaximum[i] = Math.Max(timingMaximum[i], ms); timingSamples[i]++;
        }
        for (int i = 0; i < recorders.Length; i++) if (recorders[i].isValid) {
            double ms = recorders[i].elapsedNanoseconds / 1000000d;
            blocks[i] += recorders[i].sampleBlockCount;
            total[i] += ms; maximum[i] = Math.Max(maximum[i], ms);
        }
        if (Time.realtimeSinceStartup < next) return;
        next = Time.realtimeSinceStartup + 2;
        for (int i = 0; i < recorders.Length; i++) if (recorders[i].isValid) {
            log("NS_ENGINE_COST name=" + Names[i] + " samples=" + samples + " blocks=" + blocks[i] + " meanMs=" + total[i] / samples + " maxMs=" + maximum[i] + " frame=" + Time.frameCount);
            total[i] = maximum[i] = 0;
            blocks[i] = 0;
        }
        samples = 0;
        for (int i = 0; i < timings.Length; i++) {
            log("NS_ENGINE_FRAME_TIMING name=" + TimingNames[i] + " samples=" + timingSamples[i] + " meanMs="
                + (timingSamples[i] == 0 ? -1 : timingTotal[i] / timingSamples[i]) + " maxMs=" + timingMaximum[i] + " frame=" + Time.frameCount);
            timingTotal[i] = timingMaximum[i] = 0; timingSamples[i] = 0;
        }
    }
    public void Dispose() { loop.Dispose(); foreach (var recorder in recorders) if (recorder != null && recorder.isValid) recorder.enabled = false; foreach (var timing in timings) timing.Dispose(); }
}
