using System;
using System.Collections.Generic;
namespace YiGodhome.Bridge;

// A retired target's acknowledgement keeps immutable identity, never a live
// receiver reference that could be applied to a reused native owner.
public sealed class PendingTargetAcks<T> where T : class
{
    public readonly struct Settlement
    {
        public readonly T? LiveTarget;
        public readonly int Owner;
        public readonly long Activation;
        public Settlement(T? target, int owner, long activation) { LiveTarget = target; Owner = owner; Activation = activation; }
    }
    public const int Maximum = 64;
    private readonly Dictionary<long, Settlement> pending = new Dictionary<long, Settlement>();
    public int Count => pending.Count;
    public void Add(long sequence, T target, int owner, long activation)
    {
        if (sequence <= 0 || target == null || owner == 0 || activation <= 0 || pending.Count >= Maximum || pending.ContainsKey(sequence))
            throw new InvalidOperationException("Invalid target acknowledgement lease.");
        pending.Add(sequence, new Settlement(target, owner, activation));
    }
    public void Retire(T target)
    {
        var keys = new List<long>();
        foreach (var pair in pending) if (ReferenceEquals(pair.Value.LiveTarget, target)) keys.Add(pair.Key);
        foreach (var key in keys) { var value = pending[key]; pending[key] = new Settlement(null, value.Owner, value.Activation); }
    }
    public bool TryTake(long sequence, out Settlement settlement)
    {
        if (!pending.TryGetValue(sequence, out settlement)) return false;
        pending.Remove(sequence); return true;
    }
}
