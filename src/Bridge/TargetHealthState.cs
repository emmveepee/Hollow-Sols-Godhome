using System;

namespace YiGodhome.Bridge;

// One native damageable activation. It has no encounter-completion operation.
// Phase adapters choose whether a reset keeps this activation or retires it;
// health changes alone cannot choose that policy or transfer internal injury.
public sealed class TargetHealthState
{
    public int Entity { get; }
    public long Activation { get; }
    public int Health { get; private set; }
    public int Maximum { get; private set; }
    public TargetHealthState(int entity, long activation, int health)
    {
        if (entity == 0 || activation <= 0) throw new ArgumentException("Invalid native target identity.");
        Entity = entity; Activation = activation; Observe(health);
    }
    public void Observe(int health)
    {
        if (health < 0 || health > 1000000) throw new ArgumentOutOfRangeException(nameof(health));
        Health = health; Maximum = Math.Max(Maximum, Math.Max(1, health));
    }
    public bool ObserveNative(int entity, long activation, int nativeHealth)
    {
        if (entity != Entity || activation != Activation) return false;
        Observe(Math.Max(0, nativeHealth));
        return true;
    }
    public bool Matches(HitOutcome hit) => hit.Entity == Entity && hit.TargetActivation == Activation;
    // Explicit adapter choice for a native terminal body, never encounter victory.
    public ContactFrame RemovedSnapshot(ContactFrame prior, long nativeFrame, long tick)
    {
        if (Health != 0 || prior.Entity != Entity || prior.TargetGeneration != Activation)
            throw new InvalidOperationException("Unadapted live target removal.");
        return new ContactFrame { Session = prior.Session, Encounter = prior.Encounter, GeometryFrame = prior.GeometryFrame,
            NativeFrame = nativeFrame, CaptureTick = tick, Frequency = prior.Frequency, Entity = Entity, TargetGeneration = Activation,
            Collider = prior.Collider, Damage = prior.Damage, Health = 0, MaxHealth = Maximum, Difficulty = prior.Difficulty,
            X = prior.X, Y = prior.Y, Width = prior.Width, Height = prior.Height,
            HurtCollider = prior.HurtCollider, HurtX = prior.HurtX, HurtY = prior.HurtY, HurtWidth = prior.HurtWidth, HurtHeight = prior.HurtHeight };
    }
}
