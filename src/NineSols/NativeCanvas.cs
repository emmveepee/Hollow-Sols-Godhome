using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace YiGodhome;

// Gameplay scene ownership is independent of GameCore's registered native reset level.
internal sealed class NativeCanvas : IDisposable
{
    internal static NativeCanvas? Current { get; private set; }
    internal Scene SessionServiceScene => serviceScene;
    internal const string Name = "YiGodhomeCanvas";
    internal const string ServicesName = "YiGodhomeServices";
    internal const string BootstrapName = "A2_S5_BossHorseman_Final";
    private readonly Action<string> log;
    private readonly Scene bootstrapScene, serviceScene, canvas;
    private readonly GameLevel serviceLevel;
    private readonly Player player;
    private readonly List<(GameObject root, Transform? parent, Scene scene)> transferred = new List<(GameObject, Transform?, Scene)>();
    private readonly Dictionary<Renderer, bool> roomRenderers = new Dictionary<Renderer, bool>();
    internal MonsterBase Receiver { get; }
    internal SavePoint Rest { get; }
    internal NativeContentRepository? Content { get; private set; }
    internal bool BootstrapRetired => bootstrapRetired;
    private bool disposed;
    private AsyncOperation? unloadBootstrap;
    private float unloadDeadline;
    private bool bootstrapRetired;
    internal NativeCanvas(Player player, MonsterBase receiver, Action<string> log)
    {
        if (Current != null) throw new InvalidOperationException("Duplicate canvas ownership.");
        this.player = player; Receiver = receiver; this.log = log;
        bootstrapScene = SceneManager.GetActiveScene(); serviceLevel = GameCore.Instance.gameLevel;
        if (bootstrapScene.name != BootstrapName || !receiver.Inited || !ApplicationCore.IsInBossMemoryMode)
            throw new InvalidOperationException("Canvas requires initialized native bootstrap services.");
        if (serviceLevel.SceneName != BootstrapName) throw new InvalidOperationException("Native authored level identity changed.");
        Rest = bootstrapScene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<SavePoint>(true)).Single();
        canvas = SceneManager.CreateScene(Name);
        serviceScene = SceneManager.CreateScene(ServicesName);
        Current = this;
        try {
            NativeMinionOwner.PrepareTemplate(receiver, log);
            Move(player.gameObject); Move(receiver.gameObject); Move(Rest.gameObject);
            if (!SceneManager.SetActiveScene(canvas)) throw new InvalidOperationException("Canvas active scene assignment failed.");
            // Keep the native service graph alive; remove its unrelated rendering workload.
            foreach (var renderer in bootstrapScene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Renderer>(true))) {
                roomRenderers.Add(renderer, renderer.enabled); renderer.enabled = false;
            }
            // Preserve native cached callback/reference identity. No Awake/reset replay.
            foreach (var root in bootstrapScene.GetRootGameObjects()) SceneManager.MoveGameObjectToScene(root, serviceScene);
            if (bootstrapScene.rootCount != 0 || serviceLevel.gameObject.scene != serviceScene)
                throw new InvalidOperationException("Bootstrap native graph transfer incomplete.");
            unloadDeadline = Time.realtimeSinceStartup + 30;
            unloadBootstrap = SceneManager.UnloadSceneAsync(bootstrapScene)
                ?? throw new InvalidOperationException("Empty bootstrap scene unload unavailable.");
            Validate();
            log("NS_CANVAS_TRANSFERRED scene=" + canvas.name + " serviceScene=" + serviceScene.name
                + " nativeLevel=" + serviceLevel.GetInstanceID() + " player=" + player.GetInstanceID()
                + " receiver=" + receiver.GetInstanceID() + " rest=" + Rest.GetInstanceID()
                + " excludedRoomRenderers=" + roomRenderers.Count + " bootstrapUnloaded=False");
        } catch { Dispose(); throw; }
    }
    private void Move(GameObject root)
    {
        transferred.Add((root, root.transform.parent, root.scene));
        root.transform.SetParent(null, true); SceneManager.MoveGameObjectToScene(root, canvas);
    }
    internal void Validate()
    {
        if (disposed || !canvas.isLoaded || SceneManager.GetActiveScene() != canvas
            || !serviceScene.isLoaded || serviceLevel.gameObject.scene != serviceScene
            || player.gameObject.scene != canvas || Receiver.gameObject.scene != canvas || Rest.gameObject.scene != canvas
            || GameCore.Instance.gameLevel != serviceLevel || GameCore.Instance.player != player
            || !ApplicationCore.IsInBossMemoryMode
            || (int)HarmonyLib.AccessTools.Field(typeof(SaveManager), "currentSlotIndex").GetValue(SaveManager.Instance) != 3)
            throw new InvalidOperationException("Canvas/native service authority changed.");
    }
    internal bool UpdateContent(NativeContentKind? required = null)
    {
        Validate();
        if (!bootstrapRetired) {
            if (Time.realtimeSinceStartup > unloadDeadline) throw new TimeoutException("Bootstrap unload deadline exceeded.");
            if (unloadBootstrap?.isDone != true) return false;
            if (bootstrapScene.isLoaded || SceneManager.GetSceneByName(BootstrapName).isLoaded)
                throw new InvalidOperationException("Borrowed bootstrap scene remains loaded.");
            bootstrapRetired = true;
            log("NS_CANVAS_READY scene=" + canvas.name + " serviceScene=" + serviceScene.name
                + " nativeLevel=" + serviceLevel.GetInstanceID() + " player=" + player.GetInstanceID()
                + " receiver=" + Receiver.GetInstanceID() + " rest=" + Rest.GetInstanceID()
                + " excludedRoomRenderers=" + roomRenderers.Count + " bootstrapUnloaded=True");
        }
        Content ??= new NativeContentRepository(player, canvas, log);
        if (required.HasValue) Content.Request(required.Value);
        Content.Update();
        return Content.Ready;
    }
    internal static bool IsServiceContext()
    {
        if (Current == null) return SceneManager.GetActiveScene().name == BootstrapName;
        Current.Validate(); return true;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Content?.Dispose(); Content = null;
        if (serviceScene.isLoaded) {
            SceneManager.SetActiveScene(serviceScene);
            foreach (var item in transferred) if (item.root != null) {
                item.root.transform.SetParent(null, true);
                Scene destination = item.scene == bootstrapScene ? serviceScene : item.scene;
                if (!destination.isLoaded) throw new InvalidOperationException("Native teardown destination unavailable.");
                SceneManager.MoveGameObjectToScene(item.root, destination);
                if (item.parent != null) item.root.transform.SetParent(item.parent, true);
            }
        }
        foreach (var pair in roomRenderers) if (pair.Key != null) pair.Key.enabled = pair.Value;
        if (canvas.isLoaded) SceneManager.UnloadSceneAsync(canvas);
        if (Current == this) Current = null;
        log("NS_CANVAS_DISPOSED transferred=" + transferred.Count + " nativeServicesRetained=True bootstrapUnloaded=" + bootstrapRetired);
    }
}
