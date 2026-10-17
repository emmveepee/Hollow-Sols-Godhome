using System;
using System.Collections.Generic;

namespace YiGodhome.Bridge;

// Includes quarantine in the capacity. No timer can authorize dirty reuse.
public sealed class ReceiverPool<T> : IDisposable where T : class, IDisposable
{
    private readonly int capacity;
    private readonly Func<T> create;
    private readonly Action<T> reset, verify;
    private readonly HashSet<T> leased = new HashSet<T>();
    private readonly List<(T Owner, double Ready)> quarantine = new List<(T, double)>();
    private bool disposed;
    public int Active => leased.Count;
    public int Quarantined => quarantine.Count;
    public int Retained => Active + Quarantined;
    public ReceiverPool(int capacity, Func<T> create, Action<T> reset, Action<T> verify)
    { if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity)); this.capacity = capacity; this.create = create; this.reset = reset; this.verify = verify; }
    public T Acquire(double now, out bool reused)
    {
        if (disposed) throw new ObjectDisposedException(nameof(ReceiverPool<T>));
        int index = quarantine.FindIndex(p => now >= p.Ready);
        reused = index >= 0;
        T owner;
        if (reused) { owner = quarantine[index].Owner; quarantine.RemoveAt(index); }
        else { if (Retained >= capacity) throw new InvalidOperationException("Native receiver budget exhausted including quarantine."); owner = create(); }
        try { reset(owner); verify(owner); leased.Add(owner); return owner; }
        catch { owner.Dispose(); throw; }
    }
    public void Retire(T owner, double now)
    {
        if (!leased.Remove(owner)) throw new InvalidOperationException("Receiver is not leased.");
        try { reset(owner); quarantine.Add((owner, now + 1)); }
        catch { owner.Dispose(); throw; }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        foreach (var owner in leased) owner.Dispose();
        foreach (var item in quarantine) item.Owner.Dispose();
        leased.Clear(); quarantine.Clear();
    }
}
