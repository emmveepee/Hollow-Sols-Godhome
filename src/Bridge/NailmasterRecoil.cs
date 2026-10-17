namespace YiGodhome.Bridge;

// InitialParryPush refines LargeParryPush on the wire; it is never standalone.
// Nailmaster double swings use that valid pair for stronger non-tumbling recoil.
public static class NailmasterRecoil
{
    public static AttackTraits Traits(bool doubleSwing,bool heavy) => doubleSwing
        ? AttackTraits.LargeParryPush | AttackTraits.InitialParryPush
        : heavy ? AttackTraits.LargeParryPush : AttackTraits.None;

    public static bool Tumbles(bool large,bool initial) => large && !initial;
}
