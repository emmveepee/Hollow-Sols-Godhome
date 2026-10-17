using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;
using YiGodhome.Geometry;

namespace YiGodhome;

// Native dream-fall regions are scripted returns, not DamageHero volumes.
internal static class DreamVoidSource
{
    // Main-thread scratch never escapes. Revisit live roots on every refresh so
    // additions/removals and disabled FSMs on active catcher objects remain visible.
    private static readonly List<GameObject> roots = new List<GameObject>();
    private static readonly List<PlayMakerFSM> components = new List<PlayMakerFSM>();
    private static readonly List<PlayMakerFSM> found = new List<PlayMakerFSM>();
    private static readonly Comparison<PlayMakerFSM> Order = (a,b) => a.GetInstanceID().CompareTo(b.GetInstanceID());
    private static int validatedHandle = int.MinValue;
    public static PlayMakerFSM[] Find(string scene, Action<string>? diagnostic = null)
    {
        try
        {
            for (int i = 0; i < UnitySceneManager.sceneCount; i++)
            {
                var loaded = UnitySceneManager.GetSceneAt(i);
                if (!loaded.isLoaded || loaded.name != scene) continue;
                if (roots.Capacity <= loaded.rootCount) roots.Capacity = loaded.rootCount + 1;
                loaded.GetRootGameObjects(roots);
                foreach (var root in roots)
                {
                    root.GetComponentsInChildren(true, components);
                    foreach (var f in components)
                        if (f != null && f.gameObject.scene == loaded && f.FsmName == "Control"
                            && f.name == "Dream Fall Catcher" && f.gameObject.activeInHierarchy) found.Add(f);
                    components.Clear();
                }
                roots.Clear();
            }
            // Preserve owned results and native instance ordering independently
            // of hierarchy traversal; callers may retain their returned array.
            found.Sort(Order);
            var result = found.ToArray();
            int handle = UnitySceneManager.GetSceneByName(scene).handle;
            if (diagnostic != null && HostFrameProbe.Enabled && validatedHandle != handle)
            {
                // One scoped legacy comparison per room entry in bounded diagnostics.
                var legacy = Resources.FindObjectsOfTypeAll<PlayMakerFSM>()
                    .Where(f => f.gameObject.scene.name == scene && f.name == "Dream Fall Catcher"
                        && f.FsmName == "Control" && f.gameObject.activeInHierarchy)
                    .Select(f => f.GetInstanceID()).OrderBy(id => id).ToArray();
                if (!legacy.SequenceEqual(result.Select(f => f.GetInstanceID())))
                    throw new InvalidOperationException("Room-scoped dream catcher discovery differs from native resource query.");
                validatedHandle = handle;
                diagnostic("HK_DREAM_DISCOVERY_VERIFIED scene=" + scene + " count=" + result.Length + " sameNativeOwners=True liveRefresh=True");
            }
            return result;
        }
        finally { roots.Clear(); components.Clear(); found.Clear(); }
    }

    public static Shape Capture(PlayMakerFSM fsm)
    {
        var box = fsm.GetComponent<BoxCollider2D>();
        if (box == null || !box.isTrigger || !box.enabled || box.edgeRadius != 0
            || !new[] { "Detect", "Fade Out", "Stay" }.All(n => fsm.FsmStates.Any(s => s.Name == n)))
            throw new InvalidOperationException("Dream fall catcher contract changed.");
        Vector2 h = box.size * 0.5f;
        Point P(Vector2 p) { var w = box.transform.TransformPoint(p + box.offset); return new Point(w.x, w.y); }
        return new Shape { HazardId = box.GetInstanceID(), Closed = true,
            Points = new[] { P(-h), P(new Vector2(h.x, -h.y)), P(h), P(new Vector2(-h.x, h.y)) } };
    }
}
