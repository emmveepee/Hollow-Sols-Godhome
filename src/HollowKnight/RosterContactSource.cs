using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using YiGodhome.Bridge;
using YiGodhome.Geometry;
namespace YiGodhome;

// Encounter anchor is not a damage receiver. Each authored actor retains its
// own native health and shared summon receiver lease throughout phase changes.
internal sealed class RosterContactSource : IDisposable
{
    private sealed class Target { public NativeMinionSpawnLease? SpawnLease; public NativeStaggerGeometry? Soul; public GrimmStaggerBody? Grimm; public NativeHealthNotches? Notches; public HealthManager Health = null!; public Collider2D? Shape; public CircleCollider2D? RepelCircle; public bool RepelEvidence; public Renderer? BodyRenderer; public MossBodyGeometry? MossBody; public int Maximum; public long Activation = 1, PoolActivation, ContactAction, ContactGeneration = -1, SuppressedContactGeneration = -1; public HealthManager.DeathEvent? Died; public bool Retired, Seen, HasBounds, SharedPool; public Bounds LastBounds; public float MissingAt = -1; public string Admission = ""; }
    private readonly Dictionary<int, Target> targets = new Dictionary<int, Target>();
    private readonly RetiredTargetReceipts retiredReceipts=new();
    private HutongGames.PlayMaker.FsmBool? nailmasterPhase;
    private readonly BossBarBatch bossBarBatch=new();
    private bool? observedNailmasterPhase;
    private readonly Guid session, encounter;
    private readonly Action<string>? log;
    private readonly HostPooledAttackVolumes attacks;
    private readonly int anchor;
    private float nextPrune;
    private float lastNativeBossDeathAt=-100;
    private bool parasiteSpawnEnded;
    private void EndParasiteSpawns()
    {
        if (!parasiteSpawnEnded) return;
        foreach (var target in targets.Values.ToArray())
            if (target.Health != null && OrangeMinion(target.Health)) target.SpawnLease?.StopNativeSpawn();
    }
    internal bool HidePlayer=>policy.HidePlayer;
    internal bool AwaitingNativeBossCompletion => Time.realtimeSinceStartup-lastNativeBossDeathAt<8
        && !targets.Values.Any(t=>!t.Retired && t.Health!=null && profile.IsBossName(t.Health.name) && !t.Health.isDead && t.Health.hp>0);
    private float nextEligibilityTrace;
    private int eligibilitySamples;
    private int admissionSamples;
    private int warriorSamples;
    private float nextWarriorTrace;
    private float? playerX;
    private readonly HostDamageRegistry targetRegistry;
    private readonly NativeEncounterPools? pools;
    private readonly NativeFsmAttackVolumes? fsmAttacks;
    private readonly NativeShockwaveStrikes? shockwaves;
    private readonly NativeJarAttackPolicy? jars;
    private int retiredMinionSamples;
    private readonly NativeGoopAttackPolicy? goop;
    private readonly long noskJumpGroup;
    internal bool ScriptedSkipCounter=>policy.ScriptedSkipCounter;
    internal bool ScriptedCounterPrecharge=>policy.ScriptedCounterPrecharge;
    private readonly EncounterProfile profile = EncounterProfile.Selected;
    private readonly NativeArmorHead? armorHead;
    private readonly Func<int,long,bool> reflectMeleeDebris;
    private NativeDefeatedHead? defeatedHead;
    private readonly SistersSharedHealth? sharedHealth;
    private readonly bool globalContact=HollowSolsSettings.Contact, trueGlobalContact=HollowSolsSettings.TrueContact;
    private readonly UumuuNativeEvidence? uumuu;
    private readonly RosterAttackPolicy policy;
    private readonly HornetBarbBridge? hornetBarbs;
    private readonly GodTamerMusicEvidence? musicEvidence;
    private int firebatTraceSamples;
    private long envelopeFrame = -1;
    private CombatEnvelope envelope;
    private bool hasEnvelope;
    private int balloonExamined, balloonOmitted;
    private float nextBalloonCost;
    private float balloonCeiling = float.PositiveInfinity;
    private int balloonAbove;
    private int balloonFrames;
    private sealed class ProjectileBirth { internal long Activation; internal int Frame=-1; internal float Age; internal bool Armed; }
    private readonly Dictionary<int,ProjectileBirth> projectileBirths=new();
    private int projectileBirthLogs;
    private long rainFloorGeometry=-1;
    private bool hasRainFloor;
    private float rainFloor;
    private bool OutsideRadianceNail(DamageHero damage,Bounds bounds)=>policy.OutsideRadianceNail(damage,bounds,attacks.NativeActivation(damage));
    private bool OutsideBalloon(DamageHero damage, Bounds bounds)
    {
        if (!hasEnvelope || !damage.name.StartsWith("Flameball Grimmballoon", StringComparison.Ordinal)) return false;
        balloonExamined++;
        bool above = bounds.min.y > balloonCeiling;
        bool outside = bounds.max.x < envelope.Left || bounds.min.x > envelope.Right || bounds.max.y < envelope.Floor || above;
        if (above) balloonAbove++;
        if (outside) balloonOmitted++;
        return outside;
    }
    private MeshRenderer[] defenderRageParts=Array.Empty<MeshRenderer>();
    internal MeshRenderer[] RageParts {
        get {
            if(armorHead!=null)return armorHead.RageParts;
            var original=policy.VisualParts;var dung=shockwaves?.DefenderVisuals;
            if(dung==null || dung.Length==0)return original;
            bool changed=defenderRageParts.Length!=original.Length+dung.Length;
            for(int i=0;!changed && i<original.Length;i++)changed=defenderRageParts[i]!=original[i];
            for(int i=0;!changed && i<dung.Length;i++)changed=defenderRageParts[original.Length+i]!=dung[i];
            if(changed) {
                defenderRageParts=new MeshRenderer[original.Length+dung.Length];
                Array.Copy(original,defenderRageParts,original.Length);Array.Copy(dung,0,defenderRageParts,original.Length,dung.Length);
            }
            return defenderRageParts;
        }
    }
    internal HealthManager[] RageBodies => armorHead != null ? new[] { armorHead.Armor } : policy.VisualBodies;
    internal AttackTraits? AuraTraits(HealthManager body) => armorHead?.Raging==true && body==armorHead.Armor ? AttackTraits.Crimson : policy.VisualTraits(body);
    internal void CopyPresentationBodies(List<HealthManager> result) {
        foreach (var target in targets.Values) {
            if (result.Count == 32) break;
            if (target.Health != null) result.Add(target.Health);
        }
    }
    public RosterContactSource(Guid session, Guid encounter, Action<string>? log)
    {
        this.session = session; this.encounter = encounter; this.log = log;
        reflectMeleeDebris=ReflectMeleeDebris;
        anchor = BossSceneController.Instance.GetInstanceID();
        policy = new RosterAttackPolicy(profile, log);
        noskJumpGroup=profile.Family==EncounterProfile.Nosk?policy.AllocateAction():0;
        if(profile.Family==EncounterProfile.GodTamer && SessionConfiguration.Current.RunFixtures && SessionConfiguration.Current.Has("cheat-probe")) musicEvidence=new GodTamerMusicEvidence(log);
        if (profile.Key == EncounterProfile.Uumuu.Key) uumuu = new UumuuNativeEvidence(log);
        if (profile.ArmorAndHead) armorHead = new NativeArmorHead(profile, encounter, log,policy.AllocateAction);
        if (HollowSolsSettings.SharedHealth && (profile.Family == EncounterProfile.Sisters || profile.Family == EncounterProfile.Mantis)) sharedHealth = new SistersSharedHealth(profile.Scene, encounter, log);
        log?.Invoke("HK_ENCOUNTER_CONFIGURATION shared="+(sharedHealth!=null)+" contact="+globalContact+" trueContact="+trueGlobalContact+" encounter="+encounter);
        if (profile.OwnNativePools) {
            pools = new NativeEncounterPools(profile.Scene, log);
            shockwaves=new NativeShockwaveStrikes(pools,policy.AllocateAction,log,policy.SpawnStrike,profile.Family==EncounterProfile.Gorb);
            if(profile.Family==EncounterProfile.Collector)jars=new NativeJarAttackPolicy(pools,policy.AllocateAction,log);
            if(profile.Family==EncounterProfile.Nosk || profile.Family==EncounterProfile.WingedNosk || profile.Family==EncounterProfile.GodTamer)
                goop=new NativeGoopAttackPolicy(policy.AllocateAction,log);
            log?.Invoke("HK_ROSTER_POOL_SCOPE scene=" + profile.Scene + " prefabs=" + pools.PrefabCount + " nativeMapping=True");
        }
        attacks = new HostPooledAttackVolumes(profile.Scene, ClassifyAttack, log, collapseEmptyGeometry: true, nativePools: profile.OwnNativePools, visibleBodies: profile.NativeHitAuthority,
            globalOwnership: profile.OwnNativePools ? d => pools?.Owns(d.gameObject) == true : null);
        if (profile.Family == EncounterProfile.NightmareGrimm || profile.Family == EncounterProfile.Grimm) HostDamageRegistry.PoolSpawned += TraceFirebatSpawn;
        try {
            if (profile.Family == EncounterProfile.NightmareGrimm) fsmAttacks = new NativeFsmAttackVolumes(pools!, policy.AllocateAction, log);
            targetRegistry = new HostDamageRegistry(profile.Scene, d => pools?.Owns(d.gameObject) == true, registered: d => RegisterTarget(d.GetComponentInParent<HealthManager>()), nativePools: profile.OwnNativePools);
            foreach (var h in Resources.FindObjectsOfTypeAll<HealthManager>()) RegisterTarget(h);
            if (profile.Family == EncounterProfile.HornetSentinel) hornetBarbs = new HornetBarbBridge(session, encounter, profile.Scene, pools!, log, policy.AllocateAction, LiveHornet);
        } catch { HostDamageRegistry.PoolSpawned -= TraceFirebatSpawn; armorHead?.Dispose(); jars?.Dispose(); shockwaves?.Dispose(); fsmAttacks?.Dispose(); hornetBarbs?.Dispose(); targetRegistry?.Dispose(); attacks.Dispose(); policy.Dispose(); sharedHealth?.Dispose(); pools?.Dispose(); throw; }
    }
    private AttackCategory? ClassifyAttack(DamageHero d)
    {
        if(goop!=null && pools?.Owns(d.gameObject)==true && NativeGoopAttackPolicy.Role(d) is AttackCategory goopRole)return goopRole;
        // This alternate stagger receiver is still passive body contact, not a
        // new attack. Damageable geometry and incoming attack geometry differ.
        if ((profile.Family == EncounterProfile.BrokenVessel || profile.Family == EncounterProfile.LostKin)
            && d.name == "Stun Hitbox" && d.GetComponentInParent<HealthManager>() is HealthManager staggerOwner
            && profile.IsBossName(staggerOwner.name)) return null;
        var healthOwner = d.GetComponent<HealthManager>();
        if (healthOwner != null && profile.IsBossName(healthOwner.name) && profile.Family != EncounterProfile.MassiveMossCharger) return policy.HasBody(healthOwner) ? AttackCategory.Melee : globalContact ? AttackCategory.Contact : (AttackCategory?)null;
        // The orange Ooma core is an exploding projectile, not its former minion.
        if (profile.Family == EncounterProfile.Uumuu && d.name == "Lil Jellyfish(Clone)" && pools?.Owns(d.gameObject) == true) return AttackCategory.Melee;
        // Global native pools contain unrelated bosses' dormant attacks. Scene
        // ownership alone cannot admit every DontDestroyOnLoad damage source.
        if (d.gameObject.scene.name != profile.Scene) {
            bool family = profile.Family == EncounterProfile.Grimm || profile.Family == EncounterProfile.NightmareGrimm
                ? d.name.StartsWith("Grimm", StringComparison.Ordinal) || d.name.StartsWith("Nightmare", StringComparison.Ordinal) || d.name.StartsWith("Flameball Grimm", StringComparison.Ordinal)
                : profile.Family == EncounterProfile.Mantis || profile.Family == EncounterProfile.Sisters
                    ? d.name.StartsWith("Shot Mantis Lord", StringComparison.Ordinal) : false;
            if (!family && pools?.Owns(d.gameObject) != true) return null;
        }
        if ((profile.Key == EncounterProfile.Xero.Key || profile.Family==EncounterProfile.AbsoluteRadiance) && d.gameObject.scene.name == profile.Scene && d.name == "Cloud Hazard") return AttackCategory.Hazard;
        if (d.hazardType == 2 && d.gameObject.scene.name == profile.Scene && d.damageDealt >= 1 && d.damageDealt <= 2)
            return AttackCategory.Hazard; // Native spikes use the shared NS trap/respawn path.
        if (d.hazardType != 1 || d.damageDealt < 1 || d.damageDealt > 2) return null;
        if(profile.Family==EncounterProfile.HornetSentinel && d.name.StartsWith("Hornet Barb",StringComparison.Ordinal) && pools?.Owns(d.gameObject)==true)return AttackCategory.Projectile;
        var contactOwner=d.GetComponentInParent<HealthManager>();
        if(contactOwner != null && !profile.IsBossName(contactOwner.name) && targets.ContainsKey(contactOwner.GetInstanceID())) return AttackCategory.Contact;
        if ((profile.Family == EncounterProfile.NightmareGrimm || profile.Family == EncounterProfile.Grimm) && pools?.Owns(d.gameObject) == true
            && (d.name == "Hero Hurter" || d.name == "Flash Damage") && HostPooledAttackVolumes.IsGrimmFirebat(d.transform.parent)) return AttackCategory.Projectile;
        if(profile.Family==EncounterProfile.AbsoluteRadiance && HostReflectedProjectile.RadianceNailName(d.name))return AttackCategory.Projectile;
        if(profile.ArmorAndHead && HostReflectedProjectile.FallingDebris(d)!=null) return AttackCategory.Projectile;
        if(profile.Family==EncounterProfile.HiveKnight && (d.GetComponent<HiveKnightStinger>()!=null || HostReflectedProjectile.HiveBeeName(d.name))) return AttackCategory.Projectile;
        if(profile.Family==EncounterProfile.Sheo && d.GetComponent<PaintBullet>()!=null) return AttackCategory.Projectile;
        if((profile.Family==EncounterProfile.BrokenVessel || profile.Family==EncounterProfile.LostKin) && pools?.Owns(d.gameObject)==true
            && (d.GetComponent<BrokenVesselGlob>()!=null && d.name=="IK Projectile SH(Clone)" || d.name=="IK Projectile DS(Clone)" && d.GetComponent<AutoRecycleSelf>()!=null))return AttackCategory.Projectile;
        return d.GetComponent<EnemyBullet>() != null || HostReflectedProjectile.SoulOrb(d) != null || d.name.StartsWith("Shot Mantis Lord", StringComparison.Ordinal) || d.name.StartsWith("Shot Markoth Nail", StringComparison.Ordinal) || d.name.StartsWith("Shot Slug Spear",StringComparison.Ordinal)
            ? AttackCategory.Projectile : AttackCategory.Melee;
    }
    private void TraceFirebatSpawn(GameObject root)
    {
        if (firebatTraceSamples >= 128 || root == null || !HostPooledAttackVolumes.IsGrimmFirebat(root.transform) || pools?.Owns(root) != true) return;
        firebatTraceSamples++;
        foreach (var damage in root.GetComponentsInChildren<DamageHero>(true)) {
            var shape = damage.GetComponent<Collider2D>();
            log?.Invoke("HK_FIREBAT_ADMISSION root=" + root.GetInstanceID() + " source=" + damage.GetInstanceID() + " child=" + damage.name.Replace(' ', '_')
                + " scene=" + damage.gameObject.scene.name + " damage=" + damage.damageDealt + " hazard=" + damage.hazardType
                + " category=" + ClassifyAttack(damage) + " objectActive=" + damage.gameObject.activeInHierarchy + " componentEnabled=" + damage.enabled
                + " shapeEnabled=" + (shape != null && shape.enabled) + " position=" + damage.transform.position + " " + attacks.DiagnosticState(damage)
                + " frame=" + Time.frameCount);
        }
    }
    private static Collider2D? BodyShape(HealthManager health)
    {
        // HealthManager's native hit/effect path prefers its authored box. A
        // disabled polygon preceding it can be unrelated movement geometry.
        Collider2D? shape = health.GetComponent<BoxCollider2D>();
        return shape != null ? shape : health.GetComponent<Collider2D>();
    }
    internal bool DiagnosticEligible(SummonedTarget target) => profile.Key != "Uumuu" || !target.BossBar
        || targets.TryGetValue(target.Entity, out var body) && body.Health != null && !body.Health.IsInvincible;
    internal void TraceNative(Action<string> trace) { foreach(var target in targets.Values.Take(32)) if(target.Health != null) HostContactSource.TraceBody(target.Health, trace); }
    // Receipt identity survives native death while this exact body lease remains
    // in the encounter registry. Eligibility to act on it does not.
    private static bool ReceiptKind(HitKind kind)=>kind<=HitKind.Talisman || kind==HitKind.TaiChiKick || kind==HitKind.UnboundedCounter || kind==HitKind.StrikeParried;
    internal bool Recognizes(HitOutcome h) => ReceiptKind(h.Kind) && (targets.TryGetValue(h.Entity,out var t) && h.TargetActivation==t.Activation
        || retiredReceipts.Recognizes(h.Entity,h.TargetActivation,Time.realtimeSinceStartup));
    public bool Knows(HitOutcome h) => ReceiptKind(h.Kind) && targets.TryGetValue(h.Entity,out var t) && h.TargetActivation==t.Activation
        && !t.Retired && (t.SpawnLease?.Active??true);
    public bool MatchesAnchor(HitOutcome h) => h.Entity == anchor && h.TargetActivation == 1 && h.Encounter == encounter;
    internal int KnownHealth(HitOutcome hit) => Knows(hit) && targets[hit.Entity].Health != null ? PresentedHealth(targets[hit.Entity].Health) : 0;
    private bool OrangeMinion(HealthManager health)=>(profile.Family==EncounterProfile.BrokenVessel || profile.Family==EncounterProfile.LostKin || profile.Family==EncounterProfile.WingedNosk)
        && (health.name=="Parasite Balloon Spawner(Clone)" || health.name=="Parasite Balloon Spawner");
    internal HealthManager? ParriedMinion(long action) {
        if(action<=0)return null;
        foreach(var target in targets.Values)if(target.ContactAction==action && target.ContactGeneration==target.Activation && !target.Retired
            && target.Health!=null && OrangeMinion(target.Health) && target.Health.gameObject.activeInHierarchy && !target.Health.isDead && PresentedHealth(target.Health)>0
            && (target.SpawnLease?.Active??true))return target.Health;
        return null;
    }
    public bool Counter(long action, HitKind kind) {
        bool accepted=kind==HitKind.UnboundedCounter && armorHead?.Counter(action)==true || fsmAttacks?.Counter(action,kind)==true || goop?.Parry(action,kind)==true || hornetBarbs?.Parry(action,kind)==true || jars?.Parry(action,kind)==true || shockwaves?.Counter(action,kind)==true || policy.Counter(action,kind);
        if(accepted && profile.Family==EncounterProfile.Sheo)shockwaves?.RetirePaint(action);
        if(accepted && profile.Family==EncounterProfile.GreyPrinceZote && kind==HitKind.UnboundedCounter)shockwaves?.RetireZoteSkyfall(action);
        return accepted;
    }
    private (HealthManager Health,long Generation)? LiveHornet() {
        var target=targets.Values.SingleOrDefault(t=>!t.Retired && t.Health!=null && profile.IsBossName(t.Health.name) && !t.Health.isDead && t.Health.hp>0);
        return target==null?null:(target.Health,target.Activation);
    }
    internal bool CounterTarget(HitOutcome hit) {
        bool accepted=Knows(hit) && policy.Counter(hit.AttackId,hit.Kind,hit.Entity);
        if(accepted && profile.Family==EncounterProfile.Sheo)shockwaves?.RetirePaint(hit.AttackId);
        if(accepted && profile.Family==EncounterProfile.GreyPrinceZote && hit.Kind==HitKind.UnboundedCounter)shockwaves?.RetireZoteSkyfall(hit.AttackId);
        return accepted;
    }
    public int PresentedHealth(HealthManager h) { if(targets.TryGetValue(h.GetInstanceID(),out var minionTarget) && minionTarget.SpawnLease!=null)return minionTarget.SpawnLease.Health; armorHead?.Observe(); foreach(var target in targets.Values) if(target.Grimm?.Owns(h)==true) return target.Grimm.Health; return sharedHealth?.Ready == true && sharedHealth.Contains(h) ? sharedHealth.Health : armorHead?.PresentedHealth(h) ?? Math.Max(0, h.hp); }
    internal void ObservedDefeatedHead(YiGodhome.Bridge.Pose pose)=>defeatedHead?.Melee(pose);
    internal void ObservedMelee(YiGodhome.Bridge.Pose pose,HostHitReceiver receiver) {
        armorHead?.Melee(pose,receiver);
        defeatedHead?.Melee(pose);
        jars?.Melee(pose);
        if(profile.ArmorAndHead) attacks.ObservedDebrisMelee(pose,reflectMeleeDebris);
        if(profile.Family==EncounterProfile.HiveKnight) attacks.ObservedMelee(pose);
    }
    private bool ReflectMeleeDebris(int id,long activation)=>ResolveProjectile(id,activation,true);
    private bool BodyVisible(Target target) => target.Grimm?.ExposesBat == true || target.Grimm?.ExposesBow == true || target.Grimm == null && !profile.NativeHitAuthority || target.BodyRenderer == null
        || target.BodyRenderer.enabled && target.BodyRenderer.gameObject.activeInHierarchy;
    private void TraceAdmission(int entity, Target target)
    {
        if (!SessionConfiguration.Current.RunFixtures || profile.Family != EncounterProfile.GreyPrinceZote || admissionSamples >= 128) return;
        var h = target.Health; var damage = h.GetComponent<DamageHero>();
        long stamp = damage == null ? 0 : targetRegistry.Activation(damage);
        string state = h.gameObject.activeInHierarchy + ":" + BodyVisible(target) + ":" + target.Shape?.enabled
            + ":" + h.isDead + ":" + target.Retired + ":" + stamp;
        if (target.Admission == state) return;
        target.Admission = state; admissionSamples++;
        log?.Invoke("HK_ZOTE_ADMISSION encounter=" + encounter + " entity=" + entity + " name=" + h.name + " activation=" + target.Activation
            + " nativeActivation=" + stamp + " priorNativeActivation=" + target.PoolActivation + " active=" + h.gameObject.activeInHierarchy
            + " visible=" + BodyVisible(target) + " collider=" + target.Shape?.enabled + " retired=" + target.Retired + " dead=" + h.isDead
            + " health=" + h.hp + " states=" + string.Join(",", h.GetComponents<PlayMakerFSM>().Select(f => f.FsmName + ":" + f.ActiveStateName)) + " frame=" + Time.frameCount);
    }
    public HealthManager? Resolve(HitOutcome h) {
        if (!Knows(h)) return null;
        var target = targets[h.Entity]; var health = target.Health;
        if (health == null || health.isDead || PresentedHealth(health) <= 0
            || armorHead != null && !armorHead.Damageable(health)) return null;
        if(h.Kind==HitKind.Talisman && profile.Family==EncounterProfile.Uumuu && profile.IsBossName(health.name) && health.IsInvincible)return null;
        // An attached explosion retains its exact living target lease through
        // teleport/burrow/hidden geometry. It is not a new spatial melee hit.
        bool explosion = h.Kind == HitKind.Talisman && profile.IsBossName(health.name)
            && profile.Family != EncounterProfile.AbsoluteRadiance;
        if (!explosion && (!health.gameObject.activeInHierarchy || !policy.Damageable(health)
            || !BodyVisible(target) || target.Shape == null || !target.Shape.enabled || !target.Shape.gameObject.activeInHierarchy)) return null;
        return (h.Kind == HitKind.Talisman ? target.Grimm?.TalismanReceiver : target.Grimm?.Receiver) ?? health;
    }
    public bool ResolveProjectile(int id, long activation, bool reflected) {
        if (!reflected) return attacks.Resolve(id, activation, false, null, new TargetHealthState(anchor, 1, 1));
        if (profile.Key != EncounterProfile.Markoth.Key && profile.Family != EncounterProfile.SoulWarrior && profile.Family != EncounterProfile.SoulMaster && profile.Family != EncounterProfile.SoulTyrant && profile.Family!=EncounterProfile.Oblobbles && profile.Family!=EncounterProfile.Gorb && profile.Family!=EncounterProfile.HiveKnight && profile.Family!=EncounterProfile.Sheo && profile.Family!=EncounterProfile.AbsoluteRadiance && !profile.ArmorAndHead) return false;
        if(!attacks.TryProjectileOrigin(id,activation,out var origin)) return false;
        Target? target=null;float distance=float.PositiveInfinity;
        foreach(var candidate in targets.Values) {
            if(candidate.Retired || candidate.Health==null || !profile.IsBossName(candidate.Health.name) || candidate.Health.isDead || candidate.Health.hp<=0 || !candidate.Health.gameObject.activeInHierarchy) continue;
            float next=((candidate.Shape?.enabled==true?(Vector2)candidate.Shape.bounds.center:(Vector2)candidate.Health.transform.position)-origin).sqrMagnitude;
            if(next<distance) {target=candidate;distance=next;}
        }
        if(target==null) return false;
        var selected=target;long generation=selected.Activation;
        return attacks.Resolve(id, activation, true, selected.Health, new TargetHealthState(selected.Health.GetInstanceID(),generation,selected.Health.hp),()=>ReflectionTargetActive(selected,generation));
    }
    private bool ReflectionTargetActive(Target target,long generation)
    {
        if(target.Retired || target.Health==null || target.Activation!=generation) return false;
        if(armorHead==null) return true;
        armorHead.Observe();
        return target.Health==armorHead.Armor && armorHead.ArmorLease==generation && !armorHead.Broken;
    }
    private void Decorate(AttackVolume volume, DamageHero damage)
    {
        NativeBossContracts.CaptureWeapon(damage,profile,log);
        bool nativeActive=volume.Active;
        if ((profile.Family == EncounterProfile.Grimm || profile.Family == EncounterProfile.NightmareGrimm)
            && playerX.HasValue && damage.name.StartsWith("Flameball Grimmballoon", StringComparison.Ordinal) && volume.Category == AttackCategory.Melee) {
            var boss = targets.Values.FirstOrDefault(t => t.Health != null && profile.IsBossName(t.Health.name));
            if (boss != null) volume.Traits |= AttackTraits.LargeParryPush | (playerX.Value < boss.Health.transform.position.x ? AttackTraits.PushLeft : AttackTraits.PushRight);
        }
        if (profile.Key == EncounterProfile.Markoth.Key && damage.name.StartsWith("Shot Markoth Nail", StringComparison.Ordinal)) volume.Traits |= AttackTraits.Reflectable;
        if(profile.Family==EncounterProfile.Gorb && damage.name.StartsWith("Shot Slug Spear",StringComparison.Ordinal)) {
            volume.Traits|=AttackTraits.Reflectable;
            // As with native Markoth nails, injury holds translated damage
            // without consuming the native projectile or changing its flight.
            volume.ParryCooldown=1;volume.CooldownOnDamage=true;
        }
        if(profile.Family==EncounterProfile.Sheo && damage.GetComponent<PaintBullet>()!=null) volume.Traits|=AttackTraits.Reflectable;
        if(profile.ArmorAndHead && HostReflectedProjectile.FallingDebris(damage)!=null) volume.Traits|=AttackTraits.Reflectable;
        if(profile.Family==EncounterProfile.HiveKnight && volume.Category==AttackCategory.Projectile
            && (damage.GetComponent<HiveKnightStinger>()!=null || HostReflectedProjectile.HiveBeeName(damage.name))) volume.Traits|=AttackTraits.Reflectable;
        if ((profile.Family == EncounterProfile.SoulWarrior || profile.Family == EncounterProfile.SoulMaster || profile.Family == EncounterProfile.SoulTyrant)
            && (damage.GetComponent<EnemyBullet>() != null || HostReflectedProjectile.SoulOrb(damage) != null)) volume.Traits |= AttackTraits.Reflectable;
        if(profile.Family==EncounterProfile.Oblobbles && volume.Category==AttackCategory.Projectile && damage.GetComponent<EnemyBullet>()!=null) {
            volume.Traits |= AttackTraits.Reflectable;
            if(volume.Active) {
                int id=damage.GetInstanceID();long activation=attacks.NativeActivation(damage);
                if(!projectileBirths.TryGetValue(id,out var birth)) {
                    if(projectileBirths.Count>=AttackVolumes.MaxCount) throw new InvalidOperationException("Native projectile birth capacity exceeded.");
                    projectileBirths.Add(id,birth=new ProjectileBirth());
                }
                if(birth.Activation!=activation) {birth.Activation=activation;birth.Age=0;birth.Frame=-1;birth.Armed=false;}
                if(!birth.Armed) {
                    if(birth.Frame!=Time.frameCount && GameManager.instance?.IsGamePaused()!=true) {birth.Age+=Time.deltaTime;birth.Frame=Time.frameCount;}
                    bool inside=false;var shot=damage.GetComponent<Collider2D>().bounds;
                    foreach(var bodyTarget in targets.Values) if(bodyTarget.Health!=null && profile.IsBossName(bodyTarget.Health.name) && bodyTarget.Shape!=null && bodyTarget.Shape.bounds.Intersects(shot)) {inside=true;break;}
                    birth.Armed=birth.Age>=.08f && !inside;
                    if(birth.Armed && projectileBirthLogs++<16) log?.Invoke("HK_PROJECTILE_BIRTH_ARMED id="+id+" activation="+activation+" age="+birth.Age+" wholeShapeOutsideBody=True frame="+Time.frameCount);
                }
                volume.Active &= birth.Armed;
            }
        }
        armorHead?.Decorate(volume, damage);
        policy.Decorate(volume, damage, attacks.NativeActivation(damage));
        shockwaves?.Decorate(volume,damage);
        goop?.Decorate(volume,damage);
        hornetBarbs?.Decorate(volume,damage);
        jars?.Decorate(volume,damage,attacks.NativeActivation(damage));
        // Authored injury groups prevent an initial overlap burst. Surviving
        // native shots retain their independent defenses and later threats.
        if(volume.DamageGroupId>0) {
            volume.InitialDamageGroupHold=1;
            if(!volume.ConsumeOnContact && (volume.Category==AttackCategory.Projectile || volume.Category==AttackCategory.Melee && volume.ActionId==0)) {
                volume.ParryCooldown=1;volume.CooldownOnDamage=true;
            }
        }
        var passiveBoss=policy.BodyContactOwner(damage)??damage.GetComponent<HealthManager>();
        if(globalContact && !policy.SuppressDamage(damage) && passiveBoss != null && profile.IsBossName(passiveBoss.name) && !passiveBoss.isDead && passiveBoss.hp>0) {
            if(volume.Category==AttackCategory.Contact) { volume.ParryCooldown=trueGlobalContact?.0001f:1; volume.CooldownOnDamage=true; }
            else if(nativeActive && !volume.Active && volume.Category==AttackCategory.Melee) {
                volume.Active=true; volume.PassiveContact=true; volume.ActionId=0; volume.Traits=AttackTraits.None;volume.CounterInjuryEntity=0;
                volume.DamageGroupId=0;volume.InitialDamageGroupHold=0;volume.RepeatDamageGroupHold=false;volume.CounterGroupId=0;
                volume.ParryCooldown=trueGlobalContact?.0001f:1; volume.CooldownOnDamage=true;
            }
        }
        if ((profile.Family == EncounterProfile.NightmareGrimm || profile.Family == EncounterProfile.Grimm) && volume.Category == AttackCategory.Projectile
            && HostPooledAttackVolumes.IsGrimmFirebat(damage.transform.parent)) {
            var root = damage.transform.parent;
            var lease = root.GetComponent<NativeProjectileSequenceLease>() ?? root.gameObject.AddComponent<NativeProjectileSequenceLease>();
            if (lease.ActionGeneration != lease.Generation) { lease.ActionGeneration = lease.Generation; lease.Action = policy.AllocateAction(); }
            GrimmFirebatStrike.Apply(volume,lease.Action);
        }
        var minionHealth = damage.GetComponentInParent<HealthManager>();
        if(noskJumpGroup>0 && minionHealth!=null && profile.IsBossName(minionHealth.name)) {
            if(volume.Category==AttackCategory.Melee || volume.Category==AttackCategory.Contact) {
                // Landing/next-action changes cannot escape the body injury hold.
                volume.DamageGroupId=noskJumpGroup;volume.InitialDamageGroupHold=1;volume.RepeatDamageGroupHold=true;
                bool special=(volume.Traits&(AttackTraits.Kickable|AttackTraits.Crimson))!=0;
                volume.ParryCooldown=special?0:1;volume.CooldownOnDamage=!special;
            }
        }
        if(minionHealth != null && !profile.IsBossName(minionHealth.name) && targets.TryGetValue(minionHealth.GetInstanceID(),out var minion)) {
            if(minion.SpawnLease!=null && !minion.SpawnLease.Active) {volume.Active=false;return;}
            bool live=(minion.SpawnLease?.Active??true) && (volume.Category!=AttackCategory.Contact || !minion.Retired && !minionHealth.isDead && PresentedHealth(minionHealth)>0);
            if(volume.Active && !live && volume.Category==AttackCategory.Contact && minion.SuppressedContactGeneration!=minion.Activation && retiredMinionSamples<64) {
                minion.SuppressedContactGeneration=minion.Activation;retiredMinionSamples++;
                log?.Invoke("HK_RETIRED_MINION_CONTACT_SUPPRESSED entity="+minionHealth.GetInstanceID()+" activation="+minion.Activation
                    +" action="+minion.ContactAction+" collider="+volume.Id+" retired="+minion.Retired+" hp="+minionHealth.hp+" frame="+Time.frameCount);
            }
            volume.Active &= live;
        }
        if (minionHealth != null && !profile.IsBossName(minionHealth.name) && volume.Category == AttackCategory.Contact) {
            volume.ParryCooldown = trueGlobalContact?.0001f:1; volume.CooldownOnDamage = true; volume.TalismanContactImmunity=true;
            if (targets.TryGetValue(minionHealth.GetInstanceID(), out var contactTarget)) {
                if (contactTarget.ContactGeneration != contactTarget.Activation) {
                    contactTarget.ContactGeneration = contactTarget.Activation; contactTarget.ContactAction = policy.AllocateAction();
                    log?.Invoke("HK_MINION_CONTACT_GROUP entity="+minionHealth.GetInstanceID()+" activation="+contactTarget.Activation
                        +" action="+contactTarget.ContactAction+" seconds="+volume.ParryCooldown+" nativeHealthOwner=True frame="+Time.frameCount);
                }
                volume.ActionId = contactTarget.ContactAction;
                if(OrangeMinion(minionHealth)) {
                    volume.ContactOwner=minionHealth.GetInstanceID();volume.ContactOwnerGeneration=contactTarget.Activation;
                }
                if(OrangeMinion(minionHealth)) {
                    if(contactTarget.Maximum!=1 || minionHealth.GetComponent<AutoRecycleSelf>()==null)throw new InvalidOperationException("Native orange minion parry/death manifest changed.");
                    volume.NotifyParry=true;
                }
            }
        }
        if ((profile.Family == EncounterProfile.Mantis || profile.Family == EncounterProfile.Sisters) && damage.name.StartsWith("Shot Mantis Lord", StringComparison.Ordinal)) volume.ParryCooldown = 1;
        if (profile.Family != EncounterProfile.MassiveMossCharger) return;
        var health = damage.GetComponent<HealthManager>();
        if (health != null && targets.TryGetValue(health.GetInstanceID(), out var target)) target.MossBody?.Decorate(volume);
    }
    private void RegisterTarget(HealthManager h)
    {
        // Native animation initialization can create the body's collider later;
        // dormant prefab/template copies are not playable targets until enabled.
        if (h == null || !(h.gameObject.scene.name == profile.Scene && profile.IsTargetName(h.name) || pools?.Owns(h.gameObject) == true) || h != h.GetComponent<HealthManager>() || armorHead != null && !armorHead.Owns(h)) return;
        int id = h.GetInstanceID();
        uumuu?.Register(h);
        policy.Register(h);
        if(profile.Family==EncounterProfile.GreyPrinceZote && profile.IsBossName(h.name))defeatedHead??=new NativeDefeatedHead(h,log);
        sharedHealth?.Register(h);
        if(profile.Family==EncounterProfile.OroMato && h.name=="Oro" && nailmasterPhase==null) {
            var control=h.GetComponents<PlayMakerFSM>().Single(f=>f.FsmName=="nailmaster");
            nailmasterPhase=control.FsmVariables.GetFsmBool("Phase 2");
            if(nailmasterPhase==null || !control.FsmStates.Single(s=>s.Name=="Call Mato").Actions
                .OfType<HutongGames.PlayMaker.Actions.SetBoolValue>().Any(a=>a.boolVariable==nailmasterPhase && a.boolValue.Value))
                throw new InvalidOperationException("Native nailmaster phase presentation contract changed.");
            NativeBossTitles.BindNailmasterPhase(nailmasterPhase);
        }
        if (!targets.ContainsKey(id)) {
            if ((profile.Family==EncounterProfile.SoulMaster||profile.Family==EncounterProfile.SoulTyrant||profile.Family==EncounterProfile.Grimm||profile.Family==EncounterProfile.NightmareGrimm||profile.Family==EncounterProfile.Watchers||profile.Family==EncounterProfile.BrokenVessel||profile.Family==EncounterProfile.LostKin)&&profile.IsBossName(h.name)) SoulNativeEvidence.Inspect(h,log);
            if (profile.Family == EncounterProfile.Markoth && profile.IsBossName(h.name)) MarkothNativeEvidence.Inspect(h, log);
            var target = new Target { Notches = profile.IsBossName(h.name) && !profile.ArmorAndHead ? new NativeHealthNotches(h, log) : null, Health = h, Shape = BodyShape(h), BodyRenderer = h.GetComponent<Renderer>(), Maximum = Math.Max(1, h.hp) };
            if (profile.Family == EncounterProfile.Uumuu && profile.IsBossName(h.name)) {
                target.RepelCircle = h.GetComponent<CircleCollider2D>();
                if (target.RepelCircle == null || target.RepelCircle.radius <= 0) throw new InvalidOperationException("Uumuu authored shell circle missing.");
            }
            if(profile.Family==EncounterProfile.Flukemarm && h.name.StartsWith("Fluke Fly Spawner",StringComparison.Ordinal))
                target.SpawnLease=new NativeMinionSpawnLease(h,"Fluke Fly",new[]{"Spawn","Chase"},new[]{"Init","Inert","Death","Reset"},log);
            if(profile.Family==EncounterProfile.SoulWarrior && !profile.IsBossName(h.name))
                target.SpawnLease=new NativeMinionSpawnLease(h,"Control",new[]{"Activate","Chase Start","Chase - In Sight"},new[]{"Initiate","Spawn Wait","Spawn Pause","Check Saver","Spawn"},log);
            if(OrangeMinion(h))
                target.SpawnLease=new NativeMinionSpawnLease(h,"Control",new[]{"Activate","Chase - In Sight"},new[]{"Initiate","Spawn Wait","Spawn Pause","Check Saver","Spawn","Death","Death 2","Recycle"},log);
            if ((profile.Family==EncounterProfile.SoulMaster||profile.Family==EncounterProfile.SoulTyrant)&&profile.IsBossName(h.name)) target.Soul=new NativeStaggerGeometry(h,log,h.name.EndsWith("Phase2",StringComparison.Ordinal)?null:"Stun Init");
            if ((profile.Family==EncounterProfile.BrokenVessel||profile.Family==EncounterProfile.LostKin)&&profile.IsBossName(h.name)) target.Soul=new NativeStaggerGeometry(h,log,"Stunned","Stun Hitbox");
            if ((profile.Family==EncounterProfile.Grimm||profile.Family==EncounterProfile.NightmareGrimm)&&profile.IsBossName(h.name)) target.Grimm=new GrimmStaggerBody(h,log);
            if (profile.Family == EncounterProfile.MassiveMossCharger) target.MossBody = new MossBodyGeometry(h);
            if (profile.NativeHitAuthority) {
                // Native death can reset a reused body's HP before the next capture.
                // Observe the actual event, never infer encounter completion from it.
                target.Died = () => {
                    target.Retired = true;
                    retiredReceipts.Retire(id,target.Activation,Time.realtimeSinceStartup);
                    if(profile.IsBossName(h.name)) {
                        lastNativeBossDeathAt=Time.realtimeSinceStartup;
                        if(profile.Family==EncounterProfile.WingedNosk || profile.Family==EncounterProfile.LostKin || profile.Family==EncounterProfile.BrokenVessel) {
                            parasiteSpawnEnded=true;EndParasiteSpawns();
                        }
                    }
                    log?.Invoke("HK_ROSTER_NATIVE_DEATH entity=" + id + " activation=" + target.Activation + " frame=" + Time.frameCount + " encounter=" + encounter);
                };
                h.OnDeath += target.Died;
            }
            targets.Add(id, target);
        }
    }
    public ContactFrame Capture(long geometry, YiGodhome.Bridge.Pose? pose = null)
    {
        EndParasiteSpawns();
        if (profile.Family == EncounterProfile.NightmareGrimm || profile.Family == EncounterProfile.Grimm) {
            balloonFrames++;
            var room = GeometryExport.Latest;
            if (room == null || room.SourceSession != session || room.Scene != profile.Scene || room.Frame != geometry) hasEnvelope = false;
            else if (envelopeFrame != geometry || !hasEnvelope) { envelopeFrame = geometry; hasEnvelope = CombatEnvelope.TryFromPlatform(room, out envelope); }
            balloonCeiling = room != null && pose != null && pose.GeometrySession == session && pose.GeometryFrame == geometry && pose.JumpReach > 0
                ? room.Floor.Y + pose.JumpReach + pose.Height + Math.Max(.5f, pose.Height * .25f) : float.PositiveInfinity;
            if (SessionConfiguration.Current.RunFixtures && Time.realtimeSinceStartup >= nextBalloonCost) {
                nextBalloonCost = Time.realtimeSinceStartup + 2;
                log?.Invoke("HK_BALLOON_EXPORT_CULL frames=" + balloonFrames + " examined=" + balloonExamined + " omitted=" + balloonOmitted + " above=" + balloonAbove + " ceiling=" + balloonCeiling
                    + " wholeBounds=True margin=True upperCutoff=True nativeLifetime=True frame=" + Time.frameCount);
                balloonExamined = balloonOmitted = balloonAbove = balloonFrames = 0;
            }
        }
        if(profile.Family==EncounterProfile.AbsoluteRadiance) {
            var room=GeometryExport.Latest;
            if(room==null || room.SourceSession!=session || room.Scene!=profile.Scene || room.Frame!=geometry)hasRainFloor=false;
            else if(rainFloorGeometry!=geometry || !hasRainFloor) {
                rainFloorGeometry=geometry;hasRainFloor=CombatEnvelope.TryFromPlatform(room,out _);rainFloor=room.Floor.Y;
            }
            policy.SetStandableFloor(hasRainFloor?rainFloor:(float?)null);
        }
        long heap = HostHeapCost.Begin();
        playerX = pose?.X;
        hornetBarbs?.Update(pose, geometry);
        armorHead?.Observe();
        armorHead?.ObservePose(pose);
        policy.Update(); policy.TraceBodyHeight(pose); musicEvidence?.Update();
        sharedHealth?.Update();
        if (Time.realtimeSinceStartup >= nextPrune) {
            nextPrune = Time.realtimeSinceStartup + 1;
            targetRegistry.PruneDestroyed();
            foreach (var id in targets.Keys.Where(id => targets[id].Health == null).ToArray()) {
                retiredReceipts.Retire(id,targets[id].Activation,Time.realtimeSinceStartup);
                log?.Invoke("HK_ROSTER_RECEIPT_RETAINED entity="+id+" activation="+targets[id].Activation+" nativeDestroyed=True receiptOnly=True frame="+Time.frameCount);
                targets.Remove(id);
            }
        }
        HostHeapCost.Mark(0, ref heap);
        bool nailmastersPaired=nailmasterPhase?.Value==true;
        if(profile.Family==EncounterProfile.OroMato && observedNailmasterPhase!=nailmastersPaired) {
            observedNailmasterPhase=nailmastersPaired;
            log?.Invoke("HK_NAILMASTER_PRESENTATION_PHASE encounter="+encounter+" paired="+nailmastersPaired+" nativePhaseBool=True frame="+Time.frameCount);
        }
        var bodies = new List<SummonedTarget>();
        // Pool source ownership is published after OnEnable. Its registration
        // callback can therefore precede target admission; retry only cached
        // registry owners once their exact native pool lease is visible.
        foreach (var source in targetRegistry.Snapshot()) {
            var health = source == null ? null : source.GetComponentInParent<HealthManager>();
            if (health != null && !targets.ContainsKey(health.GetInstanceID())) RegisterTarget(health);
        }
        foreach (var pair in targets) {
            var t = pair.Value; var h = t.Health;
            if (h == null) continue;
            if(profile.Family==EncounterProfile.OroMato && h.name=="Mato" && !nailmastersPaired) continue;
            if(t.SpawnLease!=null) {
                var spawnDamage=h.GetComponent<DamageHero>();
                t.SpawnLease.Observe(spawnDamage==null?0:targetRegistry.Activation(spawnDamage));
                if(!t.SpawnLease.Active) continue;
                if(t.Activation!=t.SpawnLease.Generation) {
                    retiredReceipts.Retire(pair.Key,t.Activation,Time.realtimeSinceStartup);
                    t.Activation=t.SpawnLease.Generation; t.Maximum=t.SpawnLease.Health; t.Retired=false;
                    log?.Invoke("HK_ROSTER_REACTIVATED entity="+pair.Key+" activation="+t.Activation+" health="+h.hp+" nativeSpawnPhase=True frame="+Time.frameCount+" encounter="+encounter);
                }
            }
            uumuu?.Observe(h, encounter, t.Activation);
            t.Grimm?.Observe(encounter,t.Activation);
            TraceAdmission(pair.Key, t);
            if(SessionConfiguration.Current.RunFixtures && profile.Family==EncounterProfile.SoulWarrior && profile.IsBossName(h.name)
                && warriorSamples<320 && Time.realtimeSinceStartup>=nextWarriorTrace) {
                nextWarriorTrace=Time.realtimeSinceStartup+.25f;warriorSamples++;
                var animator=h.GetComponent<tk2dSpriteAnimator>();var body=h.GetComponent<Rigidbody2D>();
                var control=h.GetComponents<PlayMakerFSM>().Single(f=>f.FsmName=="Mage Knight");
                if(control.FsmVariables.FloatVariables.Length>64 || control.FsmVariables.BoolVariables.Length>64)throw new InvalidOperationException("Scoped warrior variable budget changed.");
                log?.Invoke("HK_WARRIOR_NATIVE_STATE entity="+pair.Key+" states="+string.Join(",",h.GetComponents<PlayMakerFSM>().Select(f=>f.FsmName+":"+f.ActiveStateName+":"+f.enabled))
                    +" clip="+(animator?.CurrentClip?.name??"none")+" animationFrame="+(animator?.CurrentFrame??-1)+" paused="+(animator?.Paused??false)
                    +" position="+h.transform.position+" velocity="+(body?.linearVelocity??Vector2.zero)+" timeScale="+Time.timeScale+" health="+h.hp+" frame="+Time.frameCount
                    +" nativeFloats="+string.Join(";",control.FsmVariables.FloatVariables.Select(v=>v.Name.Replace(' ','_')+":"+v.Value))
                    +" nativeBools="+string.Join(";",control.FsmVariables.BoolVariables.Select(v=>v.Name.Replace(' ','_')+":"+v.Value)));
            }
            if(SessionConfiguration.Current.RunFixtures&&(profile.Family==EncounterProfile.SoulMaster||profile.Family==EncounterProfile.SoulTyrant||profile.Family==EncounterProfile.Grimm||profile.Family==EncounterProfile.NightmareGrimm||profile.Family==EncounterProfile.Watchers||profile.Family==EncounterProfile.BrokenVessel||profile.Family==EncounterProfile.LostKin||profile.Family==EncounterProfile.OroMato||profile.Family==EncounterProfile.Sheo)&&profile.IsBossName(h.name)&&admissionSamples<128) {
                string state=string.Join(",",h.GetComponents<PlayMakerFSM>().Select(f=>f.FsmName+":"+f.ActiveStateName));
                if(t.Admission!=state){t.Admission=state;admissionSamples++;log?.Invoke("HK_SOUL_RECEIVER_STATE encounter="+encounter+" entity="+pair.Key+" activation="+t.Activation+" states="+state+" health="+h.hp+" invincible="+h.IsInvincible+" visible="+BodyVisible(t)+" colliders="+string.Join(";",h.GetComponents<Collider2D>().Select(c=>c.GetType().Name+":"+c.enabled+":"+c.bounds))+" frame="+Time.frameCount);}
            }
            if (profile.Family == EncounterProfile.MassiveMossCharger && SessionConfiguration.Current.RunFixtures
                && eligibilitySamples < 80 && Time.realtimeSinceStartup >= nextEligibilityTrace)
            {
                nextEligibilityTrace = Time.realtimeSinceStartup + .25f; eligibilitySamples++;
                log?.Invoke("HK_MOSS_ELIGIBILITY encounter=" + encounter + " geometry=" + geometry + " entity=" + pair.Key
                    + " activation=" + t.Activation + " frame=" + Time.frameCount + " health=" + h.hp
                    + " active=" + h.gameObject.activeInHierarchy + " invincible=" + h.IsInvincible + " visible=" + BodyVisible(t)
                    + " position=" + h.transform.position
                    + " states=" + string.Join(",", h.GetComponents<PlayMakerFSM>().Select(f => f.FsmName + ":" + f.ActiveStateName))
                    + " colliders=" + string.Join(";", h.GetComponents<Collider2D>().Select(c => c.GetType().Name + ":" + c.enabled
                        + ":trigger=" + c.isTrigger + ":bounds=" + c.bounds)));
            }
            if (!t.SharedPool && sharedHealth?.Ready == true && sharedHealth.Contains(h)) {
                t.SharedPool = true;
                if (t.Seen) { retiredReceipts.Retire(pair.Key,t.Activation,Time.realtimeSinceStartup);t.Activation++; }
                t.Maximum = sharedHealth.Maximum;
            }
            if (armorHead != null) {
                long lease = h == armorHead.Armor ? armorHead.ArmorLease : armorHead.HeadLease;
                if (t.Activation != lease) { retiredReceipts.Retire(pair.Key,t.Activation,Time.realtimeSinceStartup);t.Activation = lease; t.Maximum = Math.Max(1, h.hp); t.Retired = false;
                    log?.Invoke("HK_ARMOR_LEASE entity=" + pair.Key + " activation=" + lease + " max=" + t.Maximum + " frame=" + Time.frameCount + " encounter=" + encounter); }
                if (h == armorHead.Head && !armorHead.Exposed) continue;
            }
            if (h == null || !t.Seen && (!h.gameObject.activeInHierarchy || !BodyVisible(t))) continue;
            if (!profile.IsBossName(h.name) && (!h.gameObject.activeInHierarchy || !BodyVisible(t))) continue;
            var c = t.Grimm?.Shape ?? t.Soul?.Shape ?? t.Shape;
            if((t.Soul!=null||t.Grimm!=null)&&t.Shape!=c) {
                t.Shape=c;
                log?.Invoke("HK_SOUL_WOUND_WINDOW encounter="+encounter+" entity="+pair.Key+" activation="+t.Activation+" collider="+c!.name.Replace(' ','_')+" health="+h.hp+" active="+(c.enabled&&c.gameObject.activeInHierarchy)+" frame="+Time.frameCount);
            }
            if (c == null) c = t.Shape = BodyShape(h);
            if (c == null) {
                if (t.MissingAt < 0) t.MissingAt = Time.realtimeSinceStartup;
                if (h.gameObject.activeInHierarchy && Time.realtimeSinceStartup - t.MissingAt > 5) throw new InvalidOperationException("Active roster body has no native collider: " + h.name);
                continue;
            }
            if (!t.Seen) {
                t.Seen = true;
                t.Maximum = Math.Max(1, h.hp); // Native activation can replace dormant prefab HP before first presentation.
                log?.Invoke("HK_ROSTER_TARGET entity=" + pair.Key + " activation=" + t.Activation + " name=" + h.name + " max=" + h.hp + " shape=" + c.GetType().Name);
            }
            if (h.isDead || PresentedHealth(h) <= 0 && armorHead == null) { t.Retired = true; continue; }
            if (t.Retired) {
                // Native self-destruct can reset a pooled minion's HP before
                // deactivation. HP alone is not a new activation lease.
                if (!profile.IsBossName(h.name)) {
                    var retiredDamage = h.GetComponent<DamageHero>();
                    if (retiredDamage == null || targetRegistry.Activation(retiredDamage) <= t.PoolActivation) continue;
                }
                retiredReceipts.Retire(pair.Key,t.Activation,Time.realtimeSinceStartup);
                t.Retired = false; t.Activation++; t.Maximum = h.hp;
                log?.Invoke("HK_ROSTER_REACTIVATED entity=" + pair.Key + " activation=" + t.Activation + " health=" + h.hp + " frame=" + Time.frameCount);
            }
            var damage = h.GetComponent<DamageHero>();
            long poolActivation = damage != null ? targetRegistry.Activation(damage) : 0;
            if (t.SpawnLease==null && poolActivation > 0 && t.PoolActivation != poolActivation) {
                if (t.PoolActivation > 0) { retiredReceipts.Retire(pair.Key,t.Activation,Time.realtimeSinceStartup);t.Activation++; t.Maximum = h.hp; }
                t.PoolActivation = poolActivation;
                log?.Invoke("HK_ROSTER_POOL_ACTIVATION entity=" + pair.Key + " activation=" + t.Activation + " nativeActivation=" + poolActivation + " frame=" + Time.frameCount + " encounter=" + encounter);
            }
            var b = t.MossBody?.Bounds() ?? c.bounds;
            bool geometryActive = b.size.x > 0 && b.size.y > 0;
            if (b.size.x <= 0 || b.size.y <= 0) {
                // Disabled teleport/phase geometry retains the native target lease.
                if (c is BoxCollider2D box) {
                    var scale = c.transform.lossyScale;
                    b = new Bounds(c.transform.TransformPoint(box.offset), new Vector3(Math.Abs(box.size.x * scale.x), Math.Abs(box.size.y * scale.y), 0));
                } else if (c is PolygonCollider2D polygon) {
                    bool first = true;
                    for (int path = 0; path < polygon.pathCount; path++) foreach (var vertex in polygon.GetPath(path)) {
                        var point = polygon.transform.TransformPoint(vertex + polygon.offset);
                        if (first) { b = new Bounds(point, Vector3.zero); first = false; } else b.Encapsulate(point);
                    }
                } else if (c is CircleCollider2D circle) {
                    var scale = c.transform.lossyScale;
                    b = new Bounds(c.transform.TransformPoint(circle.offset), new Vector3(2 * circle.radius * Math.Abs(scale.x), 2 * circle.radius * Math.Abs(scale.y), 0));
                } else throw new InvalidOperationException("Inactive roster shape requires audited bounds: " + c.GetType().Name);
            }
            if (b.size.x <= 0 || b.size.y <= 0) {
                if (!t.HasBounds) continue;
                b = t.LastBounds; // Native collapsed/disabled geometry remains inactive.
            } else { t.LastBounds = b; t.HasBounds = true; }
            t.Maximum = Math.Max(t.Maximum, h.hp);
            bodies.Add(new SummonedTarget { Entity = pair.Key, Generation = t.Activation, Collider = c.GetInstanceID(), Health = PresentedHealth(h), MaxHealth = t.Maximum,
                Damage = 0, SharedHealth=t.SharedPool, BossBar = profile.IndividualBossBars && profile.IsBossName(h.name) && (sharedHealth == null || !sharedHealth.Ready || sharedHealth.Bar(h)), Active = geometryActive && h.gameObject.activeInHierarchy && c.enabled && BodyVisible(t) && (profile.Family!=EncounterProfile.AbsoluteRadiance || policy.Damageable(h)) && (armorHead == null || armorHead.Damageable(h)), X = b.center.x, Y = b.center.y, Width = b.size.x, Height = b.size.y });
            policy.ProjectBar(h,bodies[bodies.Count-1]);
            bodies[bodies.Count - 1].Notches = t.Notches?.Capture(t.Maximum) ?? Array.Empty<int>();
            bodies[bodies.Count - 1].HideHealthBar = policy.HideHealthBar(h) || profile.Family == EncounterProfile.Uumuu && h.name.StartsWith("Lil Jellyfish", StringComparison.Ordinal);
            if (profile.Family == EncounterProfile.Uumuu && profile.IsBossName(h.name)) {
                var target = bodies[bodies.Count - 1]; target.PassiveRepel = h.IsInvincible;
                // The sprite rectangle includes the jelly's hanging tentacles.
                // Use the authored round shell, including its offset, for recoil.
                // The exposed core damage receiver remains the native box.
                if (target.PassiveRepel && t.RepelCircle != null) {
                    var circle = t.RepelCircle;
                    var center = circle.transform.TransformPoint(circle.offset);
                    var scale = circle.transform.lossyScale;
                    target.X = center.x; target.Y = center.y;
                    target.Width = circle.radius * 2 * Math.Abs(scale.x);
                    target.Height = circle.radius * 2 * Math.Abs(scale.y);
                    if (!t.RepelEvidence && SessionConfiguration.Current.RunFixtures) {
                        t.RepelEvidence = true;
                        log?.Invoke("HK_UUMUU_REPEL_GEOMETRY encounter=" + encounter + " entity=" + pair.Key + " activation=" + t.Activation
                            + " radius=" + circle.radius + " offset=" + circle.offset + " width=" + target.Width + " height=" + target.Height
                            + " spriteWidth=" + t.BodyRenderer?.bounds.size.x + " spriteHeight=" + t.BodyRenderer?.bounds.size.y + " authoredCircle=True frame=" + Time.frameCount);
                    }
                }
            }
            policy.Target(bodies[bodies.Count - 1]);
            if (armorHead != null) bodies[bodies.Count - 1].Health = armorHead.PresentedHealth(h);
        }
        HostHeapCost.Mark(1, ref heap);
        var pooled = attacks.Capture(true, Decorate, (profile.Family == EncounterProfile.NightmareGrimm || profile.Family == EncounterProfile.Grimm) ? OutsideBalloon : profile.Family==EncounterProfile.AbsoluteRadiance?OutsideRadianceNail:null);
        pooled=shockwaves?.RetainControllers(pooled)??pooled;
        HostHeapCost.Mark(2, ref heap);
        var flames = fsmAttacks?.Capture() ?? Array.Empty<AttackVolume>();
        HostHeapCost.Mark(3, ref heap);
        var frame = new ContactFrame { Session = session, Encounter = encounter, GeometryFrame = geometry, NativeFrame = Time.frameCount, CaptureTick = Stopwatch.GetTimestamp(),
            Entity = anchor, TargetGeneration = 1, Collider = anchor, HurtCollider = anchor, MaxHealth = 1, Health = 0, Damage = 0,
            Width = 1, Height = 1, HurtWidth = 1, HurtHeight = 1, Difficulty = BossSceneController.Instance.BossLevel,
            Summons = bodies.ToArray(), Volumes = pooled.Concat(flames).ToArray() };
        if(profile.Family==EncounterProfile.OroMato) frame.BossTitle=nailmastersPaired?BossTitleVariant.BrothersOroMato:BossTitleVariant.NailmasterOro;
        if(profile.Family==EncounterProfile.Sisters)frame.BossTitle=NativeBossTitles.CurrentBossTitle;
        frame.BossBarsReady=NativeBossTitles.ReadyFor(frame.BossTitle);
        if(profile.Family==EncounterProfile.OroMato)
            frame.BossBarsReady=bossBarBatch.Ready(frame.BossTitle,frame.BossBarsReady,
                frame.Summons.Count(t=>t.BossBar && !t.HideHealthBar && t.Health>0));
        policy.Presentation(frame);
        if(profile.Family==EncounterProfile.AbsoluteRadiance && HeroProxy.TryRadianceHazardGround(geometry,out var checkpoint)) {
            frame.HasHazardCheckpoint=true;frame.HazardCheckpointX=checkpoint.x;frame.HazardCheckpointY=checkpoint.y;
        }
        armorHead?.Warning(frame);
        HostHeapCost.Mark(4, ref heap);
        HostHeapCost.Flush(log);
        return frame;
    }
    public void Dispose() {
        armorHead?.Dispose();
        defeatedHead?.Dispose();defeatedHead=null;
        HostDamageRegistry.PoolSpawned -= TraceFirebatSpawn;
        jars?.Dispose(); shockwaves?.Dispose(); fsmAttacks?.Dispose(); targetRegistry.Dispose(); attacks.Dispose(); policy.Dispose(); sharedHealth?.Dispose(); hornetBarbs?.Dispose(); pools?.Dispose();
        foreach (var target in targets.Values) if (target.Health != null && target.Died != null) target.Health.OnDeath -= target.Died;
        targets.Clear();
        projectileBirths.Clear();
    }
}
