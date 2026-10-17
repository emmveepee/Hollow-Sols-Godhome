using System;
using System.Linq;

namespace HollowSols.Foundation;

public readonly struct CheckpointRequest
{
    public SessionToken Session { get; }
    public Guid Transaction { get; }
    public long PreviousGeneration { get; }
    public CheckpointRequest(SessionToken session, Guid transaction, long previousGeneration)
    {
        session.Slot.Validate();
        if (session.Session == Guid.Empty || transaction == Guid.Empty || previousGeneration < 1) throw new ArgumentException("Invalid checkpoint request.");
        Session = session; Transaction = transaction; PreviousGeneration = previousGeneration;
    }
}

// Main-thread coordinator: native save callbacks request a snapshot; both sides
// capture through their own serializers and submit immutable, identity-scoped bytes.
public sealed class PairedSaveCoordinator
{
    public const double SaveSeconds = 15;
    private readonly BridgeLifecycle lifecycle;
    private readonly PairedSaveStore store;
    private CheckpointRequest? pending;
    private byte[]? hk, ns;
    private double deadline;
    private double lastTime = double.NegativeInfinity;
    private bool publishing;
    public bool Busy => pending.HasValue;
    public PairedSaveCoordinator(BridgeLifecycle lifecycle, PairedSaveStore store)
    {
        if (lifecycle.Side != GameSide.HollowKnight) throw new ArgumentException("Only HK coordinates paired checkpoints.");
        this.lifecycle = lifecycle; this.store = store;
    }
    public CheckpointRequest Begin(SessionToken session, long previousGeneration, double now)
    {
        CheckTime(now);
        if (!lifecycle.AcceptsSave(session)) throw new InvalidOperationException("This session no longer owns a save.");
        if (pending.HasValue)
        {
            if (!pending.Value.Session.Equals(session) || pending.Value.PreviousGeneration != previousGeneration) throw new InvalidOperationException("A different checkpoint is in flight.");
            return pending.Value;
        }
        pending = new CheckpointRequest(session, Guid.NewGuid(), previousGeneration); hk = ns = null; deadline = now + SaveSeconds;
        return pending.Value;
    }
    public void Submit(CheckpointRequest request, GameSide side, byte[] payload, double now)
    {
        Validate(request, now);
        if(publishing)throw new InvalidOperationException("Checkpoint publication already owns its immutable snapshots.");
        if (payload == null || payload.Length == 0 || payload.Length > PairedSaveStore.MaxPayloadBytes) throw new ArgumentException("Invalid native snapshot.");
        if (side != GameSide.HollowKnight && side != GameSide.NineSols) throw new ArgumentOutOfRangeException(nameof(side));
        var existing = side == GameSide.HollowKnight ? hk : ns;
        if (existing != null && !existing.SequenceEqual(payload)) throw new InvalidOperationException("Duplicate checkpoint response changed its payload.");
        if (existing != null) return;
        if (side == GameSide.HollowKnight) hk = (byte[])payload.Clone(); else ns = (byte[])payload.Clone();
    }
    public PairedCheckpoint Commit(CheckpointRequest request, double now)
    {
        Validate(request, now);
        if(publishing)throw new InvalidOperationException("Checkpoint publication is already running.");
        if (hk == null || ns == null) throw new InvalidOperationException("Both native snapshots are required before publication.");
        try { return store.Save(request.Session.Slot, request.Session.Pair, request.PreviousGeneration, request.Session.Session, hk, ns, request.Transaction); }
        // On any I/O uncertainty, never replay this request. The next load scans
        // commits and reports recovery if necessary; preserved data decides success.
        finally { Cancel(); }
    }
    // Prepare/complete stay on HK's game thread. The returned operation touches
    // only immutable bytes and isolated store paths, retaining all store checks.
    public Func<PairedCheckpoint> PreparePublication(CheckpointRequest request,double now)
    {
        Validate(request,now);
        if(publishing || hk==null || ns==null)throw new InvalidOperationException("Publication requires both snapshots and a single owner.");
        var host=hk;var peer=ns;publishing=true;
        return ()=>store.Save(request.Session.Slot,request.Session.Pair,request.PreviousGeneration,request.Session.Session,host,peer,request.Transaction);
    }
    public PairedCheckpoint CompletePublication(CheckpointRequest request,PairedCheckpoint committed,double now)
    {
        Validate(request,now);
        if(!publishing || committed.Pair.Id!=request.Session.Pair || !committed.Pair.Slot.Equals(request.Session.Slot)
            || committed.Transaction!=request.Transaction || committed.Session!=request.Session.Session || committed.Generation<=request.PreviousGeneration)
            throw new InvalidOperationException("Foreign checkpoint publication result.");
        Cancel();return committed;
    }
    public void Cancel() { pending = null; hk = ns = null;publishing=false; }
    public void Tick(double now)
    {
        CheckTime(now);
        if (pending.HasValue && (now >= deadline || !lifecycle.AcceptsSave(pending.Value.Session))) Cancel();
    }
    public string DeleteConfirmed(ModSlot slot, Guid pair)
    {
        if (Busy || lifecycle.State != BridgeState.SaveSelection) throw new InvalidOperationException("End and save the current session before deleting a slot.");
        return store.DeleteConfirmed(slot, pair);
    }
    private void Validate(CheckpointRequest request, double now)
    {
        CheckTime(now);
        if (!pending.HasValue || pending.Value.Transaction != request.Transaction || !pending.Value.Session.Equals(request.Session)
            || pending.Value.PreviousGeneration != request.PreviousGeneration || !lifecycle.AcceptsSave(request.Session) || now >= deadline)
            throw new InvalidOperationException("Expired or foreign checkpoint response.");
    }
    private void CheckTime(double now)
    {
        if (double.IsNaN(now) || double.IsInfinity(now) || now < lastTime) throw new ArgumentException("Invalid monotonic timestamp.");
        lastTime = now;
    }
}
