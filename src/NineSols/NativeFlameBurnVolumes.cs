using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YiGodhome.Bridge;
using YiGodhome.Geometry;

namespace YiGodhome;

// Only collision is translated. Native BuffPlayer applies/extends the authored
// small-fire buff; native UpdateRunner and PlayerHealth own its DOT and expiry.
internal sealed class NativeFlameBurnVolumes : IDisposable
{
    private sealed class Entry {
        internal GameObject Root = null!;
        internal PolygonCollider2D Shape = null!;
        internal long Activation;
        internal Vector2[] Points = Array.Empty<Vector2>();
    }
    private readonly Dictionary<int, Entry> entries = new();
    private readonly GeometryLandingProbe geometry;
    private readonly MonsterBase owner;
    private readonly Action<string> log;
    private NativeContentRepository.Lease? template;
    private static NativeFlameBurnVolumes? current;
    private readonly HarmonyLib.Harmony harmony = new("local.yigodhome.native.burn");
    private readonly HashSet<RCGMaker.Core.BuffModule> modules = new();
    private readonly Dictionary<RCGMaker.Core.BuffModule, float> originalDotValues = new();
    private int tier;
    private float nextApplyLog;
    private bool initialized;
    internal NativeFlameBurnVolumes(MonsterBase owner, GeometryLandingProbe geometry, Action<string> log)
    {
        this.owner = owner; this.geometry = geometry; this.log = log;
    }
    private void Initialize()
    {
        if(initialized) return;
        if(current!=null) throw new InvalidOperationException("Duplicate native burn ownership.");
        current=this;
        try {
            harmony.Patch(HarmonyLib.AccessTools.Method(typeof(RCGMaker.Core.BuffPlayer), "InstantiateToApplyOrExtendBuff"), postfix:new HarmonyLib.HarmonyMethod(typeof(NativeFlameBurnVolumes),nameof(Applied)));
            harmony.Patch(HarmonyLib.AccessTools.Method(typeof(PlayerHealth), "<Awake>b__18_0"), prefix:new HarmonyLib.HarmonyMethod(typeof(NativeFlameBurnVolumes),nameof(DotBefore)), postfix:new HarmonyLib.HarmonyMethod(typeof(NativeFlameBurnVolumes),nameof(DotAfter)));
            harmony.Patch(HarmonyLib.AccessTools.Method(typeof(RCGMaker.Core.BuffModule), "OnStop"), prefix:new HarmonyLib.HarmonyMethod(typeof(NativeFlameBurnVolumes),nameof(Stopping)));
            initialized=true;
        } catch { harmony.UnpatchSelf(); current=null; throw; }
    }
    private static void Applied(RCGMaker.Core.BuffModule __0, RCGMaker.Core.BuffModule __result)
    {
        var c=current;
        if(c?.template==null || __0!=c.template.Root.GetComponent<RCGMaker.Core.BuffDealer>().buffModulePrefab || __result==null) return;
        bool added=c.modules.Add(__result);
        var dot=__result.GetComponent<EffectDealer>();
        if(added) c.originalDotValues.Add(__result,dot.value);
        float tick=18f*(c.tier>0?2:1);
        dot.value += tick-dot.FinalValue;
        if(added || Time.time>=c.nextApplyLog){c.nextApplyLog=Time.time+1;c.log("NS_BURN_NATIVE_APPLIED module="+__result.GetInstanceID()+" new="+added+" tick="+dot.FinalValue+" masks=2 interval=.25 duration=.75 frame="+Time.frameCount);}
    }
    private static void Stopping(RCGMaker.Core.BuffModule __instance)
    {
        var c=current;
        if(c==null || !c.originalDotValues.TryGetValue(__instance,out float value)) return;
        __instance.GetComponent<EffectDealer>().value=value;
        c.originalDotValues.Remove(__instance); c.modules.Remove(__instance);
        c.log("NS_BURN_NATIVE_EXPIRED module="+__instance.GetInstanceID()+" damageRestored=True nativeTime="+Time.time+" frame="+Time.frameCount);
    }
    private static void DotBefore(PlayerHealth __instance, EffectHitData __0, out float __state)
    {
        __state=float.NaN;
        var c=current; var buff=__0.dealer.GetComponent<RCGMaker.Core.BuffModule>();
        if(c!=null && buff!=null && c.modules.Contains(buff) && __instance==Player.i.health) __state=__instance.currentValue;
    }
    private static void DotAfter(PlayerHealth __instance, EffectHitData __0, float __state)
    {
        if(float.IsNaN(__state)) return;
        current?.log("NS_BURN_NATIVE_DOT amount="+__0.dealer.FinalValue+" healthBefore="+__state+" health="+__instance.currentValue+" nativeTime="+Time.time+" frame="+Time.frameCount);
    }
    internal void Update(ContactFrame? frame)
    {
        if(frame!=null) tier=frame.Difficulty;
        foreach(var module in modules.Where(m=>m==null || !m.gameObject.activeInHierarchy).ToArray()) {
            log("NS_BURN_NATIVE_EXPIRED module="+(module==null?0:module.GetInstanceID())+" nativeTime="+Time.time+" frame="+Time.frameCount);
            if(module!=null) Stopping(module);
            modules.Remove(module!);
        }
        foreach (var pair in entries) if (frame == null || Player.i.health.IsDead || !frame.Volumes.Any(v=>v.Id==pair.Key && v.Active && v.Category==AttackCategory.Burn)) pair.Value.Root.SetActive(false);
        if (frame == null || Player.i.health.IsDead) return;
        foreach (var v in frame.Volumes) {
            if (v.Category != AttackCategory.Burn || !v.Active) continue;
            Initialize();
            if (EncounterProfile.ForScene(geometry.SourceScene)?.Family != EncounterProfile.NightmareGrimm)
                throw new InvalidOperationException("Burn collision outside its verified encounter.");
            if (!entries.TryGetValue(v.Id, out var e)) {
                if (entries.Count >= AttackVolumes.MaxCount) throw new InvalidOperationException("Native burn collider budget exceeded.");
                template ??= (NativeContentRepository.Current ?? throw new InvalidOperationException("Native burn repository missing.")).Borrow(NativeContentKind.FlameBurn);
                template.Root.SetActive(false);
                var root = UnityEngine.Object.Instantiate(template.Root, owner.transform, false);
                root.SetActive(false);
                try {
                    foreach(var originalShape in root.GetComponents<Collider2D>()) originalShape.enabled = false;
                    var shape = root.AddComponent<PolygonCollider2D>(); shape.isTrigger = true;
                    AutoAttributeManager.AutoReferenceAllChildren(root);
                    if (root.GetComponent<RCGMaker.Core.BuffDealer>().buffModulePrefab == null || root.GetComponent<EffectDealer>().type != EffectType.Debuff)
                        throw new InvalidOperationException("Native flame buff binding changed.");
                    e = new Entry { Root = root, Shape = shape }; entries.Add(v.Id,e);
                    log("NS_BURN_COLLIDER_BOUND id="+v.Id+" nativeBuff=Burn_DOT_small owner="+root.GetInstanceID());
                } catch { UnityEngine.Object.Destroy(root); throw; }
            }
            int count = v.Points.Length / 2;
            if(e.Points.Length != count) e.Points = new Vector2[count];
            Vector2 center = Vector2.zero;
            for(int i=0;i<count;i++){var p=geometry.Map.ToTarget(new Point(v.Points[i*2],v.Points[i*2+1]));e.Points[i]=new Vector2(p.X,p.Y);center+=e.Points[i];}
            center /= count; e.Root.transform.position = center;
            var matrix=e.Root.transform.worldToLocalMatrix;
            for(int i=0;i<count;i++) e.Points[i]=matrix.MultiplyPoint3x4(e.Points[i]);
            e.Shape.SetPath(0,e.Points); e.Root.SetActive(true);
            if(e.Activation!=v.Activation){e.Activation=v.Activation;log("NS_BURN_WINDOW id="+v.Id+" activation="+v.Activation+" directDamage=False nativeBuff=True frame="+Time.frameCount);}
        }
    }
    public void Dispose(){foreach(var e in entries.Values){e.Root.SetActive(false);UnityEngine.Object.Destroy(e.Root);}entries.Clear();foreach(var module in modules.ToArray()) if(module!=null && module.gameObject.activeInHierarchy) module.SelfRemove();log("NS_BURN_NATIVE_RELEASED colliders=0 modules="+modules.Count+" frame="+Time.frameCount);modules.Clear();if(initialized)harmony.UnpatchSelf();if(current==this)current=null;template?.Dispose();template=null;}
}
