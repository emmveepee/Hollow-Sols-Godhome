using System;
using System.Collections.Generic;
namespace YiGodhome.Bridge;

// Local to one native target generation. Never retry a rejected host hit.
public sealed class InjurySettlement
{
    private readonly Dictionary<long, float> pending = new Dictionary<long, float>();
    public void Track(long sequence, float consumed)
    {
        if (sequence <= 0 || consumed < 0 || float.IsNaN(consumed) || float.IsInfinity(consumed)
            || pending.Count >= 64 || pending.ContainsKey(sequence)) throw new InvalidOperationException("Invalid injury settlement.");
        pending.Add(sequence, consumed);
    }
    public float Resolve(long sequence, HitDisposition disposition)
    {
        if (disposition != HitDisposition.Applied && disposition != HitDisposition.Refused && disposition != HitDisposition.Stale)
            throw new InvalidOperationException("Invalid injury acknowledgement.");
        if (!pending.TryGetValue(sequence, out float consumed)) return 0;
        pending.Remove(sequence);
        return disposition == HitDisposition.Applied ? 0 : consumed;
    }
}
