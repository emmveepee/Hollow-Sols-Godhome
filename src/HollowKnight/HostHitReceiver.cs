using System;
using System.Diagnostics;
using System.Collections.Generic;
using UnityEngine;
using YiGodhome.Bridge;
using HarmonyLib;

namespace YiGodhome;

internal sealed class HostHitReceiver : IDisposable
{
    private readonly HostContactSource source;
    private readonly Action<string> log;
    private readonly HitLedger ledger;
    private readonly GameObject hitSource;
    private readonly GameObject strikeSink;
    private static readonly System.Reflection.FieldInfo StrikeNail = AccessTools.Field(typeof(HealthManager), "strikeNailPrefab");
    private static readonly System.Reflection.FieldInfo SlashImpact = AccessTools.Field(typeof(HealthManager), "slashImpactPrefab");
    private double remainder;
    private readonly Dictionary<int, (long Generation, double Fraction)> summonRemainders = new Dictionary<int, (long, double)>();
    private static bool refusedFixture;
    private readonly bool refusalFixtureEnabled;
    private readonly bool fastPureVesselFixture;
    private readonly HashSet<HitKind> appliedKinds = new HashSet<HitKind>();
    private readonly HashSet<HitKind> summonKinds = new HashSet<HitKind>();
    private readonly HashSet<HostCounterHold> counterHolds = new();
    internal int AcceptedDefenses {get;private set;}
    internal int NativeCounterHolds {get;private set;}
    private void HoldCounter(HealthManager target, long sequence) {
        var lease = target.GetComponent<HostCounterHold>() ?? target.gameObject.AddComponent<HostCounterHold>();
        counterHolds.Add(lease); lease.Begin(target, sequence, log);
        NativeCounterHolds++;
    }
    private static HostHitReceiver? current;
    private readonly Harmony parryHarmony = new Harmony("local.yigodhome.host-parry-pause");
    private bool observingHit, parryPause;
    private bool attachedExplosion;
    private HealthManager? hitTarget;
    private int lowestHitHealth;
    private bool nativeSubtracted, nativeKilled;
    private static void ObserveDeath(HealthManager __instance) {
        var receiver = current;
        if (receiver?.observingHit == true && ReferenceEquals(receiver.hitTarget, __instance) && __instance.isDead)
            receiver.nativeKilled = true;
    }
    private int suppressedStrikeEffects;
    private static bool BeforeStrikeSpawn(GameObject __0, ref GameObject __result)
    {
        var receiver = current;
        if (receiver?.observingHit != true || receiver.hitTarget == null || __0 == null) return true;
        var target = receiver.hitTarget;
        if (!ReferenceEquals(__0, StrikeNail.GetValue(target))
            && !ReferenceEquals(__0, SlashImpact.GetValue(target))) return true;
        // The audited callers only position/scale this result. A private, inert
        // transform substitutes for Knight strike art; native damage still runs.
        __result = receiver.strikeSink;
        if (++receiver.suppressedStrikeEffects <= 5) receiver.log("HK_KNIGHT_STRIKE_SUPPRESSED entity=" + target.GetInstanceID() + " prefab=" + __0.name + " nativeDamage=True");
        return false;
    }
    private static void ObserveSubtract(HealthManager __instance) {
        var receiver = current;
        if (receiver?.observingHit == true && receiver.hitTarget == __instance && __instance.hp < receiver.lowestHitHealth) {
            receiver.lowestHitHealth = __instance.hp; receiver.nativeSubtracted = true;
        }
    }
    private static void ObserveFreeze(int __0) { if (__0 == 1 && current?.observingHit == true) current.parryPause = true; }
    private static bool ExplosionBlocking(HealthManager __instance, ref bool __result) {
        var receiver = current;
        if (receiver?.observingHit != true || !receiver.attachedExplosion || receiver.hitTarget != __instance
            || __instance.isDead || __instance.hp <= 0) return true;
        __result = false; return false;
    }
    private bool NativeHit(HealthManager target, HitInstance hit, bool explosion = false)
    {
        // FreezeMoment(1) is also the ordinary native invulnerable clink.
        // A complete counter hold requires the adapter's authored receiving
        // state before Hit transitions it, as well as the native freeze signal.
        bool counterWindow = source.NativeCounterWindow(target);
        attachedExplosion = explosion && EncounterProfile.Selected.Family != EncounterProfile.AbsoluteRadiance && EncounterProfile.Selected.Family != EncounterProfile.Uumuu;
        observingHit = true; parryPause = false; hitTarget = target; lowestHitHealth = target.hp; nativeSubtracted = nativeKilled = false;
        try { target.Hit(hit); return parryPause && counterWindow; }
        finally { observingHit = false; hitTarget = null; attachedExplosion = false; }
    }
    internal void NativeArmorClink(HealthManager target,float direction)
    {
        if(!target.IsInvincible || !EncounterProfile.Selected.ArmorAndHead || target.name!=EncounterProfile.Selected.Boss) throw new InvalidOperationException("Native armor clink left its inspected blocked receiver.");
        int before=target.hp;
        hitSource.transform.position=HeroController.instance.transform.position;
        // DamageEnemies.DoDamage sends this before Hit. The dream armor FSM
        // listens for TAKE DAMAGE, whereas the ordinary armor listens for HIT.
        if(EncounterProfile.Selected==EncounterProfile.FailedChampion)
            FSMUtility.SendEventToGameObject(target.gameObject,"TAKE DAMAGE",false);
        NativeHit(target,new HitInstance {Source=hitSource,AttackType=AttackTypes.Nail,DamageDealt=1,Multiplier=1,MagnitudeMultiplier=1,Direction=direction});
        if(target.hp!=before || nativeSubtracted || nativeKilled) throw new InvalidOperationException("Native armor clink unexpectedly injured receiver; no replay.");
    }
    internal static int ReflectedArmorHit(HealthManager target,HitInstance hit)
    {
        var receiver=current;
        if(receiver==null || !EncounterProfile.Selected.ArmorAndHead || receiver.observingHit) throw new InvalidOperationException("Reflected armor observer unavailable.");
        int entity=target.GetInstanceID();receiver.NativeHit(target,hit);
        int after=receiver.nativeSubtracted?Math.Max(0,receiver.lowestHitHealth):target==null?0:Math.Max(0,target.hp);
        receiver.log("HK_REFLECTED_ARMOR_OBSERVED entity="+entity+" nativeMinimum="+after+" nativeAfter="+(target==null?0:target.hp)+" nativeSubtracted="+receiver.nativeSubtracted+" frame="+Time.frameCount);
        return after;
    }
    public bool HasSummonHit(HitKind kind) => summonKinds.Contains(kind);
    public long AppliedDamageCount { get; private set; }
    public bool HasApplied(HitKind kind) => appliedKinds.Contains(kind);
    public HostHitReceiver(HostContactSource source, Action<string> log)
    {
        this.source = source; this.log = log;
        string run = System.IO.Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!;
        refusalFixtureEnabled = YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures
            && YiGodhome.Bridge.SessionConfiguration.Current.Has("injury-refusal");
        fastPureVesselFixture = EncounterProfile.Selected == EncounterProfile.PureVessel && YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures
            && YiGodhome.Bridge.SessionConfiguration.Current.Has("cheat-probe");
        var f = source.Latest ?? throw new InvalidOperationException("Host hit context unavailable.");
        ledger = new HitLedger(f.Session, f.Encounter, f.GeometryFrame, f.Entity, source.RecognizesSummon);
        hitSource = new GameObject("YiGodhome.NativeHitSource");
        strikeSink = new GameObject("YiGodhome.InertKnightStrikeSink"); strikeSink.SetActive(false);
        current = this;
        parryHarmony.Patch(AccessTools.Method(typeof(GameManager), "FreezeMoment", new[] { typeof(int) }),
            prefix: new HarmonyMethod(typeof(HostHitReceiver), nameof(ObserveFreeze)));
        parryHarmony.Patch(AccessTools.Method(typeof(HealthManager), "IsBlockingByDirection"),
            prefix: new HarmonyMethod(typeof(HostHitReceiver), nameof(ExplosionBlocking)));
        parryHarmony.Patch(AccessTools.Method(typeof(HealthManager), "Die", new[] { typeof(float?), typeof(AttackTypes), typeof(bool) }),
            postfix: new HarmonyMethod(typeof(HostHitReceiver), nameof(ObserveDeath)));
        parryHarmony.Patch(AccessTools.Method(typeof(ObjectPoolExtensions), "Spawn", new[] { typeof(GameObject), typeof(Vector3), typeof(Quaternion) }),
            prefix: new HarmonyMethod(typeof(HostHitReceiver), nameof(BeforeStrikeSpawn)));
        if (EncounterProfile.Selected.ArmorAndHead)
            parryHarmony.Patch(AccessTools.Method(typeof(HealthManager), "SubtractHealth", new[] { typeof(int) }),
                postfix: new HarmonyMethod(typeof(HostHitReceiver), nameof(ObserveSubtract)));
    }
    public HitAck Process(HitOutcome hit)
    {
        using var hitCost = HostFrameProbe.Measure(23);
        var boss = source.Boss;
        int health = boss == null ? 0 : Math.Max(0, boss.hp);
        return ledger.Process(hit, Stopwatch.GetTimestamp(), Stopwatch.Frequency, health, h => {
            if (GameManager.instance.gameState.ToString() == "PAUSED") {
                var pausedTarget = h.Entity == source.Latest!.Entity ? boss : source.ResolveSummon(h);
                int pausedHealth = pausedTarget == null ? 0 : source.PresentedHealth(pausedTarget);
                log("HK_PAUSED_OUTCOME_REFUSED sequence=" + h.Sequence + " entity=" + h.Entity + " replay=False");
                return new HitAck { Session = h.Session, Sequence = h.Sequence, Disposition = HitDisposition.Refused, Health = pausedHealth };
            }
            if (h.Kind==HitKind.StrikeParried || h.Kind==HitKind.StrikeDeflected) {
                var parriedMinion=h.Kind==HitKind.StrikeParried?source.ParriedMinion(h.AttackId):null;
                if(parriedMinion!=null && source.MatchesPrimary(h)) {
                    int beforeMinion=parriedMinion.hp;
                    nativeSubtracted=nativeKilled=false;lowestHitHealth=beforeMinion;
                    hitSource.transform.position=HeroController.instance.transform.position;
                    NativeHit(parriedMinion,new HitInstance{Source=hitSource,AttackType=AttackTypes.Nail,DamageDealt=1,Multiplier=1,MagnitudeMultiplier=1,Direction=h.Direction});
                    bool parryApplied=parriedMinion.hp<beforeMinion || nativeKilled || nativeSubtracted;
                    log("HK_ORANGE_MINION_PARRY entity="+parriedMinion.GetInstanceID()+" action="+h.AttackId+" sequence="+h.Sequence+" before="+beforeMinion
                        +" nativeMinimum="+lowestHitHealth+" killed="+nativeKilled+" applied="+parryApplied+" nativeHit=True frame="+Time.frameCount);
                    return new HitAck{Session=h.Session,Sequence=h.Sequence,Disposition=parryApplied?HitDisposition.Applied:HitDisposition.Refused,Health=health};
                }
                bool accepted=h.Entity==source.Latest!.Entity ? source.ParriedStrike(h.AttackId,h.Kind) : source.CounterSummon(h);
                log("HK_STRIKE_PARRY_RECEIPT sequence="+h.Sequence+" action="+h.AttackId+" accepted="+accepted+" damage=0 nativeSequenceContinues=True");
                return new HitAck {Session=h.Session,Sequence=h.Sequence,Disposition=accepted?HitDisposition.Applied:HitDisposition.Refused,Health=health};
            }
            if (h.Entity != source.Latest!.Entity && (h.Kind == HitKind.TaiChiKick || h.Kind == HitKind.UnboundedCounter)) {
                bool accepted = source.CounterSummon(h);
                if(accepted)AcceptedDefenses++;
                if (!accepted && !source.KnowsSummon(h)) log("HK_RETIRED_DEFENSE_REFUSED encounter=" + h.Encounter + " entity=" + h.Entity
                    + " activation=" + h.TargetActivation + " sequence=" + h.Sequence + " kind=" + h.Kind + " action=" + h.AttackId + " replay=False nativeTransitionPreserved=True");
                return new HitAck { Session = h.Session, Sequence = h.Sequence, Disposition = accepted ? HitDisposition.Applied : HitDisposition.Refused, Health = source.KnownSummonHealth(h) };
            }
            if (h.Entity != source.Latest!.Entity)
            {
                var minion = source.ResolveSummon(h); int old = minion == null ? 0 : source.PresentedHealth(minion);
                nativeSubtracted = nativeKilled = false; lowestHitHealth = old;
                summonRemainders.TryGetValue(h.Entity, out var prior);
                double scaledMinion = h.Damage * BossHealthProjection.NativeToHost + (prior.Generation == h.TargetActivation ? prior.Fraction : 0);
                int minionDamage = (int)Math.Floor(scaledMinion);
                bool paused = false;
                bool finalNail=false;
                if (minion != null && minionDamage > 0) {
                    hitSource.transform.position = HeroController.instance.transform.position;
                    finalNail=AbsoluteRadianceCombat.TryFinalNail(minion,h.Kind,log);
                    if(!finalNail && !AbsoluteRadianceCombat.FinalNailWindow(minion))paused = NativeHit(minion, new HitInstance { Source = hitSource, AttackType = h.Kind == HitKind.Melee ? AttackTypes.Nail : AttackTypes.Spell,
                        DamageDealt = minionDamage, Multiplier = 1, MagnitudeMultiplier = 1, Direction = h.Direction },
                        h.Kind == HitKind.Talisman && EncounterProfile.Selected.IsBossName(minion.name));
                }
                int remaining = minion == null ? 0 : source.PresentedHealth(minion);
                bool secondaryApplied = minion != null && (finalNail || remaining < old || EncounterProfile.Selected.ArmorAndHead && nativeSubtracted);
                if (secondaryApplied) { AppliedDamageCount++; summonKinds.Add(h.Kind); summonRemainders[h.Entity] = (h.TargetActivation, scaledMinion - minionDamage); }
                if(h.Kind==HitKind.Bow) log("HK_NATIVE_ARROW_ROUTE encounter="+h.Encounter+" entity="+h.Entity+" activation="+h.TargetActivation+" sequence="+h.Sequence+" attackType=Spell before="+old+" after="+remaining+" frame="+Time.frameCount);
                log("HK_SUMMON_HIT entity=" + h.Entity + " generation=" + h.TargetActivation + " kind=" + h.Kind + " before=" + old + " after=" + remaining
                    + " sequence=" + h.Sequence + " encounter=" + h.Encounter + " nativeFrame="+h.NativeFrame+" frame=" + Time.frameCount);
                if (EncounterProfile.Selected.ArmorAndHead) log("HK_ARMOR_HIT encounter=" + h.Encounter + " entity=" + h.Entity + " activation=" + h.TargetActivation
                    + " sequence=" + h.Sequence + " kind=" + h.Kind + " before=" + old + " nativeMinimum=" + lowestHitHealth + " nativeAfter=" + (minion == null ? 0 : minion.hp) + " presentedAfter=" + remaining + " applied=" + secondaryApplied + " frame=" + Time.frameCount);
                if (paused && !secondaryApplied && minion != null) HoldCounter(minion, h.Sequence);
                return new HitAck { Session = h.Session, Sequence = h.Sequence, Disposition = secondaryApplied ? HitDisposition.Applied : HitDisposition.Refused, Health = remaining, ParryPause = paused && !secondaryApplied, TargetKilled = secondaryApplied && nativeKilled && remaining == 0 };
            }
            int before = boss == null ? 0 : Math.Max(0, boss.hp);
            if (!source.MatchesPrimary(h))
                return new HitAck { Session = h.Session, Sequence = h.Sequence, Disposition = HitDisposition.Stale, Health = before };
            if (h.Kind == HitKind.ProjectileResolved || h.Kind == HitKind.ProjectileReflected)
            {
                bool resolved = source.ResolveProjectile(h.Dealer, h.AttackId, h.Kind == HitKind.ProjectileReflected);
                return new HitAck { Session = h.Session, Sequence = h.Sequence, Disposition = resolved ? HitDisposition.Applied : HitDisposition.Refused, Health = before };
            }
            if (h.Kind == HitKind.UnboundedCounter || h.Kind == HitKind.TaiChiKick)
            {
                bool interrupted = h.Kind == HitKind.TaiChiKick ? source.InterruptSwoop(h.AttackId) : source.InterruptBounce(h.AttackId);
                if(interrupted)AcceptedDefenses++;
                log("HK_COUNTER_RESULT sequence=" + h.Sequence + " attack=" + h.AttackId + " applied=" + interrupted);
                return new HitAck { Session = h.Session, Sequence = h.Sequence, Disposition = interrupted ? HitDisposition.Applied : HitDisposition.Refused, Health = before };
            }
            bool applied = false; nativeKilled = false;
            bool blockedPause = false;
            if (!refusedFixture && h.Kind == HitKind.Talisman && refusalFixtureEnabled)
            {
                refusedFixture = true;
                log("HK_INJURY_REFUSAL_FIXTURE sequence=" + h.Sequence + " explicitDiagnostic=True health=" + before);
                return new HitAck { Session = h.Session, Sequence = h.Sequence, Disposition = HitDisposition.Refused, Health = before };
            }
            double scaled = h.Damage * BossHealthProjection.NativeToHost + remainder;
            if (fastPureVesselFixture && appliedKinds.Contains(HitKind.Melee) && appliedKinds.Contains(HitKind.Bow) && appliedKinds.Contains(HitKind.Talisman)) {
                scaled = h.Damage * BossHealthProjection.NativeToHost * 3 + remainder;
                log("HK_DAMAGE_CHEAT multiplier=3 explicitDiagnostic=True nativeHit=True");
            }
            int damage = (int)Math.Floor(scaled);
            if (boss != null && !boss.isDead && boss.GetInstanceID() == h.Entity && damage > 0)
            {
                hitSource.transform.position = HeroController.instance.transform.position;
                bool finalNail=AbsoluteRadianceCombat.TryFinalNail(boss,h.Kind,log);
                if(!finalNail && !AbsoluteRadianceCombat.FinalNailWindow(boss))blockedPause = NativeHit(boss, new HitInstance { Source = hitSource, AttackType = h.Kind == HitKind.Melee ? AttackTypes.Nail : AttackTypes.Spell,
                    DamageDealt = damage, Multiplier = 1, MagnitudeMultiplier = 1, Direction = h.Direction }, h.Kind == HitKind.Talisman);
                applied = finalNail || boss.hp < before;
                if (applied) { AppliedDamageCount++; remainder = scaled - damage; appliedKinds.Add(h.Kind); }
            }
            int after = boss == null ? 0 : Math.Max(0, boss.hp);
            source.ObserveNativeHealth(h, after);
            log("HK_NATIVE_HIT sequence=" + h.Sequence + " kind=" + h.Kind + " nativeDamage=" + h.Damage + " translated=" + damage
                + " before=" + before + " after=" + after + " applied=" + applied + " targetDead=" + (boss == null || boss.isDead || after == 0)
                + " encounter=" + h.Encounter + " entity=" + h.Entity + " activation=" + h.TargetActivation);
            if (blockedPause && !applied && boss != null) {
                HoldCounter(boss, h.Sequence);
                log("HK_PARRY_PAUSE sequence=" + h.Sequence + " encounter=" + h.Encounter + " nativeFreeze=1 frame=" + Time.frameCount);
            }
            if (nativeKilled) log("HK_NATIVE_TARGET_KILLED sequence=" + h.Sequence + " entity=" + h.Entity + " kind=" + h.Kind + " nativeDie=True");
            return new HitAck { Session = h.Session, Sequence = h.Sequence, Disposition = applied ? HitDisposition.Applied : HitDisposition.Refused, Health = after, ParryPause = blockedPause && !applied, TargetKilled = applied && nativeKilled && after == 0 };
        });
    }
    public void Dispose() { foreach (var hold in counterHolds) if (hold != null) hold.Release(); counterHolds.Clear(); parryHarmony.UnpatchSelf(); if (current == this) current = null; if (hitSource != null) UnityEngine.Object.Destroy(hitSource); if (strikeSink != null) UnityEngine.Object.Destroy(strikeSink); }
}
