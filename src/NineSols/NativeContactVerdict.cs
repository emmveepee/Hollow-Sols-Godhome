using System;
using HarmonyLib;
using YiGodhome.Bridge;
using UnityEngine;

namespace YiGodhome;

internal sealed class NativeContactVerdict : IDisposable
{
    private static readonly System.Collections.Generic.Dictionary<DamageDealer, NativeContactVerdict> instances = new System.Collections.Generic.Dictionary<DamageDealer, NativeContactVerdict>();
    private static readonly Harmony harmony = new Harmony("local.yigodhome.contact.verdict");
    private static bool jadeBindingLogged;
    private readonly DamageDealer dealer;
    private readonly Player player;
    private readonly Action<string> log;
    private readonly ContactVerdict verdict = new ContactVerdict();
    private readonly bool duplicateProbe;
    private bool duplicateVerified;
    private string? failure;
    private EffectHitData? pendingHit;
    public bool TalismanContactImmunity;
    public Action<string>? TalismanRefused;
    private bool TalismanSafe => TalismanContactImmunity && (player.CurrentStateType == PlayerStateType.FooAttack || player.CurrentStateType == PlayerStateType.FooExplode);
    public Action? Countered;
    public Action? Resolved;
    public string ResolutionKind { get; private set; } = "";
    public bool ReflectionEligible { get; private set; }
    public bool PreciseParry { get; private set; }
    private int attachedFacing;
    private void RestoreAttachedFacing(long generation,bool accepted)
    {
        if(generation==0 || !AttachedParryFacing.Restore(attachedFacing,(int)player.Facing,accepted,player.isOnLedge,player.isOnWall,player.AttachOnWall))return;
        var recoilFacing=player.Facing;
        player.Facing=(Facings)attachedFacing;
        log("NS_LEDGE_PARRY_FACING_RESTORED previous="+recoilFacing+" attached="+player.Facing+" precise="+PreciseParry+" generation="+generation+" frame="+Time.frameCount);
    }
    public NativeContactVerdict(DamageDealer dealer, Player player, Action<string> log, bool duplicateProbe)
    {
        if (instances.ContainsKey(dealer)) throw new InvalidOperationException("Contact verdict already installed for dealer.");
        this.dealer = dealer; this.player = player; this.log = log; this.duplicateProbe = duplicateProbe; instances.Add(dealer, this);
        try
        {
            if (SessionConfiguration.Current.RunFixtures && !jadeBindingLogged) {
                jadeBindingLogged = true;
                log("NS_PROJECTILE_JADE_BIND field=ReflectJade asset=" + player.mainAbilities.ReflectJade.name + " item=" + player.mainAbilities.ReflectJade.BindingItemPicked?.name);
                log("NS_PROJECTILE_JADE_BIND field=MustReflectProjectileJade asset=" + player.mainAbilities.MustReflectProjectileJade.name + " item=" + player.mainAbilities.MustReflectProjectileJade.BindingItemPicked?.name);
            }
            if (instances.Count != 1) return;
            harmony.Patch(AccessTools.Method(typeof(DamageReceiver), "ApplyDelayDamage"), prefix: new HarmonyMethod(typeof(NativeContactVerdict), nameof(DamageBefore)), postfix: new HarmonyMethod(typeof(NativeContactVerdict), nameof(DamageAfter)));
            harmony.Patch(AccessTools.Method(typeof(DamageDealer), "OnEffectHit"), prefix: new HarmonyMethod(typeof(NativeContactVerdict), nameof(MayQueue)), postfix: new HarmonyMethod(typeof(NativeContactVerdict), nameof(Queued)));
            harmony.Patch(AccessTools.Method(typeof(PlayerParryState), "Parried"), prefix: new HarmonyMethod(typeof(NativeContactVerdict), nameof(ParryBefore)), postfix: new HarmonyMethod(typeof(NativeContactVerdict), nameof(ParryAfter)));
            harmony.Patch(AccessTools.Method(typeof(PlayerParryState), "AccurateParry"),prefix:new HarmonyMethod(typeof(NativeContactVerdict),nameof(AccurateNative)));
            harmony.Patch(AccessTools.Method(typeof(ParryCounterDefenseState), "Parried"), prefix: new HarmonyMethod(typeof(NativeContactVerdict), nameof(ParryBefore)), postfix: new HarmonyMethod(typeof(NativeContactVerdict), nameof(CounterAfter)));
        }
        catch { Dispose(); throw; }
    }
    public void Begin() { pendingHit = null; ResolutionKind = ""; ReflectionEligible = PreciseParry = false; verdict.Begin(); log("NS_CONTACT_GENERATION id=" + verdict.Generation + " frame=" + Time.frameCount + " dealer=" + dealer.GetInstanceID()); }
    public void Suspend() { pendingHit = null; verdict.Suspend(); }
    public void Drain(float duration) {
        if (!verdict.CanResolve || verdict.Draining) return;
        if (pendingHit == null) { Suspend(); return; }
        verdict.Drain(Time.time, duration);
        log("NS_CONTACT_DRAIN dealer="+dealer.GetInstanceID()+" generation="+verdict.Generation+" seconds="+duration+" frame="+Time.frameCount+" newContacts=False");
    }
    public bool Draining => verdict.Draining;
    public void Resume() { if(!verdict.Draining) verdict.Resume(); }
    public void Check() { bool draining=verdict.Draining; verdict.Tick(Time.time); if (draining && !verdict.Draining) pendingHit = null; if (failure != null) throw new InvalidOperationException(failure); }
    private static bool MayQueue(DamageDealer __instance, EffectHitData __0) {
        if (!instances.TryGetValue(__instance, out var a) || !a.TalismanSafe) return true;
        if(__0.receiver.GetComponent<DamageReceiver>()?.bindHealth != a.player.health)return true;
        a.TalismanRefused?.Invoke("queue");return false;
    }
    private static void Queued(DamageDealer __instance, EffectHitData __0) {
        if(!instances.TryGetValue(__instance,out var a) || !a.verdict.CanResolve || a.verdict.Draining) return;
        var receiver=__0.receiver.GetComponent<DamageReceiver>();
        if(receiver != null && receiver.bindHealth == a.player.health) {
            a.pendingHit=__0;
            a.log("NS_CONTACT_QUEUED dealer="+__instance.GetInstanceID()+" generation="+a.verdict.Generation+" frame="+Time.frameCount+" grounded="+a.player.IsOnGround);
        }
    }
    private bool Admit(ref bool result, out long generation)
    {
        verdict.Tick(Time.time);
        generation = verdict.Generation;
        if (verdict.CanResolve && !player.health.IsDead) return true;
        result = false;
        log("NS_CONTACT_DUPLICATE_REFUSED id=" + generation + " frame=" + Time.frameCount + " enabled=" + verdict.Enabled);
        generation = 0; return false;
    }
    private static bool DamageBefore(DamageReceiver __instance, DamageDealer __0, EffectHitData __1, ref bool __result, out long __state)
    {
        __state = 0;
        instances.TryGetValue(__0, out var a);
        a?.verdict.Tick(Time.time);
        if (a != null && a.TalismanSafe && __instance.bindHealth == a.player.health) { a.TalismanRefused?.Invoke("damage");__result=false; return false; }
        if (a != null && a.verdict.Draining && !ReferenceEquals(a.pendingHit,__1)) { a.log("NS_CONTACT_DRAIN_FOREIGN dealer="+__0.GetInstanceID()+" frame="+Time.frameCount); __result=false; return false; }
        return a == null || __0 != a.dealer || __instance.bindHealth != a.player.health || a.Admit(ref __result, out __state);
    }
    private static void DamageAfter(DamageReceiver __instance, DamageDealer __0, EffectHitData __1, bool __result, long __state)
    {
        instances.TryGetValue(__0, out var a);
        if (a != null && __state != 0 && !__result && __instance.bindHealth == a.player.health)
            a.log("NS_CONTACT_NATIVE_REJECTED dealer=" + __0.GetInstanceID() + " generation=" + __state + " invincible=" + a.player.health.IsInvincible + " hasParried=" + __0.HasParried + " state=" + a.player.CurrentStateType + " health=" + a.player.health.currentValue + " frame=" + Time.frameCount);
        if (a == null || !a.Finish(__state, __result, "damage") || !a.duplicateProbe || a.duplicateVerified) return;
        a.duplicateVerified = true;
        float health = a.player.health.currentValue;
        bool duplicate = __instance.ApplyDelayDamage(__0, __1);
        if (duplicate || a.player.health.currentValue != health) a.failure = "Duplicate native damage callback was accepted.";
        else a.log("NS_CONTACT_DUPLICATE_VERIFIED id=" + __state + " frame=" + Time.frameCount + " health=" + health);
    }
    private static bool ParryBefore(DamageDealer __2, ref bool __result, out long __state)
    {
        __state = 0;
        instances.TryGetValue(__2, out var a);
        if(a==null || __2!=a.dealer)return true;
        a.attachedFacing=a.player.isOnLedge && a.player.isOnWall && a.player.AttachOnWall?(int)a.player.Facing:0;
        return a.Admit(ref __result, out __state);
    }
    private static void AccurateNative(DamageDealer __2) {
        if(instances.TryGetValue(__2,out var a))a.PreciseParry=true;
    }
    private static void ParryAfter(PlayerParryState __instance, DamageDealer __2, bool __result, long __state) {
        if (!instances.TryGetValue(__2, out var a)) return;
        a.RestoreAttachedFacing(__state,__result);
        // Sample the native verdict after it decides precise/imprecise timing.
        // Installed ability assets distinguish injury reflection (ReflectJade)
        // from projectile reflection (MustReflectProjectileJade / Ricochet).
        bool precise = a.PreciseParry;
        bool ricochet = a.player.mainAbilities.MustReflectProjectileJade.IsActivated;
        a.ReflectionEligible = ProjectileDefense.NativeEligible(precise,ricochet);
        if (__result) a.log("NS_PROJECTILE_DEFENSE_RULE precise=" + precise + " ricochet=" + ricochet + " reflectionEligible=" + a.ReflectionEligible + " frame=" + Time.frameCount);
        a.Finish(__state, __result, "parry");
    }
    private static void CounterAfter(DamageDealer __2, bool __result, long __state)
    {
        instances.TryGetValue(__2, out var a);
        a?.RestoreAttachedFacing(__state,__result);
        // Installed successful counter calls AccurateParry(isAccurate=true),
        // observed by the same hook. Never treat a generic success as permission.
        if(a!=null && __result)a.ReflectionEligible=ProjectileDefense.NativeEligible(a.PreciseParry,a.player.mainAbilities.MustReflectProjectileJade.IsActivated);
        if (a != null && a.Finish(__state, __result, "counter")) a.Countered?.Invoke();
    }
    private bool Finish(long generation, bool accepted, string kind)
    {
        if (generation != 0 && verdict.Accept(generation, accepted))
        { if(verdict.Draining) log("NS_CONTACT_DRAIN_RESOLVED dealer="+dealer.GetInstanceID()+" generation="+generation+" kind="+kind+" frame="+Time.frameCount);
          ResolutionKind = kind; log("NS_CONTACT_RESOLVED id=" + generation + " kind=" + kind + " dealer=" + dealer.GetInstanceID() + " frame=" + Time.frameCount + " health=" + player.health.currentValue
            + " position=" + player.transform.position + " velocity=" + player.Velocity + " incomingDirection=" + dealer.GlobalAttackDir
            + " facing=" + player.Facing + " dealerX=" + dealer.transform.position.x); Resolved?.Invoke(); return true; }
        return false;
    }
    public void Dispose() { if (instances.Remove(dealer)) { verdict.Suspend(); if (instances.Count == 0) harmony.UnpatchSelf(); } }
}
