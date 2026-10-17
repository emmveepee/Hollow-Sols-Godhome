using System;
using System.Diagnostics;
using UnityEngine;

namespace YiGodhome;

// Fixed counters, no per-frame records or scene traversal.
internal static class HostWorkCost
{
    internal sealed class Meter
    {
        private long window;
        private double total, maximum;
        private int samples, units;
        internal void Record(long start, int amount, string stage, Action<string> log)
        {
            long now = Stopwatch.GetTimestamp();
            double ms = (now - start) * 1000d / Stopwatch.Frequency;
            total += ms; maximum = Math.Max(maximum, ms); samples++; units += amount;
            if (window == 0) window = now;
            if ((now - window) / (double)Stopwatch.Frequency < 2) return;
            log("HK_WORK_COST stage=" + stage + " samples=" + samples + " meanMs=" + total / samples
                + " maxMs=" + maximum + " totalMs=" + total + " units=" + units + " frame=" + Time.frameCount);
            window = now; total = maximum = 0; samples = units = 0;
        }
    }
    internal static readonly Meter Contacts = new Meter(), Detail = new Meter(), Gui = new Meter(), Contour = new Meter();
}
