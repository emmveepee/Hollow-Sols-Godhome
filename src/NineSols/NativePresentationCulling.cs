using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace YiGodhome;

// The bridge's cameras own visibility of these art-only scopes; the borrowed
// gameplay camera must not vote them inactive when Yi moves away.
internal sealed class NativePresentationCulling : IDisposable
{
    private static readonly HashSet<Transform> roots = new HashSet<Transform>();
    private static readonly Harmony harmony = new Harmony("local.yigodhome.presentation-culling");
    private readonly Transform root;
    private readonly MonoBehaviour[] groups;
    private static readonly Type groupType = AccessTools.TypeByName("RCGCullingGroup") ?? throw new InvalidOperationException("Native culling owner unavailable.");
    private static readonly System.Reflection.MethodInfo vote = AccessTools.Method(AccessTools.TypeByName("GameObjectActivateExtension"), "SetActiveVote", new[] { typeof(MonoBehaviour), typeof(bool) });
    private static bool Exit(Component __instance)
    { foreach (var root in roots) if (root != null && (__instance.transform == root || __instance.transform.IsChildOf(root))) return false; return true; }
    public NativePresentationCulling(Transform root)
    {
        this.root = root;
        if (roots.Count == 0) harmony.Patch(AccessTools.Method(groupType, "OnExitCamera"), prefix: new HarmonyMethod(typeof(NativePresentationCulling), nameof(Exit)));
        roots.Add(root);
        groups = Array.ConvertAll(root.GetComponentsInChildren(groupType, true), c => (MonoBehaviour)c);
        foreach (var group in groups) vote.Invoke(null, new object[] { group, true });
    }
    public void Dispose() { roots.Remove(root); if (roots.Count == 0) harmony.UnpatchSelf(); }
}
