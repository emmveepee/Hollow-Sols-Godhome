using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace YiGodhome;

// Revival health drops use LootSpawner, outside the FxPlayer ownership graph.
// Observe only Yi's exact revival spawner; never alter drops or pickup rules.
internal sealed class NativeRevivalDrops : IDisposable
{
    private sealed class Drop { internal Renderer[] Renderers=Array.Empty<Renderer>(); }
    private static NativeRevivalDrops? active;
    private readonly Player player;
    private readonly LootSpawner? spawner;
    private readonly Action<string> log;
    private readonly Dictionary<DropItem,Drop> drops=new();
    private readonly HashSet<Renderer> artwork=new();
    private int spawnGeneration;
    internal static int SpawnGeneration(Player player)=>active?.player==player?active.spawnGeneration:0;
    private readonly Harmony harmony=new("local.yigodhome.revival-drop-art");
    internal NativeRevivalDrops(Player player,Action<string> log)
    {
        this.player=player;this.log=log;
        spawner=AccessTools.Field(typeof(Player),"reviveLootSpawnerHealth").GetValue(player) as LootSpawner;
        if(active!=null)throw new InvalidOperationException("Revival artwork already has an owner.");
        active=this;
        try {
            harmony.Patch(AccessTools.Method(typeof(LootSpawner),"DropItemImplementation"),postfix:new HarmonyMethod(typeof(NativeRevivalDrops),nameof(Spawned)));
            harmony.Patch(AccessTools.Method(typeof(DropItem),"OnDisable"),prefix:new HarmonyMethod(typeof(NativeRevivalDrops),nameof(Retired)));
            harmony.Patch(AccessTools.Method(typeof(DropItem),"PoolBeforeReturnToPool"),prefix:new HarmonyMethod(typeof(NativeRevivalDrops),nameof(Retired)));
        } catch { Dispose();throw; }
    }
    private static void Spawned(LootSpawner __instance,DropItem __result)
    {
        var owner=active;
        if(owner==null || __instance!=owner.spawner || __result==null)return;
        owner.Remove(__result);
        if(owner.drops.Count>=128){owner.log("NS_REVIVAL_ART_BUDGET captureOnly=True");return;}
        var entry=new Drop {Renderers=__result.GetComponentsInChildren<Renderer>(true)};
        owner.drops.Add(__result,entry);
        owner.spawnGeneration++;
        foreach(var renderer in entry.Renderers)owner.artwork.Add(renderer);
        owner.log("NS_REVIVAL_ART_SPAWN nativeDrop="+__result.GetInstanceID()+" renderers="+entry.Renderers.Length+" frame="+Time.frameCount+" nativePickupUnchanged=True");
    }
    private static void Retired(DropItem __instance)=>active?.Remove(__instance);
    private void Remove(DropItem item)
    {
        if(!drops.TryGetValue(item,out var drop))return;
        foreach(var renderer in drop.Renderers)artwork.Remove(renderer);
        drops.Remove(item);
    }
    internal static bool Owns(Player player,Renderer renderer)=>active?.player==player && active.artwork.Contains(renderer);
    internal static void Collect(Player player,List<Renderer> destination)
    {
        var owner=active;if(owner==null || owner.player!=player)return;
        foreach(var pair in owner.drops) {
            if(pair.Key==null || !pair.Key.gameObject.activeInHierarchy)continue;
            foreach(var renderer in pair.Value.Renderers)if(renderer!=null)destination.Add(renderer);
        }
    }
    internal void Clear(){drops.Clear();artwork.Clear();}
    public void Dispose()
    {
        harmony.UnpatchSelf();Clear();if(active==this)active=null;
    }
}
