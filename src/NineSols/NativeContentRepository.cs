using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace YiGodhome;

// One session catalogue; serial donor leases, bounded inactive templates and explicit instances.
internal sealed class NativeContentRepository : IDisposable
{
    internal static NativeContentRepository? Current { get; private set; }
    private readonly Action<string> log;
    private readonly Player player;
    private readonly GameLevel level;
    private readonly Scene destination;
    private readonly GameObject storage;
    private readonly Dictionary<NativeContentKind, GameObject> templates = new Dictionary<NativeContentKind, GameObject>();
    private readonly HashSet<Lease> leases = new HashSet<Lease>();
    private NativeDonorQuarantine? guard;
    private AsyncOperation? operation;
    private Scene donor;
    private int cursor;
    private readonly List<NativeContentDefinition> requests = new List<NativeContentDefinition>();
    private bool unloading, reclaiming, ready = true, disposed;
    private float deadline;
    private float reclaimStarted;
    internal PoolObject? JiequanWarning => templates.TryGetValue(NativeContentKind.JiequanWarning, out var root) ? root.GetComponent<PoolObject>() : null;
    internal GameObject? ClimbArt => templates.TryGetValue(NativeContentKind.ClimbArtwork, out var root) ? root : null;
    internal bool Ready => ready && !disposed;
    internal NativeContentRepository(Player player, Scene destination, Action<string> log, params NativeContentKind[] initial)
    {
        if (Current != null) throw new InvalidOperationException("Duplicate native content repository ownership.");
        this.player = player; this.destination = destination; this.log = log; level = GameCore.Instance.gameLevel;
        storage = new GameObject("YiGodhomeContentRepository"); storage.SetActive(false);
        SceneManager.MoveGameObjectToScene(storage, destination); Current = this;
        // Install the pinned callbacks before gameplay readiness. Repatching and
        // unpatching hundreds of methods during entry stalls native image updates.
        // The guard affects only verified donor scenes; no assets load here.
        try { guard = new NativeDonorQuarantine(log); foreach (var kind in initial) Request(kind); } catch { Dispose(); throw; }
        if (Ready) log("NS_REPOSITORY_IDLE templates=0 donorsLoaded=False discovery=False");
    }
    internal static bool RouteSceneLoaded(Scene scene)
    {
        var owner = Current;
        if (owner == null || owner.guard?.Owns(scene) != true) return false;
        owner.donor = scene;
        foreach (var root in scene.GetRootGameObjects()) root.SetActive(false);
        owner.Validate();
        owner.log("NS_REPOSITORY_DONOR scene=" + scene.name + " blocked=" + owner.guard.Blocked + " levelsBlocked=" + owner.guard.LevelsBlocked);
        return true;
    }
    private void Validate()
    {
        if (GameCore.Instance.player != player || GameCore.Instance.gameLevel != level || !ApplicationCore.IsInBossMemoryMode
            || (int)HarmonyLib.AccessTools.Field(typeof(SaveManager), "currentSlotIndex").GetValue(SaveManager.Instance) != 3)
            throw new InvalidOperationException("Content repository native authority changed.");
    }
    internal void Request(NativeContentKind kind)
    {
        if (disposed) throw new ObjectDisposedException(nameof(NativeContentRepository));
        if (templates.ContainsKey(kind) || requests.Any(r => r.Kind == kind)) return;
        if (requests.Count >= 8) throw new InvalidOperationException("Native content template budget exhausted.");
        bool idle = ready;
        requests.Add(NativeContentCatalog.Definitions.Single(d => d.Kind == kind));
        ready = false;
        log("NS_REPOSITORY_REQUEST kind=" + kind + " queued=" + requests.Count);
        if (idle) Begin();
    }
    private void Begin()
    {
        if (!NativeDonorManifest.SceneIndices.Contains(requests[cursor].SceneIndex))
            throw new InvalidOperationException("Native content donor lacks a verified callback manifest.");
        if (guard == null) throw new InvalidOperationException("Native donor guard lifetime lost.");
        deadline = Time.realtimeSinceStartup + 40; unloading = false; reclaiming = false;
        operation = SceneManager.LoadSceneAsync(requests[cursor].SceneIndex, LoadSceneMode.Additive)
            ?? throw new InvalidOperationException("Donor load unavailable.");
        guard!.TrackLoad(operation);
    }
    internal void Update()
    {
        if (disposed) throw new ObjectDisposedException(nameof(NativeContentRepository));
        Validate(); if (Ready) return;
        if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Native content repository acquisition timed out.");
        if (operation?.isDone != true) return;
        if (reclaiming) {
            if (cursor < requests.Count) { Begin(); return; }
            foreach (var entry in templates) if (entry.Value == null) throw new InvalidOperationException("Template lost during unused asset release.");
            ready = true;
            log("NS_REPOSITORY_READY templates=" + templates.Count + " donorsLoaded=False player=" + player.GetInstanceID()
                + " nativeLevel=" + level.GetInstanceID() + " canvas=" + destination.name + " discovery=False assetReleaseMs="
                + ((Time.realtimeSinceStartup - reclaimStarted) * 1000));
            return;
        }
        var definition = requests[cursor];
        if (!unloading) {
            if (!donor.isLoaded || guard!.LevelsBlocked != cursor + 1) throw new InvalidOperationException("Native donor level quarantine failed.");
            var template = definition.Extract(donor, storage.transform);
            if (template.activeInHierarchy || template.scene != destination) throw new InvalidOperationException("Content template owner/activation contract failed.");
            NativeContentReferences.Validate(template, donor);
            templates.Add(definition.Kind, template);
            log("NS_REPOSITORY_TEMPLATE kind=" + definition.Kind + " owner=" + template.GetInstanceID() + " inactive=True");
            unloading = true; deadline = Time.realtimeSinceStartup + 40;
            operation = SceneManager.UnloadSceneAsync(donor) ?? throw new InvalidOperationException("Donor unload unavailable.");
        } else {
            if (donor.isLoaded || templates[definition.Kind] == null) throw new InvalidOperationException("Retained content lost across donor unload.");
            cursor++;
            if (cursor < requests.Count) { Begin(); return; }
            reclaiming = true; reclaimStarted = Time.realtimeSinceStartup; deadline = reclaimStarted + 40;
            operation = Resources.UnloadUnusedAssets();
        }
    }
    // Pooled warning instances are initialized/reset by the native PoolObject contract.
    // Visual instances are independent clones: disposal never returns unverified state to a pool.
    internal Lease Borrow(NativeContentKind kind)
    {
        if (!Ready || leases.Count >= 8) throw new InvalidOperationException("Native content is not ready or its lease budget is full.");
        var template = templates[kind];
        var pool = template.GetComponent<PoolObject>();
        GameObject root = pool != null ? pool.BorrowOrInstantiate().gameObject : UnityEngine.Object.Instantiate(template);
        try {
            if (root.transform.parent != null) root.transform.SetParent(null, true);
            if (root.scene != destination) SceneManager.MoveGameObjectToScene(root, destination);
            var lease = new Lease(this, kind, root, pool != null);
            leases.Add(lease);
            log("NS_REPOSITORY_LEASE kind=" + kind + " owner=" + root.GetInstanceID() + " retained=" + leases.Count + " pool=" + (pool != null));
            return lease;
        } catch {
            if (pool != null) root.GetComponent<PoolObject>().ReturnToPool();
            else UnityEngine.Object.Destroy(root);
            throw;
        }
    }
    internal sealed class Lease : IDisposable
    {
        private NativeContentRepository? owner;
        internal GameObject Root { get; }
        private readonly NativeContentKind kind;
        private readonly bool pooled;
        internal Lease(NativeContentRepository owner, NativeContentKind kind, GameObject root, bool pooled)
        { this.owner = owner; this.kind = kind; Root = root; this.pooled = pooled; }
        public void Dispose()
        {
            var context = owner; if (context == null) return; owner = null;
            if (Root != null) {
                if (pooled) Root.GetComponent<PoolObject>().ReturnToPool();
                else { Root.SetActive(false); UnityEngine.Object.Destroy(Root); }
            }
            context.leases.Remove(this);
            context.log("NS_REPOSITORY_RETIRE kind=" + kind + " retained=" + context.leases.Count);
        }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        foreach (var lease in new List<Lease>(leases)) lease.Dispose();
        if (donor.isLoaded) SceneManager.UnloadSceneAsync(donor);
        guard?.Dispose(); UnityEngine.Object.Destroy(storage);
        if (Current == this) Current = null;
        log("NS_REPOSITORY_DISPOSED templates=" + templates.Count + " leases=" + leases.Count);
    }
}
