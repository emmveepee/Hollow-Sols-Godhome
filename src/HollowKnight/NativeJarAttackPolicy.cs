using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using YiGodhome.Bridge;
using Pose = YiGodhome.Bridge.Pose;
namespace YiGodhome;

internal sealed class NativeJarGeneration : MonoBehaviour {
    internal long Generation;
    private SpawnJarControl? jar;private float originalBreak;private bool changed;
    private static readonly System.Reflection.FieldInfo Break=AccessTools.Field(typeof(SpawnJarControl),"breakY");
    private void OnEnable()=>Generation++;
    internal void BreakInAir(SpawnJarControl owner) {jar=owner;originalBreak=(float)Break.GetValue(owner);changed=true;Break.SetValue(owner,owner.transform.position.y);}
    private void OnDisable(){if(changed && jar!=null)Break.SetValue(jar,originalBreak);changed=false;}
}

// A native jar keeps its authored fall/break sequence. Parry clears contents;
// melee breaks it in air with the native enemy spawn intact.
internal sealed class NativeJarAttackPolicy : IDisposable
{
    private sealed class Entry { internal SpawnJarControl Jar=null!; internal CircleCollider2D Shape=null!; internal NativeJarGeneration Stamp=null!; internal long Generation=-1,NativeGeneration,Action; internal bool Parried,MeleeStruck; internal int Spawned; internal float GroundAt=-1; internal Bounds StrikeBounds; }
    private readonly struct Scope { internal readonly Entry? Previous,Entry; internal readonly bool Falling; internal Scope(Entry? previous,Entry? entry,bool falling){Previous=previous;Entry=entry;Falling=falling;} }
    private static NativeJarAttackPolicy? current;
    private Entry? stepping;
    private readonly Harmony evidence=new("local.yigodhome.collector-jar-evidence");
    private bool observing;
    private int breakSamples;
    private static readonly System.Reflection.FieldInfo IteratorOwner=AccessTools.Field(AccessTools.Inner(typeof(SpawnJarControl),"<Behaviour>d__16"),"<>4__this");
    private readonly Dictionary<GameObject,Entry> entries=new();
    private readonly NativeEncounterPools pools;
    private readonly Func<long> allocate;
    private readonly Action<string>? log;
    private static readonly System.Reflection.FieldInfo Collider=AccessTools.Field(typeof(SpawnJarControl),"col");
    private static readonly System.Reflection.FieldInfo Contents=AccessTools.Field(typeof(SpawnJarControl),"enemyToSpawn");
    private static readonly System.Reflection.FieldInfo BreakY=AccessTools.Field(typeof(SpawnJarControl),"breakY");
    private static readonly System.Reflection.FieldInfo IteratorState=AccessTools.Field(AccessTools.Inner(typeof(SpawnJarControl),"<Behaviour>d__16"),"<>1__state");
    internal NativeJarAttackPolicy(NativeEncounterPools pools,Func<long> allocate,Action<string>? log){this.pools=pools;this.allocate=allocate;this.log=log;
        {
            if(current!=null)throw new InvalidOperationException("Duplicate Collector jar evidence owner.");
            current=this;
            try {evidence.Patch(AccessTools.Method(AccessTools.Inner(typeof(SpawnJarControl),"<Behaviour>d__16"),"MoveNext"),
                prefix:new HarmonyMethod(typeof(NativeJarAttackPolicy),nameof(BeginStep)),finalizer:new HarmonyMethod(typeof(NativeJarAttackPolicy),nameof(EndStep)));
                HostDamageRegistry.PoolSpawned+=Spawned;observing=true;}
            catch {Dispose();throw;}
        }
    }
    private static bool BeginStep(object __instance,ref bool __result,out Scope __state) {
        var owner=current;__state=new Scope(owner?.stepping,null,false);
        if(owner==null || IteratorOwner.GetValue(__instance) is not SpawnJarControl jar || jar==null || !owner.entries.TryGetValue(jar.gameObject,out var entry))return true;
        entry.Spawned=0;owner.stepping=entry;__state=new Scope(__state.Previous,entry,entry.Shape.enabled);
        if(!entry.Parried && !entry.MeleeStruck && entry.Shape.enabled && (int)IteratorState.GetValue(__instance)==2
            && jar.transform.position.y<=(float)BreakY.GetValue(jar)) {
            if(entry.GroundAt<0)entry.GroundAt=Time.time;
            // Keep the exact native break pending briefly for the in-flight Yi verdict.
            // No content is spawned provisionally or removed after spawning.
            if(Time.time-entry.GroundAt<.15f) {
                var p=jar.transform.position;p.y=(float)BreakY.GetValue(jar);jar.transform.position=p;
                var body=jar.GetComponent<Rigidbody2D>();if(body!=null)body.linearVelocity=Vector2.zero;
                __result=true;return false;
            }
        }
        return true;
    }
    private static void Spawned(GameObject root) {
        var owner=current;
        if(owner?.stepping is Entry entry && root!=null && root.GetComponent<HealthManager>() is HealthManager health && owner.pools.Owns(root)) {
            entry.Spawned++;
            if(entry.MeleeStruck) {
                var shape=root.GetComponent<Collider2D>();
                owner.log?.Invoke("HK_JAR_SPAWNED_ENEMY entity="+health.GetInstanceID()+" action="+entry.Action+" health="+health.hp+" nativeCoroutine=True frame="+Time.frameCount);
                if(owner.breakSamples<32)owner.log?.Invoke("HK_JAR_SPAWN_GEOMETRY entity="+health.GetInstanceID()+" nativeInvincible="+health.IsInvincible
                    +" shapeEnabled="+(shape?.enabled==true)+" shape="+(shape?.bounds.ToString()??"none")+" strike="+entry.StrikeBounds+" overlap="+(shape!=null && shape.bounds.Intersects(entry.StrikeBounds))+" frame="+Time.frameCount);
            }
        }
    }
    private static void EndStep(Scope __state) {
        var owner=current;if(owner==null)return;
        var entry=__state.Entry;
        if(entry!=null && entry.Jar!=null && __state.Falling && !entry.Shape.enabled && owner.breakSamples++<32)
            owner.log?.Invoke("HK_JAR_NATIVE_BREAK action="+entry.Action+" root="+entry.Jar.GetInstanceID()+" generation="+entry.Generation
                +" parried="+entry.Parried+" contentsNull="+(Contents.GetValue(entry.Jar)==null)+" nativeEnemySpawns="+entry.Spawned
                +" nativeCoroutine=True frame="+Time.frameCount);
        owner.stepping=__state.Previous;
    }
    internal void Decorate(AttackVolume volume,DamageHero source,long generation) {
        if(source.name!="Spawn Jar(Clone)" || !pools.Owns(source.gameObject))return;
        var jar=source.GetComponent<SpawnJarControl>();
        if(jar==null || Collider.GetValue(jar) is not CircleCollider2D shape || shape.gameObject!=source.gameObject || source.GetComponent<HealthManager>()!=null)
            throw new InvalidOperationException("Native Collector jar collider/content manifest changed.");
        if(!entries.TryGetValue(source.gameObject,out var entry)) {
            if(entries.Count>=64)throw new InvalidOperationException("Native Collector jar capacity exceeded.");
            entries.Add(source.gameObject,entry=new Entry{Jar=jar,Shape=shape,Stamp=jar.GetComponent<NativeJarGeneration>()??jar.gameObject.AddComponent<NativeJarGeneration>()});
        }
        if(entry.Generation!=generation){entry.Generation=generation;entry.NativeGeneration=entry.Stamp.Generation;entry.Action=allocate();entry.Parried=false;entry.MeleeStruck=false;entry.GroundAt=-1;
            log?.Invoke("HK_JAR_STRIKE action="+entry.Action+" root="+jar.GetInstanceID()+" generation="+generation+" nativeCollider=True frame="+Time.frameCount);}
        volume.Category=AttackCategory.Melee;volume.ActionId=entry.Action;volume.NotifyParry=true;
        volume.Active &= !entry.Parried && !entry.MeleeStruck && Contents.GetValue(jar) is GameObject enemy && enemy!=null;
    }
    internal void Melee(Pose pose) {
        if(GameManager.instance.IsGamePaused())return;
        foreach(var entry in entries.Values) {
            if(entry.Jar==null || !entry.Jar.gameObject.activeInHierarchy || entry.Stamp.Generation!=entry.NativeGeneration
                || entry.Parried || entry.MeleeStruck || !entry.Shape.enabled || !pools.Owns(entry.Jar.gameObject))continue;
            foreach(var window in pose.Melee) {
                var bounds=new Bounds(new Vector3(window.X,window.Y,entry.Shape.bounds.center.z),new Vector3(window.Width,window.Height,1));
                if(!entry.Shape.bounds.Intersects(bounds))continue;
                entry.MeleeStruck=true;
                entry.StrikeBounds=bounds;
                entry.Stamp.BreakInAir(entry.Jar);
                log?.Invoke("HK_JAR_MELEE_BREAK action="+entry.Action+" generation="+entry.Generation+" collider="+window.Collider
                    +" activation="+window.Activation+" swing="+window.Swing+" nativeBreak=True contentsPreserved=True frame="+Time.frameCount);
                break;
            }
        }
    }
    internal bool Parry(long action,HitKind kind) {
        if(kind!=HitKind.StrikeParried)return false;
        foreach(var entry in entries.Values) {
            if(entry.Action!=action || entry.Stamp.Generation!=entry.NativeGeneration || entry.Parried || entry.Jar==null || !entry.Jar.gameObject.activeInHierarchy || !entry.Shape.enabled)continue;
            if(Contents.GetValue(entry.Jar) is not GameObject enemy || enemy==null)return false;
            entry.Jar.SetEnemySpawn(null!,0);entry.Parried=true;
            entry.Stamp.BreakInAir(entry.Jar);
            log?.Invoke("HK_JAR_PARRY_CONTENTS_CLEARED action="+action+" root="+entry.Jar.GetInstanceID()+" generation="+entry.Generation
                +" nativeSetter=True nativeAirBreak=True contentsNull="+(Contents.GetValue(entry.Jar)==null)+" frame="+Time.frameCount);
            return true;
        }
        return false;
    }
    public void Dispose(){if(observing)HostDamageRegistry.PoolSpawned-=Spawned;evidence.UnpatchSelf();if(current==this)current=null;observing=false;stepping=null;entries.Clear();}
}
