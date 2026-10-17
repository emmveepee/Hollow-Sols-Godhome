using System;
namespace YiGodhome.Bridge;

// A defense suppresses one live collider lease temporarily. Caller supplies the
// paused gameplay clock; a different native activation cannot inherit the hold.
public sealed class DefenseCooldown
{
    private long activation;
    private double until;
    public void Begin(long lease, double now, double seconds)
    {
        if (lease <= 0 || seconds <= 0 || seconds > 10 || double.IsNaN(now) || double.IsInfinity(now) || double.IsNaN(seconds) || double.IsInfinity(seconds))
            throw new ArgumentOutOfRangeException(nameof(lease));
        activation = lease; until = now + seconds;
    }
    public bool Held(long lease, double now) => lease == activation && now < until;
}
