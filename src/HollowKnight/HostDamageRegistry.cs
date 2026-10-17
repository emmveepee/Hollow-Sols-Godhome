using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace YiGodhome;

// Track native pool activations without a scene-wide scan on every frame.
internal sealed class HostDamageRegistry : IDisposable
{
    private static readonly HashSet<HostDamageRegistry> registries = new HashSet<HostDamageRegistry>();
    private static readonly Harmony harmony = new Harmony("local.yigodhome.damage-registry");
    private static bool spawnPatched;
    internal static event Action<GameObject>? PoolSpawned;
    private readonly HashSet<DamageHero> sources = new HashSet<DamageHero>();
    private DamageHero[] snapshot = Array.Empty<DamageHero>();
    private bool snapshotDirty;
    private readonly List<DamageHero> destroyed = new List<DamageHero>();
    private readonly Dictionary<DamageHero, long> activations = new Dictionary<DamageHero, long>();
    private readonly string scene;
    private readonly bool nativePools;
    private readonly Func<DamageHero, bool>? includeGlobal;
    private readonly Action<DamageHero>? registered;
    public HostDamageRegistry(string scene, Func<DamageHero, bool>? includeGlobal = null, Action<DamageHero>? registered = null, bool nativePools = false)
    {
        this.nativePools = nativePools; this.scene = scene; this.includeGlobal = includeGlobal; this.registered = registered;
        try {
            if (registries.Count == 0)
                harmony.Patch(AccessTools.Method(typeof(DamageHero), "OnEnable"), postfix: new HarmonyMethod(typeof(HostDamageRegistry), nameof(Enabled)));
            if (nativePools && !spawnPatched) {
                // Spawn adds its source mapping after SetActive/OnEnable.
                harmony.Patch(AccessTools.Method(typeof(ObjectPool), "Spawn", new[] { typeof(GameObject), typeof(Transform), typeof(Vector3), typeof(Quaternion) }),
                    postfix: new HarmonyMethod(typeof(HostDamageRegistry), nameof(Spawned)));
                spawnPatched = true;
            }
            registries.Add(this);
            foreach (var source in Resources.FindObjectsOfTypeAll<DamageHero>()) Register(source);
        } catch { Dispose(); throw; }
    }
    private static void Enabled(DamageHero __instance) { foreach (var registry in registries) registry.Register(__instance); }
    private static void Spawned(GameObject __result)
    {
        if (__result == null) return;
        foreach (var registry in registries) if (registry.nativePools)
            foreach (var source in __result.GetComponentsInChildren<DamageHero>(true))
                if (source.gameObject.scene.name == "DontDestroyOnLoad") registry.Register(source);
        PoolSpawned?.Invoke(__result);
    }
    internal static void NativeCreated(GameObject body)
    {
        foreach (var registry in registries) if (registry.nativePools)
            foreach (var source in body.GetComponentsInChildren<DamageHero>(true)) registry.Register(source);
    }
    private void Register(DamageHero source)
    {
        if (source == null) return;
        if (source.gameObject.scene.name != scene && !(source.gameObject.scene.name == "DontDestroyOnLoad"
            && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == scene && includeGlobal?.Invoke(source) == true)) return;
        if (sources.Add(source)) snapshotDirty = true;
        activations.TryGetValue(source, out var count); activations[source] = count + 1;
        registered?.Invoke(source);
    }
    public long Activation(DamageHero source) => activations.TryGetValue(source, out var count) ? count : 0;
    public void PruneDestroyed()
    {
        destroyed.Clear();
        foreach (var source in sources) if (source == null) destroyed.Add(source!);
        foreach (var dead in destroyed) { sources.Remove(dead); activations.Remove(dead); snapshotDirty = true; }
    }
    public DamageHero[] Snapshot()
    { PruneDestroyed(); if (snapshotDirty) { snapshot = sources.ToArray(); snapshotDirty = false; } return snapshot; }
    public void Dispose() { registries.Remove(this); if (registries.Count == 0) { harmony.UnpatchSelf(); spawnPatched = false; } sources.Clear(); activations.Clear(); }
}
