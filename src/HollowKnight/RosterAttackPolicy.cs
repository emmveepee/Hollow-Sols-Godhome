using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YiGodhome.Bridge;

namespace YiGodhome;

// Small authored-state adapters. Shared native receivers still resolve Yi's
// defense, and HK owns animation/recovery; no replacement boss combat loop.
internal sealed class RosterAttackPolicy : IDisposable
{
    private sealed class Body { internal NativeFsmStepLease? VengeflyHold; internal float VengeflyReleaseAt; internal HealthManager Health = null!; internal PlayMakerFSM Control = null!; internal int ActiveLayer = -1, AnticipationFrame = -1; internal bool Kick, Crimson, Windup, Descending, AuraReady, SoulFinal, SoulDive, AirDash, Balloon, Pillar, Slash; internal Collider2D[] SlashShapes = Array.Empty<Collider2D>(); internal float Anticipation, SoulDiveAckUntil; internal long Action, SoulDiveAckAction, SoulDiveCountered; internal string State = ""; internal DamageHero[] SoulLandings=Array.Empty<DamageHero>(); }
    private sealed class CrimsonPart {
        internal DamageHero Source = null!;
        internal Transform Root = null!;
        internal long Native, Action;
        internal bool Active, Retired, Balloon;
        internal long SourceActivation = -1;
        internal MeshRenderer[] Renderers = Array.Empty<MeshRenderer>();
        internal bool Present => Active && !Retired && Source != null && Source.gameObject.activeInHierarchy
            && Source.damageDealt > 0 && Source.GetComponent<Collider2D>()?.enabled == true;
    }
    private readonly Dictionary<int, CrimsonPart> crimsonParts = new();
    private readonly List<int> deadCrimson = new();
    private readonly HashSet<int> harmlessUppercuts = new();
    private readonly Dictionary<int, Body> bodies = new Dictionary<int, Body>();
    private readonly EncounterProfile profile, nativeProfile;
    private readonly Dictionary<int,WatcherSequence> watcherSequences=new();
    private readonly Action<string>? log;
    private long nextAction, nextVisual, visualSequence;
    private readonly Dictionary<HutongGames.PlayMaker.FsmStateAction,GameObject> soulOrbitSpawns=new();
    private int soulOrbitFrame=-1;
    private long soulOrbitGroup;
    private int soulOrbitCount;
    private int soulStaggerSamples;
    private MarkothShieldPolicy? markoth;
    private HealthManager? markothBody;
    private HornetAttackPolicy? hornet;
    private readonly NativeAuthoredMoves authored;
    private AbsoluteRadianceCombat? radiance;
    private NativeWeaponAttackPolicy? weapons;
    private NativeRingAttackPolicy? rings;
    internal RosterAttackPolicy(EncounterProfile profile, Action<string>? log) { nativeProfile = profile; this.profile = profile.Family; this.log = log; authored = new NativeAuthoredMoves(profile, AllocateAction, log); }
    internal void SetStandableFloor(float? floor)=>radiance?.SetStandableFloor(floor);
    internal bool OutsideRadianceNail(DamageHero source,Bounds bounds,long nativeActivation)=>radiance?.OutsideNail(source,bounds,nativeActivation)==true;
    internal long AllocateAction() => ++nextAction;
    internal bool ScriptedSkipCounter=>authored.ScriptedSkipCounter;
    internal bool ScriptedCounterPrecharge=>authored.ScriptedCounterPrecharge || weapons?.Precharge==true;
    internal NativeSpawnStrike SpawnStrike(HutongGames.PlayMaker.FsmStateAction source,GameObject prefab) {
        if(radiance?.RainSpawn(source,prefab)==true)return new NativeSpawnStrike(AllocateAction(),AttackTraits.Crimson);
        if((profile==EncounterProfile.SoulMaster || profile==EncounterProfile.SoulTyrant) && prefab.name=="Shockwave Wave" && source.State.Name=="Quake Waves") {
            var body=bodies.Values.SingleOrDefault(b=>b.Control.Fsm==source.Fsm);
            if(body!=null && body.Action>0) {
                log?.Invoke("HK_SOUL_LANDING_WAVE_STRIKE entity="+body.Health.GetInstanceID()+" action="+body.Action+" nativeDiveAndWave=True ordinaryWave=True frame="+Time.frameCount);
                return new NativeSpawnStrike(body.Action,AttackTraits.None);
            }
        }
        if(soulOrbitSpawns.TryGetValue(source,out var ownedPrefab) && ownedPrefab==prefab) {
            if(soulOrbitFrame!=Time.frameCount) {
                soulOrbitFrame=Time.frameCount;soulOrbitGroup=AllocateAction();
                log?.Invoke("HK_SOUL_ORBIT_VOLLEY group="+soulOrbitGroup+" owner="+source.Fsm.GameObject.GetInstanceID()+" nativeSpawns="+soulOrbitCount+" independentDefense=True frame="+Time.frameCount);
            }
            return new NativeSpawnStrike(0,AttackTraits.None,soulOrbitGroup);
        }
        return authored.SpawnStrike(source,prefab);
    }
    internal bool SuppressDamage(DamageHero damage)=>authored.SuppressDamage(damage) || hornet?.SuppressDamage(damage)==true;
    internal HealthManager? BodyContactOwner(DamageHero damage)=>authored.BodyContactOwner(damage);
    private bool Mantis => profile == EncounterProfile.Mantis || profile == EncounterProfile.Sisters;
    internal void Register(HealthManager health)
    {
        if (!nativeProfile.IsBossName(health.name) || bodies.ContainsKey(health.GetInstanceID()) || authored.Owns(health)) return;
        NativeBossContracts.Capture(health,log);
        if(profile==EncounterProfile.ElderHu) {rings??=new NativeRingAttackPolicy(AllocateAction,log);rings.Register(health);return;}
        if(profile==EncounterProfile.Galien) {weapons??=new NativeWeaponAttackPolicy(AllocateAction,log);weapons.Register(health);return;}
        if(profile==EncounterProfile.AbsoluteRadiance) {radiance??=new AbsoluteRadianceCombat(health,AllocateAction,log);return;}
        if (authored.Register(health)) return;
        if (profile == EncounterProfile.Markoth) { markothBody = health; markoth ??= new MarkothShieldPolicy(health, log); return; }
        if (profile == EncounterProfile.HornetProtector || profile == EncounterProfile.HornetSentinel) { hornet ??= new HornetAttackPolicy(health, log); return; }
        bool soul = profile == EncounterProfile.SoulMaster || profile == EncounterProfile.SoulTyrant;
        bool soulFinal = soul && health.name.EndsWith("Phase2", StringComparison.Ordinal);
        if(soul && !soulFinal) {
            var summon=health.GetComponents<PlayMakerFSM>().SelectMany(f=>f.FsmStates).Single(s=>s.Name=="HS Orb").Actions
                .OfType<HutongGames.PlayMaker.Actions.SendEventByName>().Single(a=>a.sendEvent.Value=="SPINNER SUMMON");
            var spinner=FSMUtility.GetSafe(summon.eventTarget.gameObject,summon);
            if(spinner==null || spinner.gameObject.scene!=health.gameObject.scene || spinner.GetComponent<HealthManager>()!=null)
                throw new InvalidOperationException("Native Soul orbit event ownership changed.");
            var spawn=spinner.GetComponents<PlayMakerFSM>().Single(f=>f.FsmName=="Summon Orbs").FsmStates.Single(s=>s.Name=="Spawn");
            var emissions=spawn.Actions.OfType<HutongGames.PlayMaker.Actions.SpawnObjectFromGlobalPool>().ToArray();
            log?.Invoke("HK_SOUL_ORBIT_EMISSION_CONTRACT entity="+health.GetInstanceID()+" count="+emissions.Length+" nativeEventOwner=True");
            if(emissions.Length!=(profile==EncounterProfile.SoulTyrant?6:4) || emissions.Any(a=>a.gameObject.Value==null || (a.gameObject.Value.name!="Mage Orb Over" && a.gameObject.Value.name!="Mage Orb")) || soulOrbitSpawns.Count!=0)
                throw new InvalidOperationException("Native Soul orbit volley contract changed.");
            soulOrbitCount=emissions.Length;
            foreach(var emission in emissions) {
                soulOrbitSpawns.Add(emission,emission.gameObject.Value);
                if(SessionConfiguration.Current.RunFixtures)log?.Invoke("HK_SOUL_ORBIT_SPAWN_BOUND owner="+spinner.GetInstanceID()+" prefab="+emission.gameObject.Value.name.Replace(' ','_')+" nativeEventReference=True");
            }
        }
        bool grimm = profile == EncounterProfile.Grimm || profile == EncounterProfile.NightmareGrimm;
        bool gruz = profile == EncounterProfile.Gruz, vengefly = profile == EncounterProfile.Vengefly;
        if (!Mantis && profile != EncounterProfile.Watchers && !soul && !grimm && !gruz && !vengefly) return;
        string[] required = vengefly ? new[] { "Swoop L", "Swoop R", "Swoop Rise", "Idle" }
            : gruz ? new[] { "Charge", "Slam Antic", "Slam Up", "Slam Down", "Slam End", "Super End" }
            : Mantis ? new[] { "Dash Antic", "Dash Attack", "Dash Recover", "Dash Leave", "Dstab Attack", "Dstab Land" }
            : soulFinal ? new[] { "Quake Antic", "Quake Down", "Quake Land" }
            : soul ? new[] { "Quake Down", "Charge Antic", "Charge Left", "Charge Right" }
            : grimm ? new[] { "Slash 1", "Uppercut Up", "G Dash", "AD Fire" }
            : new[] { "Charge", "Bounce Air", "Charge Recover" };
        var control = health.GetComponents<PlayMakerFSM>().Single(f => required.All(s => f.FsmStates.Any(state => state.Name == s)));
        int activeLayer = profile == EncounterProfile.Watchers
            ? control.FsmStates.Single(s => s.Name == "Roar End").Actions.OfType<HutongGames.PlayMaker.Actions.SetLayer>().Single().layer : -1;
        var slashShapes = grimm ? health.GetComponentsInChildren<PolygonCollider2D>(true).Where(c => c.name == "Slash1" || c.name == "Slash2" || c.name == "Slash3").Cast<Collider2D>().ToArray() : Array.Empty<Collider2D>();
        if (grimm && (slashShapes.Length != 3 || !new[] { "Slash Recover", "Slash End", "Uppercut Antic", "Uppercut Up", "UP Explode" }.All(n => control.FsmStates.Any(s => s.Name == n))))
            throw new InvalidOperationException("Native Grimm slash continuation contract changed.");
        DamageHero[] soulLandings=Array.Empty<DamageHero>();
        if(soulFinal) {
            var activation=control.FsmStates.Single(s=>s.Name=="Quake Land").Actions.OfType<HutongGames.PlayMaker.Actions.ActivateGameObject>()
                .Single(a=>a.gameObject.GameObject?.Name=="Quake Hit");
            var part=FSMUtility.GetSafe(activation.gameObject,activation);
            soulLandings=part?.GetComponentsInChildren<DamageHero>(true)??Array.Empty<DamageHero>();
            if(part==null || part.name!="Quake Hit" || !activation.activate.Value || part.GetComponentInParent<HealthManager>()!=health
                || soulLandings.Length!=(profile==EncounterProfile.SoulTyrant?2:1)
                || soulLandings.Any(d=>d.name!="Quake Hit" || d.GetComponentInParent<HealthManager>()!=health || d.GetComponent<Collider2D>()==null))
                throw new InvalidOperationException("Native Soul final landing strike ownership changed.");
            foreach(var dealer in soulLandings)log?.Invoke("HK_SOUL_FINAL_LANDING_BOUND entity="+health.GetInstanceID()+" dealer="+dealer.GetInstanceID()+" collider="+dealer.GetComponent<Collider2D>().GetInstanceID()
                +" body="+health.GetComponent<Collider2D>().GetInstanceID()+" retainedDiveStrike=True");
        }
        bodies.Add(health.GetInstanceID(), new Body { Health = health, Control = control, ActiveLayer = activeLayer, SlashShapes = slashShapes, SoulFinal = soulFinal, SoulLandings=soulLandings });
        if(profile==EncounterProfile.Watchers)watcherSequences.Add(health.GetInstanceID(),new WatcherSequence(health,control,AllocateAction,log));
        log?.Invoke("HK_ROSTER_ATTACK_POLICY entity=" + health.GetInstanceID() + " boss=" + profile.Key + " nativeFSM=" + control.FsmName);
    }
    internal void Update()
    {
        NativeBossContracts.RefreshQueries(log);
        authored.Update();
        radiance?.Update();
        weapons?.Update();
        rings?.Update();
        deadCrimson.Clear();
        foreach (var pair in crimsonParts) if (pair.Value.Source == null || pair.Value.Root == null) deadCrimson.Add(pair.Key);
        foreach (var id in deadCrimson) crimsonParts.Remove(id);
        deadCrimson.Clear();
        markoth?.Update(); markoth?.Action(ref nextAction);
        hornet?.Update(ref nextAction);
        foreach (var id in bodies.Keys.Where(id => bodies[id].Health == null).ToArray()) {
            bodies[id].VengeflyHold?.Dispose();bodies.Remove(id);
        }
        foreach (var body in bodies.Values) {
            if(body.VengeflyHold!=null) {
                if(body.Health.isDead || body.Health.hp<=0 || !body.Health.gameObject.activeInHierarchy) {
                    body.VengeflyHold.Dispose();body.VengeflyHold=null;body.Kick=false;continue;
                }
                var motion=body.Health.GetComponent<Rigidbody2D>();
                if(motion!=null)motion.linearVelocity=Vector2.zero;
                if(Time.time<body.VengeflyReleaseAt)continue;
                body.VengeflyHold.Dispose();body.VengeflyHold=null;
                body.Control.SetState("Swoop Rise");
                var flight=body.Control.FsmStates.Single(s=>s.Name=="Idle").Actions.OfType<HutongGames.PlayMaker.Actions.Tk2dPlayAnimation>().Single().clipName.Value;
                body.Health.GetComponent<tk2dSpriteAnimator>().Play(flight);
                log?.Invoke("HK_VENGEFLY_STUN_END entity="+body.Health.GetInstanceID()+" action="+body.Action+" nativeRise=True frame="+Time.frameCount);
            }
            if(watcherSequences.TryGetValue(body.Health.GetInstanceID(),out var watcher)) {
                watcher.Update();body.Action=watcher.Action;body.Kick=watcher.Kick;body.Windup=watcher.Present&&!watcher.Kick;body.Crimson=body.Descending=false;body.State=body.Control.ActiveStateName;
                continue;
            }
            string state = body.Control.ActiveStateName;
            bool soul = profile == EncounterProfile.SoulMaster || profile == EncounterProfile.SoulTyrant;
            bool grimm = profile == EncounterProfile.Grimm || profile == EncounterProfile.NightmareGrimm;
            bool gruz = profile == EncounterProfile.Gruz, vengefly = profile == EncounterProfile.Vengefly;
            bool slash = grimm && (state == "Slash Antic" || state == "Slash 1" || state == "Slash 2" || state == "Slash 3");
            bool airDash = grimm && (state == "AD Antic" || state == "AD Fire" || state == "AD Edge" || state == "GD Antic" || state == "G Dash");
            bool balloon = grimm && (state == "Balloon Antic" || state == "Fire Pause" || state == "Fire Repeat" || state.StartsWith("Fire Low ",StringComparison.Ordinal) || state.StartsWith("Fire Mid ",StringComparison.Ordinal) || state.StartsWith("Fire High ",StringComparison.Ordinal) || state == "End Pause" || state == "Balloon End");
            bool pillar = profile == EncounterProfile.NightmareGrimm && (state == "Pillar Antic" || state == "Pillar" || state == "Repeat?" || state == "Pillar End");
            bool bounce = gruz && HostContactSource.IsBounce(state);
            body.SoulDive=soul && !body.SoulFinal && (state=="Quake Antic" || state=="Quake Down");
            bool windup = vengefly ? false : gruz ? bounce && !HostContactSource.IsBodyAttack(state) : Mantis ? state == "Dash Antic" : soul ? state == (body.SoulFinal ? "Quake Antic" : "Charge Antic") || body.SoulDive && state=="Quake Antic" : grimm ? false : state == "Charge Start" || state == "Charge Antic" || state == "Jump Start" || state == "Jump Antic";
            bool active = vengefly ? state == "Swoop L" || state == "Swoop R" : gruz ? bounce && HostContactSource.IsBodyAttack(state) : Mantis ? state == "Dash Attack" : soul ? body.SoulFinal ? state == "Quake Down" : state == "Charge Left" || state == "Charge Right" || state=="Quake Down" : grimm ? false
                : state == "Charge" || state == "Jump Launch" || state == "Jump Air" || state == "Bounce" || state == "Bounce Air";
            bool descending = gruz && !bounce && HostContactSource.IsBodyAttack(state) || Mantis && state == "Dstab Attack" || soul && !body.SoulFinal && state == "Quake Down"
                || grimm && (state == "AD Fire" || state == "G Dash" || state == "Uppercut Up" || state == "Slash 1" || state == "Slash 2" || state == "Slash 3");
            if (grimm) { windup = state == "Slash Antic" || state == "AD Antic" || state == "Balloon Antic" || state == "Pillar Antic"; active = (airDash || slash) && !windup; }
            bool wasAirDash = body.AirDash;
            bool wasSlash = body.Slash;
            if (slash && !body.Slash || airDash || balloon || pillar || state == "Idle") harmlessUppercuts.Remove(body.Health.GetInstanceID());
            if (slash && !body.Slash) body.Action = ++nextAction;
            body.Slash = slash;
            if (grimm && (airDash || balloon || pillar) && !(body.AirDash || body.Balloon || body.Pillar)) body.Action = ++nextAction;
            body.AirDash = airDash; body.Balloon = balloon; body.Pillar = pillar;
            if (descending && !body.Descending && !(soul && body.SoulDive && (body.Windup || body.Crimson)) && !(grimm && (airDash || wasAirDash || slash || wasSlash))) body.Action = ++nextAction;
            body.Descending = descending;
            if ((windup || active) && !(body.Windup || body.Kick || body.Crimson) && !(grimm && (airDash || balloon || pillar || slash))) body.Action = ++nextAction;
            if(windup && !body.Windup) {body.Anticipation=0; body.AuraReady=false;}
            if(windup && body.AnticipationFrame!=Time.frameCount && GameManager.instance?.IsGamePaused() != true) {body.Anticipation+=Time.deltaTime;body.AnticipationFrame=Time.frameCount;}
            if(Mantis && !body.AuraReady && (active || windup && body.Anticipation>=.28f)) {
                body.AuraReady=true;
                log?.Invoke("HK_MANTIS_AURA_READY entity="+body.Health.GetInstanceID()+" action="+body.Action+" anticipation="+body.Anticipation+" frame="+Time.frameCount);
            }
            body.Windup = windup; body.Kick = !body.SoulFinal && !body.SoulDive && active; body.Crimson = (body.SoulFinal || body.SoulDive) && (windup || active) || balloon || pillar;
            if(soul && state=="Quake Down") {body.SoulDiveAckAction=body.Action;body.SoulDiveAckUntil=Time.time+.25f;}
            if (body.State != state) {
                body.State = state;
                if(soul && !body.SoulFinal && (state.StartsWith("Stun",StringComparison.Ordinal) || state=="Stunned") && SessionConfiguration.Current.RunFixtures && SessionConfiguration.Current.Has("trace-probe") && soulStaggerSamples++<64)
                    log?.Invoke("HK_SOUL_FIRST_STAGGER_STATE entity="+body.Health.GetInstanceID()+" state="+state.Replace(' ','_')+" position="+body.Health.transform.position+" invincible="+body.Health.IsInvincible
                        +" renderer="+body.Health.GetComponent<MeshRenderer>().enabled+" clip="+body.Health.GetComponent<tk2dSpriteAnimator>().CurrentClip?.name.Replace(' ','_')+" frame="+Time.frameCount);
                if (state == "Uppercut Up" && harmlessUppercuts.Contains(body.Health.GetInstanceID())) log?.Invoke("HK_GRIMM_SAFE_UPPERCUT_WINDOW entity=" + body.Health.GetInstanceID() + " action=" + body.Action + " damaging=False nativeMotion=True frame=" + Time.frameCount);
                if (windup || active || descending) log?.Invoke("HK_ROSTER_ATTACK_WINDOW entity=" + body.Health.GetInstanceID() + " state=" + state.Replace(' ', '_') + " action=" + body.Action + " kick=" + body.Kick + " crimson=" + body.Crimson + " frame=" + Time.frameCount);
            }
        }
    }
    internal UnityEngine.MeshRenderer[] VisualParts => markoth?.VisualParts ?? crimsonParts.Values.Where(p => p.Present && !p.Balloon).SelectMany(p => p.Renderers).Concat(weapons?.VisualParts??Array.Empty<MeshRenderer>()).Concat(radiance?.VisualParts??Array.Empty<MeshRenderer>()).Where(r => r != null).Distinct().ToArray();
    internal HealthManager[] VisualBodies => hornet?.Present == true ? new[] { hornet.Boss } : markoth?.IsPresent == true ? new[] { markothBody! } : bodies.Values.Where(b => b.Health != null && (b.Kick || b.Crimson || b.Windup && (!Mantis || b.AuraReady))).Select(b => b.Health).Concat(authored.VisualBodies).Concat(radiance?.VisualBodies??Array.Empty<HealthManager>()).ToArray();
    internal AttackTraits? VisualTraits(HealthManager health) {
        var radianceTraits=radiance?.VisualTraits(health);if(radianceTraits.HasValue)return radianceTraits;
        var native=authored.VisualTraits(health);
        if(native.HasValue) return native;
        if(!bodies.TryGetValue(health.GetInstanceID(),out var body)) return null;
        return body.Crimson ? AttackTraits.Crimson : body.Kick || body.Windup ? AttackTraits.Kickable : AttackTraits.None;
    }
    internal void TraceBodyHeight(YiGodhome.Bridge.Pose? pose) => authored.TraceBodyHeight(pose);
    internal void Presentation(ContactFrame frame) {
        if(radiance?.Presentation(frame)==true)return;
        if (hornet?.Present == true) { hornet.Presentation(frame); return; }
        if (markoth?.Present(frame) == true) return;
        if (authored.Presentation(frame)) return;
        if(weapons?.Presentation(frame)==true) return;
        var body = bodies.Values.FirstOrDefault(b => b.Health != null && (b.Kick || b.Crimson || b.Windup));
        if (body == null) {
            // Surviving balloon bullets remain counterable through their volume
            // leases, but are not new boss windups (including during bat stagger).
            var part = crimsonParts.Values.FirstOrDefault(p => p.Present && !p.Balloon);
            if (part == null) { visualSequence = 0; return; }
            if (visualSequence == 0) visualSequence = ++nextVisual;
            frame.CrimsonSequence = visualSequence; frame.CrimsonCharge = 1; frame.AttackId = part.Action; frame.Crimson = true;
            var shape = part.Source.GetComponent<Collider2D>().bounds;
            frame.X = shape.center.x; frame.Y = shape.center.y; frame.Width = Math.Max(1,shape.size.x); frame.Height = Math.Max(1,shape.size.y);
            return;
        }
        if (visualSequence == 0) visualSequence = ++nextVisual;
        frame.AttackId = body.Action; frame.CrimsonSequence = visualSequence; frame.CrimsonCharge = 1;
        frame.Crimson = body.Crimson; frame.Windup = body.Crimson && body.Windup;
        frame.Kickable = body.Kick && frame.Volumes.Any(v => v.Active && v.ActionId == body.Action && (v.Traits & AttackTraits.Kickable) != 0);
        frame.KickWindup = !frame.Crimson && !frame.Kickable;
        var bounds = body.Health.GetComponent<Collider2D>().bounds;
        frame.X = bounds.center.x; frame.Y = bounds.center.y;
        frame.Width = Math.Max(1, bounds.size.x); frame.Height = Math.Max(1, bounds.size.y);
    }
    internal bool HidePlayer=>radiance?.HidePlayer==true;
    internal void ProjectBar(HealthManager h,SummonedTarget target)=>radiance?.ProjectBar(h,target);
    internal bool HideHealthBar(HealthManager health)=>radiance?.HideBar(health)==true;
    internal bool HasBody(HealthManager health) => bodies.ContainsKey(health.GetInstanceID()) || authored.Owns(health) || radiance?.Owns(health)==true;
    internal bool Damageable(HealthManager health) => radiance?.Owns(health)==true ? radiance.Damageable(health) : !bodies.TryGetValue(health.GetInstanceID(), out var body)
        || (body.ActiveLayer < 0 || health.gameObject.layer == body.ActiveLayer) && (profile != EncounterProfile.Vengefly || !health.IsInvincible);
    internal void Decorate(AttackVolume volume, DamageHero damage, long nativeActivation = 0)
    {
        markoth?.Decorate(volume, damage);
        hornet?.Decorate(volume, damage, ref nextAction);
        authored.Decorate(volume, damage);
        if(radiance!=null) {radiance.Decorate(volume,damage,nativeActivation);return;}
        weapons?.Decorate(volume,damage);
        rings?.Decorate(volume,damage);
        if (profile == EncounterProfile.Grimm || profile == EncounterProfile.NightmareGrimm) {
            int id = damage.GetInstanceID();
            crimsonParts.TryGetValue(id, out var part);
            Transform? projectileRoot = part?.Root;
            // Native pool activation can reparent an owner. Revalidate on that
            // boundary, never rebuild parent-component arrays on every sample.
            if (part == null || part.SourceActivation != nativeActivation) {
                projectileRoot = null;
                for (var node = damage.transform; node != null; node = node.parent) {
                    string name = node.name;
                    if (name.StartsWith("Flameball Grimmballoon", StringComparison.Ordinal)
                        || profile == EncounterProfile.NightmareGrimm && name.StartsWith("Grimm_flare_pillar", StringComparison.Ordinal)) { projectileRoot = node; break; }
                }
                if (part != null && part.Root != projectileRoot) { crimsonParts.Remove(id); part = null; }
            }
            if (projectileRoot != null) {
                if (part == null) {
                    if (crimsonParts.Count >= AttackVolumes.MaxCount) throw new InvalidOperationException("Crimson projectile owner capacity exceeded.");
                    part = new CrimsonPart { Source = damage, Root = projectileRoot, Balloon = projectileRoot.name.StartsWith("Flameball Grimmballoon", StringComparison.Ordinal), Renderers = projectileRoot.GetComponentsInChildren<MeshRenderer>(true).Where(r => !r.name.StartsWith("YiGodhome",StringComparison.Ordinal)).Take(8).ToArray() }; crimsonParts.Add(id,part);
                }
                part.SourceActivation = nativeActivation;
                var lease = projectileRoot.GetComponent<CrimsonProjectileLease>() ?? projectileRoot.gameObject.AddComponent<CrimsonProjectileLease>();
                if (lease.Generation != part.Native) {
                    part.Native = lease.Generation;
                    var sibling = crimsonParts.Values.FirstOrDefault(p => p != part && p.Root == projectileRoot && p.Native == part.Native);
                    part.Action = sibling?.Action ?? ++nextAction; part.Retired = sibling?.Retired ?? false;
                }
                part.Active = volume.Active; volume.ActionId = part.Action; volume.Traits |= AttackTraits.Crimson;
                if (part.Retired) volume.Active = false;
                return;
            }
        }
        var health = damage.GetComponentInParent<HealthManager>();
        if (health == null || !bodies.TryGetValue(health.GetInstanceID(), out var body)) return;
        // Parentage alone does not make an emitted projectile part of the body.
        volume.CounterInjuryEntity=volume.Category==AttackCategory.Melee?health.GetInstanceID():0;
        if(watcherSequences.TryGetValue(health.GetInstanceID(),out var watcher)) {watcher.Decorate(volume,damage);return;}
        if(Array.IndexOf(body.SoulLandings,damage)>=0) {
            volume.ActionId=body.Action;
            volume.Active &= body.Action>0 && (body.State=="Quake Land" || body.State=="Quake Waves" || body.State=="Re Pos");
            return;
        }
        if (body.State == "Uppercut Up" && harmlessUppercuts.Contains(health.GetInstanceID())) {
            volume.Active = false;
            return;
        }
        bool root = profile == EncounterProfile.Gruz ? damage.name == "Hero Damager" : damage.gameObject == health.gameObject;
        if (root) volume.Active &= body.Kick || body.Descending || body.Crimson && !body.Windup;
        // Grimm's slash and ground dash already enable authored weapon shapes.
        // His passive body must not become a second overlapping dealer for them.
        if (root && (profile == EncounterProfile.Grimm || profile == EncounterProfile.NightmareGrimm)
            && (body.Balloon || body.Pillar || body.State == "G Dash" || body.State == "Slash 1" || body.State == "Slash 2" || body.State == "Slash 3")) volume.Active = false;
        if ((profile == EncounterProfile.Grimm || profile == EncounterProfile.NightmareGrimm) && body.Descending) volume.ActionId = body.Action;
        if (root && body.Descending) volume.ActionId = body.Action;
        if (body.Kick || body.Windup && !body.Crimson) { volume.ActionId = body.Action; volume.Traits |= AttackTraits.Kickable; }
        if (body.Crimson) { volume.ActionId = body.Action; volume.Traits |= AttackTraits.Crimson; volume.Active &= !body.Windup; }
    }
    internal void Target(SummonedTarget target)
    {
        hornet?.Target(target);
        authored.Target(target);
        if (!bodies.TryGetValue(target.Entity, out var body)) return;
        target.Active &= Damageable(body.Health);
        target.Kickable = body.Kick; target.AttackId = body.Kick || (Mantis || profile==EncounterProfile.Watchers) && body.Windup ? body.Action : 0;
    }
    internal bool Counter(long action, HitKind kind, int entity = 0)
    {
        Update();
        if(kind==HitKind.UnboundedCounter && radiance?.Counter(action,entity)==true)return true;
        if(weapons?.Counter(action,kind)==true) return true;
        if (markoth?.Counter(action, kind) == true) return true;
        if (hornet?.Counter(action, kind, entity) == true) return true;
        if (authored.Counter(action, kind, entity)) return true;
        foreach(var watcher in watcherSequences.Values)if(watcher.Counter(action,kind,entity))return true;
        var part = crimsonParts.Values.FirstOrDefault(p => p.Action == action && p.Present);
        if (part != null && kind == HitKind.UnboundedCounter && entity == 0) {
            foreach (var sibling in crimsonParts.Values.Where(p => p.Action == action)) sibling.Retired = true;
            var ball = part.Root.GetComponent<GrimmballControl>();
            if (ball != null) ball.DoHit();
            log?.Invoke("HK_GRIMM_CRIMSON_COUNTER action=" + action + " source=" + part.Source.name + " nativeSequenceContinues=True frame=" + Time.frameCount);
            if (ball != null) log?.Invoke("HK_GRIMM_BALL_IMPACT action=" + action + " root=" + part.Root.GetInstanceID() + " nativeDoHit=True frame=" + Time.frameCount);
            return true;
        }
        if(kind==HitKind.UnboundedCounter && action>0) foreach(var landing in bodies.Values) {
            if(landing.Health==null || landing.Health.isDead || landing.Health.hp<=0 || !landing.Health.gameObject.activeInHierarchy
                || landing.SoulDiveAckAction!=action || landing.Action!=action || landing.SoulDiveCountered==action || Time.time>landing.SoulDiveAckUntil
                || entity!=0 && entity!=landing.Health.GetInstanceID())continue;
            if(landing.Control.ActiveStateName!="Quake Land" && landing.Control.ActiveStateName!="Quake Waves" && landing.Control.ActiveStateName!="Re Pos")continue;
            log?.Invoke("HK_SOUL_DIVE_DELAYED_ACK entity="+landing.Health.GetInstanceID()+" action="+action+" nativeState="+landing.Control.ActiveStateName.Replace(' ','_')
                +" gameplayTime="+Time.time+" deadline="+landing.SoulDiveAckUntil+" exactStrike=True frame="+Time.frameCount);
            return CounterSoul(landing,action);
        }
        var body = bodies.Values.SingleOrDefault(b => b.Action == action && (b.Kick || b.Crimson));
        if (body == null || action <= 0 || entity != 0 && body.Health.GetInstanceID() != entity) return false;
        if (body.Crimson) {
            if (kind != HitKind.UnboundedCounter || body.Windup) return false;
            if (body.Balloon || body.Pillar) return true;
            return CounterSoul(body,action);
        }
        if (kind != HitKind.TaiChiKick && kind != HitKind.UnboundedCounter) return false;
        if ((profile == EncounterProfile.Grimm || profile == EncounterProfile.NightmareGrimm) && body.Slash) {
            // Native Slash Recover clears the final slash, but a counter can skip
            // the earlier cleanup states. Retire those exact authored shapes too.
            foreach (var shape in body.SlashShapes) shape.enabled = false;
            harmlessUppercuts.Add(body.Health.GetInstanceID());
            body.Control.SetState("Slash Recover");
            body.Kick = false;
            log?.Invoke("HK_GRIMM_SLASH_COUNTER entity=" + body.Health.GetInstanceID() + " action=" + action + " state=Slash_Recover continuation=Uppercut_Antic nativeSequenceContinues=True frame=" + Time.frameCount);
            log?.Invoke("HK_GRIMM_UPPERCUT_SAFE entity=" + body.Health.GetInstanceID() + " counterAction=" + action + " damage=False fireRain=True frame=" + Time.frameCount);
            return true;
        }
        var rb = body.Health.GetComponent<Rigidbody2D>(); if (rb != null) rb.linearVelocity = Vector2.zero;
        if(profile==EncounterProfile.Vengefly) {
            // Match attuned InterruptSwoop: stop travel, retain the current
            // animation for 0.65 gameplay seconds, then take native Swoop Rise.
            // Each ascended/radiant king owns its own hold and action identity.
            iTween.Stop(body.Health.gameObject);
            body.VengeflyHold=new NativeFsmStepLease(body.Control.Fsm,deferTransitions:true);
            body.VengeflyReleaseAt=Time.time+.65f;body.Kick=false;
            log?.Invoke("HK_VENGEFLY_STUN_BEGIN entity="+body.Health.GetInstanceID()+" action="+action+" seconds=0.65 nativeRise=True frame="+Time.frameCount);
            return true;
        }
        body.Control.SetState(profile == EncounterProfile.Gruz ? "Slam End" : Mantis ? "Dash Recover" : profile == EncounterProfile.SoulMaster || profile == EncounterProfile.SoulTyrant ? "Re Pos" : profile == EncounterProfile.Grimm || profile == EncounterProfile.NightmareGrimm ? "G Dash Recover" : "Charge Recover");
        body.Kick = false;
        log?.Invoke("HK_ROSTER_KICK_RECOVERY entity=" + body.Health.GetInstanceID() + " action=" + action + " state=" + body.Control.ActiveStateName.Replace(' ', '_') + " frame=" + Time.frameCount);
        return true;
    }
    private bool CounterSoul(Body body,long action) {
        if(body.SoulDiveCountered==action || body.Health==null || body.Health.isDead || body.Health.hp<=0 || !body.Health.gameObject.activeInHierarchy)return false;
        body.SoulDiveCountered=action;
        log?.Invoke("HK_SOUL_DIVE_COUNTER entity="+body.Health.GetInstanceID()+" action="+action+" interrupted=False nativeSequenceContinues=True nativeState="+body.Control.ActiveStateName.Replace(' ','_')+" final="+body.SoulFinal+" frame="+Time.frameCount);
        return true;
    }
    public void Dispose() { foreach(var body in bodies.Values){body.VengeflyHold?.Dispose();body.VengeflyHold=null;} radiance?.Dispose();rings?.Dispose();weapons?.Dispose();authored.Dispose();markoth?.Dispose();NativeFsmStepLease.ReleaseHooks(); }
}

// The native pool root owns one strike even when it has several child dealers.
// A new native activation gets a fresh group; old counters cannot retire it.
internal sealed class CrimsonProjectileLease : MonoBehaviour
{
    internal long Generation { get; private set; }
    private void OnEnable() => Generation++;
}
