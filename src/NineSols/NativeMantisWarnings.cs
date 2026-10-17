using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YiGodhome.Bridge;
using YiGodhome.Geometry;

namespace YiGodhome;

// Independent visual leases; never activate a damage receiver or change boss timing.
internal sealed class NativeMantisWarnings : IDisposable
{
    private sealed class Cue { internal long Action; internal Guid Encounter; internal Transform Anchor=null!; internal PoolObject? Effect; }
    private static bool Owns(Cue cue) => cue.Effect!=null && cue.Effect.GetComponent<NativeWarningTiming>()?.Owns(cue.Action,cue.Encounter)==true;
    private readonly Dictionary<int,Cue> cues=new Dictionary<int,Cue>();
    private readonly FxPlayer effect;
    private readonly SoundPlayer sound;
    private readonly GeometryLandingProbe geometry;
    private readonly Action<string> log;
    private readonly bool watchers;
    internal NativeMantisWarnings(FxPlayer effect,SoundPlayer sound,GeometryLandingProbe geometry,Action<string> log,bool watchers=false) {
        this.effect=effect;this.sound=sound;this.geometry=geometry;this.log=log;
        this.watchers=watchers;
    }
    internal IEnumerable<Renderer> Renderers => cues.Values.Where(Owns)
        .SelectMany(c=>c.Effect!.GetComponentsInChildren<Renderer>(true));
    internal void Update(ContactFrame? frame) {
        var targets=frame?.Summons.Where(t=>t.AttackId>0 && t.Health>0).ToArray() ?? Array.Empty<SummonedTarget>();
        if(targets.Length>(watchers?6:3)) throw new InvalidOperationException("Native independent warning owner budget exceeded.");
        foreach(var id in cues.Keys.Where(id=>!targets.Any(t=>t.Entity==id)).ToArray()) { Retire(cues[id]);cues.Remove(id); }
        foreach(var t in targets) {
            if(!cues.TryGetValue(t.Entity,out var cue)) {
                cue=new Cue {Anchor=new GameObject(watchers?"YiGodhome Watcher Cue Anchor":"YiGodhome Mantis Cue Anchor").transform};cues.Add(t.Entity,cue);
            }
            var p=geometry.Map.ToTarget(new Point(t.X,t.Y));cue.Anchor.position=new Vector3(p.X,p.Y,0);
            if(cue.Action==t.AttackId) continue;
            if(Owns(cue)) cue.Effect!.ReturnToPool();
            cue.Action=t.AttackId;cue.Encounter=frame!.Encounter;
            var constraint=effect.setPositionConstraint;
            try {
                effect.setPositionConstraint=cue.Anchor;
                var spawned=effect.PlayCustomAt(cue.Anchor.position,Vector3.zero,cue.Anchor);
                cue.Effect=spawned==null?null:spawned.GetComponent<PoolObject>();
            } finally {effect.setPositionConstraint=constraint;}
            if(cue.Effect==null) throw new InvalidOperationException("Native Mantis warning did not spawn.");
            (cue.Effect.GetComponent<NativeWarningTiming>() ?? cue.Effect.gameObject.AddComponent<NativeWarningTiming>())
                .Begin(cue.Effect,log,t.AttackId,frame!.Encounter);
            bool sounded=sound.Play(null);
            log((watchers?"NS_WATCHER_WARNING":"NS_MANTIS_WARNING")+" encounter="+frame!.Encounter+" entity="+t.Entity+" activation="+t.Generation+" action="+t.AttackId+" owner="+cue.Effect.GetInstanceID()+" sounded="+sounded+" frame="+Time.frameCount);
        }
    }
    private static void Retire(Cue cue) {
        if(Owns(cue)) cue.Effect!.ReturnToPool();
        if(cue.Anchor!=null) UnityEngine.Object.Destroy(cue.Anchor.gameObject);
    }
    public void Dispose() {foreach(var c in cues.Values) Retire(c);cues.Clear();log(watchers?"NS_WATCHER_WARNINGS_RETIRED owners=0 anchors=0":"NS_MANTIS_WARNINGS_RETIRED owners=0 anchors=0");}
}
