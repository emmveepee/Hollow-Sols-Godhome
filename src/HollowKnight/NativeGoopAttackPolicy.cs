using System;
using System.Collections.Generic;
using System.Linq;
using HutongGames.PlayMaker.Actions;
using UnityEngine;
using YiGodhome.Bridge;

namespace YiGodhome;

internal sealed class NativeGoopGeneration : MonoBehaviour
{
    internal long Generation;
    private void OnEnable()=>Generation++;
}

// Flight and puddle are separate authored colliders. Native Land/Recycle own
// their presentation and lifetime; only the injury window is shared.
internal sealed class NativeGoopAttackPolicy
{
    private sealed class Entry {
        internal GameObject Root=null!,Puddle=null!;
        internal PlayMakerFSM Control=null!;
        internal NativeGoopGeneration Marker=null!;
        internal long Generation=-1,FlightAction,PuddleAction;
    }
    private readonly Dictionary<int,Entry> entries=new();
    private readonly List<int> destroyed=new();
    private readonly Func<long> allocate;
    private readonly Action<string>? log;
    private readonly long group, flightGroup;
    private int samples;
    internal NativeGoopAttackPolicy(Func<long> allocate,Action<string>? log){this.allocate=allocate;this.log=log;group=allocate();flightGroup=allocate();}
    private static bool RootName(string name)=>name=="Vomit Glob Nosk(Clone)" || name=="Vomit Glob(Clone)";
    private static GameObject? Root(DamageHero damage)=>RootName(damage.name)?damage.gameObject
        :damage.name=="Puddle Box" && damage.transform.parent!=null && RootName(damage.transform.parent.name)?damage.transform.parent.gameObject:null;
    internal static AttackCategory? Role(DamageHero damage)=>Root(damage)==null?null:RootName(damage.name)?AttackCategory.Projectile:AttackCategory.Melee;
    private Entry Bind(GameObject root) {
        int id=root.GetInstanceID();
        if(entries.TryGetValue(id,out var entry) && entry.Root==root)return entry;
        Prune();if(entries.Count>=AttackVolumes.MaxCount/2)throw new InvalidOperationException("Native goop root/quarantine budget exceeded.");
        var control=root.GetComponents<PlayMakerFSM>().Single(f=>f.FsmName=="Vomit Glob");
        var puddle=root.transform.Find("Puddle Box")?.gameObject;
        if(puddle==null || root.GetComponent<DamageHero>()==null || puddle.GetComponent<DamageHero>()==null
            || root.GetComponent<BoxCollider2D>()==null || puddle.GetComponent<BoxCollider2D>()==null
            || root.GetComponentsInChildren<DamageHero>(true).Length!=2)
            throw new InvalidOperationException("Native flight/puddle collider manifest changed.");
        var init=control.FsmStates.Single(s=>s.Name=="Init");var land=control.FsmStates.Single(s=>s.Name=="Land");
        if(!control.FsmStates.Any(s=>s.Name=="In Air")
            || !init.Actions.OfType<ActivateGameObject>().Any(a=>!a.activate.Value && FSMUtility.GetSafe(a.gameObject,a)==puddle)
            || !land.Actions.OfType<ActivateGameObject>().Any(a=>a.activate.Value && FSMUtility.GetSafe(a.gameObject,a)==puddle)
            || !land.Actions.OfType<SetCollider>().Any(a=>!a.active.Value && FSMUtility.GetSafe(a.gameObject,a)==root)
            || !control.FsmStates.Single(s=>s.Name=="Recycle").Actions.OfType<RecycleSelf>().Any())
            throw new InvalidOperationException("Native goop land/recycle contract changed.");
        entry=new Entry{Root=root,Puddle=puddle,Control=control,Marker=root.GetComponent<NativeGoopGeneration>()??root.AddComponent<NativeGoopGeneration>()};
        entries[id]=entry;
        if(samples++<64)log?.Invoke("HK_GOOP_NATIVE_MANIFEST root="+id+" name="+root.name.Replace(' ','_')+" flight="+root.GetComponent<DamageHero>().GetInstanceID()
            +" puddle="+puddle.GetComponent<DamageHero>().GetInstanceID()+" group="+group+" nativeLand=True nativeRecycle=True frame="+Time.frameCount);
        return entry;
    }
    internal void Decorate(AttackVolume volume,DamageHero damage) {
        var root=Root(damage);if(root==null)return;
        var entry=Bind(root);
        if(entry.Generation!=entry.Marker.Generation) {
            entry.Generation=entry.Marker.Generation;entry.FlightAction=allocate();entry.PuddleAction=allocate();
            if(samples++<128)log?.Invoke("HK_GOOP_NATIVE_GENERATION root="+root.GetInstanceID()+" generation="+entry.Generation+" flightAction="+entry.FlightAction
                +" puddleAction="+entry.PuddleAction+" group="+group+" sharedSeconds=1 frame="+Time.frameCount);
        }
        bool flight=damage.gameObject==root;
        volume.ActionId=flight?entry.FlightAction:entry.PuddleAction;
        volume.DamageGroupId=flight?flightGroup:group;volume.InitialDamageGroupHold=1;volume.RepeatDamageGroupHold=true;
        if(flight)volume.Traits|=AttackTraits.RetireOnParry;
        volume.ConsumeOnContact=false;volume.ParryCooldown=1;volume.CooldownOnDamage=true;volume.NotifyParry=true;
    }
    internal bool Parry(long action,HitKind kind) {
        if(kind!=HitKind.StrikeParried || action<=0)return false;
        foreach(var entry in entries.Values) {
            if(entry.Root==null || !entry.Root.activeInHierarchy || entry.Generation!=entry.Marker.Generation || action!=entry.FlightAction && action!=entry.PuddleAction)continue;
            string before=entry.Control.ActiveStateName;
            // A real queued parry can reach HK just after Land; the same pool
            // generation is still its owner, and its puddle must also disappear.
            entry.Control.SetState("Recycle");
            if(entry.Root.activeInHierarchy || entry.Puddle.activeInHierarchy)throw new InvalidOperationException("Native goop recycle did not retire its flight and puddle; no replay.");
            log?.Invoke("HK_GOOP_PARRY_RECYCLED action="+action+" root="+entry.Root.GetInstanceID()+" generation="+entry.Generation+" previous="+before.Replace(' ','_')
                +" puddleActive=False nativeRecycle=True landSkipped=True frame="+Time.frameCount);return true;
        }
        return false;
    }
    private void Prune(){destroyed.Clear();foreach(var pair in entries)if(pair.Value.Root==null)destroyed.Add(pair.Key);foreach(var id in destroyed)entries.Remove(id);}
}
