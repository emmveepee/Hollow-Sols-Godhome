using System;
using System.Collections.Generic;

namespace YiGodhome.Bridge;

// Only immutable hit identity crosses receiver retirement/reuse. Zero HP alone
// is insufficient: the host must acknowledge an applied native death callback.
public sealed class KillRewardSettlement
{
    private readonly Dictionary<long, HitOutcome> pending = new();
    public void Track(long sequence, HitOutcome outcome)
    {
        if (outcome.Kind != HitKind.Talisman) return;
        if (sequence <= 0 || pending.Count >= 64 || pending.ContainsKey(sequence)) throw new InvalidOperationException("Invalid kill reward acknowledgement lease.");
        pending.Add(sequence, new HitOutcome { Session = outcome.Session, Encounter = outcome.Encounter, GeometryFrame = outcome.GeometryFrame,
            Entity = outcome.Entity, TargetActivation = outcome.TargetActivation, Kind = outcome.Kind });
    }
    public HitOutcome? Resolve(HitAck ack)
    {
        if (!pending.TryGetValue(ack.Sequence, out var outcome)) return null;
        pending.Remove(ack.Sequence);
        return ack.Session == outcome.Session && ack.Disposition == HitDisposition.Applied && ack.TargetKilled && ack.Health == 0 ? outcome : null;
    }
}
