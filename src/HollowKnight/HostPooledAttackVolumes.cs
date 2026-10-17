using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YiGodhome.Bridge;
using HarmonyLib;

namespace YiGodhome;

internal sealed class NativeProjectileSequenceLease : MonoBehaviour
{
    public int Generation { get; private set; }
    internal int ActionGeneration = -1;
    internal long Action;
    private void OnEnable() { Generation++; }
}

// Native pool lifetime and enabled hitboxes define attack generations. Classification
// is supplied by the encounter; geometry and native receiver handling are shared.
internal sealed class HostPooledAttackVolumes : IDisposable
{
    private sealed class Entry
    {
        public DamageHero Damage = null!;
        public Collider2D Shape = null!;
        public Renderer? BodyRenderer;
        public AttackCategory Category;
        public long Activation, NativeActivation;
        public readonly AttackGeneration Generation = new AttackGeneration();
        public bool Active;
        public bool Admitted = true;
        public EnemyBullet? Bullet;
        public PaintBullet? Paint;
        public PlayMakerFSM? Sword;
        public PlayMakerFSM? Firebat;
        public PlayMakerFSM? Debris;
        public PlayMakerFSM? HiveBee;
        public HiveKnightStinger? Stinger;
        public Recoil? BeeRecoil;
        public long MeleeActivation, MeleeSwing;
        public bool MeleeMotionPending;
        public Vector2 MeleeMotionStart;
        public float MeleeMotionAt,MeleeMotionDirection;
        public long MeleeMotionActivation,MeleeMotionSwing;
        public NativeProjectileSequenceLease? FirebatLease;
        public int FirebatGeneration;
        public long ResolvedActivation;
        public long ActionId;
        public bool CrimsonRain, RadianceNail, MarkothNail;
        public HostReflectedProjectile? Reflection;
        public Transform? Foreground;
        public float OriginalDepth;
        public Renderer? ForegroundRenderer;
        public int OriginalSortingLayer, OriginalSortingOrder;
        public readonly ImmutableContourSnapshot<(Matrix4x4, Vector2, float, Vector2)> Geometry = new();
    }
    private readonly HostDamageRegistry registry;
    private readonly Func<DamageHero, AttackCategory?> classify;
    private readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>();
    private readonly Dictionary<DamageHero, (long Activation, int Damage, int Hazard)> classified = new();
    private readonly Action<string>? log;
    private readonly bool collapseEmptyGeometry;
    private readonly bool visibleBodies;
    private readonly bool activeOnly;
    private static HostPooledAttackVolumes? current;
    private readonly Harmony harmony = new Harmony("local.yigodhome.projectile-ownership");
    private static readonly System.Reflection.FieldInfo BulletActive = AccessTools.Field(typeof(EnemyBullet), "active");
    private int projectileGeometrySamples;
    private int registrationSamples;
    private int shockwaveDepthSamples;
    private static readonly Vector2[] UnitCircle = Enumerable.Range(0, 64)
        .Select(i => new Vector2(Mathf.Cos(i * Mathf.PI / 32), Mathf.Sin(i * Mathf.PI / 32))).ToArray();
    private readonly List<Vector2> polygonPoints=new();
    private readonly Vector2[] boxPoints=new Vector2[4];
    private readonly List<AttackVolume> sampled=new();
    private readonly List<int> removed=new();
    private readonly List<DamageHero> removedSources=new();
    private int matrixSamples;
    private int circleMatrixSamples;
    private int primitiveSamples;
    private int primitiveFallbackSamples;
    private readonly bool circlePrimitives;
    private int contourReuses, contourBuilds, contourFloats;
    private float nextContourReport;
    internal static bool IsGrimmFirebat(Transform? root) => root != null && (root.name.StartsWith("Nightmare Firebat", StringComparison.Ordinal) || root.name.StartsWith("Grimm Firebat", StringComparison.Ordinal));
    internal long NativeActivation(DamageHero damage) => registry.Activation(damage);
    internal string DiagnosticState(DamageHero damage) => "registered=" + (registry.Activation(damage) > 0)
        + " nativeActivation=" + registry.Activation(damage) + " classified=" + classified.ContainsKey(damage)
        + " entries=" + entries.Values.Count(e => e.Damage == damage) + " admitted=" + entries.Values.Any(e => e.Damage == damage && e.Admitted);
    public HostPooledAttackVolumes(string scene, Func<DamageHero, AttackCategory?> classify, Action<string>? log, bool collapseEmptyGeometry = false, bool nativePools = false, bool visibleBodies = false, Func<DamageHero, bool>? globalOwnership = null)
    {
        this.collapseEmptyGeometry = collapseEmptyGeometry;
        this.visibleBodies = visibleBodies;
        // Apply the verified Grimm native-circle path to every encounter.
        // The complete independently transformed contour remains on the wire;
        // strict validation keeps nonuniform/sheared transforms as polygons.
        circlePrimitives = true;
        // Missing volumes already close/drain the receiving native lease. Grimm's
        // scene-owned projectile pool can retain more inactive objects than the
        // wire budget; those historical colliders are not current attacks.
        activeOnly = nativePools || scene == "GG_Grimm" || scene == "GG_Grimm_Nightmare";
        // Ownership survives a native Init/Impact state's temporary zero damage.
        // Registration must precede the later authored damaging window.
        registry = new HostDamageRegistry(scene, globalOwnership ?? (d => classify(d).HasValue), nativePools: nativePools); this.classify = classify; this.log = log; current = this;
        try {
            if(EncounterProfile.Selected.Family==EncounterProfile.HiveKnight) NativeFsmStepLease.Prepare();
            harmony.Patch(AccessTools.Method(typeof(EnemyBullet), "OnTriggerEnter2D"), prefix: new HarmonyMethod(typeof(HostPooledAttackVolumes), nameof(BeforeHeroContact)));
            foreach(var method in new[]{"DoTriggerEnter2D","DoTriggerStay2D"}) {
                harmony.Patch(AccessTools.Method(typeof(HutongGames.PlayMaker.Actions.Trigger2dEventLayer),method),prefix:new HarmonyMethod(typeof(HostPooledAttackVolumes),nameof(BeforeSoulHeroContact)));
                harmony.Patch(AccessTools.Method(typeof(HutongGames.PlayMaker.Actions.Trigger2dEvent),method),prefix:new HarmonyMethod(typeof(HostPooledAttackVolumes),nameof(BeforeSoulHeroContact)));
            }
            if(EncounterProfile.Selected.Family==EncounterProfile.Sheo) {
                harmony.Patch(AccessTools.Method(typeof(PaintBullet),"OnTriggerEnter2D"),prefix:new HarmonyMethod(typeof(HostPooledAttackVolumes),nameof(BeforePaintHeroContact)));
                harmony.Patch(AccessTools.Method(typeof(PaintBullet),"OnCollisionEnter2D"),prefix:new HarmonyMethod(typeof(HostPooledAttackVolumes),nameof(BeforePaintTerrainContact)));
            }
            if(EncounterProfile.Selected.ArmorAndHead) {
            foreach(var method in new[]{"DoTriggerEnter2D","DoTriggerStay2D"}) {
                harmony.Patch(AccessTools.Method(typeof(HutongGames.PlayMaker.Actions.Trigger2dEventLayer),method),prefix:new HarmonyMethod(typeof(HostPooledAttackVolumes),nameof(BeforeDebrisHeroContact)));
                harmony.Patch(AccessTools.Method(typeof(HutongGames.PlayMaker.Actions.Trigger2dEvent),method),prefix:new HarmonyMethod(typeof(HostPooledAttackVolumes),nameof(BeforeDebrisEnemyContact)));
            }
            foreach(var method in new[]{"DoCollisionEnter2D","DoCollisionStay2D"})
                harmony.Patch(AccessTools.Method(typeof(HutongGames.PlayMaker.Actions.Collision2dEvent),method),prefix:new HarmonyMethod(typeof(HostPooledAttackVolumes),nameof(BeforeDebrisEnemyContact)));
            }
        }
        catch { Dispose(); throw; }
    }
    private static bool BeforeHeroContact(EnemyBullet __instance, Collider2D __0)
    {
        var adapter = current;
        if (adapter == null || GameManager.instance?.profileID != 4 || !__0.CompareTag("HeroBox")) return true;
        var damage = __instance.GetComponent<DamageHero>();
        return damage == null || adapter.classify(damage) != AttackCategory.Projectile;
    }
    private int soulProxyContacts;
    private static bool BeforeSoulHeroContact(HutongGames.PlayMaker.FsmStateAction __instance,Collider2D __0) {
        var adapter=current;var hero=HeroController.instance;
        if(adapter==null || hero==null || __0==null || !__0.transform.IsChildOf(hero.transform)
            || GameManager.instance?.profileID!=4 || __instance.Owner==null)return true;
        DamageHero? damage;
        if(__instance.Fsm.Name=="Orb Control") {
            // Orb Control is on the root; native injury data is on its exact
            // Hero Hurter child. Querying the root admits premature impact.
            damage=__instance.Owner.transform.Find("Hero Hurter")?.GetComponent<DamageHero>();
            if(damage==null || HostReflectedProjectile.SoulOrb(damage)?.Fsm!=__instance.Fsm)return true;
        } else if(__instance.Fsm.Name=="Vomit Glob" && (EncounterProfile.Selected.Family==EncounterProfile.Nosk
            || EncounterProfile.Selected.Family==EncounterProfile.WingedNosk)) {
            damage=__instance.Owner.GetComponent<DamageHero>();
            if(damage==null || NativeGoopAttackPolicy.Role(damage)!=AttackCategory.Projectile)return true;
        } else return true;
        if(adapter.registry.Activation(damage)<=0 || adapter.classify(damage)!=AttackCategory.Projectile)return true;
        if(adapter.soulProxyContacts++<16)adapter.log?.Invoke("HK_SOUL_PROXY_CONTACT_IGNORED root="+damage.GetInstanceID()+" state="+__instance.State.Name.Replace(' ','_')+" nativeYiVerdictOwnsImpact=True frame="+Time.frameCount);
        return false;
    }
    private static bool BeforePaintHeroContact(PaintBullet __instance,Collider2D __0) {
        var adapter=current;
        if(adapter==null || GameManager.instance?.profileID!=4 || !__0.CompareTag("HeroBox"))return true;
        var damage=__instance.GetComponent<DamageHero>();
        return damage==null || adapter.registry.Activation(damage)<=0 || adapter.classify(damage)!=AttackCategory.Projectile;
    }
    private static bool BeforePaintTerrainContact(PaintBullet __instance)=>__instance.GetComponent<HostReflectedProjectile>()?.IsReflected!=true;
    private static bool BeforeDebrisEnemyContact(HutongGames.PlayMaker.FsmStateAction __instance)=>HostReflectedProjectile.EnemyCallback(__instance);
    private static bool BeforeDebrisHeroContact(HutongGames.PlayMaker.Actions.Trigger2dEventLayer __instance,Collider2D __0)
    {
        var adapter=current;var hero=HeroController.instance;
        if(adapter==null || GameManager.instance?.profileID!=4 || hero==null || __0==null || !__0.transform.IsChildOf(hero.transform)
            || __instance.Fsm.Name!="Fall Barrel Control" || __instance.Owner==null) return true;
        var damage=__instance.Owner.GetComponent<DamageHero>();
        return damage==null || adapter.registry.Activation(damage)<=0 || adapter.classify(damage)!=AttackCategory.Projectile;
    }
    public bool Resolve(int id, long activation, bool reflected, HealthManager? target, TargetHealthState health,Func<bool>? targetLease=null)
    {
        if (!entries.TryGetValue(id, out var e) || (!e.Active && e.Firebat == null) || e.Activation != activation || e.ResolvedActivation == activation
            || e.Damage == null || e.NativeActivation != registry.Activation(e.Damage)
            || e.Bullet == null && e.Paint==null && e.Sword == null && e.Firebat == null && e.Debris==null && e.HiveBee==null && e.Stinger==null
            || e.Bullet != null && !(bool)BulletActive.GetValue(e.Bullet) || e.Paint!=null && !HostReflectedProjectile.PaintIsActive(e.Paint)) return false;
        if (e.Firebat != null) {
            if (reflected || !e.Firebat.gameObject.activeInHierarchy || e.FirebatLease == null || e.FirebatGeneration != e.FirebatLease.Generation
                || (e.Firebat.ActiveStateName != "Fire" && e.Firebat.ActiveStateName != "Tween Up")) return false;
            foreach (var sibling in entries.Values.Where(s => s.Firebat == e.Firebat)) { sibling.ResolvedActivation = sibling.Activation; sibling.Active = false; }
            e.Firebat.SetState("Impact");
            log?.Invoke("HK_PROJECTILE_RESOLVED id=" + id + " activation=" + activation + " nativeImpact=True root=" + e.Firebat.name + " state=" + e.Firebat.ActiveStateName + " siblingRetired=True frame=" + Time.frameCount);
            return true;
        }
        if (reflected) {
            var flight = e.Damage.GetComponent<Rigidbody2D>() ?? HostReflectedProjectile.SoulOrb(e.Damage)?.GetComponent<Rigidbody2D>();
            if (target == null || target.isDead || e.Sword == null && e.Bullet == null && e.Paint==null && e.Debris==null && e.HiveBee==null && e.Stinger==null || flight == null || flight.linearVelocity.sqrMagnitude <= 0 && HostReflectedProjectile.SoulOrb(e.Damage) == null
                && HostReflectedProjectile.RadianceCombVelocity(e.Damage).sqrMagnitude<=0) return false;
            e.Reflection ??= e.Damage.gameObject.AddComponent<HostReflectedProjectile>();
            e.Reflection.Begin(e.Debris??e.HiveBee??e.Sword, e.Damage, e.Shape, target, health, activation, log,validTargetLease:targetLease,crimsonRain:e.CrimsonRain);
            e.ResolvedActivation = activation; e.Active = false;
            return true;
        }
        // An imperfect sword parry retires this translated strike, but leaves
        // the native sword/comb moving and recycling on its authored path.
        if(EncounterProfile.Selected.Family==EncounterProfile.AbsoluteRadiance && HostReflectedProjectile.RadianceNailName(e.Damage.name)) {
            e.ResolvedActivation=activation;e.Active=false;
            log?.Invoke("HK_RADIANCE_SWORD_PARRIED id="+id+" activation="+activation+" nativeFlightPreserved=True reflected=False");
            return true;
        }
        if(EncounterProfile.Selected.Family==EncounterProfile.Gorb && e.Damage.name.StartsWith("Shot Slug Spear",StringComparison.Ordinal)) {
            e.ResolvedActivation=activation;e.Active=false;
            log?.Invoke("HK_GORB_SPEAR_PARRIED id="+id+" activation="+activation+" nativeFlightPreserved=True reflected=False");
            return true;
        }
        e.ResolvedActivation = activation; e.Active = false;
        if (e.Bullet != null) {
            BulletActive.SetValue(e.Bullet, false);
            e.Bullet.StartCoroutine((System.Collections.IEnumerator)AccessTools.Method(typeof(EnemyBullet), "Collision").Invoke(e.Bullet, new object[] { Vector2.zero, false }));
        } else HostReflectedProjectile.RecycleSword(e.Damage);
        log?.Invoke("HK_PROJECTILE_RESOLVED id=" + id + " activation=" + activation + " nativeImpact=True");
        return true;
    }
    internal bool TryProjectileOrigin(int id,long activation,out Vector2 origin) {
        origin=default;
        if(!entries.TryGetValue(id,out var entry) || entry.Activation!=activation || entry.Shape==null || !entry.Active) return false;
        origin=entry.Shape.bounds.center;return true;
    }
    public AttackVolume[] Capture(bool alive, Action<AttackVolume, DamageHero>? decorate = null, Func<DamageHero, Bounds, bool>? outside = null)
    {
        long heap = HostHeapCost.Begin();
        removedSources.Clear();
        foreach(var source in classified.Keys)if(source==null)removedSources.Add(source!);
        foreach(var source in removedSources)classified.Remove(source);
        foreach (var source in registry.Snapshot())
        {
            var stamp = (registry.Activation(source), source.damageDealt, source.hazardType);
            if (classified.TryGetValue(source, out var previous) && previous == stamp) continue;
            var category = classify(source);
            if (!category.HasValue || source.damageDealt == 0) {
                classified[source] = stamp;
                foreach (var e in entries.Values) if (e.Damage == source) e.Admitted = false;
                continue;
            }
            if (source.damageDealt < 1 || source.damageDealt > 2 || source.hazardType != 1
                && !(category == AttackCategory.Hazard && source.hazardType == 2)) throw new InvalidOperationException("Unaudited pooled damage semantics.");
            var shapes = source.GetComponents<Collider2D>();
            if (shapes.Length == 0) continue; // Native animation may create these after OnEnable.
            classified[source] = stamp;
            foreach (var shape in shapes)
                if (entries.TryGetValue(shape.GetInstanceID(), out var existing)) {
                    existing.Admitted = true; existing.Category = category.Value;
                    existing.RadianceNail=EncounterProfile.Selected.Family==EncounterProfile.AbsoluteRadiance && HostReflectedProjectile.RadianceNailName(source.name);
                    existing.MarkothNail=source.name.StartsWith("Shot Markoth Nail",StringComparison.Ordinal);
                } else
                {
                    PlayMakerFSM? sword = null;
                    if (HostReflectedProjectile.SoulOrb(source) is PlayMakerFSM orb) {
                        if (!new[] { "Chase Hero", "Orbiting", "Impact", "Recycle" }.All(n => orb.FsmStates.Any(s => s.Name == n)) || orb.GetComponent<Rigidbody2D>() == null)
                            throw new InvalidOperationException("Soul native orb control contract changed.");
                        sword = orb;
                        NativeSoulOrbImpact.Bind(orb,shape.GetInstanceID(),log);
                    }
                    if (source.name.StartsWith("Shot HK Shadow", StringComparison.Ordinal)) {
                        sword = source.GetComponents<PlayMakerFSM>().Single(f => f.FsmName == "Control");
                        if (!sword.FsmStates.Any(s => s.Name == "Start") || !sword.FsmStates.Any(s => s.Name == "Fire")
                            || source.GetComponent<AutoRecycleSelf>() == null || source.GetComponent<Rigidbody2D>() == null)
                            throw new InvalidOperationException("PV native sword graph changed.");
                    }
                    if (source.name.StartsWith("Shot Markoth Nail", StringComparison.Ordinal)) {
                        sword = source.GetComponents<PlayMakerFSM>().Single(f => f.FsmName == "Control");
                        if (!new[] { "Init", "Antic", "Fire", "Recycle" }.All(name => sword.FsmStates.Any(s => s.Name == name))
                            || source.GetComponent<Rigidbody2D>() == null
                            || !sword.FsmStates.Single(s => s.Name == "Recycle").Actions.Any(a => a.GetType().Name == "RecycleSelf"))
                            throw new InvalidOperationException("Markoth native nail graph changed.");
                    }
                    if (source.name.StartsWith("Shot Slug Spear",StringComparison.Ordinal)) {
                        sword=source.GetComponents<PlayMakerFSM>().Single(f=>f.FsmName=="Control");
                        if(!new[]{"Pause","Poke Out","Fire","Dissipate","Recycle"}.All(n=>sword.FsmStates.Any(s=>s.Name==n))
                            || source.GetComponent<Rigidbody2D>()==null || !sword.FsmStates.Single(s=>s.Name=="Recycle").Actions.Any(a=>a.GetType().Name=="RecycleSelf"))
                            throw new InvalidOperationException("Gorb native spear graph changed.");
                    }
                    if(HostReflectedProjectile.RadianceNailName(source.name) && EncounterProfile.Selected.Family==EncounterProfile.AbsoluteRadiance) {
                        sword=HostReflectedProjectile.RadianceNail(source);
                        if(sword==null || source.GetComponent<Rigidbody2D>()==null
                            || !sword.FsmStates.Any(s=>s.Name=="Recycle" && s.Actions.Any(a=>a.GetType().Name=="RecycleSelf")))
                            throw new InvalidOperationException("Native Radiance sword flight/recycle contract changed.");
                    }
                    var firebat = category == AttackCategory.Projectile && IsGrimmFirebat(source.transform.parent)
                        ? source.transform.parent.GetComponents<PlayMakerFSM>().Single(f => f.FsmName == "Control") : null;
                    if (firebat != null && !new[] { "Fire", "Impact", "Impact End", "Recycle" }.All(n => firebat.FsmStates.Any(s => s.Name == n))) throw new InvalidOperationException("Native firebat impact contract changed.");
                    if (firebat != null && firebat.name.StartsWith("Nightmare Firebat",StringComparison.Ordinal) && !firebat.FsmStates.Any(s=>s.Name=="Tween Up")) throw new InvalidOperationException("Native nightmare firebat flight contract changed.");
                    var debris=category==AttackCategory.Projectile?HostReflectedProjectile.FallingDebris(source):null;
                    if(debris!=null && (!new[]{"Idle","Check Direct","Break","Recycle"}.All(n=>debris.FsmStates.Any(s=>s.Name==n))
                        || source.GetComponent<Rigidbody2D>()==null || !debris.FsmStates.Single(s=>s.Name=="Break").Actions.OfType<HutongGames.PlayMaker.Actions.SetCollider>().Any(a=>!a.active.Value)))
                        throw new InvalidOperationException("Native falling debris contract changed.");
                    var stinger=category==AttackCategory.Projectile && EncounterProfile.Selected.Family==EncounterProfile.HiveKnight?source.GetComponent<HiveKnightStinger>():null;
                    var hiveBee=category==AttackCategory.Projectile && EncounterProfile.Selected.Family==EncounterProfile.HiveKnight?HostReflectedProjectile.HiveBee(source):null;
                    if((stinger!=null || hiveBee!=null) && source.GetComponent<Rigidbody2D>()==null) throw new InvalidOperationException("Native Hive projectile body missing.");
                    if(hiveBee!=null && (!new[]{"Init","Idle","Pause","Swarm Start","Swarm","Spell Death","Reset"}.All(n=>hiveBee.FsmStates.Any(s=>s.Name==n))
                        || !hiveBee.FsmStates.Single(s=>s.Name=="Spell Death").Actions.OfType<HutongGames.PlayMaker.Actions.SetCircleCollider>().Any(a=>!a.active.Value)))
                        throw new InvalidOperationException("Native Hive bee cleanup contract changed.");
                    var beeRecoil=hiveBee==null?null:source.GetComponent<Recoil>();
                    if(hiveBee!=null && (beeRecoil==null || shape is not CircleCollider2D)) throw new InvalidOperationException("Native Hive bee recoil receiver changed.");
                    entries.Add(shape.GetInstanceID(), new Entry { Damage = source, Shape = shape, BodyRenderer = visibleBodies && source.GetComponent<HealthManager>() != null ? source.GetComponent<Renderer>() : null, Category = category.Value,
                        RadianceNail=EncounterProfile.Selected.Family==EncounterProfile.AbsoluteRadiance && HostReflectedProjectile.RadianceNailName(source.name),
                        MarkothNail=source.name.StartsWith("Shot Markoth Nail",StringComparison.Ordinal), Bullet = source.GetComponent<EnemyBullet>(), Paint=source.GetComponent<PaintBullet>(), Sword = sword, Firebat = firebat,Debris=debris,Stinger=stinger,HiveBee=hiveBee,BeeRecoil=beeRecoil });
                    if (source.name.StartsWith("Shockwave Spurt", StringComparison.Ordinal)) {
                        var renderer = source.GetComponent<Renderer>() ?? source.GetComponentInChildren<Renderer>(true);
                        if (renderer != null) { var entry = entries[shape.GetInstanceID()]; entry.Foreground = renderer.transform; entry.OriginalDepth = renderer.transform.position.z;
                            entry.ForegroundRenderer = renderer; entry.OriginalSortingLayer = renderer.sortingLayerID; entry.OriginalSortingOrder = renderer.sortingOrder;
                            if (shockwaveDepthSamples++ < 32)
                                log?.Invoke("HK_SHOCKWAVE_DEPTH name=" + source.name + " renderer=" + renderer.name + " nativeZ=" + entry.OriginalDepth + " nativeSortingOrder=" + renderer.sortingOrder); }
                    }
                    if (registrationSamples++ < 64)
                        log?.Invoke("HK_POOLED_REGISTER name=" + source.name + " scene=" + source.gameObject.scene.name + " shape=" + shape.GetType().Name);
                }
        }
        removed.Clear();
        foreach(var pair in entries)if(pair.Value.Shape==null || pair.Value.Damage==null)removed.Add(pair.Key);
        foreach(var id in removed)entries.Remove(id);
        HostHeapCost.Mark(5, ref heap);
        var result=sampled;result.Clear();
        foreach (var pair in entries)
        {
            var e = pair.Value; long nativeActivation = registry.Activation(e.Damage);
            // Installed HeroBox reads DamageHero as data; its enabled flag is
            // not consulted. The native collider/object owns the damage window.
            bool enabled = alive && e.Admitted && e.Damage.damageDealt > 0 && e.Shape.enabled && e.Shape.gameObject.activeInHierarchy
                && (e.BodyRenderer == null || e.BodyRenderer.enabled && e.BodyRenderer.gameObject.activeInHierarchy);
            if (e.Firebat != null) enabled &= e.Firebat.ActiveStateName != "Impact" && e.Firebat.ActiveStateName != "Impact End" && e.Firebat.ActiveStateName != "Dissipate" && e.Firebat.ActiveStateName != "Stop Particles" && e.Firebat.ActiveStateName != "Recycle";
            if (e.Bullet != null) enabled &= (bool)BulletActive.GetValue(e.Bullet);
            if (e.Paint != null) enabled &= HostReflectedProjectile.PaintIsActive(e.Paint);
            // A reused orb is active in Pause before Init restores its pose and
            // renderer. Only the authored flight states own a live strike.
            if (e.Sword != null && e.Sword.FsmName == "Orb Control")
                enabled &= RadianceAttackWindows.OrbFlight(e.Sword.ActiveStateName,HostReflectedProjectile.RadianceOrbName(e.Sword.name));
            if (e.Sword != null && e.MarkothNail) enabled &= e.Sword.ActiveStateName == "Fire";
            OrderForeground(e, enabled);
            if (activeOnly && !enabled) {
                e.Activation = e.Generation.Observe(false, nativeActivation);
                if (e.Active || e.NativeActivation != nativeActivation)
                    log?.Invoke("HK_POOLED_ATTACK id=" + pair.Key + " activation=" + e.Activation + " active=False category=" + e.Category
                        + " name=" + e.Damage.name + " action=0 frame=" + Time.frameCount
                        + (e.Sword?.FsmName=="Orb Control" ? " nativeOrbState="+e.Sword.ActiveStateName.Replace(' ','_') : ""));
                e.Active = false; e.NativeActivation = nativeActivation;
                continue; // Missing volumes deactivate NS fixtures; native generation history remains owned here.
            }
            if (enabled && outside?.Invoke(e.Damage, e.Shape.bounds) == true) {
                // Preserve native identity/receipts even when no polygon crosses
                // the bridge. A pending counter must still match this activation.
                var omitted = new AttackVolume { Id = pair.Key, Damage = e.Damage.damageDealt, Activation = e.Activation,
                    Active = enabled, Category = e.Category, ConsumeOnContact = e.Bullet != null || e.Paint!=null || e.Sword != null || e.Firebat != null ||e.Debris!=null ||e.HiveBee!=null ||e.Stinger!=null };
                decorate?.Invoke(omitted, e.Damage);
                Observe(e, pair.Key, omitted, nativeActivation);
                continue;
            }
            System.Collections.Generic.IReadOnlyList<Vector2> points;
            float radius = 1;
            if (e.Shape is BoxCollider2D box && box.edgeRadius == 0)
            { var h=box.size*.5f; boxPoints[0]=new Vector2(-h.x,-h.y);boxPoints[1]=new Vector2(h.x,-h.y);boxPoints[2]=h;boxPoints[3]=new Vector2(-h.x,h.y);points=boxPoints; }
            else if (e.Shape is PolygonCollider2D polygon && polygon.pathCount == 1) {polygon.GetPath(0,polygonPoints);points=polygonPoints;}
            else if (e.Shape is CircleCollider2D circle)
            { points = UnitCircle; radius = circle.radius; }
            else throw new InvalidOperationException("Unaudited pooled collider shape: " + e.Shape.GetType().Name);
            // Frame arrays escape to the transport thread and remain immutable.
            // Allocate only that final array, not one array/native call per vertex.
            var matrix = e.Shape.transform.localToWorldMatrix;
            var offset = e.Shape.offset;
            // Boxes/circles have fixed topology. Exact transform/offset/size
            // equality permits sharing an immutable contour, even across old
            // queued frames. Polygon paths remain freshly sampled and built.
            var geometryKey = (matrix, offset, radius, e.Shape is BoxCollider2D boxShape ? boxShape.size : Vector2.zero);
            bool cacheable = e.Shape is BoxCollider2D || e.Shape is CircleCollider2D;
            float[] world = Array.Empty<float>();
            bool reused = cacheable && e.Geometry.TryGet(geometryKey, out world);
            if (!reused) {
                world = new float[points.Count * 2];
                for (int i = 0; i < points.Count; i++) {
                    var w = matrix.MultiplyPoint3x4(points[i] * radius + offset);
                    world[i * 2] = w.x; world[i * 2 + 1] = w.y;
                }
                if (cacheable) e.Geometry.Publish(geometryKey, world);
                contourBuilds++; contourFloats += world.Length;
            } else contourReuses++;
            if (reused && HostFrameProbe.Enabled && Time.frameCount % 144 == 0) {
                for (int i = 0; i < points.Count; i++) {
                    var expected = matrix.MultiplyPoint3x4(points[i] * radius + offset);
                    if (world[i * 2] != expected.x || world[i * 2 + 1] != expected.y)
                        throw new InvalidOperationException("Retained contour differs from current native geometry.");
                }
            }
            if ((e.Shape is CircleCollider2D ? circleMatrixSamples < 8 : matrixSamples < 32) && enabled && YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures) {
                if (e.Shape is CircleCollider2D) circleMatrixSamples++; else matrixSamples++;
                float error = 0;
                for (int i = 0; i < points.Count; i++) {
                    var expected = e.Shape.transform.TransformPoint(points[i] * radius + offset);
                    error = Mathf.Max(error, Vector2.Distance(expected, new Vector2(world[i * 2], world[i * 2 + 1])));
                }
                if (float.IsNaN(error) || error > .0001f) throw new InvalidOperationException("Host collider matrix differs from native Transform.");
                log?.Invoke("HK_POLYGON_MATRIX_VERIFIED id=" + pair.Key + " vertices=" + points.Count + " maxError=" + error + " frame=" + Time.frameCount);
            }
            var volume = new AttackVolume { Id = pair.Key, Damage = e.Damage.damageDealt, Activation = e.Generation.Preview(enabled,nativeActivation), Active = enabled, Category = e.Category, ConsumeOnContact = e.Bullet != null || e.Paint!=null || e.Sword != null || e.Firebat != null ||e.Debris!=null ||e.HiveBee!=null ||e.Stinger!=null,
                Points = world };
            if (circlePrimitives && e.Shape is CircleCollider2D) {
                var x = matrix.MultiplyVector(new Vector3(radius, 0, 0));
                var y = matrix.MultiplyVector(new Vector3(0, radius, 0));
                float r = x.magnitude;
                if (r > 0 && Mathf.Abs(r-y.magnitude) <= r*.00001f && Mathf.Abs(Vector3.Dot(x,y)) <= r*r*.00001f) {
                    var center = matrix.MultiplyPoint3x4(offset);
                    bool primitive = AttackVolumes.TryUseNativeCircle(volume, center.x, center.y, r);
                    if (!primitive && enabled && primitiveFallbackSamples++ < 8)
                        log?.Invoke("HK_NATIVE_CIRCLE_FALLBACK id=" + pair.Key + " activation=" + e.Activation + " radius=" + r
                            + " center=" + center + " axisX=" + x + " axisY=" + y + " nativeContourRetained=True frame=" + Time.frameCount);
                    if (primitive && enabled && primitiveSamples < 8 && YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures) {
                        primitiveSamples++;
                        var b = e.Shape.bounds;
                        if (Vector2.Distance(center,b.center) > .0002f || Mathf.Abs(b.extents.x-r) > .0002f || Mathf.Abs(b.extents.y-r) > .0002f)
                            throw new InvalidOperationException("Native pooled circle metadata differs from installed collider bounds.");
                        log?.Invoke("HK_NATIVE_CIRCLE_VERIFIED id=" + pair.Key + " radius=" + r + " nativeBounds=True contourVertices=" + points.Count + " frame=" + Time.frameCount);
                    }
                }
            }
            // Native Markoth nails survive a player collision. Preserve their
            // trajectory and hold only the translated damage activation.
            if (e.Sword != null && e.MarkothNail) { volume.ParryCooldown = 1; volume.CooldownOnDamage = true; }
            HostHeapCost.Mark(6, ref heap);
            decorate?.Invoke(volume, e.Damage);
            HostHeapCost.Mark(7, ref heap);
            if (projectileGeometrySamples < 32 && volume.Active && YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures
                && (e.Firebat != null || e.Damage.name.StartsWith("Flameball Grimmballoon", StringComparison.Ordinal))) {
                projectileGeometrySamples++;
                var root = e.Firebat != null ? e.Firebat.transform : e.Damage.transform;
                var renderer = root.GetComponent<Renderer>();
                log?.Invoke("HK_GRIMM_PROJECTILE_GEOMETRY id=" + pair.Key + " activation=" + volume.Activation + " root=" + root.name.Replace(' ', '_')
                    + " category=" + volume.Category + " localOffset=" + e.Shape.offset + " collider=" + e.Shape.bounds + " visual=" + (renderer == null ? "none" : renderer.bounds.ToString())
                    + " firstX=" + volume.Points[0] + " firstY=" + volume.Points[1] + " nativeTransform=True frame=" + Time.frameCount);
            }
            double area = 0;
            for (int i = 0; i < volume.Points.Length; i += 2) {
                int n = (i + 2) % volume.Points.Length;
                area += (double)volume.Points[i] * volume.Points[n + 1] - (double)volume.Points[i + 1] * volume.Points[n];
            }
            bool collapsed = collapseEmptyGeometry && Math.Abs(area) < 0.00001;
            if (collapsed) {
                if (e.Active) log?.Invoke("HK_NATIVE_ATTACK_COLLAPSED id=" + pair.Key + " name=" + e.Damage.name + " frame=" + Time.frameCount + " nativeScale=" + e.Shape.transform.lossyScale);
                volume.Active = false;
            }
            Observe(e, pair.Key, volume, nativeActivation);
            // A native parent strike retains its volley injury retirement across
            // collider gaps; this inactive metadata creates no damaging dealer.
            if (!collapsed && (!activeOnly || volume.Active || volume.BlocksProjectiles || volume.ActionId>0 && volume.DamageGroupId>0)) result.Add(volume);
        }
        HostHeapCost.Mark(8, ref heap);
        if (HostFrameProbe.Enabled && Time.realtimeSinceStartup >= nextContourReport) {
            nextContourReport = Time.realtimeSinceStartup + 2;
            log?.Invoke("HK_CONTOUR_REUSE reused=" + contourReuses + " built=" + contourBuilds + " allocatedFloats=" + contourFloats + " retainedEntries=" + entries.Count + " frame=" + Time.frameCount);
            contourReuses = contourBuilds = contourFloats = 0;
        }
        return result.ToArray();
    }
    private void Observe(Entry e, int id, AttackVolume volume, long nativeActivation)
    {
            // Adapter windows are part of the attack's lifetime. A native child
            // can remain enabled when an interrupted FSM skips its cleanup state.
            if (e.ActionId != volume.ActionId) e.Generation.Observe(false, nativeActivation);
            e.Activation = e.Generation.Observe(volume.Active || volume.BlocksProjectiles, nativeActivation);
            if (volume.Active && e.Firebat != null) {
                e.FirebatLease ??= e.Firebat.GetComponent<NativeProjectileSequenceLease>() ?? e.Firebat.gameObject.AddComponent<NativeProjectileSequenceLease>();
                e.FirebatGeneration = e.FirebatLease.Generation;
            }
            volume.Activation = e.Activation;
            if (e.Active != volume.Active || e.NativeActivation != nativeActivation || e.ActionId != volume.ActionId)
                log?.Invoke("HK_POOLED_ATTACK id=" + id + " activation=" + e.Activation + " active=" + volume.Active + " category=" + e.Category
                    + " name=" + e.Damage.name + " action=" + volume.ActionId + " frame=" + Time.frameCount
                    + (e.Sword?.FsmName=="Orb Control" ? " nativeOrbState="+e.Sword.ActiveStateName.Replace(' ','_') : ""));
            e.Active = volume.Active; e.NativeActivation = nativeActivation; e.ActionId = volume.ActionId;
            e.CrimsonRain=e.RadianceNail
                && ProjectileDefense.CounterDeflects(volume.Traits) && volume.CounterInjuryEntity==0;
    }
    private int beeMeleeSamples;
    private int debrisMeleeSamples;
    private readonly Collider2D[] meleeOverlaps=new Collider2D[256];
    internal void ObservedDebrisMelee(YiGodhome.Bridge.Pose pose,Func<int,long,bool> reflect) {
        if(!EncounterProfile.Selected.ArmorAndHead || GameManager.instance.IsGamePaused())return;
        foreach(var window in pose.Melee) {
            if(window.Swing<=0)continue;
            // Native Physics2D shape intersection, including rotated debris;
            // never reflect a rock merely because its enclosing AABB overlaps.
            int count=Physics2D.OverlapBox(new Vector2(window.X,window.Y),new Vector2(window.Width,window.Height),0,
                new ContactFilter2D {useTriggers=true},meleeOverlaps);
            if(count==meleeOverlaps.Length)throw new InvalidOperationException("Melee debris intersection capacity exceeded.");
            for(int i=0;i<count;i++) {
                var shape=meleeOverlaps[i];
                if(shape==null || !entries.TryGetValue(shape.GetInstanceID(),out var e) || e.Debris==null
                    || e.Damage==null || !e.Active || e.ResolvedActivation==e.Activation || e.Reflection?.IsReflected==true
                    || !shape.isActiveAndEnabled || !e.Damage.gameObject.activeInHierarchy
                    || e.NativeActivation!=registry.Activation(e.Damage))continue;
                if(e.MeleeActivation!=e.Activation){e.MeleeActivation=e.Activation;e.MeleeSwing=0;}
                if(e.MeleeSwing==window.Swing)continue;
                e.MeleeSwing=window.Swing; // Refusal/uncertainty cannot replay the swing.
                bool accepted=reflect(shape.GetInstanceID(),e.Activation);
                if(debrisMeleeSamples++<64)log?.Invoke("HK_DEBRIS_MELEE_REFLECT id="+shape.GetInstanceID()+" activation="+e.Activation
                    +" swing="+window.Swing+" sourceCollider="+window.Collider+" accepted="+accepted+" nativeIntersection=True frame="+Time.frameCount);
            }
        }
    }
    internal void ObservedMelee(YiGodhome.Bridge.Pose pose) {
        foreach(var pair in entries) {
            var e=pair.Value;
            if(e.MeleeMotionPending && Time.time>=e.MeleeMotionAt) {
                e.MeleeMotionPending=false;
                if(e.HiveBee!=null && e.HiveBee.ActiveStateName=="Swarm" && e.Activation==e.MeleeMotionActivation && e.Shape!=null && e.Shape.isActiveAndEnabled) {
                    Vector2 delta=(Vector2)e.Shape.bounds.center-e.MeleeMotionStart;
                    log?.Invoke("HK_HIVE_NATIVE_MELEE_MOTION id="+pair.Key+" activation="+e.Activation+" swing="+e.MeleeMotionSwing+" direction="+e.MeleeMotionDirection
                        +" deltaX="+delta.x+" deltaY="+delta.y+" nativeRecoiling="+e.BeeRecoil?.IsRecoiling+" nativeState=Swarm frame="+Time.frameCount);
                }
            }
            if(e.Reflection?.IsReflected==true || e.HiveBee==null || e.BeeRecoil==null || e.Damage==null || e.Shape==null || !e.Active || e.ResolvedActivation==e.Activation || e.HiveBee.ActiveStateName!="Swarm"
                || !e.Damage.isActiveAndEnabled || !e.Shape.isActiveAndEnabled) continue;
            if(e.MeleeActivation!=e.Activation) {e.MeleeActivation=e.Activation;e.MeleeSwing=0;}
            var bounds=e.Shape.bounds;
            if(Math.Abs(bounds.extents.x-bounds.extents.y)>.001f) throw new InvalidOperationException("Native Hive bee circle became elliptical.");
            foreach(var window in pose.Melee) {
                if(window.Swing<=0 || e.MeleeSwing==window.Swing) continue;
                float x=Mathf.Clamp(bounds.center.x,window.X-window.Width*.5f,window.X+window.Width*.5f);
                float y=Mathf.Clamp(bounds.center.y,window.Y-window.Height*.5f,window.Y+window.Height*.5f);
                if((new Vector2(x,y)-(Vector2)bounds.center).sqrMagnitude>bounds.extents.x*bounds.extents.x) continue;
                e.MeleeSwing=window.Swing; // A native refusal is never replayed.
                bool before=e.BeeRecoil.IsRecoiling;
                Vector2 start=bounds.center;
                e.BeeRecoil.RecoilByDamage(new HitInstance {Source=HeroController.instance.gameObject,AttackType=AttackTypes.Nail,
                    Direction=window.Direction,MagnitudeMultiplier=1,Multiplier=1});
                if(!before && e.BeeRecoil.IsRecoiling && SessionConfiguration.Current.RunFixtures && SessionConfiguration.Current.Has("trace-probe") && beeMeleeSamples<64) {
                    e.MeleeMotionPending=true;e.MeleeMotionStart=start;e.MeleeMotionAt=Time.time+.08f;e.MeleeMotionDirection=window.Direction;
                    e.MeleeMotionActivation=e.Activation;e.MeleeMotionSwing=window.Swing;
                }
                if(beeMeleeSamples++<64) log?.Invoke("HK_HIVE_NATIVE_MELEE id="+pair.Key+" activation="+e.Activation+" swing="+window.Swing
                    +" sourceCollider="+window.Collider+" sourceActivation="+window.Activation+" direction="+window.Direction
                    +" previouslyRecoiling="+before+" nativeRecoiling="+e.BeeRecoil.IsRecoiling+" nativeState="+e.HiveBee.ActiveStateName
                    +" healthReceiver=False frame="+Time.frameCount);
            }
        }
    }
    internal static void RefreshForeground() { if (current != null) foreach (var e in current.entries.Values) OrderForeground(e,e.Active); }
    private static void OrderForeground(Entry e, bool enabled)
    {
            if (e.Foreground != null && HeroController.instance != null) {
                var position = e.Foreground.position;
                var actor = VisualPresenter.ActorRenderer;
                position.z = enabled ? Math.Min(e.OriginalDepth, (actor != null ? actor.transform.position.z : HeroController.instance.transform.position.z) - 0.01f) : e.OriginalDepth;
                e.Foreground.position = position;
                if (e.ForegroundRenderer != null) {
                    int layer = e.OriginalSortingLayer, order = e.OriginalSortingOrder;
                    if (enabled && actor != null) {
                        int value = SortingLayer.GetLayerValueFromID(layer), actorValue = SortingLayer.GetLayerValueFromID(actor.sortingLayerID);
                        if (value < actorValue || value == actorValue && order < actor.sortingOrder) { layer = actor.sortingLayerID; order = actor.sortingOrder; }
                    }
                    e.ForegroundRenderer.sortingLayerID = layer; e.ForegroundRenderer.sortingOrder = order;
                }
            }
    }
    public void Dispose() { foreach (var e in entries.Values) {
        if (e.Foreground != null) { var p = e.Foreground.position; p.z = e.OriginalDepth; e.Foreground.position = p; }
        if (e.ForegroundRenderer != null) { e.ForegroundRenderer.sortingLayerID = e.OriginalSortingLayer; e.ForegroundRenderer.sortingOrder = e.OriginalSortingOrder; }
        if (e.Reflection != null) { e.Reflection.Retire(); UnityEngine.Object.Destroy(e.Reflection); }
    } harmony.UnpatchSelf(); registry.Dispose(); entries.Clear(); classified.Clear(); if (current == this) current = null; }
}
