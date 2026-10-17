using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace YiGodhome;

// SetHP transfers a native stagger accumulator without HealthManager's lethal-hit
// check. Relay only this exact authored transfer into its native death receiver.
internal sealed class NativeAccumulatorDeathRelay : MonoBehaviour
{
    private static readonly Dictionary<SetHP, NativeAccumulatorDeathRelay> owners = new();
    private static readonly Dictionary<HealthManager, NativeAccumulatorDeathRelay> accumulators = new();
    private static bool patched;
    private SetHP action = null!;
    private HealthManager health = null!;
    private Action<string>? log;
    private HealthManager? accumulator;
    private Func<bool>? accumulating;
    private bool committed;
    internal void BindAccumulator(HealthManager receiver, Func<bool> active)
    {
        accumulator=receiver; accumulating=active; accumulators[receiver]=this;
    }
    internal void Bind(SetHP transfer, HealthManager boss, Action<string>? logger)
    {
        if(action != null) owners.Remove(action);
        if(accumulator != null) accumulators.Remove(accumulator);
        accumulator=null; accumulating=null; committed=false;
        action=transfer; health=boss; log=logger;
        owners.Add(transfer,this);
        if (!patched) {
            new Harmony("local.yigodhome.native-accumulator-death").Patch(AccessTools.Method(typeof(SetHP), "OnEnter"),
                postfix: new HarmonyMethod(typeof(NativeAccumulatorDeathRelay), nameof(AfterTransfer)));
            new Harmony("local.yigodhome.native-accumulator-hit").Patch(AccessTools.Method(typeof(HealthManager), "Hit"),
                postfix: new HarmonyMethod(typeof(NativeAccumulatorDeathRelay), nameof(AfterHit)));
            patched=true;
        }
    }
    private static void AfterHit(HealthManager __instance, HitInstance hitInstance)
    {
        if(!accumulators.TryGetValue(__instance,out var owner) || owner.committed || owner.health == null
            || owner.health.isDead || owner.health.hp<=0 || owner.accumulating?.Invoke()!=true) return;
        // This is damage already accepted by the native bat, capped by the
        // authored Adjust HP action. Only a terminal debit is transferred early.
        int debit=-Math.Max(-50,Math.Min(0,__instance.hp));
        int before=owner.health.hp;
        if(debit<before) return;
        owner.committed=true; // An uncertain native mutation must never be replayed.
        owner.health.SubtractHealth(debit);
        if(owner.health.hp!=before-debit)
            throw new InvalidOperationException("Native terminal accumulator debit differed; damage will not be replayed.");
        owner.log?.Invoke("HK_ACCUMULATOR_LETHAL_IMMEDIATE entity="+owner.health.GetInstanceID()+" receiver="+__instance.GetInstanceID()
            +" before="+before+" debit="+debit+" health="+owner.health.hp+" nativeDeath=True damageReplayed=False frame="+Time.frameCount);
        owner.health.Die(null,hitInstance.AttackType,false);
    }
    private static void AfterTransfer(SetHP __instance)
    {
        if (!owners.TryGetValue(__instance,out var owner) || owner.health == null) return;
        if (FSMUtility.GetSafe(__instance.target,__instance)?.GetComponent<HealthManager>() != owner.health)
            throw new InvalidOperationException("Native accumulator transfer owner changed.");
        if (owner.health.hp > 0 || owner.health.isDead) return;
        owner.log?.Invoke("HK_ACCUMULATOR_LETHAL_TRANSFER entity="+owner.health.GetInstanceID()+" health="+owner.health.hp+" nativeDeath=True damageReplayed=False frame="+Time.frameCount);
        owner.health.Die(null,AttackTypes.Spell,false);
    }
    private void OnDestroy() { if(action != null) owners.Remove(action); if(accumulator != null) accumulators.Remove(accumulator); }
}
