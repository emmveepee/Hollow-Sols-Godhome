using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

namespace YiGodhome;

// Live room discovery, not a geometry cache. Scratch cannot escape a refresh.
internal static class RoomColliderSource
{
    private static readonly List<GameObject> roots = new();
    private static readonly List<Collider2D> components = new(), found = new();
    private static readonly Comparison<Collider2D> Order = (a,b) => a.GetInstanceID().CompareTo(b.GetInstanceID());
    private static int validatedHandle = int.MinValue;
    private static Collider2D[] Current(string scene)
    {
        try {
            for (int i=0;i<UnitySceneManager.sceneCount;i++) {
                var loaded = UnitySceneManager.GetSceneAt(i);
                if (!loaded.isLoaded || loaded.name != scene) continue;
                if (roots.Capacity <= loaded.rootCount) roots.Capacity = loaded.rootCount + 1;
                loaded.GetRootGameObjects(roots);
                foreach (var root in roots) {
                    root.GetComponentsInChildren(false, components);
                    foreach (var collider in components)
                        if (collider != null && collider.gameObject.scene == loaded && collider.gameObject.activeInHierarchy) found.Add(collider);
                    components.Clear();
                }
                roots.Clear();
            }
            found.Sort(Order);
            return found.ToArray();
        } finally { roots.Clear(); components.Clear(); found.Clear(); }
    }
    private static Collider2D[] Legacy(string scene) => UnityEngine.Object.FindObjectsByType<Collider2D>(FindObjectsSortMode.None)
        .Where(c => c.gameObject.scene.name == scene).OrderBy(c => c.GetInstanceID()).ToArray();
    internal static Collider2D[] Find(string scene, Action<string>? diagnostic)
    {
        var result = Current(scene);
        int handle = UnitySceneManager.GetSceneByName(scene).handle;
        if (diagnostic == null || !HostFrameProbe.Enabled || handle == validatedHandle) return result;
        var legacy = Legacy(scene);
        if (!legacy.Select(c => c.GetInstanceID()).SequenceEqual(result.Select(c => c.GetInstanceID())))
            throw new InvalidOperationException("Room collider discovery differs from native global query.");
        const int count = 16;
        int collections = GC.CollectionCount(0);
        long before = GC.GetTotalMemory(false);
        for (int i=0;i<count;i++) { var sample = Legacy(scene); GC.KeepAlive(sample); }
        long middle = GC.GetTotalMemory(false);
        for (int i=0;i<count;i++) { var sample = Current(scene); GC.KeepAlive(sample); }
        long after = GC.GetTotalMemory(false);
        validatedHandle = handle;
        diagnostic("HK_COLLIDER_DISCOVERY_CONTROL scene=" + scene + " count=" + result.Length + " calls=" + count
            + " legacyProcessHeapDeltaBytes=" + (middle-before) + " currentProcessHeapDeltaBytes=" + (after-middle)
            + " gc=" + (GC.CollectionCount(0)-collections) + " sameNativeOwners=True liveRefresh=True");
        return result;
    }
}
