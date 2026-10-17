using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YiGodhome.Bridge;

namespace YiGodhome;

internal sealed class HostSummons : IDisposable
{
    private sealed class Entry { public DamageHero Damage = null!; public HealthManager Health = null!; public long Generation; public int MaxHealth; }
    private readonly HostDamageRegistry registry;
    private readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>();
    private readonly Func<DamageHero, bool> classify;
    private readonly Action<string>? log;
    internal void CopyPresentationBodies(List<HealthManager> result) {
        foreach (var entry in entries.Values) {
            if (result.Count == 32) break;
            if (entry.Health != null) result.Add(entry.Health);
        }
    }
    public HostSummons(string scene, Func<DamageHero, bool> classify, Action<string>? log)
    { this.classify = classify; this.log = log; registry = new HostDamageRegistry(scene, classify); }
    public bool Knows(HitOutcome hit) => hit.Kind <= HitKind.Talisman && entries.TryGetValue(hit.Entity, out var e) && hit.TargetActivation > 0 && hit.TargetActivation <= e.Generation;
    public HealthManager? Resolve(HitOutcome hit)
    {
        if (!entries.TryGetValue(hit.Entity, out var e) || e.Health == null || e.Damage == null || e.Health.isDead
            || !e.Health.gameObject.activeInHierarchy || registry.Activation(e.Damage) != hit.TargetActivation) return null;
        return e.Health;
    }
    public SummonedTarget[] Capture(bool bossAlive)
    {
        var result = new List<SummonedTarget>();
        foreach (var damage in registry.Snapshot().Where(classify))
        {
            var health = damage.GetComponent<HealthManager>(); if (health == null) continue;
            int id = health.GetInstanceID(); long generation = registry.Activation(damage);
            if (!entries.TryGetValue(id, out var e)) { entries.Add(id, e = new Entry { Damage = damage, Health = health }); log?.Invoke("HK_SUMMON_SHAPE entity=" + id + " shapes=" + string.Join(",", health.GetComponents<Collider2D>().Select(c => c.GetType().Name))); }
            if (e.Generation != generation) { e.Generation = generation; e.MaxHealth = Math.Max(1, health.hp); log?.Invoke("HK_SUMMON_GENERATION entity=" + id + " generation=" + generation + " hp=" + health.hp); }
            if (!bossAlive || !damage.isActiveAndEnabled || health.isDead || health.hp <= 0) continue;
            var collider = health.GetComponent<BoxCollider2D>();
            if (collider == null || collider.edgeRadius != 0 || !collider.enabled) continue;
            var center = collider.transform.TransformPoint(collider.offset); var scale = collider.transform.lossyScale;
            if (Math.Abs(Mathf.DeltaAngle(collider.transform.eulerAngles.z, 0)) > 0.01f) throw new InvalidOperationException("Rotated summon requires polygon projection.");
            result.Add(new SummonedTarget { Entity = id, Generation = generation, Collider = collider.GetInstanceID(), Health = health.hp, MaxHealth = e.MaxHealth,
                Damage = damage.damageDealt, Active = true, X = center.x, Y = center.y, Width = Math.Abs(collider.size.x * scale.x), Height = Math.Abs(collider.size.y * scale.y) });
        }
        var snapshot = result.ToArray(); SummonedTargets.Validate(snapshot); return snapshot;
    }
    public void Dispose() { registry.Dispose(); entries.Clear(); }
}
