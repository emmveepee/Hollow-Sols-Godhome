namespace YiGodhome.Bridge;

public static class ProjectileDefense
{
    public static bool NativeEligible(bool preciseParry,bool ricochetEquipped)=>preciseParry || ricochetEquipped;
    public static bool CounterDeflects(AttackTraits traits) => traits == (AttackTraits.Crimson | AttackTraits.Reflectable);
    public static bool Reflects(AttackTraits traits, bool ordinaryDefense, bool counter, bool nativeEligible) => nativeEligible
        && (traits & AttackTraits.Reflectable) != 0 && (ordinaryDefense || counter && CounterDeflects(traits));
}
