using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YiGodhome.Bridge;
using YiGodhome.Geometry;

namespace YiGodhome;

// Each independent host activation owns a native attack/parry graph and verdict.
// Host motion and visuals remain authoritative; Yi's native defense resolves hits.
internal sealed class NativeAttackVolumes : IDisposable
{
    private sealed class Entry : IDisposable
    {
        public NativeContactFixture Native = null!;
        public NativeContactVerdict Verdict = null!;
        public long Activation;
        public AttackCategory Category;
        public bool Active;
        public bool Touching;
        public float NextSample;
        public bool Near;
        public bool Consume;
        public float ParryCooldown;
        public bool CooldownOnDamage;
        public bool NotifyParry;
        public readonly DefenseCooldown Cooldown = new DefenseCooldown();
        public readonly AttackRetirement Retirement = new AttackRetirement();
        public ContactFrame? Frame;
        public long ActionId;
        public int ContactOwner;
        public long ContactOwnerGeneration;
        public long DamageGroupId;
        public float InitialDamageGroupHold;
        public bool RepeatDamageGroupHold;
        public long HeldInitialGroup;
        public float HeldInitialUntil;
        public long CounterGroupId;
        public AttackTraits Traits;
        public long TalismanEvidenceActivation;
        public long LastSeen;
        public Vector2[] Points = Array.Empty<Vector2>();
        public void Dispose() { Verdict.Dispose(); Native.Dispose(); }
    }
    private readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>();
    private readonly NativeProjectileBlockers? blockers;
    private readonly NativeFlameBurnVolumes burns;
    private readonly Player player;
    private readonly MonsterBase owner;
    private readonly GeometryLandingProbe geometry;
    private readonly Action<string> log;
    private readonly bool scripted;
    private readonly bool cacheColliders;
    private const int MaximumRetained = AttackVolumes.MaxCount * 2;
    private bool voidPlaced, voidTrapped, voidRecovered;
    private float voidAt, voidHealth;
    private Guid cacheEncounter;
    private double updateTotal, updateMax, syncTotal;
    private int updateCount;
    private long polygonCount, syncCount;
    private long polygonReuseCount;
    private float nextCost;
    private readonly bool vesselPerformance;
    private int pacingFrame=-1,pacingFrames,pacingOver9,pacingOver12,pacingFrozen,pacingHostHeld,pacingSkippedFrames,fixtureCreates;
    private long pacingPrevious;
    private double pacingWall,pacingUnity,pacingNative,pacingUnscaled,pacingMax,fixtureCreateTotal,fixtureCreateMax;
    private readonly System.Collections.Generic.HashSet<int> seenVolumes = new();
    private readonly System.Collections.Generic.HashSet<int> activeVolumes = new();
    private readonly System.Collections.Generic.List<int> missingVolumes = new();
    private long placementTicks, queryTicks;
    private readonly List<AttackVolume> reachableVolumes = new();
    private readonly HashSet<(int Id,long Activation)> outsideVolumes = new();
    private readonly HashSet<(int Id,long Activation)> currentVolumeStamps = new();
    private int culledVolumes;
    private readonly RadiantRevivalProbe revivalProbe;
    private readonly Queue<HitOutcome> outcomes = new Queue<HitOutcome>();
    private readonly ActionRetirement retiredActions = new ActionRetirement();
    private readonly ActionRetirement retiredCounterGroups = new();
    internal bool DiagnosticVolumeActive(AttackVolume volume)=>SessionConfiguration.Current.RunFixtures
        && SessionConfiguration.Current.Has("cheat-probe") && entries.TryGetValue(volume.Id,out var entry)
        && entry.Activation==volume.Activation && entry.Active && entry.Native.Collider.enabled
        && !retiredActions.Contains(volume.ActionId) && !retiredDamageGroups.Contains(volume.DamageGroupId,Time.time);
    private void RetireCounterGroup(long group) {
        if(!retiredCounterGroups.Retire(group)) return;
        int sources=0;
        foreach(var entry in entries.Values) if(entry.CounterGroupId==group) {
            RetireAction(entry.ActionId); sources++;
            entry.Native.SetWorldPolygon(Array.Empty<Vector2>(),false);entry.Verdict.Suspend();entry.Active=entry.Touching=false;
        }
        log("NS_COUNTER_GROUP_RETIRED group="+group+" sources="+sources+" sameNativeFrame="+Time.frameCount+" damaging=False");
    }
    private readonly DamageGroupRetirement retiredDamageGroups = new();
    private readonly TimedActionRetirement timedActions=new();
    private int initialRearmSamples;
    private int talismanRefusalSamples;
    private void CoolAction(long action,float seconds)
    {
        timedActions.Hold(action,Time.time,seconds);
        foreach(var entry in entries.Values.Where(e=>e.ActionId==action)) {
            entry.Cooldown.Begin(entry.Activation,Time.time,seconds);
            entry.Native.SetWorldPolygon(Array.Empty<Vector2>(),false);entry.Verdict.Suspend();entry.Active=entry.Touching=false;
        }
        log("NS_ACTION_COOLDOWN action="+action+" seconds="+seconds+" sameNativeFrame="+Time.frameCount+" damaging=False");
    }
    private void RetireDamageGroup(long group,float initialHold,bool repeat)
    {
        if(!retiredDamageGroups.Resolve(group,true,Time.time,initialHold,repeat)) return;
        foreach(var entry in entries.Values.Where(e=>e.DamageGroupId==group)) {
            entry.Native.SetWorldPolygon(Array.Empty<Vector2>(),false);entry.Verdict.Suspend();entry.Active=entry.Touching=false;
        }
        log("NS_VOLLEY_DAMAGE_RETIRED group="+group+" sameNativeFrame="+Time.frameCount+" defenseIndependent=True initialHold="+initialHold+" gameplayTime="+Time.time+" independentAfterHold="+(initialHold>0 && !repeat)+" repeat="+repeat);
    }
    private static void CloseContact(Entry entry) {
        // The native queued hit is authoritative. The sampled overlap can miss a
        // fast strike between independently stepped physics updates; Drain itself
        // refuses any entry without an actual queued native hit.
        if(entry.Active && (entry.Category == AttackCategory.Melee || entry.Category == AttackCategory.Contact || entry.Category == AttackCategory.Projectile))
            entry.Verdict.Drain(Math.Min(.2f,entry.Native.NativeDelay + 2*Time.fixedDeltaTime));
        else if(entry.Active) entry.Verdict.Suspend();
        entry.Native.SetWorldPolygon(Array.Empty<Vector2>(),false);
        entry.Active = entry.Touching = false;
    }
    public void RetireAction(long action)
    {
        if (!retiredActions.Retire(action)) return;
        foreach (var entry in entries.Values.Where(e => e.ActionId == action)) {
            entry.Retirement.Retire(entry.Activation);
            entry.Native.SetWorldPolygon(Array.Empty<Vector2>(), false); entry.Verdict.Suspend(); entry.Active = false;
        }
        log("NS_VOLUME_ACTION_RETIRED action=" + action + " sameNativeFrame=" + Time.frameCount + " damaging=False");
    }
    public bool TryGetOutcome(out HitOutcome outcome) { if (outcomes.Count > 0) { outcome = outcomes.Dequeue(); return true; } outcome = null!; return false; }
    internal void RetireContactOwner(int entity,long generation,Guid encounter) {
        foreach(var entry in entries.Values)if(entry.Frame?.Encounter==encounter && entry.ContactOwner==entity && entry.ContactOwnerGeneration==generation)
            RetireAction(entry.ActionId);
    }
    public NativeAttackVolumes(Player player, MonsterBase owner, GeometryLandingProbe geometry, Action<string> log, bool projectileBlockers = true)
    {
        this.player = player; this.owner = owner; this.geometry = geometry; this.log = log;
        burns = new NativeFlameBurnVolumes(owner, geometry, log);
        revivalProbe = new RadiantRevivalProbe(owner, log);
        if (projectileBlockers) blockers = new NativeProjectileBlockers(player, geometry, log);
        scripted = YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures;
        var family=EncounterProfile.ForScene(geometry.SourceScene)?.Family;
        vesselPerformance=family==EncounterProfile.BrokenVessel || family==EncounterProfile.LostKin;
        cacheColliders = EncounterProfile.ForScene(geometry.SourceScene)?.OwnNativePools == true;
    }
    public void Update(ContactFrame? frame)
    {
        timedActions.Prune(Time.time);
        burns.Update(frame);
        blockers?.Update(frame);
        revivalProbe.Update(frame);
        if (revivalProbe.Enabled) return;
        if (frame == null || player.health.IsDead)
        {
            foreach (var e in entries.Values) { e.Native.SetWorldPolygon(Array.Empty<Vector2>(), false); e.Verdict.Suspend(); e.Active = false; }
            return;
        }
        NativeRecoveryAnchor.ObserveHost(player,frame,geometry.Map,geometry.SourceFrame);
        long costStart = System.Diagnostics.Stopwatch.GetTimestamp(), syncStart = NativeContactFixture.SyncTicks, polygonsStart = NativeContactFixture.PolygonWrites, reusesStart = NativeContactFixture.PolygonReuses, syncCallsStart = NativeContactFixture.SyncCalls;
        if(vesselPerformance && pacingFrame!=Time.frameCount) {
            int priorFrame=pacingFrame;pacingFrame=Time.frameCount;
            if(pacingPrevious!=0) {
                pacingSkippedFrames+=Math.Max(0,Time.frameCount-priorFrame-1);
                double wall=(costStart-pacingPrevious)/(double)System.Diagnostics.Stopwatch.Frequency;
                pacingFrames++;pacingWall+=wall;pacingMax=Math.Max(pacingMax,wall*1000);
                if(wall>=.009)pacingOver9++;if(wall>=.012)pacingOver12++;
                pacingUnity+=Time.deltaTime;pacingUnscaled+=Time.unscaledDeltaTime;pacingNative+=RCGTime.deltaTime;
                if(RCGTime.deltaTime==0)pacingFrozen++;
                if(NativeInputBridge.HostPaused || NativeHitPauseObserver.Held)pacingHostHeld++;
            }
            pacingPrevious=costStart;
        }
        cacheEncounter = frame.Encounter;
        // A confirmed dead/dormant owner is not a finished attack animation:
        // cancel its pending native injury instead of draining it after death.
        foreach(var entry in entries.Values)if(entry.ContactOwner!=0 && !ContactOwnerLease.Live(frame.Summons,entry.ContactOwner,entry.ContactOwnerGeneration)) {
            RetireAction(entry.ActionId);entry.ContactOwner=0;entry.ContactOwnerGeneration=0;
        }
        var volumes = frame.Volumes;
        culledVolumes=0;
        if((EncounterProfile.ForScene(geometry.SourceScene)?.Family==EncounterProfile.NightmareGrimm || EncounterProfile.ForScene(geometry.SourceScene)?.Family==EncounterProfile.Grimm) && geometry.TryCombatEnvelope(out var envelope)) {
            reachableVolumes.Clear();
            currentVolumeStamps.Clear();
            foreach(var v in volumes) {
                currentVolumeStamps.Add((v.Id,v.Activation));
                if(v.Active && (v.Category==AttackCategory.Projectile || (v.Category==AttackCategory.Melee && (v.Traits&AttackTraits.LargeParryPush)!=0)) && envelope.Outside(v.Points)) {
                    culledVolumes++;
                    if(outsideVolumes.Add((v.Id,v.Activation))) log("NS_BALLOON_OUTSIDE_PLATFORM id="+v.Id+" activation="+v.Activation+" left="+envelope.Left+" right="+envelope.Right+" floor="+envelope.Floor+" wholeShape=True nativeLifetime=True queuedHitDrain=True frame="+Time.frameCount);
                } else reachableVolumes.Add(v);
            }
            // Keep only current identities; native pool reuse gets a fresh lease.
            outsideVolumes.RemoveWhere(stamp=>!currentVolumeStamps.Contains(stamp));
            volumes=reachableVolumes.ToArray();
        }
        seenVolumes.Clear(); activeVolumes.Clear(); missingVolumes.Clear();
        foreach (var v in volumes) { seenVolumes.Add(v.Id); if (v.Active) activeVolumes.Add(v.Id); }
        foreach (var id in entries.Keys) if (!seenVolumes.Contains(id)) missingVolumes.Add(id);
        foreach (var id in missingVolumes) {
            var missing = entries[id];
            if (cacheColliders) {
                CloseContact(missing); missing.Verdict.Check();
                if(!missing.Verdict.Draining) missing.Frame = null;
            } else { missing.Dispose(); entries.Remove(id); log("NS_ATTACK_REMOVED id=" + id); }
        }
        retiredActions.Retain(frame.AttackId, volumes, frame.Summons);
        retiredCounterGroups.RetainCounterGroups(frame.CrimsonSequence,volumes);
        retiredDamageGroups.Retain(volumes,Time.time);
        foreach (var v in volumes)
        {
            if (v.Category == AttackCategory.Burn) continue;
            if (!entries.TryGetValue(v.Id, out var e))
            {
                if (cacheColliders && !v.Active) continue;
                if (cacheColliders && entries.Count >= MaximumRetained) {
                    var oldest = entries.Where(p => !p.Value.Active && !p.Value.Verdict.Draining && !activeVolumes.Contains(p.Key))
                        .OrderBy(p => p.Value.LastSeen).FirstOrDefault();
                    if (oldest.Value == null) throw new InvalidOperationException("Native attack fixture cache capacity exceeded.");
                    oldest.Value.Dispose(); entries.Remove(oldest.Key); log("NS_ATTACK_CACHE_EVICTED id=" + oldest.Key + " retained=" + entries.Count);
                }
                long fixtureStart=vesselPerformance?System.Diagnostics.Stopwatch.GetTimestamp():0;
                var native = new NativeContactFixture(owner, log, 27, independent: true);
                try { e = new Entry { Native = native, Verdict = new NativeContactVerdict(native.Dealer, player, log, scripted), Category = v.Category, Consume = v.ConsumeOnContact }; }
                catch { native.Dispose(); throw; }
                entries.Add(v.Id, e);
                if(vesselPerformance) {
                    double ms=(System.Diagnostics.Stopwatch.GetTimestamp()-fixtureStart)*1000d/System.Diagnostics.Stopwatch.Frequency;
                    fixtureCreates++;fixtureCreateTotal+=ms;fixtureCreateMax=Math.Max(fixtureCreateMax,ms);
                }
                if (cacheColliders) log("NS_ATTACK_CACHE_BOUND id=" + v.Id + " retained=" + entries.Count + " limit=" + MaximumRetained + " encounter=" + frame.Encounter);
                var entry = e; int hostId = v.Id;
                if(SessionConfiguration.Current.RunFixtures && SessionConfiguration.Current.Has("trace-probe"))
                    e.Verdict.TalismanRefused = stage => {
                        if(entry.TalismanEvidenceActivation==entry.Activation || talismanRefusalSamples>=64 || entry.Frame==null)return;
                        if(entry.Category!=AttackCategory.Contact)throw new InvalidOperationException("Talisman immunity escaped contact-only policy.");
                        entry.TalismanEvidenceActivation=entry.Activation;talismanRefusalSamples++;
                        log("NS_TALISMAN_CONTACT_REFUSED encounter="+entry.Frame.Encounter+" collider="+hostId+" activation="+entry.Activation
                            +" action="+entry.ActionId+" state="+player.CurrentStateType+" stage="+stage+" health="+player.health.currentValue
                            +" contactOnly=True nativeFrame="+Time.frameCount+" gameplayTime="+Time.time);
                    };
                e.Verdict.Resolved = () => {
                    var context = entry.Frame;
                    if (context != null) log("NS_VOLUME_VERDICT encounter=" + context.Encounter + " collider=" + hostId
                        + " activation=" + entry.Activation + " action=" + entry.ActionId + " kind=" + entry.Verdict.ResolutionKind
                        + " hostFrame=" + context.NativeFrame + " nativeFrame=" + Time.frameCount + " health=" + player.health.currentValue+" grounded="+player.IsOnGround
                        +" incomingDirection="+entry.Native.Dealer.GlobalAttackDir+" facing="+player.Facing+" playerX="+player.transform.position.x+" dealerX="+entry.Native.Dealer.transform.position.x+" damageGroup="+entry.DamageGroupId+" gameplayTime="+Time.time);
                    bool reflectableProjectile = entry.Category == AttackCategory.Projectile && (entry.Traits & AttackTraits.Reflectable) != 0;
                    bool ordinaryDefense=entry.Verdict.ResolutionKind=="parry" || entry.Verdict.ResolutionKind=="counter" && (entry.Traits&(AttackTraits.Crimson|AttackTraits.Kickable))==0;
                    bool reflectedDefense=ProjectileDefense.Reflects(entry.Traits,ordinaryDefense,entry.Verdict.ResolutionKind=="counter",entry.Verdict.ReflectionEligible);
                    if(ordinaryDefense && entry.NotifyParry && entry.Category==AttackCategory.Melee
                        && EncounterProfile.ForScene(geometry.SourceScene)?.Family==EncounterProfile.Collector && !entry.Verdict.PreciseParry) {
                        // An imperfect native defense protects Yi without breaking the jar.
                        entry.Verdict.Begin();return;
                    }
                    if(ordinaryDefense && entry.Category==AttackCategory.Melee && entry.DamageGroupId>0 && (entry.Traits&AttackTraits.RetireOnParry)!=0) {RetireAction(entry.ActionId);return;}
                    if(entry.DamageGroupId>0 && entry.Verdict.ResolutionKind=="damage") RetireDamageGroup(entry.DamageGroupId,entry.InitialDamageGroupHold,entry.RepeatDamageGroupHold);
                    if(ordinaryDefense && entry.Category==AttackCategory.Projectile && entry.DamageGroupId>0 && (entry.Traits&AttackTraits.RetireOnParry)!=0)
                        RetireDamageGroup(entry.DamageGroupId,entry.InitialDamageGroupHold,entry.RepeatDamageGroupHold);
                    if(context!=null && entry.NotifyParry && (entry.Category==AttackCategory.Projectile || entry.Category==AttackCategory.Contact
                        || entry.Category==AttackCategory.Melee && entry.ParryCooldown>0) && ordinaryDefense) {
                        if(entry.Category==AttackCategory.Melee && !entry.Verdict.PreciseParry) {
                            CoolAction(entry.ActionId,entry.ParryCooldown);return;
                        }
                        if(entry.Category==AttackCategory.Contact && entry.ContactOwner!=0 && !entry.Verdict.PreciseParry) {
                            // Ricochet only grants projectile deflection. It does
                            // not turn an imperfect body parry into a minion kill.
                            CoolAction(entry.ActionId,entry.ParryCooldown);
                            log("NS_MINION_IMPERFECT_PARRY owner="+entry.ContactOwner+" generation="+entry.ContactOwnerGeneration
                                +" action="+entry.ActionId+" killed=False nativeFrame="+Time.frameCount);
                            return;
                        }
                        bool directedTrap=entry.Category==AttackCategory.Projectile
                            && EncounterProfile.ForScene(geometry.SourceScene)?.Family==EncounterProfile.HornetSentinel;
                        if(directedTrap && !entry.Verdict.ReflectionEligible) {
                            // Imperfect parry protects Yi briefly, but cannot launch
                            // the trap. Keep its native object/activation intact.
                            CoolAction(entry.ActionId,entry.ParryCooldown);
                            log("NS_HORNET_TRAP_IMPERFECT_PARRY action="+entry.ActionId+" reflected=False ricochet=False nativeFrame="+Time.frameCount);
                            return;
                        }
                        RetireAction(entry.ActionId);
                        outcomes.Enqueue(new HitOutcome {Session=context.Session,Encounter=context.Encounter,GeometryFrame=context.GeometryFrame,
                            HostTick=context.CaptureTick,NativeFrame=Time.frameCount,Entity=context.Entity,TargetActivation=context.TargetGeneration,
                            Dealer=hostId,Kind=directedTrap?HitKind.StrikeDeflected:HitKind.StrikeParried,AttackId=entry.ActionId});
                        log((entry.Category==AttackCategory.Projectile?"NS_NATIVE_PROJECTILE_PARRY_NOTIFY":entry.Category==AttackCategory.Melee?"NS_NATIVE_PUDDLE_PARRY_NOTIFY":"NS_NATIVE_MINION_PARRY_NOTIFY")+" action="+entry.ActionId+" collider="+hostId+" nativeFrame="+Time.frameCount+" damage=0 verdict="+entry.Verdict.ResolutionKind);return;
                    }
                    if(entry.DamageGroupId>0 && ordinaryDefense && scripted) {
                        int siblings=entries.Values.Count(e=>e!=entry && e.DamageGroupId==entry.DamageGroupId && e.Active && e.Native.Collider.enabled && e.Native.Dealer.isActiveAndEnabled);
                        log("NS_VOLLEY_PARRY_INDEPENDENT encounter="+context?.Encounter+" group="+entry.DamageGroupId+" collider="+hostId+" siblingsDamaging="+siblings+" nativeFrame="+Time.frameCount);
                    }
                    if (context != null && entry.ActionId > 0 && entry.Category == AttackCategory.Melee && entry.ParryCooldown == 0 && entry.Verdict.ResolutionKind == "damage") {
                        RetireAction(entry.ActionId); return;
                    }
                    if (context != null && entry.ParryCooldown > 0 && (ordinaryDefense && !reflectableProjectile || entry.CooldownOnDamage && entry.Verdict.ResolutionKind == "damage")) {
                        if(entry.ActionId>0) {CoolAction(entry.ActionId,entry.ParryCooldown);return;}
                        entry.Cooldown.Begin(entry.Activation, Time.time, entry.ParryCooldown);
                        entry.Native.SetWorldPolygon(Array.Empty<Vector2>(), false); entry.Verdict.Suspend(); entry.Active = entry.Touching = false;
                        log("NS_PROJECTILE_PARRY_HOLD id=" + hostId + " activation=" + entry.Activation + " seconds=" + entry.ParryCooldown + " recycle=False frame=" + Time.frameCount);
                        return;
                    }
                    bool retireStrike = ordinaryDefense && entry.Category==AttackCategory.Melee && entry.ParryCooldown==0 && (entry.Traits & AttackTraits.Crimson)==0;
                    if(retireStrike && entry.ActionId>0) {
                        RetireAction(entry.ActionId);
                        if(entry.NotifyParry && context!=null)
                            outcomes.Enqueue(new HitOutcome {Session=context.Session,Encounter=context.Encounter,GeometryFrame=context.GeometryFrame,
                                HostTick=context.CaptureTick,NativeFrame=Time.frameCount,Entity=context.Entity,TargetActivation=context.TargetGeneration,
                                Dealer=hostId,Kind=HitKind.StrikeParried,AttackId=entry.ActionId});
                        log("NS_LOGICAL_STRIKE_PARRY_RETIRED action="+entry.ActionId+" collider="+hostId+" nativeFrame="+Time.frameCount+" nativeSequenceInterrupted=False");
                        return;
                    }
                    if ((!entry.Consume && !retireStrike) || context == null || !entry.Retirement.Retire(entry.Activation)) return;
                    if (entry.Consume && entry.Category == AttackCategory.Projectile && entry.ActionId > 0) {
                        RetireAction(entry.ActionId);
                        log("NS_PROJECTILE_GROUP_RETIRED action=" + entry.ActionId + " collider=" + hostId + " nativeFrame=" + Time.frameCount + " siblingDamage=False");
                    }
                    entry.Native.SetWorldPolygon(Array.Empty<Vector2>(), false);
                    entry.Verdict.Suspend(); entry.Active = false; entry.Touching = false;
                    if (retireStrike) {
                        log("NS_STRIKE_PARRY_RETIRED encounter=" + context.Encounter + " target=" + context.Entity + " targetActivation=" + context.TargetGeneration
                            + " id=" + hostId + " activation=" + entry.Activation + " hostFrame=" + context.NativeFrame + " nativeFrame=" + Time.frameCount + " damaging=False");
                        return;
                    }
                    outcomes.Enqueue(new HitOutcome { Session = context.Session, Encounter = context.Encounter, GeometryFrame = context.GeometryFrame,
                        HostTick = context.CaptureTick, NativeFrame = Time.frameCount, Entity = context.Entity, TargetActivation = context.TargetGeneration, Dealer = hostId,
                        Kind = reflectedDefense ? HitKind.ProjectileReflected : HitKind.ProjectileResolved, AttackId = entry.Activation });
                    log("NS_PROJECTILE_RESOLVED id=" + hostId + " activation=" + entry.Activation + " sameNativeFrame=" + Time.frameCount
                        + " verdict=" + entry.Verdict.ResolutionKind + " reflected=" + reflectedDefense);
                };
                e.Verdict.Countered = () => {
                    // Detached crimson projectiles resolve through deflection,
                    // never a synthetic internal-injury outcome against the boss.
                    if(entry.Category==AttackCategory.Projectile && ProjectileDefense.CounterDeflects(entry.Traits))return;
                    var context = entry.Frame;
                    if (context == null || entry.ActionId <= 0 || retiredActions.Contains(entry.ActionId) || (entry.Traits & (AttackTraits.Crimson | AttackTraits.Kickable)) == 0) return;
                    long action = entry.ActionId;
                    if(entry.CounterGroupId>0) {
                        if(retiredCounterGroups.Contains(entry.CounterGroupId)) return;
                        RetireCounterGroup(entry.CounterGroupId);
                    }
                    RetireAction(action);
                    outcomes.Enqueue(new HitOutcome { Session = context.Session, Encounter = context.Encounter, GeometryFrame = context.GeometryFrame,
                        HostTick = context.CaptureTick, NativeFrame = Time.frameCount, Entity = context.Entity, TargetActivation = context.TargetGeneration,
                        Dealer = hostId, Kind = HitKind.UnboundedCounter, AttackId = action });
                    log("NS_VOLUME_CRIMSON_COUNTER action=" + action + " frame=" + Time.frameCount + " encounter=" + context.Encounter + " collider=" + hostId + " activation=" + entry.Activation);
                };
                log("NS_ATTACK_BOUND id=" + v.Id + " dealer=" + native.Dealer.GetInstanceID() + " category=" + v.Category);
            }
            e.LastSeen = frame.Sequence;
            e.Verdict.Check();
            if (e.Category != v.Category) throw new InvalidOperationException("Attack category changed within a collider identity.");
            if (e.Consume != v.ConsumeOnContact) throw new InvalidOperationException("Projectile impact semantics changed within a collider identity.");
            if (v.Activation < e.Activation) throw new InvalidOperationException("Attack activation regressed.");
            bool newActivation = v.Activation != e.Activation;
            if (newActivation)
            {
                // A fast native strike can finish while its delayed verdict is
                // still queued. Do not recycle that dealer for the next host
                // activation until the exact queued receipt resolves/expires.
                CloseContact(e);
                if (e.Verdict.Draining) continue;
                e.Verdict.Suspend();
            }
            if(!newActivation && !v.Active) { CloseContact(e); continue; }
            if(e.Verdict.Draining) continue;
            // A queued native verdict still reads its dealer's damage. Preserve
            // the old activation's settings until its drain resolves/expires;
            // an inactive/new host sample must not mutate that outstanding hit.
            e.Native.SetDifficulty(v.Damage, frame.Difficulty);
            if(e.Activation!=v.Activation || e.ActionId!=v.ActionId || e.DamageGroupId!=v.DamageGroupId){e.HeldInitialGroup=0;e.HeldInitialUntil=0;}
            e.Activation = v.Activation;
            e.Frame = frame;
            e.Verdict.TalismanContactImmunity = v.TalismanContactImmunity && EncounterProfile.ForScene(geometry.SourceScene)?.Family != EncounterProfile.Watchers;
            e.ActionId = v.ActionId; e.DamageGroupId=v.DamageGroupId; e.CounterGroupId=v.CounterGroupId; e.Traits = v.Traits; e.ParryCooldown = v.ParryCooldown; e.CooldownOnDamage = v.CooldownOnDamage;e.NotifyParry=v.NotifyParry;
            e.ContactOwner=v.ContactOwner;e.ContactOwnerGeneration=v.ContactOwnerGeneration;
            if(v.ContactOwner!=0 && !ContactOwnerLease.Live(frame.Summons,v.ContactOwner,v.ContactOwnerGeneration)) {RetireAction(v.ActionId);continue;}
            if (!v.Active) continue;
            e.InitialDamageGroupHold=v.InitialDamageGroupHold;
            e.RepeatDamageGroupHold=v.RepeatDamageGroupHold;
            if(retiredDamageGroups.Contains(v.DamageGroupId,Time.time)) {
                if(v.InitialDamageGroupHold>0) {e.HeldInitialGroup=v.DamageGroupId;e.HeldInitialUntil=retiredDamageGroups.InitialHoldUntil(v.DamageGroupId);}
                continue;
            }
            if(retiredCounterGroups.Contains(v.CounterGroupId)) continue;
            if (v.ActionId > 0 && retiredActions.Contains(v.ActionId)) continue;
            if(timedActions.Held(v.ActionId,Time.time)) continue;
            if (e.Retirement.Contains(v.Activation) || e.Cooldown.Held(v.Activation, Time.time)) continue;
            if(e.HeldInitialGroup==v.DamageGroupId && e.HeldInitialGroup>0) {
                if(scripted && initialRearmSamples++<128)log((v.RepeatDamageGroupHold?"NS_RECURRING_GROUP_REARM":"NS_INITIAL_GROUP_REARM")+" collider="+v.Id+" activation="+v.Activation+" action="+v.ActionId+" group="+v.DamageGroupId
                    +" holdUntil="+e.HeldInitialUntil+" gameplayTime="+Time.time+" nativeFrame="+Time.frameCount+" independent="+(!v.RepeatDamageGroupHold)+" encounter="+frame.Encounter);
                e.HeldInitialGroup=0;
            }
            if (!e.Active) { if (newActivation) e.Verdict.Begin(); else if (e.ParryCooldown > 0) e.Verdict.Begin(); else e.Verdict.Resume(); e.Active = true;
                log("NS_ATTACK_BEGIN id=" + v.Id + " activation=" + v.Activation + " encounter=" + frame.Encounter + " target=" + frame.Entity
                    + " targetActivation=" + frame.TargetGeneration + " damage=" + v.Damage + " tier=" + frame.Difficulty + " frame=" + Time.frameCount); }
            if (e.Points.Length != v.Points.Length / 2) e.Points = new Vector2[v.Points.Length / 2];
            var points = e.Points;
            for (int i = 0; i < points.Length; i++) { var p = geometry.Map.ToTarget(new Point(v.Points[i * 2], v.Points[i * 2 + 1])); points[i] = new Vector2(p.X, p.Y); }
            bool large = (v.Traits & AttackTraits.LargeParryPush) != 0;
            int direction = (v.Traits & AttackTraits.PushRight) != 0 ? 1 : (v.Traits & AttackTraits.PushLeft) != 0 ? -1 : 0;
            bool balloonPush = direction != 0 && (EncounterProfile.ForScene(geometry.SourceScene)?.Family == EncounterProfile.Grimm || EncounterProfile.ForScene(geometry.SourceScene)?.Family == EncounterProfile.NightmareGrimm);
            var pushFamily=EncounterProfile.ForScene(geometry.SourceScene)?.Family;
            bool ordinarySword=pushFamily==EncounterProfile.Sly && (v.Traits&(AttackTraits.Crimson|AttackTraits.Kickable))==0;
            e.Native.SetLargeParryPush(large, (v.Traits & AttackTraits.InitialParryPush) != 0, direction, gentle: balloonPush,
                nailmaster:pushFamily==EncounterProfile.OroMato,swordCombo:ordinarySword);
            var origin = geometry.Map.ToTarget(new Point(frame.X, frame.Y));
            long placementStart = System.Diagnostics.Stopwatch.GetTimestamp();
            Vector2? circleCenter = null;
            if (v.CircleRadius > 0) { var mapped = geometry.Map.ToTarget(new Point(v.CircleX,v.CircleY)); circleCenter = new Vector2(mapped.X,mapped.Y); }
            e.Native.SetWorldPolygon(points, true, large && direction==0 ? new Vector2(origin.X, origin.Y) : (Vector2?)null, synchronize: false, projectile: v.Category == AttackCategory.Projectile,
                nativeCircleCenter: circleCenter, nativeCircleRadius: v.CircleRadius * geometry.Map.Scale);
            placementTicks += System.Diagnostics.Stopwatch.GetTimestamp() - placementStart;
            e.Native.SetCrimson((v.Traits & (AttackTraits.Crimson | AttackTraits.Kickable)) != 0);
            e.Native.SetCounterOwner(NativeTargetProbe.CounterOwner(v.CounterInjuryEntity));
            if (newActivation && v.Traits != AttackTraits.None) log("NS_ATTACK_POLICY encounter=" + frame.Encounter + " target=" + frame.Entity + " targetActivation=" + frame.TargetGeneration
                + " id=" + v.Id + " activation=" + v.Activation + " action=" + v.ActionId + " traits=" + v.Traits + " " + e.Native.Policy + " frame=" + Time.frameCount);
        }
        // Place every native dealer before one synchronization and the queries.
        // Each dealer keeps its own activation, queued verdict and cooldown.
        long batchSyncStart = System.Diagnostics.Stopwatch.GetTimestamp();
        Physics2D.SyncTransforms();
        NativeContactFixture.SyncCalls++;
        NativeContactFixture.SyncTicks += System.Diagnostics.Stopwatch.GetTimestamp() - batchSyncStart;
        foreach (var v in volumes) {
            if (!entries.TryGetValue(v.Id, out var e) || !e.Active || e.Activation != v.Activation || e.LastSeen != frame.Sequence) continue;
            e.Native.VerifyCircle();
            long queryStart = System.Diagnostics.Stopwatch.GetTimestamp();
            var separation = Physics2D.Distance(e.Native.Collider, player.myCollider);
            queryTicks += System.Diagnostics.Stopwatch.GetTimestamp() - queryStart;
            float distance = separation.distance;
            if(EncounterProfile.ForScene(geometry.SourceScene)?.Family==EncounterProfile.AbsoluteRadiance
                && RadiancePresentation.RainCounterNear(v.RadianceRainCounter,player.counteringDefenseCondition.CustomInputBuffer,distance,geometry.Map.Scale))
                e.Native.TryRainCounter(player);
            bool near = distance < 25;
            if (near && !e.Near) log("NS_ATTACK_NEAR id=" + v.Id + " distance=" + distance + " colliderEnabled=" + e.Native.Collider.enabled
                + " rigidbody=" + e.Native.Collider.attachedRigidbody.simulated + " effect=" + e.Native.Dealer.effectDealer.isActiveAndEnabled + " bounds=" + e.Native.Collider.bounds + " player=" + player.myCollider.bounds);
            e.Near = near;
            if (Time.realtimeSinceStartup >= e.NextSample)
            {
                e.NextSample = Time.realtimeSinceStartup + 1;
                log("NS_ATTACK_SAMPLE id=" + v.Id + " activation=" + v.Activation + " bounds=" + e.Native.Collider.bounds
                    + " distance=" + distance + " enabled=" + e.Native.Dealer.isActiveAndEnabled + " type=" + e.Native.Dealer.type);
            }
            if (v.Category == AttackCategory.Hazard || v.Category == AttackCategory.Melee || v.Category == AttackCategory.Contact)
            {
                if (v.Category == AttackCategory.Hazard) e.Native.SetHazard();
                bool touching = separation.isOverlapped;
                if (e.Touching && !touching) {
                    CloseContact(e);
                    if(v.Category == AttackCategory.Hazard) e.Verdict.Begin();
                    log("NS_HAZARD_SEPARATED id=" + v.Id + " category=" + v.Category + " health=" + player.health.currentValue + " position=" + player.transform.position);
                }
                e.Touching = touching;
            }
        }
        if (vesselPerformance || EncounterProfile.ForScene(geometry.SourceScene)?.Family == EncounterProfile.NightmareGrimm || EncounterProfile.ForScene(geometry.SourceScene)?.Family == EncounterProfile.Grimm) {
            double elapsed = (System.Diagnostics.Stopwatch.GetTimestamp() - costStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            updateTotal += elapsed; updateMax = Math.Max(updateMax, elapsed); updateCount++;
            syncTotal += (NativeContactFixture.SyncTicks - syncStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            polygonCount += NativeContactFixture.PolygonWrites - polygonsStart; syncCount += NativeContactFixture.SyncCalls - syncCallsStart;
            polygonReuseCount += NativeContactFixture.PolygonReuses - reusesStart;
            if (Time.realtimeSinceStartup >= nextCost) {
                nextCost = Time.realtimeSinceStartup + 2;
                log((vesselPerformance ? "NS_VESSEL_ATTACK_COST encounter=" : EncounterProfile.ForScene(geometry.SourceScene)?.Family == EncounterProfile.Grimm ? "NS_GRIMM_ATTACK_COST encounter=" : "NS_NKG_ATTACK_COST encounter=") + frame.Encounter + " retained=" + entries.Count + " active=" + entries.Values.Count(e=>e.Active) + " volumes=" + volumes.Length
                    + " meanMs=" + (updateTotal / updateCount).ToString("F3",System.Globalization.CultureInfo.InvariantCulture) + " maxMs=" + updateMax.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)
                    + " placementMeanMs=" + (placementTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / updateCount).ToString("F3",System.Globalization.CultureInfo.InvariantCulture) + " queryMeanMs=" + (queryTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / updateCount).ToString("F3",System.Globalization.CultureInfo.InvariantCulture)
                    + " syncMeanMs=" + (syncTotal / updateCount).ToString("F3",System.Globalization.CultureInfo.InvariantCulture) + " polygonWrites=" + polygonCount + " syncCalls=" + syncCount + " samples=" + updateCount
                    + " culled="+culledVolumes+" polygonReuses="+polygonReuseCount+" unityDelta="+Time.deltaTime+" unscaledDelta="+Time.unscaledDeltaTime+" maximumDelta="+Time.maximumDeltaTime+" captureDelta="+Time.captureDeltaTime
                    + " unityScale=" + Time.timeScale + " gameplayScale=" + TimePauseManager.GamePlayTimeScaleModifier.finalTimeScale + " nativeDelta=" + RCGTime.deltaTime + " animatorSpeed=" + player.animator.speed + " hostPaused=" + NativeInputBridge.HostPaused + " held=" + NativeHitPauseObserver.Held + " frame=" + Time.frameCount);
                if(vesselPerformance) {
                    log(FormattableString.Invariant($"NS_VESSEL_PACING encounter={frame.Encounter} frames={pacingFrames} wallSeconds={pacingWall:F4} unitySeconds={pacingUnity:F4} nativeSeconds={pacingNative:F4} unscaledSeconds={pacingUnscaled:F4} maxUpdateGapMs={pacingMax:F3} updateGapsOver9={pacingOver9} updateGapsOver12={pacingOver12} skippedFrames={pacingSkippedFrames} frozenFrames={pacingFrozen} bridgeHeldFrames={pacingHostHeld} newFixtures={fixtureCreates} fixtureTotalMs={fixtureCreateTotal:F3} fixtureMaxMs={fixtureCreateMax:F3} frame={Time.frameCount}"));
                    pacingFrames=pacingOver9=pacingOver12=pacingFrozen=pacingHostHeld=pacingSkippedFrames=fixtureCreates=0;
                    pacingWall=pacingUnity=pacingNative=pacingUnscaled=pacingMax=fixtureCreateTotal=fixtureCreateMax=0;
                }
                updateTotal = updateMax = syncTotal = 0; updateCount = 0; polygonCount = polygonReuseCount = syncCount = 0; placementTicks = queryTicks = 0;
            }
        }
        else { placementTicks = queryTicks = 0; }
        if (SessionConfiguration.Current.Has("void-probe") && !NativeRoomEntry.Active) VerifyVoid(frame);
    }
    private void VerifyVoid(ContactFrame frame)
    {
        if (!voidPlaced) {
            var hazard = frame.Volumes.FirstOrDefault(v => v.Active && v.Category == AttackCategory.Hazard && v.Points.Length >= 6);
            if (hazard == null) return;
            if (!entries.TryGetValue(hazard.Id, out var entry) || !entry.Native.Collider.enabled) return;
            // A concave/ring hazard's centroid can be safe space. Place the native
            // player's receiver on an actual contour and require real overlap.
            var vertex = geometry.Map.ToTarget(new Point(hazard.Points[0], hazard.Points[1]));
            Vector2 p = entry.Native.Collider.ClosestPoint(new Vector2(vertex.X, vertex.Y));
            float offset = player.myCollider.bounds.center.y - player.transform.position.y;
            player.SetPosition(new Vector3(p.x, p.y - offset, player.transform.position.z), true);
            player.Velocity = Vector2.zero; player.SetOnGround = false; player.ResetNeedToCheckGroundFlag(); Physics2D.SyncTransforms();
            if (!Physics2D.Distance(entry.Native.Collider, player.myCollider).isOverlapped)
                throw new InvalidOperationException("Void fixture could not establish native hazard overlap.");
            var host = geometry.Map.ToSource(new Point(p.x, p.y)); float x = host.X, y = host.Y;
            voidHealth = player.health.currentValue; voidAt = Time.realtimeSinceStartup; voidPlaced = true;
            log("NS_VOID_FIXTURE_PLACED scene=" + geometry.SourceScene + " encounter=" + frame.Encounter + " geometry=" + frame.GeometryFrame + " hazard=" + hazard.Id + " activation=" + hazard.Activation + " hostX=" + x + " hostY=" + y + " nativeCollision=True overlap=True");
        }
        if (!voidTrapped && (player.TouchTraped || player.CurrentStateType == PlayerStateType.Respawn)) {
            voidTrapped = true; log("NS_VOID_NATIVE_TRAP scene=" + geometry.SourceScene + " healthBefore=" + voidHealth + " health=" + player.health.currentValue);
        }
        if (voidTrapped && !voidRecovered && !player.TouchTraped && player.CurrentStateType == PlayerStateType.Normal && player.IsOnGround) {
            var p = geometry.Map.ToSource(new Point(player.transform.position.x, player.transform.position.y));
            if (Math.Abs(p.X - geometry.SourceFloor.X) > 0.1f || Math.Abs(p.Y - geometry.SourceFloor.Y) > 0.1f)
                throw new InvalidOperationException("Void recovery did not use verified arena entry: " + p.X + "," + p.Y);
            voidRecovered = true;
            log("NS_VOID_RECOVERED scene=" + geometry.SourceScene + " geometry=" + geometry.SourceFrame + " x=" + p.X + " y=" + p.Y + " nativeNormal=True grounded=True elapsed=" + (Time.realtimeSinceStartup - voidAt));
        }
        if (voidPlaced && !voidRecovered && Time.realtimeSinceStartup - voidAt > 8) throw new InvalidOperationException("Native void recovery deadline exceeded.");
    }

    public void Dispose() { burns.Dispose(); blockers?.Dispose(); revivalProbe.Dispose(); int released = entries.Count; foreach (var e in entries.Values) e.Dispose(); entries.Clear();
        if (cacheColliders && released > 0) log("NS_ATTACK_CACHE_DISPOSE encounter=" + cacheEncounter + " released=" + released + " retained=0"); }
}
