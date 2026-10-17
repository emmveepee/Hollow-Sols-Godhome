using System;
using System.Collections.Generic;

namespace YiGodhome.Bridge;

// Persistent authored weapons may rearm after a timed retirement. All shapes
// of one action, including shapes arriving later, share that same deadline.
public sealed class TimedActionRetirement
{
    private readonly Dictionary<long,float> ends=new();
    private readonly List<long> expired=new();
    public bool Held(long action,float now) => action>0 && ends.TryGetValue(action,out var end) && now<end;
    public void Hold(long action,float now,float seconds)
    {
        if(action<=0 || !float.IsFinite(now) || !float.IsFinite(seconds) || seconds<=0 || seconds>60)
            throw new InvalidOperationException("Invalid timed strike retirement.");
        Prune(now);
        if(!ends.ContainsKey(action) && ends.Count>=256) throw new InvalidOperationException("Timed strike/quarantine capacity exceeded.");
        ends[action]=Math.Max(ends.TryGetValue(action,out var prior)?prior:now,now+seconds);
    }
    public void Prune(float now)
    {
        expired.Clear();
        foreach(var entry in ends) if(now>=entry.Value) expired.Add(entry.Key);
        foreach(var action in expired) ends.Remove(action);
    }
}
