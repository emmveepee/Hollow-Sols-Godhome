using System;
using System.Diagnostics;
using UnityEngine;
using Unity.Profiling;

namespace YiGodhome;

// Bounded, allocation-free sampling between two-second summaries.
internal sealed class FramePerformance : IDisposable
{
    private ProfilerRecorder nativeAllocations;
    private bool recorderStarted;
    private long windowAllocations;
    private long previous, window;
    private int count, collections, collections1, collections2, spikes;
#if !HOLLOW_KNIGHT
    private int previousGc, previousGc2, entrySpikes;
#endif
    private long allocated;
    private static readonly Func<long>? allocationCounter = AllocationCounter();
    private static Func<long>? AllocationCounter()
    {
        var method = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", Type.EmptyTypes);
        return method == null ? null : (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), method);
    }
    private double total, maximum;
    private readonly double[] samples = new double[2048];
    public void Sample(Action<string> log)
    {
        if (!recorderStarted) {
            recorderStarted = true; nativeAllocations = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            if(HollowSolsMenuPlugin.Instance?.FixtureRosterEndurance==true)
                log("NATIVE_GC_POLICY mode="+UnityEngine.Scripting.GarbageCollector.GCMode
                    +" incremental="+UnityEngine.Scripting.GarbageCollector.isIncremental
                    +" sliceNanoseconds="+UnityEngine.Scripting.GarbageCollector.incrementalTimeSliceNanoseconds+" policyUnchanged=True");
        }
        if (nativeAllocations.Valid) windowAllocations += nativeAllocations.LastValue;
        long now = Stopwatch.GetTimestamp(); int gc = GC.CollectionCount(0);
        if (previous == 0) { previous = window = now; collections = gc; collections1 = GC.CollectionCount(1); collections2 = GC.CollectionCount(2); allocated = allocationCounter?.Invoke() ?? 0;
#if !HOLLOW_KNIGHT
            previousGc=gc;previousGc2=collections2;
#endif
            return; }
        double ms = (now - previous) * 1000.0 / Stopwatch.Frequency; previous = now;
#if !HOLLOW_KNIGHT
        int gc2=GC.CollectionCount(2);
        if(ms>=100 && NativeGesturePresentation.FrameKey.StartsWith("ArmInspection",StringComparison.Ordinal) && entrySpikes++<4)
            log(FormattableString.Invariant($"NS_INITIAL_FRAME_SPIKE frame={Time.frameCount} ms={ms:F3} gc0={gc-previousGc} gc2={gc2-previousGc2} gesture={NativeGesturePresentation.FrameKey} controlsHeld={NativeRoomEntry.Active}"));
        previousGc=gc;previousGc2=gc2;
#endif
        total += ms; maximum = Math.Max(maximum, ms);
        if (count < samples.Length) samples[count++] = ms;
        if (ms >= 25) spikes++;
        if ((now - window) / (double)Stopwatch.Frequency < 2) return;
        Array.Sort(samples, 0, count);
        long allocationNow = allocationCounter?.Invoke() ?? 0;
        int receivers = 0;
#if !HOLLOW_KNIGHT
        receivers = NativeMinionOwner.Retained;
#endif
        using var process = Process.GetCurrentProcess();
        log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "FRAME_PERF scene={0} frames={1} meanMs={2:F3} p95Ms={3:F3} maxMs={4:F3} spikes25={5} gc0={6} managedMB={7:F2} p99Ms={8:F3} gc1={9} gc2={10} mainAllocatedBytes={11} privateMB={12:F2} workingMB={13:F2} handles={14} threads={15} frame={16} receiverOwners={17} unityAllocatedBytes={18} width={19} height={20} vsync={21} frameCap={22}",
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, count, total / Math.Max(1, count),
            samples[Math.Max(0, (int)(count * 0.95) - 1)], maximum, spikes, gc - collections, GC.GetTotalMemory(false) / 1048576.0,
            samples[Math.Max(0, (int)(count * 0.99) - 1)], GC.CollectionCount(1) - collections1, GC.CollectionCount(2) - collections2,
            allocationCounter == null || allocationNow == 0 ? -1 : allocationNow - allocated, process.PrivateMemorySize64 > 0 ? process.PrivateMemorySize64 / 1048576.0 : -1,
            process.WorkingSet64 > 0 ? process.WorkingSet64 / 1048576.0 : -1, process.HandleCount > 0 ? process.HandleCount : -1, process.Threads.Count > 0 ? process.Threads.Count : -1, Time.frameCount, receivers, nativeAllocations.Valid ? windowAllocations : -1,
            Screen.width, Screen.height, QualitySettings.vSyncCount, Application.targetFrameRate));
        windowAllocations = 0;
        allocated = allocationNow; collections1 = GC.CollectionCount(1); collections2 = GC.CollectionCount(2);
        count = spikes = 0; total = maximum = 0; window = now; collections = gc;
    }
    public void Dispose() { nativeAllocations.Dispose(); }
}
