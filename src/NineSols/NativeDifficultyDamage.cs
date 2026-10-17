using System;
using System.Collections.Generic;
using HarmonyLib;

namespace YiGodhome;

// Scope lethal damage to the native health receiver, after native defense admits
// the hit. Parry reflection still reads the ordinary attack strength.
internal static class NativeDifficultyDamage
{
    private static readonly Dictionary<DamageDealer, int> tiers = new Dictionary<DamageDealer, int>();
    private static readonly Harmony harmony = new Harmony("local.yigodhome.difficulty-damage");
    private static DamageDealer? receiving;
    public static void Register(DamageDealer dealer)
    {
        if (tiers.Count == 0) {
            harmony.Patch(AccessTools.Method(typeof(PlayerHealth), "ReceiveDamage"), prefix: new HarmonyMethod(typeof(NativeDifficultyDamage), nameof(Enter)), finalizer: new HarmonyMethod(typeof(NativeDifficultyDamage), nameof(Leave)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(DamageDealer), "DamageAmount"), postfix: new HarmonyMethod(typeof(NativeDifficultyDamage), nameof(Amount)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(PlayerAbilityData), "IsActivated"), postfix: new HarmonyMethod(typeof(NativeDifficultyDamage), nameof(Conversion)));
        }
        tiers.Add(dealer, 0);
    }
    public static void Set(DamageDealer dealer, int tier) => tiers[dealer] = tier;
    public static void Remove(DamageDealer dealer) { tiers.Remove(dealer); if (tiers.Count == 0) { receiving = null; harmony.UnpatchSelf(); } }
    private static void Enter(PlayerHealth __instance, DamageDealer __0, out DamageDealer? __state)
    { __state = receiving; receiving = __0 != null && __instance == Player.i?.health && tiers.TryGetValue(__0, out int tier) && tier == 2 ? __0 : null; }
    private static void Leave(DamageDealer? __state) { RadiantRevivalProbe.ObserveNativeDamage(); receiving = __state; }
    private static void Amount(DamageDealer __instance, ref float __result)
    { if (receiving == __instance) __result = 1000000; }
    private static void Conversion(PlayerAbilityData __instance, ref bool __result)
    {
        // The all-internal-damage talisman jade cannot turn a Radiant hit into
        // harmless injury. Revival's native charge/death interception is untouched.
        if (receiving != null && Player.i != null && __instance == Player.i.mainAbilities.EndureFooExplodeJade) __result = false;
    }
}
