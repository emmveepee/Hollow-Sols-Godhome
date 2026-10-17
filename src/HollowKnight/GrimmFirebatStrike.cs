using YiGodhome.Bridge;

namespace YiGodhome;

internal static class GrimmFirebatStrike
{
    internal static void Apply(AttackVolume volume,long borrowAction)
    {
        // Flash and flight belong to this bat, never the volley or boss action.
        volume.ActionId=borrowAction;
        volume.DamageGroupId=volume.CounterGroupId=0;
        volume.InitialDamageGroupHold=0;volume.RepeatDamageGroupHold=false;
        volume.ParryCooldown=0;volume.CooldownOnDamage=false;
    }
}
