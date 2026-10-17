using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace YiGodhome;

// Authored terrain sensors assume the Knight artwork pivot. Preserve that
// reference height without changing Yi collision or aimed-attack positions.
internal sealed class NativeHeroRangeQuery : IDisposable
{
    private static readonly Dictionary<GetPosition,NativeHeroRangeQuery> owners=new();
    private static readonly Harmony hooks=new("local.yigodhome.native.hero.range.query");
    private readonly List<GetPosition> queries=new();
    private readonly HealthManager actor;
    private readonly Action<string>? log;
    private bool logged;
    internal NativeHeroRangeQuery(HealthManager actor,Action<string>? log) {
        this.actor=actor;this.log=log;
        try {
            foreach(var name in new[]{"Dash Range L","Dash Range R","Stomp Range"}) {
                var child=actor.transform.Find(name)??throw new InvalidOperationException("Native terrain sensor missing.");
                var fsm=child.GetComponents<PlayMakerFSM>().Single(f=>f.FsmName=="follow_hero");
                var query=fsm.FsmStates.Single(s=>s.Name=="Follow").Actions.OfType<GetPosition>().Single();
                log?.Invoke("HK_NATIVE_RANGE_QUERY_CONTRACT name="+name.Replace(' ','_')+" space="+query.space+" everyFrame="+query.everyFrame
                    +" yNone="+query.y.IsNone+" yVariable="+query.y.Name+" xNone="+query.x.IsNone+" vectorNone="+query.vector.IsNone
                    +" target="+(FSMUtility.GetSafe(query.gameObject,query)?.name??"none")+" hero="+HeroController.instance.name);
                if(query.space!=Space.World || !query.everyFrame || query.y.IsNone && query.vector.IsNone
                    || FSMUtility.GetSafe(query.gameObject,query)!=HeroController.instance.gameObject)
                    throw new InvalidOperationException("Native terrain sensor position contract changed.");
                if(owners.ContainsKey(query))throw new InvalidOperationException("Competing native terrain query owner.");
                if(owners.Count==0)hooks.Patch(AccessTools.Method(typeof(GetPosition),"DoGetPosition"),postfix:new HarmonyMethod(typeof(NativeHeroRangeQuery),nameof(Read)));
                owners.Add(query,this);queries.Add(query);
            }
        } catch {Dispose();throw;}
    }
    private static void Read(GetPosition __instance) {
        if(!owners.TryGetValue(__instance,out var owner) || owner.actor==null || !owner.actor.gameObject.activeInHierarchy
            || !HeroProxy.TryRangePivotCorrection(owner.actor.gameObject.scene.name,out float correction))return;
        if(!__instance.y.IsNone)__instance.y.Value+=correction;
        if(!__instance.vector.IsNone)__instance.vector.Value+=Vector3.up*correction;
        if(!owner.logged) {
            owner.logged=true;
            owner.log?.Invoke("HK_NATIVE_RANGE_PIVOT entity="+owner.actor.GetInstanceID()+" correction="+correction+" queries=3 nativeTerrainSensors=True aimedTargetUnchanged=True frame="+Time.frameCount);
        }
    }
    public void Dispose() {
        foreach(var query in queries)owners.Remove(query);
        queries.Clear();if(owners.Count==0)hooks.UnpatchSelf();
    }
}
