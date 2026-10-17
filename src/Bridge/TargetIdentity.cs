using System;
namespace YiGodhome.Bridge;

// Receiver identity is independent of encounter verdict and display grouping.
// Native phase adapters must acquire a fresh lease for replacement bodies.
public readonly struct TargetIdentity : IEquatable<TargetIdentity>
{
    public readonly Guid Session, Encounter;
    public readonly long Geometry, Activation;
    public readonly int Entity;
    public TargetIdentity(ContactFrame frame)
    { Session = frame.Session; Encounter = frame.Encounter; Geometry = frame.GeometryFrame; Entity = frame.Entity; Activation = frame.TargetGeneration; }
    public bool Equals(TargetIdentity other) => Session == other.Session && Encounter == other.Encounter && Geometry == other.Geometry && Entity == other.Entity && Activation == other.Activation;
    public override bool Equals(object? obj) => obj is TargetIdentity other && Equals(other);
    public override int GetHashCode() => Session.GetHashCode() ^ Encounter.GetHashCode() ^ Geometry.GetHashCode() ^ Entity ^ Activation.GetHashCode();
    public override string ToString() => "encounter=" + Encounter + " geometry=" + Geometry + " entity=" + Entity + " activation=" + Activation;
}
