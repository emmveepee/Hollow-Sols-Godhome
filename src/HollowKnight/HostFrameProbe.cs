using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.LowLevel;
using YiGodhome.Bridge;

namespace YiGodhome;

// Fixed diagnostic brackets and counters; no runtime discovery or per-frame log I/O.
internal sealed class HostFrameProbe : IDisposable
{
    private sealed class Boundary { }
    private static HostFrameProbe? active;
    internal static bool Enabled => HollowSolsMenuPlugin.Instance?.FixtureRosterEndurance==true || HollowSolsMenuPlugin.Instance?.FixtureRadiancePerformance==true || SessionConfiguration.Current.Has("engine-perf") ||
        SessionConfiguration.Current.RunFixtures && SessionConfiguration.Current.Has("trace-probe") && EncounterProfile.Selected.Family == EncounterProfile.NightmareGrimm;
    private static readonly string[] Names = { "Initialization", "EarlyUpdate", "FixedUpdate", "PreUpdate", "Update", "PreLateUpdate", "PostLateUpdate",
        "Update+ScriptRunBehaviourUpdate", "PreLateUpdate+ScriptRunBehaviourLateUpdate", "PreLateUpdate+Physics2DLateUpdate",
        "PostLateUpdate+UpdateAllRenderers", "PostLateUpdate+FinishFrameRendering", "plugin", "probe", "actor_upload", "player_hud", "boss_hud",
        "lifecycle", "presence", "presentation", "controls", "contacts", "aura", "hit_work", "detail" };
    private readonly long[] started = new long[Names.Length];
    private readonly long[] heapStarted = new long[Names.Length], heapGrowth = new long[Names.Length];
    private readonly int[] heapSamples = new int[Names.Length];
    private readonly double[] current = new double[Names.Length], total = new double[Names.Length], maximum = new double[Names.Length];
    private readonly double[,] spikes = new double[8, Names.Length];
    private readonly double[] spikeMs = new double[8];
    private readonly int[] spikeFrames = new int[8], spikeGc = new int[8];
    private readonly Action<string> log;
    private long previous, window;
    private int samples, gc, spikeCount, omitted;
    private double frameTotal, frameMax;
    private string scene = "";
    private bool disposed;
    private float nextPixels;
    internal void Pixels(string channel, VisualEndpoint? endpoint)
    {
        if (endpoint != null) log("HK_PIXEL_POOL channel=" + channel + " created=" + endpoint.PixelAllocations + " outstanding=" + endpoint.OutstandingPixels
            + " allocatedBytes=" + endpoint.AllocatedPixelBytes + " evictedBytes=" + endpoint.EvictedPixelBytes + " retainedBytes=" + endpoint.RetainedPixelBytes + " frame=" + Time.frameCount);
    }
    internal bool PixelWindow()
    {
        if (Time.realtimeSinceStartup < nextPixels) return false;
        nextPixels = Time.realtimeSinceStartup + 2;
        return true;
    }
    internal HostFrameProbe(Action<string> log)
    {
        if (!Enabled || active != null) throw new InvalidOperationException("Host frame diagnostic requires bounded authorization and one owner.");
        this.log = log;
        if (SessionConfiguration.Current.Has("engine-perf")) WireSample();
        FamilySample();
        log("HK_GC_NATIVE_POLICY mode="+UnityEngine.Scripting.GarbageCollector.GCMode
            +" incremental="+UnityEngine.Scripting.GarbageCollector.isIncremental
            +" nativeHeapThresholdMB="+GCManager.HeapUsageThreshold+" policyUnchanged=True");
        var original = PlayerLoop.GetCurrentPlayerLoop();
        var measured = original;
        for (int i = 0; i < 12; i++) if (Insert(ref measured, i) != 1) throw new InvalidOperationException("Missing/ambiguous native timing stage: " + Names[i]);
        var children = new List<PlayerLoopSystem>(measured.subSystemList);
        children.Insert(0, new PlayerLoopSystem { type = typeof(Boundary), updateDelegate = Begin });
        measured.subSystemList = children.ToArray();
        var restored = measured; Remove(ref restored);
        if (!Same(original, restored)) throw new InvalidOperationException("Host timing changed original engine systems/order.");
        active = this; PlayerLoop.SetPlayerLoop(measured);
        log("HK_FRAME_PROBE_ATTACHED originalOrderVerified=True nativeSystemsUnchanged=True stages=12 scopes=13 heapSampling=processWideEvery144Frames");
    }
    internal readonly struct Scope : IDisposable
    {
        private readonly HostFrameProbe? owner;
        private readonly int index;
        private readonly long start;
        private readonly long heap;
        private readonly bool sampled;
        internal Scope(int index) { owner = active; this.index = index; start = owner == null ? 0 : Stopwatch.GetTimestamp(); sampled = owner != null && Time.frameCount % 144 == 0; heap = sampled ? GC.GetTotalMemory(false) : 0; }
        public void Dispose() { if (owner != null && !owner.disposed) { owner.current[index] += Elapsed(start); if (sampled) owner.Heap(index, heap); } }
    }
    internal static Scope Measure(int index) => new Scope(index);
    private void WireSample()
    {
        const int count = 8192;
        using var reference = new System.IO.MemoryStream(count * 4);
        using var candidate = new System.IO.MemoryStream(count * 4);
        using var standard = new System.IO.BinaryWriter(reference);
        using var exact = new WireWriter(candidate);
        standard.Write(.125f); exact.Write(.125f); reference.Position = candidate.Position = 0;
        int collections = GC.CollectionCount(0);
        long before = GC.GetTotalMemory(false);
        for (int i = 0; i < count; i++) standard.Write((i - count / 2) * .125f);
        long middle = GC.GetTotalMemory(false);
        for (int i = 0; i < count; i++) exact.Write((i - count / 2) * .125f);
        long after = GC.GetTotalMemory(false);
        var a = reference.GetBuffer(); var b = candidate.GetBuffer();
        for (int i = 0; i < count * 4; i++) if (a[i] != b[i]) throw new InvalidOperationException("Native floating wire bytes changed.");
        log("HK_WIRE_FLOAT_SAMPLE floats=" + count + " standardProcessHeapDeltaBytes=" + (middle - before)
            + " exactProcessHeapDeltaBytes=" + (after - middle) + " gc=" + (GC.CollectionCount(0) - collections) + " bytesEqual=True preallocatedStreams=True scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
    }
    private sealed class LegacyFamilyControl
    {
        internal EncounterProfile Profile = null!;
        internal EncounterProfile Family => System.Linq.Enumerable.FirstOrDefault(EncounterProfile.All, p => p.Key == Profile.Key) ?? Profile;
    }
    private void FamilySample()
    {
        const int count = 8192;
        var control = new LegacyFamilyControl { Profile = EncounterProfile.Selected };
        var expected = control.Family;
        if (control.Profile.Family != expected) throw new InvalidOperationException("Native family lookup changed identity.");
        int collections = GC.CollectionCount(0);
        long before = GC.GetTotalMemory(false);
        for (int i = 0; i < count; i++) if (control.Family != expected) throw new InvalidOperationException("Legacy family identity changed.");
        long middle = GC.GetTotalMemory(false);
        for (int i = 0; i < count; i++) if (control.Profile.Family != expected) throw new InvalidOperationException("Family identity changed.");
        long after = GC.GetTotalMemory(false);
        log("HK_FAMILY_HEAP_CONTROL calls=" + count + " legacyProcessHeapDeltaBytes=" + (middle-before)
            + " currentProcessHeapDeltaBytes=" + (after-middle) + " gc=" + (GC.CollectionCount(0)-collections)
            + " sameIdentity=True startupOnly=True");
    }
    private static double Elapsed(long start) => (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
    private void Heap(int index, long before) { heapGrowth[index] += GC.GetTotalMemory(false) - before; heapSamples[index]++; }
    private int Insert(ref PlayerLoopSystem root, int index)
    {
        if (root.subSystemList == null) return 0;
        var list = new List<PlayerLoopSystem>(); int found = 0;
        foreach (var original in root.subSystemList) {
            var child = original;
            if (child.type == typeof(Boundary)) { list.Add(child); continue; }
            found += Insert(ref child, index);
            if (child.type?.FullName == "UnityEngine.PlayerLoop." + Names[index]) {
                found++;
                list.Add(new PlayerLoopSystem { type = typeof(Boundary), updateDelegate = () => { started[index] = Stopwatch.GetTimestamp(); if (Time.frameCount % 144 == 0) heapStarted[index] = GC.GetTotalMemory(false); } });
                list.Add(child);
                list.Add(new PlayerLoopSystem { type = typeof(Boundary), updateDelegate = () => { current[index] += Elapsed(started[index]); if (Time.frameCount % 144 == 0) Heap(index, heapStarted[index]); } });
            } else list.Add(child);
        }
        root.subSystemList = list.ToArray(); return found;
    }
    private void Begin()
    {
        if (disposed) return;
        long now = Stopwatch.GetTimestamp(); int collections = GC.CollectionCount(0);
        if (previous != 0) {
            double ms = (now - previous) * 1000d / Stopwatch.Frequency;
            samples++; frameTotal += ms; frameMax = Math.Max(frameMax, ms);
            for (int i = 0; i < Names.Length; i++) { total[i] += current[i]; maximum[i] = Math.Max(maximum[i], current[i]); }
            if (ms >= 12) {
                if (spikeCount < 8) {
                    spikeMs[spikeCount] = ms; spikeFrames[spikeCount] = Time.frameCount; spikeGc[spikeCount] = collections - gc;
                    for (int i = 0; i < Names.Length; i++) spikes[spikeCount, i] = current[i];
                    spikeCount++;
                } else omitted++;
            }
        }
        Array.Clear(current, 0, current.Length);
        previous = now; gc = collections;
        if (window == 0) window = now;
        if ((now - window) / (double)Stopwatch.Frequency >= 2) {
            log("HK_FRAME_WINDOW scene=" + scene + " frames=" + samples + " meanMs=" + frameTotal / Math.Max(1, samples) + " maxMs=" + frameMax + " omittedSpikes=" + omitted + " frame=" + Time.frameCount);
            for (int i = 0; i < Names.Length; i++) log("HK_FRAME_STAGE stage=" + Names[i] + " meanMs=" + total[i] / Math.Max(1, samples) + " maxMs=" + maximum[i] + " frame=" + Time.frameCount + " heapSamples=" + heapSamples[i] + " sampledProcessHeapDeltaBytes=" + heapGrowth[i]);
            for (int j = 0; j < spikeCount; j++) {
                var line = "HK_FRAME_SPIKE scene=" + scene + " frame=" + spikeFrames[j] + " ms=" + spikeMs[j] + " gc=" + spikeGc[j];
                for (int i = 0; i < Names.Length; i++) line += " " + Names[i] + "=" + spikes[j, i];
                log(line);
            }
            Array.Clear(total, 0, total.Length); Array.Clear(maximum, 0, maximum.Length);
            Array.Clear(heapGrowth, 0, heapGrowth.Length); Array.Clear(heapSamples, 0, heapSamples.Length);
            samples = spikeCount = omitted = 0; frameTotal = frameMax = 0; window = now;
            log("HK_FRAME_PROBE_FLUSH frame=" + Time.frameCount + " diagnosticCostMs=" + Elapsed(now));
        }
        scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
    }
    private static void Remove(ref PlayerLoopSystem root)
    {
        if (root.subSystemList == null) return;
        var list = new List<PlayerLoopSystem>();
        foreach (var original in root.subSystemList) { if (original.type == typeof(Boundary)) continue; var child = original; Remove(ref child); list.Add(child); }
        root.subSystemList = list.ToArray();
    }
    private static bool Same(PlayerLoopSystem a, PlayerLoopSystem b)
    {
        if (a.type != b.type || a.updateDelegate != b.updateDelegate || a.updateFunction != b.updateFunction || a.loopConditionFunction != b.loopConditionFunction || (a.subSystemList == null) != (b.subSystemList == null)) return false;
        if (a.subSystemList == null) return true;
        if (b.subSystemList == null || a.subSystemList.Length != b.subSystemList.Length) return false;
        for (int i = 0; i < a.subSystemList.Length; i++) if (!Same(a.subSystemList[i], b.subSystemList[i])) return false;
        return true;
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; active = null;
        var loop = PlayerLoop.GetCurrentPlayerLoop(); Remove(ref loop); PlayerLoop.SetPlayerLoop(loop);
        log("HK_FRAME_PROBE_DETACHED ownedBrackets=0 nativeSystemsRetained=True");
    }
}
