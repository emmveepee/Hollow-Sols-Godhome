using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using YiGodhome.Bridge;
using YiGodhome.Geometry;

namespace YiGodhome;

// First validate the initialized target receiver graph against host geometry.
// This diagnostic observes native damage; it does not change host health.
internal sealed class NativeTargetProbe : IDisposable
{
    private sealed class Shape
    {
        public readonly Collider2D Collider;
        public readonly Vector2 Size, Offset;
        public readonly Vector3 LocalPosition;
        public readonly Vector2[][] Paths;
        private Vector2 lastSize;
        private Vector3 lastScale;
        public Shape(Collider2D c)
        {
            Collider = c; Size = c is BoxCollider2D b ? b.size : default; Offset = c.offset; LocalPosition = c.transform.localPosition;
            Paths = c is PolygonCollider2D p ? Enumerable.Range(0, p.pathCount).Select(p.GetPath).ToArray() : Array.Empty<Vector2[]>();
        }
        public void Place(Vector2 center, Vector2 size, bool enabled)
        {
            var c = Collider; var scale = c.transform.lossyScale;
            c.transform.position = new Vector3(center.x, center.y, c.transform.position.z); c.offset = Vector2.zero;
            if (lastSize != size || lastScale != scale)
            {
                if (c is BoxCollider2D box) box.size = new Vector2(size.x / Math.Abs(scale.x), size.y / Math.Abs(scale.y));
                else if (c is PolygonCollider2D polygon)
                {
                    Vector2 Local(float x, float y) => c.transform.InverseTransformPoint(new Vector3(center.x + x, center.y + y, c.transform.position.z));
                    Vector2 h = size * 0.5f;
                    polygon.pathCount = 1; polygon.SetPath(0, new[] { Local(-h.x, -h.y), Local(h.x, -h.y), Local(h.x, h.y), Local(-h.x, h.y) });
                }
                lastSize = size; lastScale = scale;
            }
            c.enabled = enabled;
        }
        public void Restore()
        {
            if (Collider == null) return;
            Collider.transform.localPosition = LocalPosition; Collider.offset = Offset;
            if (Collider is BoxCollider2D b) b.size = Size;
            if (Collider is PolygonCollider2D p) { p.pathCount = Paths.Length; for (int i = 0; i < Paths.Length; i++) p.SetPath(i, Paths[i]); }
            Collider.enabled = false; // Retired projected bodies remain quarantined until native reset/rebind.
        }
    }
    private static NativeTargetProbe? active;
    private static readonly Dictionary<MonsterBase, NativeTargetProbe> targets = new Dictionary<MonsterBase, NativeTargetProbe>();
    private static readonly Dictionary<EffectReceiver,(NativeTargetProbe Target,bool Attachment,bool Explosion)> talismanReceivers=new();
    private int talismanEventRefusals;
    private void BindTalismanReceiver(EffectReceiver receiver,bool attachment,bool explosion) {
        if(receiver==null)return;
        if(talismanReceivers.TryGetValue(receiver,out var prior)) {
            if(prior.Target!=this)throw new InvalidOperationException("Native talisman event has competing target owners.");
            attachment|=prior.Attachment;explosion|=prior.Explosion;
        }
        talismanReceivers[receiver]=(this,attachment,explosion);
    }
    private static bool AdmitTalismanEvent(EffectReceiver __instance) {
        if(!talismanReceivers.TryGetValue(__instance,out var binding))return true;
        var target=binding.Target;var frame=target.current;
        bool live=!target.disposed && frame!=null && frame.Health>0;
        if(TalismanShieldPolicy.AllowsEvent(live,target.available,target.talismanShielded,binding.Attachment,binding.Explosion))return true;
        if(target.talismanEventRefusals++<64)
            target.log("NS_TALISMAN_EVENT_REFUSED entity="+frame?.Entity+" shielded="+target.talismanShielded+" attachment="+binding.Attachment
                +" explosion="+binding.Explosion+" live="+live+" beforeNativeDeposit=True frame="+Time.frameCount);
        return false;
    }
    internal static MonsterBase? CounterOwner(int entity) {
        if(entity==0)return null;
        foreach(var pair in targets)
            if(pair.Key!=null && pair.Value.current?.Entity==entity && pair.Value.current.Health>0)
                return pair.Key;
        return null;
    }
    private readonly bool secondary;
    private NativeSummonBridge? summons;
    private NativeHomingTarget? homing;
    public IEnumerable<Renderer> SummonRenderers => summons?.Renderers ?? Array.Empty<Renderer>();
    private readonly Harmony harmony = new Harmony("local.yigodhome.target.probe");
    private readonly MonsterBase owner;
    private readonly GeometryLandingProbe geometry;
    private readonly Action<string> log;
    private readonly Shape[] boxes;
    private readonly HashSet<Collider2D> kickBoxes;
    private readonly MonsterDecreasePostureReceiver[] groups;
    private readonly bool[] aligns;
    private readonly List<(FxPlayer Effect, Transform Constraint, Vector3 Position, GameObject Anchor)> fooAnchors = new List<(FxPlayer, Transform, Vector3, GameObject)>();
    private int identity;
    private TargetIdentity? leaseIdentity;
    private float nextSample;
    private bool available, disposed, talismanShielded;
    private ContactFrame? current;
    internal void CheckNativeBirthMelee() {
        if(available && current!=null && current.Health>0)geometry.CheckNativeBirthMelee(boxes.Select(b=>b.Collider).ToArray(),current.Entity);
    }
    private readonly Queue<HitOutcome> outcomes = new Queue<HitOutcome>();
    private readonly Dictionary<HitOutcome, float> consumedInjury = new Dictionary<HitOutcome, float>();
    private readonly InjurySettlement injurySettlement = new InjurySettlement();
    private readonly KillRewardSettlement killRewards = new();
    private readonly HashSet<FooDeposit> registeredDeposits = new();
    private static void ExpiredDeposit(FooDeposit __instance)
    {
        foreach(var target in targets.Values)if(target.registeredDeposits.Contains(__instance))target.PruneExpiredDeposits();
    }
    private void PruneExpiredDeposits()
    {
        foreach(var deposit in registeredDeposits.Where(d=>d==null || !d.HasFoo()).ToArray()) {
            var attachment=deposit==null?null:deposit.bindingFooAttachable;
            if(attachment!=null && attachment.GetComponentInParent<MonsterBase>()!=owner){registeredDeposits.Remove(deposit!);continue;}
            if(attachment!=null && attachment.bindingFoo==deposit)attachment.OnExpired();
            if(FooManager.IsAvailable())FooManager.Instance.FooRemoveFromList(deposit);
            registeredDeposits.Remove(deposit!);
            log("NS_FOO_EXPIRED_UNREGISTERED ownedReceiver=True liveDepositsPreserved=True frame="+Time.frameCount);
        }
    }
    private static void RegisteredDeposit(FooDeposit __0) {
        if (__0 == null || __0.bindingFooAttachable == null) return;
        var monster = __0.bindingFooAttachable.GetComponentInParent<MonsterBase>();
        if (monster == null || !targets.TryGetValue(monster, out var target)) return;
        foreach (var old in target.registeredDeposits.Where(d => d == null || !d.HasFoo()).ToArray()) {
            FooManager.Instance.FooRemoveFromList(old); target.registeredDeposits.Remove(old);
        }
        if (target.registeredDeposits.Count >= 128) throw new InvalidOperationException("Native deposit ownership budget exceeded.");
        target.registeredDeposits.Add(__0);
        target.log("NS_FOO_REGISTERED entity="+target.current?.Entity+" nativeCount="+FooManager.Instance.GetCurrentFooHitCount+" frame="+Time.frameCount);
    }
    private static void AutoExplosion() {
        var target=active;
        if(target==null)return;
        target.log("NS_FOO_AUTO_EXPLODE entity="+target.current?.Entity+" nativeCount="+FooManager.Instance.GetCurrentFooHitCount+" ownedLive="+target.registeredDeposits.Count(d=>d!=null && d.HasFoo())+" frame="+Time.frameCount);
    }
    private static bool settlingKillReward;
    private static readonly Harmony rewardHarmony = new("local.yigodhome.confirmed-kill-rewards");
    private static bool rewardPatched;
    // The native callback's gold reward concerns campaign economy, outside
    // Godhome. Keep its native combat healing/Chi branches without that write.
    private static bool RewardGold() => !settlingKillReward;
    private long kickedAttack;
    private readonly WeaknessActivator weakness;
    private readonly bool weaknessWasActive;
    private static int acceptedKickFrame = -1;
    private static Guid acceptedKickEncounter;
    private static void BeforeWeakness(Player __instance, out bool __state) => __state = __instance.kicked;
    private static void AfterWeakness(Player __instance, DownSlashParameter __0, bool __state)
    {
        var a = targets.Values.FirstOrDefault(t => t.current?.Kickable == true && __0 == t.weakness.DownSlashParameter); var f = a?.current;
        if (a == null || !a.emitOutcomes || f?.Kickable != true || f.AttackId == a.kickedAttack
            || !__instance.kicked || __0 != a.weakness.DownSlashParameter) return;
        bool companion = __state && acceptedKickFrame == Time.frameCount && acceptedKickEncounter == f.Encounter
            && EncounterProfile.ForScene(a.geometry.SourceScene) == EncounterProfile.Sisters;
        if(__state && !companion) return;
        if(!__state) {acceptedKickFrame=Time.frameCount;acceptedKickEncounter=f.Encounter;}
        a.kickedAttack = f.AttackId;
        NativeHostContact.EndNativeAttack(f);
        a.outcomes.Enqueue(new HitOutcome { Session = f.Session, Encounter = f.Encounter, GeometryFrame = f.GeometryFrame,
            HostTick = f.CaptureTick, NativeFrame = Time.frameCount, Entity = f.Entity, TargetActivation = f.TargetGeneration, Dealer = __instance.GetInstanceID(), Kind = HitKind.TaiChiKick, AttackId = f.AttackId });
        a.log("NS_TAICHI_NATIVE attack=" + f.AttackId + " weakness=True bounce=" + (__instance.Velocity.y > 0) + " frame=" + Time.frameCount);
        if(companion) a.log("NS_SISTERS_SHARED_KICK encounter="+f.Encounter+" entity="+f.Entity+" action="+f.AttackId+" nativeCallback=True extraPlayerBounce=False frame="+Time.frameCount);
    }
    private static bool KeepHostState(MonsterBase __instance, MonsterBase.States __0) => !targets.TryGetValue(__instance, out var t) || t.current?.Kickable != true || __0 != t.weakness.breakState;
    private static bool AdmitReceiver(EffectReceiver __instance, EffectHitData __0, ref bool __result)
    {
        var owner = __instance.GetComponentInParent<MonsterBase>();
        if (owner == null || !targets.TryGetValue(owner, out var target)) return true;
        if (target.current != null && target.current.Health > 0
            && TalismanShieldPolicy.Allows(target.talismanShielded,__0.dealer.detailType==EffectDetailType.FooHit,__0.dealer.detailType==EffectDetailType.FooExplode)
            && (target.available || __0.dealer.detailType == EffectDetailType.FooExplode)) {
            if (__0.dealer.detailType == EffectDetailType.FooHit) {
                // Native attachment rejects a non-null binding, even when its
                // deposit is already empty. FooExpired then returns early and
                // never clears that reference. Repair only this owned receiver;
                // a deposit reassigned to another attachment stays untouched.
                foreach (var foo in target.groups.Select(g => g.fooAttachable).Where(f => f != null).Distinct()) {
                    var deposit = foo.bindingFoo;
                    if (deposit == null || deposit.bindingFooAttachable == foo && deposit.HasFoo()) continue;
                    bool expired = !deposit.HasFoo();
                    bool sameOwner = deposit.bindingFooAttachable == foo;
                    foo.OnExpired();
                    if (expired && sameOwner && FooManager.IsAvailable()) FooManager.Instance.FooRemoveFromList(deposit);
                    target.log("NS_FOO_STALE_BINDING_CLEARED entity=" + target.current.Entity + " activation=" + target.current.TargetGeneration
                        + " expired=" + expired + " reassigned=" + !sameOwner + " otherLiveDepositUnchanged=True frame=" + Time.frameCount);
                }
            }
            return true;
        }
        // Refuse before native hit-enter listeners and hit presentation. Gating
        // only posture damage is too late to prevent corpse smack effects.
        __0.EffectMuted = true; __0.SetFXMuted(true); __result = false;
        return false;
    }
    public void Submitted(long sequence, HitOutcome outcome)
    {
        if (!secondary) killRewards.Track(sequence, outcome);
        summons?.Submitted(sequence, outcome);
        if (!consumedInjury.TryGetValue(outcome, out float consumed)) return;
        consumedInjury.Remove(outcome); injurySettlement.Track(sequence, consumed);
    }
    public void Acknowledged(HitAck ack)
    {
        if (!secondary && killRewards.Resolve(ack) is HitOutcome reward && Player.i != null) {
            var player = Player.i;
            float healthBefore = player.health.currentValue, chiBefore = player.chiContainer.Value;
            bool healActive = player.mainAbilities.FooExplodeKillGainHealth.IsActivated;
            bool chiActive = player.mainAbilities.FooExplodeKillGainAmmo.IsActivated;
            settlingKillReward = true;
            try { player.FooExplodeKillMonster(player.Center); }
            finally { settlingKillReward = false; }
            log("NS_TALISMAN_KILL_REWARD sequence=" + ack.Sequence + " encounter=" + reward.Encounter + " entity=" + reward.Entity
                + " activation=" + reward.TargetActivation + " nativeCallback=True receiverRetirementIndependent=True campaignGold=False"
                + " healActive=" + healActive + " chiActive=" + chiActive + " healthBefore=" + healthBefore + " healthAfter=" + player.health.currentValue
                + " chiBefore=" + chiBefore + " chiAfter=" + player.chiContainer.Value);
        }
        bool summoned = summons?.Acknowledged(ack) == true;
        if (disposed) return; // A disposed observer only settles retired identity metadata.
        if (!summoned) NativeHitPause.Accept(ack, leaseIdentity?.Encounter ?? Guid.Empty, owner, log);
        float restore = injurySettlement.Resolve(ack.Sequence, ack.Disposition);
        if (restore <= 0 || disposed || ack.Health <= 0) return;
        var posture = owner.postureSystem;
        float before = posture.InternalInjury;
        posture.CurrentInternalInjury = before + restore;
        CapInjury();
        log("NS_INJURY_RESTORED sequence=" + ack.Sequence + " " + leaseIdentity + " reason=" + ack.Disposition + " before=" + before
            + " consumed=" + restore + " after=" + posture.InternalInjury + " hostDamageRetried=False");
    }
    private string? failure;
    private readonly bool emitOutcomes;
    private readonly CombatCheatFixture cheat;
    private readonly float originalPostureOverride;
    private readonly NativeBossHealth? bossHealth;
    public UIBossHP? BossHealthView => bossHealth?.View;
    public bool TryGetOutcome(out HitOutcome outcome)
    { if (outcomes.Count != 0) { outcome = outcomes.Dequeue(); return true; } if (summons != null) return summons.TryGetOutcome(out outcome); outcome = null!; return false; }
    public NativeTargetProbe(MonsterBase owner, GeometryLandingProbe geometry, Action<string> log, bool emitOutcomes = false, bool secondary = false)
    {
        if (!secondary && active != null) throw new InvalidOperationException("Target probe already installed.");
        this.owner = owner; this.geometry = geometry; this.log = log; this.emitOutcomes = emitOutcomes; this.secondary = secondary;
        if (!secondary && !rewardPatched) {
            rewardHarmony.Patch(AccessTools.Method(typeof(PlayerGamePlayData), "AddGold", new[] { typeof(int), typeof(GoldSourceTag) }),
                prefix: new HarmonyMethod(typeof(NativeTargetProbe), nameof(RewardGold)));
            rewardPatched = true;
        }
        originalPostureOverride = owner.postureSystem.OverridePostureValue;
        weakness = owner.monsterCore.weaknessActivator;
        if (weakness == null || weakness.DownSlashParameter == null) throw new InvalidOperationException("Native weakness graph unavailable.");
        if (targets.Values.Any(t => t.weakness.DownSlashParameter == weakness.DownSlashParameter)) throw new InvalidOperationException("Tai-Chi receiver parameter is shared across target owners.");
        weaknessWasActive = weakness.gameObject.activeSelf;
        cheat = new CombatCheatFixture(log);
        var coreField = typeof(MonsterDecreasePostureReceiver).GetField("monsterCore", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
        groups = owner.GetComponentsInChildren<MonsterDecreasePostureReceiver>(true)
            .Where(r => ReferenceEquals(coreField.GetValue(r), owner.monsterCore) && r.gameObject.activeInHierarchy).ToArray();
        if (groups.Length == 0) throw new InvalidOperationException("Native monster receivers unavailable.");
        aligns = groups.Select(g => g.ignoreAlign).ToArray();
        var field = typeof(EffectReceiver).GetField("myCollider2D", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
        foreach (var g in groups)
            foreach (var r in new[] { g.postureDecreaseReceiver, g.fooEffectReceiver, g.fooExplodeReceiver, g.jumpKickReceiver }.Where(r => r != null))
            {
                var c = (Collider2D)field.GetValue(r);
                log("NS_TARGET_BINDING group=" + g.name + " receiver=" + r.name + " active=" + r.isActiveAndEnabled + " collider=" + (c == null ? "null" : c.name + ":" + c.GetType().Name + ":" + c.GetInstanceID()));
            }
        var colliders = groups.SelectMany(g => new[] { g.postureDecreaseReceiver, g.fooEffectReceiver, g.fooExplodeReceiver, g.jumpKickReceiver })
            .Where(r => r != null).Select(r => (Collider2D)field.GetValue(r)).Distinct().ToArray();
        if (colliders.Length == 0 || colliders.Any(c => !(c is BoxCollider2D) && !(c is PolygonCollider2D))) throw new InvalidOperationException("Unsupported native target receiver shape.");
        boxes = colliders.Select(c => new Shape(c)).ToArray();
        kickBoxes = new HashSet<Collider2D>(groups.Where(g => g.jumpKickReceiver != null).Select(g => (Collider2D)field.GetValue(g.jumpKickReceiver)));
        if (!secondary) active = this; targets.Add(owner, this);
        try
        {
            foreach(var group in groups) {
                BindTalismanReceiver(group.fooEffectReceiver,true,false);
                BindTalismanReceiver(group.fooExplodeReceiver,false,true);
            }
            homing = new NativeHomingTarget(owner, log);
            if (!secondary) { if (EncounterProfile.ForScene(geometry.SourceScene)?.Roster != true) bossHealth = new NativeBossHealth(owner, log); summons = new NativeSummonBridge(owner, geometry, log); }
            foreach (var effect in owner.GetComponentsInChildren<FooAttachable>(true).Select(f => f.fooFXPlayer).Where(f => f != null).Distinct()) {
                var anchor = new GameObject("YiGodhome Host Talisman Anchor");
                anchor.transform.SetParent(owner.transform, false);
                fooAnchors.Add((effect, effect.setPositionConstraint, effect.transform.localPosition, anchor));
                effect.setPositionConstraint = anchor.transform;
            }
            foreach (var g in groups) g.ignoreAlign = true;
            if (!secondary) harmony.Patch(AccessTools.Method(typeof(Player), "WeaknessHit"), prefix: new HarmonyMethod(typeof(NativeTargetProbe), nameof(BeforeWeakness)), postfix: new HarmonyMethod(typeof(NativeTargetProbe), nameof(AfterWeakness)));
            if (!secondary) harmony.Patch(AccessTools.Method(typeof(MonsterBase), "ChangeStateIfValid", new[] { typeof(MonsterBase.States) }), prefix: new HarmonyMethod(typeof(NativeTargetProbe), nameof(KeepHostState)));
            foreach (var b in boxes) { b.Collider.enabled = false; log("NS_TARGET_RECEIVER name=" + b.Collider.name + " id=" + b.Collider.GetInstanceID()); }
            if (!secondary) harmony.Patch(AccessTools.Method(typeof(MonsterBase), "HittedByPlayerDecreasePosture"), prefix: new HarmonyMethod(typeof(NativeTargetProbe), nameof(Before)), postfix: new HarmonyMethod(typeof(NativeTargetProbe), nameof(After)));
            if (!secondary) harmony.Patch(AccessTools.Method(typeof(EffectReceiver), "OnBeforeHitEnter"), prefix: new HarmonyMethod(typeof(NativeTargetProbe), nameof(AdmitReceiver)));
            if (!secondary) foreach(var method in new[]{"EffectHitEnter","OnHitEnter"})
                harmony.Patch(AccessTools.Method(typeof(EffectReceiver),method),prefix:new HarmonyMethod(typeof(NativeTargetProbe),nameof(AdmitTalismanEvent)));
            if (!secondary) harmony.Patch(AccessTools.Method(typeof(MonsterBase), "InternalInjuryVirtual"), prefix: new HarmonyMethod(typeof(NativeTargetProbe), nameof(BeforeInjury)), postfix: new HarmonyMethod(typeof(NativeTargetProbe), nameof(InjuryAfter)));
            if (!secondary) harmony.Patch(AccessTools.Method(typeof(FooManager), "FooDeposit"), postfix: new HarmonyMethod(typeof(NativeTargetProbe), nameof(RegisteredDeposit)));
            if (!secondary) harmony.Patch(AccessTools.Method(typeof(FooDeposit), "FooExpired"), postfix: new HarmonyMethod(typeof(NativeTargetProbe), nameof(ExpiredDeposit)));
            if (!secondary) harmony.Patch(AccessTools.Method(typeof(FooManager), "AutoExplode"), prefix: new HarmonyMethod(typeof(NativeTargetProbe), nameof(AutoExplosion)));
        }
        catch { Dispose(); throw; }
    }
    private readonly struct HitSample {
        internal readonly Vector2 Previous; internal readonly bool Arrow, Permitted, DownStrike; internal readonly string Weapon;
        internal HitSample(Vector2 previous,EffectDealer dealer,bool permitted){Previous=previous;Permitted=permitted;DownStrike=Player.i.CurrentStateType==PlayerStateType.JumpKick;Arrow=NativePlayerArrow.Owner(dealer,Player.i)!=null;Weapon=Arrow?NativePlayerArrow.WeaponName(dealer,Player.i):"unresolved";}
    }
    private static bool Before(MonsterBase __instance,EffectHitData __0,out HitSample __state) {
        __state=default;
        if(!targets.TryGetValue(__instance,out var target))return true;
        bool permitted=target.current!=null && target.current.Health>0
            && TalismanShieldPolicy.Allows(target.talismanShielded,__0.dealer.detailType==EffectDetailType.FooHit,__0.dealer.detailType==EffectDetailType.FooExplode)
            && (target.available || __0.dealer.detailType==EffectDetailType.FooExplode);
        __state=new HitSample(new Vector2(__instance.postureSystem.PostureValue,__instance.postureSystem.InternalInjury),__0.dealer,permitted);
        return permitted; // Reject stale/dead receiver hits before native smack/effects.
    }
    private static void BeforeInjury(MonsterBase __instance, out Vector2 __state)
    {
        __state = default;
        if (targets.ContainsKey(__instance)) __state = new Vector2(__instance.postureSystem.PostureValue, __instance.postureSystem.InternalInjury);
    }
    private static void After(MonsterBase __instance, EffectHitData __0, HitSample __state)
    {
        if (!__state.Permitted || !targets.TryGetValue(__instance, out var a)) return;
        a.CapInjury();
        var p = __instance.postureSystem;
        if (__state.Previous.x == p.PostureValue && __state.Previous.y == p.InternalInjury) return;
        a.log("NS_TARGET_NATIVE_HIT frame=" + Time.frameCount + " available=" + a.available + " dealer=" + __0.dealer.GetInstanceID()
            + " kind=" + __0.dealer.detailType + " before=" + __state.Previous.x + " after=" + p.PostureValue
            + " injuryBefore=" + __state.Previous.y + " injuryAfter=" + p.InternalInjury + " hostDamage=False");
        float damage = ResolvedDamage.Loss(__state.Previous.x, __state.Previous.y, p.PostureValue, p.InternalInjury);
        bool attachedHidden = __0.dealer.detailType == EffectDetailType.FooExplode && !a.talismanShielded
            && a.current != null && a.current.Health > 0;
        if (!a.emitOutcomes || !a.available && !attachedHidden || a.current == null || damage <= 0) return;
        HitKind kind;
        switch (__0.dealer.detailType)
        {
            case EffectDetailType.PlayerNormalAttack:
            case EffectDetailType.PlayerThirdAttack:
            case EffectDetailType.CounterReflect:
            case EffectDetailType.PlayerChargeAttack: kind = HitKind.Melee; break;
            case EffectDetailType.PlayerArrow:
            case EffectDetailType.PlayerAirArrow: kind = HitKind.Bow; break;
            case EffectDetailType.Undefined when __state.Arrow: kind = HitKind.Bow; break;
            case EffectDetailType.FooExplode: kind = HitKind.Talisman; break;
            default: a.failure = "Unsupported native outgoing attack category: " + __0.dealer.detailType; return;
        }
        if (a.outcomes.Count >= 64) { a.failure = "Native outcome queue overflow."; return; }
        var f = a.current;
        float direction=Player.i.transform.position.x<=a.owner.transform.position.x?0:180;
        if(kind==HitKind.Melee && EncounterProfile.ForScene(a.geometry.SourceScene)?.Family==EncounterProfile.Uumuu) {
            var nativeDirection=__0.dealer.GetDirection();
            direction=__state.DownStrike ? 270 : NativeMeleeDirection.Angle(nativeDirection.x,nativeDirection.y,direction);
            if(__state.DownStrike || nativeDirection.y<-.5f)a.log("NS_UUMUU_DOWN_STRIKE entity="+f.Entity+" dealer="+__0.dealer.name+" direction="+direction+" nativeFrame="+Time.frameCount);
        }
        var outcome = new HitOutcome { Session = f.Session, Encounter = f.Encounter, GeometryFrame = f.GeometryFrame, HostTick = f.CaptureTick,
            NativeFrame = Time.frameCount, Entity = f.Entity, TargetActivation = f.TargetGeneration, Dealer = __0.dealer.GetInstanceID(), Kind = kind, Damage = damage,
            Direction = direction };
        a.outcomes.Enqueue(outcome);
        if(kind==HitKind.Bow) a.log("NS_NATIVE_ARROW_OUTCOME encounter="+f.Encounter+" entity="+f.Entity+" activation="+f.TargetGeneration+" dealer="+__0.dealer.GetInstanceID()+" weapon="+__state.Weapon+" damage="+damage+" nativeFrame="+Time.frameCount);
        if (attachedHidden && !a.available) a.log("NS_ATTACHED_HIDDEN_TALISMAN encounter=" + f.Encounter + " entity=" + f.Entity + " activation=" + f.TargetGeneration + " damage=" + damage + " frame=" + Time.frameCount);
        if (kind == HitKind.Talisman) a.consumedInjury.Add(outcome, Math.Max(0, __state.Previous.y - p.InternalInjury));
    }
    private static void InjuryAfter(MonsterBase __instance, Vector2 __state)
    {
        if (!targets.TryGetValue(__instance, out var a)) return;
        a.CapInjury();
        var p = __instance.postureSystem;
        a.log("NS_TARGET_INTERNAL_INJURY entity=" + a.current?.Entity + " before=" + __state.y + " after=" + p.InternalInjury
            + " realized=" + ResolvedDamage.Loss(__state.x, __state.y, p.PostureValue, p.InternalInjury) + " native=True");
    }
    private void CapInjury() {
        if (current == null) return;
        var p = owner.postureSystem; float excess = p.InternalInjury - HealthNotches.InjuryLimit(current.Health, current.Notches);
        if (excess <= 0) return;
        p.CurrentInternalInjury -= excess; p.PostureValue += excess;
        log("NS_PHASE_INJURY_DAM entity=" + current.Entity + " activation=" + current.TargetGeneration + " health=" + current.Health + " injury=" + p.InternalInjury + " excess=" + excess + " frame=" + Time.frameCount);
    }
    public void Update(ContactFrame? frame,bool blockTalisman=false)
    {
        PruneExpiredDeposits();
        if (frame != null) {
            var incoming = new TargetIdentity(frame);
            if (leaseIdentity.HasValue && !leaseIdentity.Value.Equals(incoming))
                throw new InvalidOperationException("Target replacement requires a fresh receiver lease: " + incoming);
            if (!leaseIdentity.HasValue) {
                // A canvas sequence preserves Yi, but the private receiver is a
                // new target lease. Reuse the verified minion reset operations.
                foreach (var foo in owner.GetComponentsInChildren<FooAttachable>(true)) if (foo.bindingFoo != null) foo.bindingFoo.FooExpired();
                owner.postureSystem.CurrentInternalInjury = 0;
                owner.postureSystem.PostureValue = owner.postureSystem.MaxPostureValue;
                if (owner.postureSystem.InternalInjury != 0 || owner.GetComponentsInChildren<FooAttachable>(true).Any(f => f.bindingFoo != null))
                    throw new InvalidOperationException("Fresh primary receiver retained target state.");
                leaseIdentity = incoming; log("NS_TARGET_LEASE " + incoming + " maxHealth=" + frame.MaxHealth + " injury=0 attachments=0");
                log("NS_TARGET_HEALTH_POLICY encounter="+frame.Encounter+" entity="+frame.Entity+" activation="+frame.TargetGeneration
                    +" hostHealth="+frame.Health+" maximum="+frame.MaxHealth+" notchCount="+frame.Notches.Length
                    +" injuryLimit="+HealthNotches.InjuryLimit(frame.Health,frame.Notches)
                    +" fullRemainingLimit="+HealthNotches.InjuryLimit(frame.Health,Array.Empty<int>())+" nativeInjury="+owner.postureSystem.InternalInjury);
            }
        }
        summons?.Update(frame);
        if (failure != null) throw new InvalidOperationException(failure);
        if (owner.enabled || !owner.fsm.isPaused || owner.animator.enabled || owner.IsDead()) throw new InvalidOperationException("Native projected target left paused contract.");
        if (frame != null) {
            var reserve = owner.postureSystem;
            float capacity = Math.Max(reserve.MaxPostureValue, (float)Math.Ceiling(frame.MaxHealth / BossHealthProjection.NativeToHost) + 2000);
            float injury = reserve.InternalInjury;
            float injuryLimit = HealthNotches.InjuryLimit(frame.Health, frame.Notches);
            if (injury > injuryLimit) {
                // A target cannot contain more unrealized damage than remaining
                // host HP. Return only the excess to the private solid reserve.
                float excess = injury - injuryLimit;
                reserve.CurrentInternalInjury = injuryLimit;
                reserve.PostureValue += excess;
                injury = reserve.InternalInjury;
                log("NS_TARGET_INJURY_CAP hostHealth=" + frame.Health + " injury=" + injury + " excess=" + excess);
            }
            if (capacity > reserve.MaxPostureValue || reserve.RemainTotal < 2000) {
                reserve.OverridePostureValue = capacity;
                reserve.PostureValue = capacity - injury;
                if (reserve.InternalInjury != injury || reserve.RemainTotal < 1000) throw new InvalidOperationException("Native reserve rebase changed injury or failed capacity.");
                log("NS_TARGET_RESERVE capacity=" + capacity + " injuryBefore=" + injury + " injuryAfter=" + reserve.InternalInjury + " hostHealth=" + frame.Health);
            }
        }
        talismanShielded=blockTalisman;
        available = frame != null && frame.HurtActive && frame.Health > 0;
        current = frame;
        bool kickable = frame?.Kickable == true && frame.Health > 0;
        if (weakness.gameObject.activeSelf != kickable) weakness.gameObject.SetActive(kickable);
        bossHealth?.Update(frame, geometry.SourceScene);
        if (frame == null) { foreach (var box in boxes) box.Collider.enabled = false; homing?.Update(null, Vector2.zero, Vector2.one); return; }
        if (frame.GeometryFrame != geometry.SourceFrame) throw new InvalidOperationException("Target geometry generation changed.");
        // A native body can expose another collider without replacing its health owner.
        // The validated target lease above owns health/injury; collider identity owns geometry only.
        if (identity != 0 && identity != frame.HurtCollider) log("NS_TARGET_GEOMETRY_REBOUND entity=" + frame.Entity + " activation=" + frame.TargetGeneration + " previous=" + identity + " collider=" + frame.HurtCollider + " injury=" + owner.postureSystem.InternalInjury + " frame=" + Time.frameCount);
        identity = frame.HurtCollider;
        if (!secondary) cheat.MaintainInvulnerability(frame);
        if (!secondary && !cheat.PreserveWallMovement() && !cheat.PreserveArmorMovement(frame) && !cheat.PrepareUumuu(frame,geometry.Map) && !cheat.PrepareXero(frame,geometry.Map,geometry.SourceFloor) && !cheat.PrepareRings(frame, geometry.Map, geometry.SourceFloor) && !cheat.PrepareCrimson(frame, geometry.Map, geometry.SourceFloor) && !cheat.PrepareShield(frame, geometry.Map, geometry.SourceFloor) && !cheat.PrepareSoulProjectiles(summons?.CheatTarget ?? frame,geometry.Map,geometry.SourceFloor) && !cheat.PrepareSoul(frame, geometry.Map, geometry.SourceFloor) && !cheat.PrepareMace(frame, geometry.Map)) cheat.Update(summons?.CheatTarget ?? frame, geometry.Map, geometry.SourceFloor);
        Point p = geometry.Map.ToTarget(new Point(frame.HurtX, frame.HurtY));
        Vector2 center = new Vector2(p.X, p.Y), size = new Vector2(frame.HurtWidth, frame.HurtHeight) * geometry.Map.Scale;
        homing?.Update(frame, center, size);
        foreach (var anchor in fooAnchors) {
            anchor.Anchor.transform.position = new Vector3(center.x, center.y, anchor.Effect.transform.position.z);
            anchor.Effect.transform.position = anchor.Anchor.transform.position;
        }
        foreach (var box in boxes)
        {
            var c = box.Collider; var scale = c.transform.lossyScale;
            if (Math.Abs(Mathf.DeltaAngle(c.transform.eulerAngles.z, 0)) > 0.01f || Math.Abs(scale.x) < 0.01f || Math.Abs(scale.y) < 0.01f)
                throw new InvalidOperationException("Unsupported native receiver transform.");
            box.Place(center, size, available || kickable && kickBoxes.Contains(c));
        }
        Physics2D.SyncTransforms();
        foreach (var box in boxes)
            if (available && (Vector2.Distance(box.Collider.bounds.center, center) > 0.1f || Vector2.Distance(box.Collider.bounds.size, size) > 0.1f))
                throw new InvalidOperationException("Native target bounds differ from host health collider.");
        if (Time.realtimeSinceStartup >= nextSample)
        {
            nextSample = Time.realtimeSinceStartup + 1;
            log("NS_TARGET_SAMPLE sequence=" + frame.Sequence + " collider=" + identity + " available=" + available + " center=" + center + " size=" + size + " posture=" + owner.postureSystem.PostureValue);
        }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        foreach(var receiver in talismanReceivers.Where(p=>p.Value.Target==this).Select(p=>p.Key).ToArray())talismanReceivers.Remove(receiver);
        // FooManager's cast gate is list.Count, not whether its deposits are
        // alive. Expire/unregister this exact receiver's deposits before the
        // pooled body/anchor is retired, without clearing another live body.
        if (owner != null) foreach (var foo in owner.GetComponentsInChildren<FooAttachable>(true))
            if (foo.bindingFoo != null) registeredDeposits.Add(foo.bindingFoo);
        int expired = 0;
        foreach (var deposit in registeredDeposits.ToArray()) {
            if (deposit != null && deposit.bindingFooAttachable != null
                && deposit.bindingFooAttachable.GetComponentInParent<MonsterBase>() != owner) continue;
            if (deposit != null) deposit.FooExpired();
            if (FooManager.IsAvailable()) FooManager.Instance.FooRemoveFromList(deposit);
            expired++;
        }
        registeredDeposits.Clear();
        if (expired > 0) log("NS_FOO_RECEIVER_RETIRED expired=" + expired + " globalClear=False otherLiveDepositsPreserved=True");
        if (owner != null) NativeHitPause.Cancel(owner);
        cheat.Dispose();
        summons?.Dispose(); homing?.Dispose(); targets.Remove(owner!); // Retire the reference key even after Unity destroys its native object.
        outcomes.Clear(); consumedInjury.Clear(); current = null;
        if (weakness != null) weakness.gameObject.SetActive(weaknessWasActive);
        bossHealth?.Dispose();
        if (owner != null) owner.postureSystem.OverridePostureValue = originalPostureOverride;
        if (active == this) { active = null; harmony.UnpatchSelf(); }
        for (int i = 0; i < groups.Length; i++) if (groups[i] != null) groups[i].ignoreAlign = aligns[i];
        foreach (var anchor in fooAnchors) {
            if (anchor.Effect != null) { anchor.Effect.setPositionConstraint = anchor.Constraint; anchor.Effect.transform.localPosition = anchor.Position; }
            if (anchor.Anchor != null) {
                // Deposits are pooled native objects. Preserve them for the
                // native reset instead of destroying children with our anchor.
                while (anchor.Anchor.transform.childCount > 0)
                    anchor.Anchor.transform.GetChild(0).SetParent(anchor.Constraint != null ? anchor.Constraint : anchor.Effect != null ? anchor.Effect.transform : null, true);
                UnityEngine.Object.Destroy(anchor.Anchor);
            }
        }
        foreach (var b in boxes) b.Restore();
        if(boxes.Any(b=>b.Collider!=null && b.Collider.enabled))throw new InvalidOperationException("Retired receiver left a live collider.");
        log("NS_TARGET_RETIRED_QUARANTINE colliders="+boxes.Length+" enabled=0 nativeResetRequired=True");
    }
}
