using System;
using System.Collections.Generic;
using HarmonyLib;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;
using YiGodhome.Bridge;

namespace YiGodhome;

internal readonly struct NativeSpawnStrike {
    internal readonly long Action;
    internal readonly AttackTraits Traits;
    internal readonly long DamageGroup;
    internal readonly long VisualSequence;
    internal readonly bool Suppress, CancelOnCounter;
    internal NativeSpawnStrike(long action,AttackTraits traits,long damageGroup=0,bool suppress=false,bool cancelOnCounter=false,long visualSequence=0) {Action=action;Traits=traits;DamageGroup=damageGroup;Suppress=suppress;CancelOnCounter=cancelOnCounter;VisualSequence=visualSequence;}
}

internal sealed class NativeWaveStrikeLease : MonoBehaviour
{
    internal long Action;
    internal long Generation;
    internal bool CancelOnCounter;
    internal AttackTraits Traits;
    internal long DamageGroup;
    internal long VisualSequence;
    internal MeshRenderer[]? DefenderArtwork;
    internal float[]? LastPoints;
    private void OnEnable() { Action=DamageGroup=VisualSequence=0;CancelOnCounter=false; Traits=AttackTraits.None; Generation++; LastPoints=null; }
}

// Shockwave Wave emits a new Spurt every update. Those are shape samples of
// one authored wave, not independent strikes. Propagate its native spawn lease.
internal sealed class NativeShockwaveStrikes : IDisposable
{
    private static NativeShockwaveStrikes? current;
    private readonly List<MeshRenderer> defenderArtwork=new();
    private MeshRenderer[] defenderVisuals=Array.Empty<MeshRenderer>();
    internal MeshRenderer[] DefenderVisuals=>defenderVisuals;
    private readonly Harmony harmony=new Harmony("local.yigodhome.shockwave.strikes");
    private readonly NativeEncounterPools pools;
    private readonly Func<long> allocate;
    private readonly Action<string>? log;
    private readonly Func<FsmStateAction,GameObject,NativeSpawnStrike> authoredStrike;
    private NativeSpawnStrike spawning;
    private readonly bool gorb;
    private readonly bool hive;
    private readonly bool uumuu;
    private readonly bool sheo;
    private PlayMakerFSM? hiveControl;
    private bool hiveRoaring;
    private long hiveMouthGroup;
    private readonly Dictionary<long,List<(NativeWaveStrikeLease Lease,long Generation)>> paintGroups=new();
    private readonly Dictionary<(Fsm Fsm,FsmState State),long> spawnGroups=new();
    private int spawnFrame=-1;
    private int volleySamples;
    private int soulSpawnSamples;
    private int soulSpawnResultSamples;
    private int emissionOwnerSamples;
    private readonly HashSet<GameObject> hiveGlobs=new();
    private readonly Dictionary<long,List<NativeWaveStrikeLease>> crimsonGroups=new();
    private int samples;
    private int waveSamples;
    private int throwSamples;
    private readonly Dictionary<int,NativeWaveStrikeLease> waves=new();
    private readonly Dictionary<int,(NativeWaveStrikeLease Lease,long Generation)> zoteWaveParts=new();
    private readonly List<long> ended=new();
    internal NativeShockwaveStrikes(NativeEncounterPools pools, Func<long> allocate, Action<string>? log, Func<FsmStateAction,GameObject,NativeSpawnStrike> authoredStrike,bool gorb=false) {
        if(current!=null) throw new InvalidOperationException("Duplicate shockwave strike owner.");
        this.pools=pools; this.allocate=allocate; this.log=log; this.authoredStrike=authoredStrike; this.gorb=gorb; hive=EncounterProfile.Selected.Family==EncounterProfile.HiveKnight; uumuu=EncounterProfile.Selected.Family==EncounterProfile.Uumuu;sheo=EncounterProfile.Selected.Family==EncounterProfile.Sheo; current=this;
        try {
            if(hive) {
                harmony.Patch(AccessTools.Method(typeof(ActivateAllChildren),"OnEnter"),prefix:new HarmonyMethod(typeof(NativeShockwaveStrikes),nameof(HiveBurst)));
                harmony.Patch(AccessTools.Method(typeof(SetCircleCollider),"OnEnter"),prefix:new HarmonyMethod(typeof(NativeShockwaveStrikes),nameof(HiveArmed)));
            }
            harmony.Patch(AccessTools.Method(typeof(SpawnObjectFromGlobalPool),"OnEnter"),prefix:new HarmonyMethod(typeof(NativeShockwaveStrikes),nameof(Begin)),finalizer:new HarmonyMethod(typeof(NativeShockwaveStrikes),nameof(End)));
            harmony.Patch(AccessTools.Method(typeof(FlingObjectsFromGlobalPool),"OnEnter"),prefix:new HarmonyMethod(typeof(NativeShockwaveStrikes),nameof(BeginFling)),finalizer:new HarmonyMethod(typeof(NativeShockwaveStrikes),nameof(End)));
            harmony.Patch(AccessTools.Method(typeof(SpawnObjectFromGlobalPoolOverTime),"OnUpdate"),prefix:new HarmonyMethod(typeof(NativeShockwaveStrikes),nameof(BeginTimed)),finalizer:new HarmonyMethod(typeof(NativeShockwaveStrikes),nameof(End)));
            harmony.Patch(AccessTools.Method(typeof(SpawnObjectFromGlobalPoolOverTimeV2),"OnUpdate"),prefix:new HarmonyMethod(typeof(NativeShockwaveStrikes),nameof(BeginTimedV2)),finalizer:new HarmonyMethod(typeof(NativeShockwaveStrikes),nameof(End)));
            HostDamageRegistry.PoolSpawned+=Spawned;
        } catch { Dispose(); throw; }
    }
    private static bool Begin(SpawnObjectFromGlobalPool __instance,out NativeSpawnStrike __state) => Enter(__instance,__instance.gameObject.Value,out __state);
    private static bool BeginFling(FlingObjectsFromGlobalPool __instance,out NativeSpawnStrike __state) => Enter(__instance,__instance.gameObject.Value,out __state);
    private static bool HiveRoot(GameObject root)=>root.scene.name==EncounterProfile.HiveKnight.Scene
        && (root.name=="Hive Knight Glob" || root.name=="Hive Knight Glob (1)" || root.name=="Hive Knight Glob (2)");
    private static void HiveArmed(SetCircleCollider __instance) {
        var owner=current;var root=__instance.Owner;
        if(owner==null || !owner.hive || root==null || !__instance.active.Value || __instance.Fsm.Name!="Control" || __instance.State.Name!="Form 2"
            || !HiveRoot(root) || FSMUtility.GetSafe(__instance.gameObject,__instance)!=root) return;
        var lease=root.GetComponent<NativeWaveStrikeLease>()??root.AddComponent<NativeWaveStrikeLease>();
        lease.Action=0;lease.Traits=AttackTraits.None;lease.DamageGroup=owner.allocate();lease.Generation++;
        if(owner.volleySamples++<64) owner.log?.Invoke("HK_HIVE_NATIVE_VOLLEY group="+lease.DamageGroup+" root="+root.name.Replace(' ','_')
            +" nativeChildren=8 independentDefense=True coreAndBurst=True frame="+Time.frameCount);
    }
    private static void HiveBurst(ActivateAllChildren __instance) {
        var owner=current;var root=__instance.Owner;var children=__instance.gameObject.Value;
        if(owner==null || !__instance.activate || root==null || children==null || __instance.Fsm.Name!="Control" || __instance.State.Name!="Burst"
            || !HiveRoot(root) || children.name!="Stingers" || children.transform.parent!=root.transform) return;
        if(owner.hiveGlobs.Add(root)) {
            if(owner.hiveGlobs.Count>3 || children.GetComponentsInChildren<HiveKnightStinger>(true).Length!=8)
                throw new InvalidOperationException("Native Hive volley manifest changed.");
        }
        var lease=root.GetComponent<NativeWaveStrikeLease>();
        if(lease==null || lease.DamageGroup<=0) throw new InvalidOperationException("Native Hive burst lost its armed core group.");
    }
    private static bool BeginTimed(SpawnObjectFromGlobalPoolOverTime __instance,out NativeSpawnStrike __state) => Enter(__instance,__instance.gameObject.Value,out __state);
    private static bool BeginTimedV2(SpawnObjectFromGlobalPoolOverTimeV2 __instance,out NativeSpawnStrike __state) => Enter(__instance,__instance.gameObject.Value,out __state);
    private long SpawnGroup(FsmStateAction action) {
        if(spawnFrame!=Time.frameCount) {spawnFrame=Time.frameCount;spawnGroups.Clear();}
        var key=(action.Fsm,action.State);
        if(!spawnGroups.TryGetValue(key,out var group)) {
            if(spawnGroups.Count>=16) throw new InvalidOperationException("Native strike spawn source budget exceeded.");
            spawnGroups.Add(key,group=allocate());
            if(SessionConfiguration.Current.RunFixtures && SessionConfiguration.Current.Has("trace-probe") && emissionOwnerSamples++<64)
                log?.Invoke("HK_NATIVE_EMISSION_OWNER group="+group+" owner="+action.Fsm.GameObject.name.Replace(' ','_')+" ownerId="+action.Fsm.GameObject.GetInstanceID()
                    +" healthOwner="+(action.Fsm.GameObject.GetComponentInParent<HealthManager>(true)?.GetInstanceID()??0)+" state="+action.State.Name.Replace(' ','_')+" frame="+Time.frameCount);
        }
        return group;
    }
    private static bool Enter(FsmStateAction action,GameObject prefab,out NativeSpawnStrike previous) {
        var owner=current; previous=owner?.spawning??default;
        if(owner==null || prefab==null) return true;
        if(SessionConfiguration.Current.RunFixtures && SessionConfiguration.Current.Has("trace-probe") && (EncounterProfile.Selected.Family==EncounterProfile.SoulMaster || EncounterProfile.Selected.Family==EncounterProfile.SoulTyrant)
            && (prefab.name=="Mage Orb" || prefab.name=="Mage Orb Over") && owner.pools.OwnsPrefab(prefab) && owner.soulSpawnSamples++<32)
            owner.log?.Invoke("HK_SOUL_ORB_SPAWN_CONTRACT prefab="+prefab.name.Replace(' ','_')+" owner="+action.Fsm.GameObject.name.Replace(' ','_')+" ownerId="+action.Fsm.GameObject.GetInstanceID()
                +" fsm="+action.Fsm.Name.Replace(' ','_')+" state="+action.State.Name.Replace(' ','_')+" frame="+Time.frameCount);
        if(owner.gorb && prefab.name=="Shot Slug Spear" && owner.pools.OwnsPrefab(prefab)
            && ((action.Fsm.Name=="Attacking" && (action.State.Name=="Attack" || action.State.Name=="Double" || action.State.Name=="Triple"))
                || action.Fsm.Name=="Distance Attack" && action.State.Name=="Attack")) {
            owner.spawning=new NativeSpawnStrike(0,AttackTraits.None,owner.SpawnGroup(action));return true;
        }
        var parent=action.Fsm.GameObject;
        var inherited=parent.GetComponent<NativeWaveStrikeLease>();
        if(owner.uumuu && prefab.name=="Gas Explosion Uumuu" && owner.pools.OwnsPrefab(prefab)
            && parent.name.StartsWith("Lil Jellyfish",StringComparison.Ordinal) && action.Fsm.Name=="Lil Jelly") {
            if(inherited==null || inherited.DamageGroup<=0) throw new InvalidOperationException("Native Ooma explosion lost its core injury group.");
            owner.spawning=new NativeSpawnStrike(0,AttackTraits.None,inherited.DamageGroup);
            if(owner.samples++<64) owner.log?.Invoke("HK_OOMA_EXPLOSION_GROUP core="+parent.GetInstanceID()+" group="+inherited.DamageGroup+" nativeState="+action.State.Name.Replace(' ','_')+" frame="+Time.frameCount);
            return true;
        }
        if(inherited!=null && inherited.Action>0) owner.spawning=new NativeSpawnStrike(inherited.Action,inherited.Traits,cancelOnCounter:inherited.CancelOnCounter);
        else if((prefab.name=="Shockwave Wave" || EncounterProfile.Selected.Family==EncounterProfile.GreyPrinceZote && prefab.name=="Shockwave Wave Z") && owner.pools.OwnsPrefab(prefab)) {
            var authored=owner.authoredStrike(action,prefab);
            owner.spawning=authored.Action>0?authored:new NativeSpawnStrike(owner.SpawnGroup(action),AttackTraits.None);
        }
        else {
            owner.spawning=owner.pools.OwnsPrefab(prefab)?owner.authoredStrike(action,prefab):default;
            if(!owner.spawning.Suppress && owner.spawning.Action==0 && owner.spawning.DamageGroup==0 && owner.pools.OwnsPrefab(prefab)
                && prefab.GetComponent<EnemyBullet>()!=null && prefab.GetComponent<DamageHero>()!=null && prefab.GetComponent<HealthManager>()==null)
                owner.spawning=new NativeSpawnStrike(0,AttackTraits.None,owner.SpawnGroup(action));
        }
        if(owner.spawning.Suppress) {action.Finish();return false;}
        return true;
    }
    private static void End(NativeSpawnStrike __state) { if(current!=null) current.spawning=__state; }
    internal static void OomaCreated(GameObject root) {
        if (current?.uumuu == true && root != null && root.name == "Lil Jellyfish(Clone)") Spawned(root);
    }
    private static void Spawned(GameObject root) {
        var owner=current;
        if(owner==null || !owner.pools.Owns(root)) return;
        bool core=owner.uumuu && root.name=="Lil Jellyfish(Clone)";
        if(!core && owner.spawning.Action<=0 && owner.spawning.DamageGroup<=0) return;
        var lease=root.GetComponent<NativeWaveStrikeLease>()??root.AddComponent<NativeWaveStrikeLease>();
        lease.Action=core?0:owner.spawning.Action;lease.Traits=core?AttackTraits.None:owner.spawning.Traits;lease.DamageGroup=core?owner.allocate():owner.spawning.DamageGroup;
        lease.CancelOnCounter=owner.spawning.CancelOnCounter;
        lease.VisualSequence=owner.spawning.VisualSequence;
        if(lease.VisualSequence>0 && root.name=="Dung Ball Small(Clone)" && lease.DefenderArtwork==null)
            lease.DefenderArtwork=Array.FindAll(root.GetComponentsInChildren<MeshRenderer>(true),r=>!r.name.StartsWith("YiGodhome",StringComparison.Ordinal));
        if(SessionConfiguration.Current.RunFixtures && SessionConfiguration.Current.Has("trace-probe")
            && (EncounterProfile.Selected.Family==EncounterProfile.SoulMaster || EncounterProfile.Selected.Family==EncounterProfile.SoulTyrant)
            && root.name=="Mage Orb Over(Clone)" && lease.DamageGroup>0 && owner.soulSpawnResultSamples++<64)
            SoulOrbitSpawnEvidence.Bind(root,lease.DamageGroup,owner.log);
        if(owner.sheo && root.GetComponent<PaintBullet>()!=null && lease.DamageGroup>0) {
            if(!owner.paintGroups.TryGetValue(lease.DamageGroup,out var group)) {
                if(owner.paintGroups.Count>=64)throw new InvalidOperationException("Native paint volley group capacity exceeded.");
                owner.paintGroups.Add(lease.DamageGroup,group=new List<(NativeWaveStrikeLease,long)>());
            }
            if(group.Count>=64)throw new InvalidOperationException("Native paint volley closure exceeded.");
            group.Add((lease,lease.Generation));
        }
        if(lease.DamageGroup>0 && owner.volleySamples++<128) owner.log?.Invoke("HK_NATIVE_VOLLEY_GROUP group="+lease.DamageGroup+" root="+root.name.Replace(' ','_')+" rootId="+root.GetInstanceID()+" generation="+lease.Generation+" frame="+Time.frameCount+" defenseIndependent=True");
        if((lease.Traits&AttackTraits.Crimson)!=0) {
            if(!owner.crimsonGroups.TryGetValue(lease.Action,out var group)) {
                if(owner.crimsonGroups.Count>=64) throw new InvalidOperationException("Native crimson spawn group budget exceeded.");
                owner.crimsonGroups.Add(lease.Action,group=new List<NativeWaveStrikeLease>());
            }
            if(!group.Contains(lease)) {
                if(group.Count>=64) throw new InvalidOperationException("Native crimson spawn closure exceeded.");
                group.Add(lease);
            }
        }
        if(EncounterProfile.Selected.Family==EncounterProfile.GreyPrinceZote && lease.Action>0 && lease.CancelOnCounter) {
            if(!owner.zoteWaveParts.ContainsKey(root.GetInstanceID()) && owner.zoteWaveParts.Count>=512)
                throw new InvalidOperationException("Native Zote wave-part budget exceeded.");
            owner.zoteWaveParts[root.GetInstanceID()]=(lease,lease.Generation);
        }
        bool waveController=root.name=="Shockwave Wave(Clone)" || EncounterProfile.Selected.Family==EncounterProfile.GreyPrinceZote && root.name=="Shockwave Wave Z(Clone)";
        if(waveController) {
            int id=root.GetInstanceID();
            if(!owner.waves.ContainsKey(id) && owner.waves.Count>=64) throw new InvalidOperationException("Native shockwave controller budget exceeded.");
            owner.waves[id]=lease;
        }
        bool defenderThrow = root.name == "Dung Ball Large(Clone)" || root.name == "Dung Ball Large W(Clone)";
        bool sampled = waveController ? owner.waveSamples++ < 64 : defenderThrow ? owner.throwSamples++ < 64 : owner.samples++ < 64;
        if(waveController && owner.waveSamples==65) owner.log?.Invoke("HK_SHOCKWAVE_EVIDENCE_CAPPED frame="+Time.frameCount+" controllers=64 laterControllersUnobserved=True");
        if(sampled) owner.log?.Invoke("HK_SHOCKWAVE_STRIKE_GROUP action="+lease.Action+" root="+root.name.Replace(' ','_')+" frame="+Time.frameCount+" emittedShapesShareVerdict=True traits="+lease.Traits+" rootId="+root.GetInstanceID()+" generation="+lease.Generation);
    }
    internal void Decorate(AttackVolume volume,DamageHero source) {
        if(hive && HostReflectedProjectile.HiveBee(source)!=null) {
            if(hiveControl==null) {
                foreach(var health in Resources.FindObjectsOfTypeAll<HealthManager>())
                    if(health.gameObject.scene.name==EncounterProfile.HiveKnight.Scene && EncounterProfile.HiveKnight.IsBossName(health.name))
                        foreach(var fsm in health.GetComponents<PlayMakerFSM>())if(fsm.FsmName=="Control")hiveControl=fsm;
            }
            bool roaring=hiveControl!=null && hiveControl.ActiveStateName=="Roar";
            if(roaring && !hiveRoaring)hiveMouthGroup=allocate();
            hiveRoaring=roaring;
            var mouth=source.GetComponent<NativeWaveStrikeLease>()??source.gameObject.AddComponent<NativeWaveStrikeLease>();
            if(roaring && mouth.DamageGroup==0)mouth.DamageGroup=hiveMouthGroup;
            if(mouth.DamageGroup>0) {volume.DamageGroupId=mouth.DamageGroup;volume.InitialDamageGroupHold=1;}
        }
        var lease=source.GetComponentInParent<NativeWaveStrikeLease>();
        bool defenderDung=lease!=null && lease.DamageGroup>0
            && (EncounterProfile.Selected.Family==EncounterProfile.WhiteDefender || EncounterProfile.Selected.Family==EncounterProfile.DungDefender)
            && (lease.name=="Dung Ball Small(Clone)" || lease.name=="Dung Ball Large(Clone)" || lease.name=="Dung Ball Large W(Clone)");
        if(defenderDung) {
            // Injury grace spans all dung, including new balls arriving during
            // the hold; individual ActionIds still retire defenses separately.
            volume.DamageGroupId=lease!.DamageGroup;volume.InitialDamageGroupHold=1;volume.RepeatDamageGroupHold=true;
        }
        if(lease!=null && lease.DamageGroup>0 && (volume.Category==AttackCategory.Projectile || hive && source.gameObject==lease.gameObject && HiveRoot(lease.gameObject)
            || uumuu && volume.Category==AttackCategory.Melee && (lease.name=="Lil Jellyfish(Clone)" || lease.name=="Gas Explosion Uumuu(Clone)"))) volume.DamageGroupId=lease.DamageGroup;
        bool vesselProjectile=(EncounterProfile.Selected.Family==EncounterProfile.BrokenVessel || EncounterProfile.Selected.Family==EncounterProfile.LostKin)
            && volume.Category==AttackCategory.Projectile && (source.name=="IK Projectile DS(Clone)" || source.name=="IK Projectile SH(Clone)" && source.GetComponent<BrokenVesselGlob>()!=null);
        bool defenderProjectile=lease?.VisualSequence>0 && source.name=="Dung Ball Small(Clone)";
        if(lease==null || lease.Action<=0 || volume.Category!=AttackCategory.Melee && !vesselProjectile && !defenderProjectile) return;
        volume.ActionId=lease.Action; volume.Traits|=lease.Traits;
        if(!vesselProjectile && !(defenderDung && volume.Category==AttackCategory.Projectile))volume.Traits|=AttackTraits.RetireOnParry;
        if(vesselProjectile) {
            volume.ConsumeOnContact=false;
            if(source.name=="IK Projectile DS(Clone)") {
                volume.InitialDamageGroupHold=1;volume.ParryCooldown=1;volume.CooldownOnDamage=true;
            }
        }
        if(volume.Points.Length>=6) foreach(var wave in waves.Values) if(wave.Action==lease.Action) wave.LastPoints=volume.Points;
    }
    internal bool Counter(long action,HitKind kind) {
        if(kind!=HitKind.UnboundedCounter || !crimsonGroups.TryGetValue(action,out var group)) return false;
        foreach(var lease in group) {
            if(lease==null || lease.Action!=action || !lease.gameObject.activeInHierarchy) continue;
            var damage=lease.GetComponent<DamageHero>();
            if(damage==null || !damage.isActiveAndEnabled || damage.damageDealt<=0 || damage.GetComponent<Collider2D>()?.enabled!=true) continue;
            log?.Invoke("HK_NATIVE_SPAWN_CRIMSON_COUNTER action="+action+" source="+lease.name.Replace(' ','_')+" nativeSequenceContinues=True frame="+Time.frameCount);
            if(damage.GetComponent<BrokenVesselGlob>()!=null && damage.name=="IK Projectile SH(Clone)") {
                if(damage.GetComponent<AutoRecycleSelf>()==null)throw new InvalidOperationException("Native vessel counter recycle owner changed.");
                HostReflectedProjectile.RecycleSword(damage);
                log?.Invoke("HK_VESSEL_SHAKE_COUNTER action="+action+" nativeRecycle=True bossInterrupted=False frame="+Time.frameCount);
            }
            return true;
        }
        return false;
    }
    internal void RetireZoteSkyfall(long action) {
        // A counter receipt may arrive just after Ft Waves already spawned.
        // Recycle only this exact strike's native controllers/emitted parts;
        // the counter-stagger's later Charge Slam waves have a separate action.
        int retired=0;
        foreach(var part in zoteWaveParts.Values) {
            var lease=part.Lease;
            if(lease==null || lease.Generation!=part.Generation || lease.Action!=action || !lease.gameObject.activeInHierarchy)continue;
            lease.gameObject.Recycle();retired++;
        }
        log?.Invoke("HK_ZOTE_SKYFALL_WAVES_RETIRED action="+action+" nativeParts="+retired+" generationChecked=True frame="+Time.frameCount);
    }
    internal void RetirePaint(long action) {
        if(!sheo || !paintGroups.TryGetValue(action,out var group))return;
        int retired=0;
        foreach(var member in group) {
            var lease=member.Lease;
            if(lease==null || lease.Generation!=member.Generation || lease.DamageGroup!=action || !lease.gameObject.activeInHierarchy)continue;
            var paint=lease.GetComponent<PaintBullet>();
            if(paint!=null && HostReflectedProjectile.PaintIsActive(paint)) {HostReflectedProjectile.ImpactPaint(paint);retired++;}
        }
        log?.Invoke("HK_SHEO_PAINT_RETIRE action="+action+" liveProjectiles="+retired+" nativeImpact=True generationChecked=True frame="+Time.frameCount);
    }
    internal AttackVolume[] RetainControllers(AttackVolume[] volumes) {
        ended.Clear();
        foreach(var pair in zoteWaveParts)if(pair.Value.Lease==null || pair.Value.Lease.Generation!=pair.Value.Generation
            || !pair.Value.Lease.gameObject.activeInHierarchy)ended.Add(pair.Key);
        foreach(var id in ended)zoteWaveParts.Remove((int)id);
        ended.Clear();
        foreach(var pair in paintGroups) {
            for(int i=pair.Value.Count-1;i>=0;i--) {
                var member=pair.Value[i];var lease=member.Lease;
                if(lease==null || lease.Generation!=member.Generation || lease.DamageGroup!=pair.Key || !lease.gameObject.activeInHierarchy)pair.Value.RemoveAt(i);
            }
            if(pair.Value.Count==0)ended.Add(pair.Key);
        }
        foreach(var action in ended)paintGroups.Remove(action);
        ended.Clear();
        foreach(var pair in crimsonGroups) {
            pair.Value.RemoveAll(l=>l==null || l.Action!=pair.Key || !l.gameObject.activeInHierarchy);
            if(pair.Value.Count==0) ended.Add(pair.Key);
        }
        foreach(var action in ended) crimsonGroups.Remove(action);
        defenderArtwork.Clear();
        foreach(var group in crimsonGroups.Values)foreach(var lease in group) {
            if(lease.VisualSequence==0 || lease.DefenderArtwork==null)continue;
            foreach(var part in lease.DefenderArtwork)if(part!=null)defenderArtwork.Add(part);
        }
        bool changed=defenderVisuals.Length!=defenderArtwork.Count;
        for(int i=0;!changed && i<defenderVisuals.Length;i++)changed=defenderVisuals[i]!=defenderArtwork[i];
        if(changed)defenderVisuals=defenderArtwork.ToArray();
        ended.Clear();
        foreach(var pair in waves) if(pair.Value==null || !pair.Value.gameObject.activeInHierarchy || pair.Value.Action<=0) ended.Add(pair.Key);
        foreach(var id in ended) waves.Remove((int)id);
        if(waves.Count==0) return volumes;
        var result=new List<AttackVolume>(volumes);
        foreach(var pair in waves) {
            var wave=pair.Value;
            if(wave.LastPoints==null) continue;
            if(result.Count>=AttackVolumes.MaxCount) throw new InvalidOperationException("Native shockwave retention capacity exceeded.");
            // Inactive metadata retains the native controller's logical strike
            // across gaps between emitted shapes. It creates no NS hitbox.
            result.Add(new AttackVolume {Id=wave.GetInstanceID(),Activation=wave.Generation,ActionId=wave.Action,Category=AttackCategory.Melee,Damage=0,Active=false,Points=wave.LastPoints});
        }
        return result.ToArray();
    }
    public void Dispose() { zoteWaveParts.Clear();HostDamageRegistry.PoolSpawned-=Spawned; harmony.UnpatchSelf(); if(current==this) current=null; }
}
