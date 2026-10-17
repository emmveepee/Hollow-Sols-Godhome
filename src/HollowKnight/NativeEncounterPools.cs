using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace YiGodhome;

// Admission follows native scene-owned PersonalObjectPool prefab references.
// Names and globally resident prefabs do not establish encounter ownership.
internal sealed class NativeEncounterPools : IDisposable
{
    private readonly HashSet<GameObject> prefabs = new HashSet<GameObject>();
    private readonly HashSet<HutongGames.PlayMaker.Actions.CreateObject> oomaSpawns = new();
    private readonly HashSet<GameObject> nativeInstances = new();
    private readonly HashSet<GetNextPreSpawnedGameObject> summonSpawns = new();
    private readonly HashSet<EnemySpawner> enemySpawners=new();
    private readonly HashSet<GameObject> summonSpawnerRoots=new();
    private readonly HashSet<GameObject> directOomaInstances=new();
    private int coreCreationSamples;
    private static readonly System.Reflection.FieldInfo SpawnedEnemy=AccessTools.Field(typeof(EnemySpawner),"spawnedEnemy");
    private static readonly System.Reflection.FieldInfo OomaCorpse=AccessTools.Field(typeof(EnemyDeathEffects),"corpse");
    private static readonly System.Reflection.FieldInfo OomaCorpsePrefab=AccessTools.Field(typeof(EnemyDeathEffects),"corpsePrefab");
    private readonly Harmony creation = new Harmony("local.yigodhome.ooma-native-creation");
    private static NativeEncounterPools? current;
    private readonly Action<string>? log;
    private static readonly System.Reflection.FieldInfo Startup = AccessTools.Field(typeof(PersonalObjectPool), "startupPool");
    private static readonly System.Reflection.FieldInfo Spawned = AccessTools.Field(typeof(ObjectPool), "spawnedObjects");
    public int PrefabCount => prefabs.Count;
    internal bool OwnsPrefab(GameObject prefab) => prefabs.Contains(prefab);
    public NativeEncounterPools(string scene, Action<string>? log)
    {
        this.log = log;
        var profile=Bridge.EncounterProfile.ForScene(scene);
        if(profile != null) foreach(var boss in Resources.FindObjectsOfTypeAll<HealthManager>()) {
            if(boss.gameObject.scene.name!=scene || !profile.IsBossName(boss.name)) continue;
            foreach(var fsm in boss.GetComponents<PlayMakerFSM>()) foreach(var state in fsm.FsmStates) foreach(var action in state.Actions)
                if(action is GetNextPreSpawnedGameObject spawn) {
                    if(summonSpawns.Count>=32) throw new InvalidOperationException("Native summon array scope exceeded.");
                    summonSpawns.Add(spawn); PinSummonArray(spawn);
                }
        }
        var pending = new Queue<GameObject>();
        void Add(PersonalObjectPool pool) {
            var startup = Startup.GetValue(pool) as IEnumerable;
            if (startup == null) return;
            foreach (var entry in startup) {
                var prefab = (GameObject)AccessTools.Field(entry.GetType(), "prefab").GetValue(entry);
                if (prefab == null || !prefabs.Add(prefab)) continue;
                if (prefabs.Count > 256) throw new InvalidOperationException("Native encounter prefab ownership budget exceeded.");
                pending.Enqueue(prefab);
            }
        }
        foreach (var pool in Resources.FindObjectsOfTypeAll<PersonalObjectPool>())
            if (pool.gameObject.scene.name == scene) Add(pool);
        // Minions such as Collector's Spitter own their projectile pool. Traverse
        // only already admitted prefab graphs; never scan unrelated global pools.
        while (pending.Count > 0)
            foreach (var pool in pending.Dequeue().GetComponentsInChildren<PersonalObjectPool>(true)) Add(pool);
        if (Bridge.EncounterProfile.ForScene(scene)?.Key == "Uumuu") {
            AddOomaClosure(log);
            // The authored spawner mixes direct CreateObject with pooled spawn.
            // Pin exact prefab references; clone names do not confer ownership.
            var spawner = System.Linq.Enumerable.Single(Resources.FindObjectsOfTypeAll<PlayMakerFSM>(), f => f.gameObject.scene.name == scene && f.name == "Jellyfish Spawner" && f.FsmName == "Spawn");
            foreach (var state in spawner.FsmStates)
                foreach (var action in state.Actions)
                    if (action is HutongGames.PlayMaker.Actions.CreateObject create && create.gameObject.Value != null && prefabs.Contains(create.gameObject.Value) && create.gameObject.Value.name == "Jellyfish GG") oomaSpawns.Add(create);
            if (oomaSpawns.Count == 0) throw new InvalidOperationException("Native Ooma direct spawn manifest missing.");
            current = this;
            try {
                creation.Patch(AccessTools.Method(typeof(HutongGames.PlayMaker.Actions.CreateObject), "OnEnter"), postfix: new HarmonyMethod(typeof(NativeEncounterPools), nameof(Created)));
                creation.Patch(AccessTools.Method(typeof(EnemyDeathEffects), "EmitCorpse"), prefix: new HarmonyMethod(typeof(NativeEncounterPools), nameof(CorpseEmitting)));
            }
            catch { Dispose(); throw; }
        }
        if(summonSpawns.Count>0 && current==null) {
            current=this;
            try { creation.Patch(AccessTools.Method(typeof(GetNextPreSpawnedGameObject),"OnEnter"),postfix:new HarmonyMethod(typeof(NativeEncounterPools),nameof(Summoned))); }
            catch { Dispose(); throw; }
        } else if(summonSpawns.Count>0) creation.Patch(AccessTools.Method(typeof(GetNextPreSpawnedGameObject),"OnEnter"),postfix:new HarmonyMethod(typeof(NativeEncounterPools),nameof(Summoned)));
        if (Bridge.SessionConfiguration.Current.RunFixtures)
            foreach (var prefab in prefabs) log?.Invoke("HK_ROSTER_POOL_PREFAB scene=" + scene + " name=" + prefab.name
                + " scripts=" + string.Join(",", Array.ConvertAll(prefab.GetComponents<MonoBehaviour>(), component => component == null ? "missing" : component.GetType().Name)));
        if (Bridge.SessionConfiguration.Current.RunFixtures) foreach (var prefab in prefabs) {
            if (prefab.name != "Mage Orb" && prefab.name != "Mage Orb Over" && prefab.name != "Grimm_flare_pillar" && prefab.name != "Nightmare Firebat" && prefab.name != "Grimm Firebat") continue;
            foreach (var shape in prefab.GetComponentsInChildren<Collider2D>(true)) log?.Invoke("HK_PINNED_PROJECTILE_SHAPE root=" + prefab.name + " name=" + shape.name + " parent=" + shape.transform.parent?.name + " scripts=" + string.Join(",", Array.ConvertAll(shape.GetComponents<MonoBehaviour>(), c => c == null ? "missing" : c.GetType().Name)));
            foreach (var fsm in prefab.GetComponentsInChildren<PlayMakerFSM>(true)) foreach (var state in fsm.FsmStates) {
                log?.Invoke("HK_PINNED_PROJECTILE_GRAPH root=" + prefab.name + " owner=" + fsm.name + " fsm=" + fsm.FsmName + " state=" + state.Name + " transitions=" + string.Join(",",Array.ConvertAll(state.Transitions,t=>t.EventName+"->"+t.ToState)));
                foreach (var a in state.Actions) log?.Invoke("HK_PINNED_PROJECTILE_ACTION root=" + prefab.name + " state=" + state.Name + " type=" + a.GetType().Name);
            }
        }
    }
    private void PinSummonArray(GetNextPreSpawnedGameObject spawn) {
        int inspected=0;
        foreach(var value in spawn.storedArray.Values) if(value is GameObject root && root != null) {
            var receivers=root.GetComponentsInChildren<HealthManager>(true);
            if(Bridge.SessionConfiguration.Current.RunFixtures && inspected++<2) log?.Invoke("HK_NATIVE_SUMMON_ROOT name="+root.name+" receivers="+receivers.Length+" scripts="+string.Join(",",Array.ConvertAll(root.GetComponentsInChildren<MonoBehaviour>(true),c=>c==null?"missing":c.GetType().Name)));
            if(receivers.Length==0) {
                var spawner=root.GetComponent<EnemySpawner>();
                if(spawner==null) continue; // Native decoration is not a target.
                if(!summonSpawnerRoots.Contains(root) && summonSpawnerRoots.Count>=256) throw new InvalidOperationException("Native summon spawner budget exceeded.");
                summonSpawnerRoots.Add(root);
                if(enemySpawners.Add(spawner)) spawner.OnEnemySpawned+=SpawnedSummon;
                var body=SpawnedEnemy.GetValue(spawner) as GameObject;
                if(body!=null) PinReceiver(body);
                continue;
            }
            if(receivers.Length>4) throw new InvalidOperationException("Native summon receiver closure exceeded.");
            if(!nativeInstances.Contains(root) && nativeInstances.Count>=256) throw new InvalidOperationException("Native pre-spawned summon ownership budget exceeded.");
            nativeInstances.Add(root);
        }
        log?.Invoke("HK_NATIVE_SUMMON_ARRAY owner="+spawn.Fsm.GameObject.name.Replace(' ','_')+" state="+spawn.State.Name.Replace(' ','_')+" owned="+nativeInstances.Count+" authoredReferences=True");
    }
    private void PinReceiver(GameObject body) {
        int count=body.GetComponentsInChildren<HealthManager>(true).Length;
        if(count<1 || count>4) throw new InvalidOperationException("Native EnemySpawner receiver closure changed.");
        if(!nativeInstances.Contains(body) && nativeInstances.Count>=256) throw new InvalidOperationException("Native summoned receiver ownership budget exceeded.");
        nativeInstances.Add(body);
    }
    private void SpawnedSummon(GameObject body) {
        if(body==null || !nativeInstances.Contains(body)) throw new InvalidOperationException("Unpinned native summon event.");
        HostDamageRegistry.NativeCreated(body);
        log?.Invoke("HK_NATIVE_SUMMON_RECEIVER entity="+body.GetComponentInChildren<HealthManager>(true).GetInstanceID()+" nativeEnemySpawner=True frame="+Time.frameCount);
    }
    private static void Summoned(GetNextPreSpawnedGameObject __instance) {
        var scope=current;
        if(scope==null || !scope.summonSpawns.Contains(__instance)) return;
        var root=__instance.storeObject.Value;
        if(root==null || !scope.nativeInstances.Contains(root) && !scope.summonSpawnerRoots.Contains(root)) { scope.PinSummonArray(__instance); root=__instance.storeObject.Value; }
        if(root!=null && scope.nativeInstances.Contains(root)) HostDamageRegistry.NativeCreated(root);
    }
    private static void Created(HutongGames.PlayMaker.Actions.CreateObject __instance) {
        var scope = current;
        if (scope == null) return;
        var prefab = __instance.gameObject.Value;
        if (prefab != null && prefab.name == "Lil Jellyfish" && scope.prefabs.Contains(prefab)
            && Bridge.SessionConfiguration.Current.RunFixtures && scope.coreCreationSamples++ < 16)
            scope.log?.Invoke("HK_OOMA_CORE_CREATE_SOURCE owner=" + __instance.Fsm.GameObject.name.Replace(' ','_')
                + " ownerId=" + __instance.Fsm.GameObject.GetInstanceID() + " scene=" + __instance.Fsm.GameObject.scene.name
                + " fsm=" + __instance.Fsm.Name.Replace(' ','_') + " state=" + __instance.State.Name.Replace(' ','_')
                + " core=" + (__instance.storeObject.Value?.GetInstanceID() ?? 0) + " sourceOwned=" + scope.Owns(__instance.Fsm.GameObject) + " pinnedPrefab=True frame=" + Time.frameCount);
        bool core = prefab != null && prefab.name == "Lil Jellyfish" && scope.prefabs.Contains(prefab)
            && __instance.Fsm.Name == "corpse" && __instance.State.Name == "Explode"
            && __instance.Fsm.GameObject.name == "Corpse Jellyfish(Clone)"
            && (__instance.Fsm.GameObject.scene.name == Bridge.EncounterProfile.Selected.Scene
                || __instance.Fsm.GameObject.scene.name == "DontDestroyOnLoad"
                    && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == Bridge.EncounterProfile.Selected.Scene
                    && scope.Owns(__instance.Fsm.GameObject));
        if (!core && !scope.oomaSpawns.Contains(__instance)) return;
        var body = __instance.storeObject.Value;
        if (body == null || body.GetComponent<HealthManager>() == null) throw new InvalidOperationException("Native Ooma spawn receiver missing.");
        scope.nativeInstances.RemoveWhere(o => o == null);
        scope.directOomaInstances.RemoveWhere(o=>o==null);
        if (!scope.directOomaInstances.Contains(body) && scope.directOomaInstances.Count >= 64) throw new InvalidOperationException("Native Ooma instance ownership budget exceeded.");
        scope.directOomaInstances.Add(body);
        scope.nativeInstances.Add(body);
        if (core) NativeShockwaveStrikes.OomaCreated(body);
        HostDamageRegistry.NativeCreated(body);
        scope.log?.Invoke("HK_OOMA_DIRECT_SPAWN entity=" + body.GetComponent<HealthManager>().GetInstanceID() + " prefab=" + __instance.gameObject.Value.name + " nativeCreate=True frame=" + Time.frameCount);
    }
    private static void CorpseEmitting(EnemyDeathEffects __instance) {
        var scope=current;
        if(scope==null || Bridge.EncounterProfile.Selected.Family!=Bridge.EncounterProfile.Uumuu || __instance==null
            || !__instance.name.StartsWith("Jellyfish GG",StringComparison.Ordinal) || !scope.Owns(__instance.gameObject)) return;
        var prefab=OomaCorpsePrefab.GetValue(__instance) as GameObject;
        if(prefab==null || prefab.name!="Corpse Jellyfish" || !scope.prefabs.Contains(prefab)) return;
        var corpse=OomaCorpse.GetValue(__instance) as GameObject;
        if(corpse==null) return; // Native recyclable branch retains ObjectPool identity.
        scope.nativeInstances.RemoveWhere(o=>o==null);
        if(!scope.nativeInstances.Contains(corpse) && scope.nativeInstances.Count>=256) throw new InvalidOperationException("Native Ooma corpse ownership budget exceeded.");
        scope.nativeInstances.Add(corpse);
        if(Bridge.SessionConfiguration.Current.RunFixtures) scope.log?.Invoke("HK_OOMA_CORPSE_OWNER receiver="+__instance.gameObject.GetInstanceID()
            +" corpse="+corpse.GetInstanceID()+" prefab="+prefab.GetInstanceID()+" scene="+corpse.scene.name+" nativeField=True beforeDetach=True frame="+Time.frameCount);
    }
    private void AddOomaClosure(Action<string>? log) {
        var ooma = System.Linq.Enumerable.Single(prefabs, p => p.name == "Jellyfish GG");
        var effects = ooma.GetComponent<EnemyDeathEffects>();
        var corpse = (GameObject)AccessTools.Field(typeof(EnemyDeathEffects), "corpsePrefab").GetValue(effects);
        if (corpse == null || corpse.name != "Corpse Jellyfish") throw new InvalidOperationException("Native Ooma corpse contract changed.");
        var pending = new Queue<GameObject>(); pending.Enqueue(corpse);
        var visited = new HashSet<GameObject>();
        while (pending.Count > 0) {
            var prefab = pending.Dequeue();
            if (!visited.Add(prefab)) continue;
            if (visited.Count > 3) throw new InvalidOperationException("Native Ooma gameplay closure exceeded.");
            prefabs.Add(prefab);
            foreach (var fsm in prefab.GetComponents<PlayMakerFSM>())
                foreach (var state in fsm.FsmStates) foreach (var action in state.Actions) {
                    // Pinned exact gameplay dependencies, not arbitrary graph loading.
                    if (action.GetType().Name != "CreateObject" && action.GetType().Name != "SpawnObjectFromGlobalPool") continue;
                    var field = AccessTools.Field(action.GetType(), "gameObject");
                    var reference = field?.GetValue(action) as HutongGames.PlayMaker.FsmGameObject;
                    if (reference?.UseVariable != false || reference.Value == null) continue;
                    if (reference.Value.name == "Lil Jellyfish" || reference.Value.name == "Gas Explosion Uumuu") pending.Enqueue(reference.Value);
                }
            log?.Invoke("HK_OOMA_OWNERSHIP prefab=" + prefab.name + " nativeChain=True callbacksUnchanged=True");
        }
        if (!System.Linq.Enumerable.Any(visited, p => p.name == "Lil Jellyfish") || !System.Linq.Enumerable.Any(visited, p => p.name == "Gas Explosion Uumuu"))
            throw new InvalidOperationException("Native Ooma core/explosion closure incomplete.");
    }
    public bool Owns(GameObject body)
    {
        for(var node=body.transform;node!=null;node=node.parent) if(nativeInstances.Contains(node.gameObject)) return true;
        if (prefabs.Count == 0) return false;
        var pool = ObjectPool.instance;
        if (pool == null) return false;
        var spawned = (IDictionary)Spawned.GetValue(pool);
        for (var node = body.transform; node != null; node = node.parent)
            if (nativeInstances.Contains(node.gameObject) || spawned.Contains(node.gameObject) && prefabs.Contains((GameObject)spawned[node.gameObject])) return true;
        return false;
    }
    internal GameObject[] OwnedInstances()
    {
        var result = new List<GameObject>();
        foreach(var body in nativeInstances) if(body!=null) result.Add(body);
        var pool = ObjectPool.instance;
        if (pool == null) return result.ToArray();
        foreach (DictionaryEntry pair in (IDictionary)Spawned.GetValue(pool))
            if (pair.Key is GameObject body && body != null && prefabs.Contains((GameObject)pair.Value)) result.Add(body);
        return result.ToArray();
    }
    public void Dispose() { foreach(var spawner in enemySpawners) if(spawner!=null) spawner.OnEnemySpawned-=SpawnedSummon; enemySpawners.Clear(); summonSpawnerRoots.Clear(); directOomaInstances.Clear(); creation.UnpatchSelf(); if (current == this) current = null; oomaSpawns.Clear(); summonSpawns.Clear(); nativeInstances.Clear(); prefabs.Clear(); }
}
