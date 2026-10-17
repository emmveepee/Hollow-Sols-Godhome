using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace YiGodhome;

// Exact donor metadata manifest; no assembly-wide discovery in ordinary sessions.
internal sealed class NativeDonorQuarantine : IDisposable
{
    private static NativeDonorQuarantine? current;
    private readonly Harmony harmony = new Harmony("local.yigodhome.content-donors");
    private readonly Action<string> log;
    private readonly string[] scenes;
    internal int Methods { get; private set; }
    internal int Blocked { get; private set; }
    internal int LevelsBlocked { get; private set; }
    private bool disposed;
    private AsyncOperation? pendingLoad;
    internal void TrackLoad(AsyncOperation operation) { pendingLoad = operation; }
    internal bool Owns(Scene scene) => scenes.Contains(scene.name);
    internal NativeDonorQuarantine(Action<string> log)
    {
        if (current != null) throw new InvalidOperationException("Concurrent donor acquisition prohibited.");
        if (typeof(GameCore).Assembly.ManifestModule.ModuleVersionId != new Guid("dcdc095d-05f0-4390-897b-045fd51ebc28"))
            throw new InvalidOperationException("Native donor manifest is pinned to a different installed game build.");
        this.log = log;
        scenes = NativeDonorManifest.SceneIndices.Select(SceneUtility.GetScenePathByBuildIndex).Select(System.IO.Path.GetFileNameWithoutExtension).ToArray();
        if (scenes[0] != "A5_S5_JieChuanHall" || !scenes[1].StartsWith("AG_Tutorial_Lear_S2_", StringComparison.Ordinal) || scenes[2] != "A10_S1_TombEntrance_remake" || scenes[3] != "AG_Tutorial_Lear_S0_格擋複習")
            throw new InvalidOperationException("Pinned donor mapping changed.");
        current = this; SceneManager.sceneUnloaded += Unloaded; SceneManager.sceneLoaded += Loaded;
        try {
            var methods = new HashSet<MethodInfo>();
            var names = new HashSet<string>(new[] { "Awake", "OnEnable", "Start", "Update", "FixedUpdate", "LateUpdate", "OnDisable", "OnDestroy" });
            int missing = 0;
            foreach (string identity in NativeDonorManifest.Types) {
                var type = Type.GetType(identity, false);
                // Serialized missing/stripped scripts have no managed callbacks.
                if (type == null) { missing++; continue; }
                if (!typeof(MonoBehaviour).IsAssignableFrom(type)) continue;
                for (var t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType) {
                    if (t.ContainsGenericParameters) continue;
                    foreach (var method in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                        if (names.Contains(method.Name) && !method.IsAbstract && method.GetParameters().Length == 0 && method.GetMethodBody() != null)
                            methods.Add(method);
                }
            }
            foreach (var method in methods) harmony.Patch(method, prefix: new HarmonyMethod(typeof(NativeDonorQuarantine), nameof(Callback)));
            Methods = methods.Count;
            log("NS_CONTENT_MANIFEST types=" + NativeDonorManifest.Types.Length + " methods=" + Methods + " stripped=" + missing + " discovery=False");
        } catch { Dispose(); throw; }
    }
    private static bool Callback(MonoBehaviour __instance, MethodBase __originalMethod)
    {
        var scope = current;
        if (scope == null || __instance == null || !scope.Owns(__instance.gameObject.scene)) return true;
        scope.Blocked++;
        if (__instance is GameLevel && __originalMethod.Name == "Awake") scope.LevelsBlocked++;
        return false;
    }
    private void Unloaded(Scene scene) { if (disposed) Release(); }
    private void Loaded(Scene scene, LoadSceneMode mode)
    {
        if (!disposed || !Owns(scene)) return;
        foreach (var root in scene.GetRootGameObjects()) root.SetActive(false);
        SceneManager.UnloadSceneAsync(scene);
    }
    private void Release()
    {
        if (pendingLoad != null && !pendingLoad.isDone) return;
        if (scenes.Any(name => SceneManager.GetSceneByName(name).isLoaded)) return;
        SceneManager.sceneUnloaded -= Unloaded; SceneManager.sceneLoaded -= Loaded; harmony.UnpatchSelf();
        if (current == this) current = null;
        log("NS_CONTENT_GUARD_RELEASED methods=" + Methods + " donorsLoaded=False");
    }
    public void Dispose() { if (disposed) return; disposed = true; Release(); }
}
