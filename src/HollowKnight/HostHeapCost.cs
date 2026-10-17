using System;
using UnityEngine;

namespace YiGodhome;

// Process-wide heap deltas, not allocation bytes. Existing bounded timing
// authorization only; no collections, inventories or escaping scratch arrays.
internal static class HostHeapCost
{
    private static readonly string[] Names = { "policy", "targets", "pooled", "fsm_flames", "frame", "pool_discovery", "pool_geometry", "pool_decorate", "pool_finish" };
    private static readonly long[] Growth = new long[Names.Length];
    private static readonly int[] Samples = new int[Names.Length];
    private static float next;
    internal static long Begin() => HostFrameProbe.Enabled && Time.frameCount % 144 == 0 ? GC.GetTotalMemory(false) : -1;
    internal static void Mark(int stage, ref long before)
    {
        if (before < 0) return;
        long now = GC.GetTotalMemory(false); Growth[stage] += now - before; Samples[stage]++; before = now;
    }
    internal static void Flush(Action<string>? log)
    {
        if (!HostFrameProbe.Enabled || Time.realtimeSinceStartup < next) return;
        next = Time.realtimeSinceStartup + 2;
        for (int i = 0; i < Names.Length; i++) {
            log?.Invoke("HK_CONTACT_HEAP stage=" + Names[i] + " samples=" + Samples[i] + " processHeapDeltaBytes=" + Growth[i] + " frame=" + Time.frameCount);
            Growth[i] = 0; Samples[i] = 0;
        }
    }
}
