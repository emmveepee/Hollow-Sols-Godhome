using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace YiGodhome;

// Explicit player-requested variant. Native Hit still decides eligibility and
// native Die/encounter callbacks remain completion authority.
internal sealed class SistersSharedHealth : IDisposable
{
    private static SistersSharedHealth? current;
    private readonly System.Collections.Generic.Dictionary<string, HealthManager> members = new System.Collections.Generic.Dictionary<string, HealthManager>();
    private readonly System.Collections.Generic.List<HealthManager> sisters = new System.Collections.Generic.List<HealthManager>(3);
    private readonly Action<string>? log;
    private readonly Guid encounter;
    private readonly int count;
    private readonly Harmony harmony = new Harmony("local.yigodhome.sisters.shared-health");
    private static readonly System.Reflection.FieldInfo damageOverride = AccessTools.Field(typeof(HealthManager), "damageOverride");
    private static readonly System.Reflection.MethodInfo die = AccessTools.Method(typeof(HealthManager), "Die",
        new[] { typeof(float?), typeof(AttackTypes), typeof(bool) });
    internal int Health { get; private set; }
    internal int Maximum { get; private set; }
    internal bool Ready => Maximum > 0;
    internal bool Contains(HealthManager h) => h != null && members.TryGetValue(h.name, out var member) && member == h;
    internal void Register(HealthManager h)
    {
        if (h.name != "Mantis Lord S1" && h.name != "Mantis Lord S2" && h.name != "Mantis Lord S3") return;
        if (members.TryGetValue(h.name, out var previous) && previous != h) throw new InvalidOperationException("Duplicate live Sisters phase member.");
        if (!members.ContainsKey(h.name)) { members.Add(h.name, h); sisters.Add(h); }
    }
    internal bool Bar(HealthManager h) => !Contains(h) || h.name == "Mantis Lord S1";
    internal SistersSharedHealth(string scene, Guid encounter, Action<string>? log)
    {
        this.encounter = encounter; this.log = log;
        count=scene==YiGodhome.Bridge.EncounterProfile.Mantis.Scene?2:3;
        current = this;
        harmony.Patch(AccessTools.Method(typeof(HealthManager), "SubtractHealth", new[] { typeof(int) }),
            prefix: new HarmonyMethod(typeof(SistersSharedHealth), nameof(Subtract)));
    }
    internal void Update()
    {
        if (!Ready && members.Count == count && sisters.All(h => h != null && h.gameObject.activeInHierarchy && !h.isDead && h.hp > 0)) {
            Health = Maximum = sisters.Sum(h => h.hp);
            foreach (var h in sisters) h.hp = Health;
            log?.Invoke("HK_SISTERS_POOL_BEGIN encounter=" + encounter + " max=" + Maximum + " members="
                + string.Join(",", sisters.Select(h => h.GetInstanceID())) + " nativeIntroPreserved=True frame=" + Time.frameCount);
        }
        if (!Ready || Health > 0) return;
        foreach (var h in sisters) if (h != null && !h.isDead) {
            h.hp = 0;
            die.Invoke(h, new object?[] { null, AttackTypes.Nail, true });
            log?.Invoke("HK_SISTERS_NATIVE_DEATH encounter=" + encounter + " target=" + h.GetInstanceID() + " frame=" + Time.frameCount);
        }
    }
    private static bool Subtract(HealthManager __instance, int __0)
    {
        var pool = current;
        if (pool == null || !pool.Ready || !pool.Contains(__instance)) return true;
        int before = pool.Health;
        int damage = (bool)damageOverride.GetValue(__instance) ? 1 : Math.Max(0, __0);
        pool.Health = Math.Max(0, before - damage);
        foreach (var h in pool.sisters) if (h != null && !h.isDead) h.hp = pool.Health;
        pool.log?.Invoke("HK_SISTERS_POOL_HIT encounter=" + pool.encounter + " target=" + __instance.GetInstanceID()
            + " before=" + before + " after=" + pool.Health + " damage=" + damage + " nativeEligibility=True frame=" + Time.frameCount);
        return false;
    }
    public void Dispose() { harmony.UnpatchSelf(); if (current == this) current = null; }
}
