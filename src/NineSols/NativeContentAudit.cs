using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using YiGodhome.Bridge;
using Object = UnityEngine.Object;

namespace YiGodhome;

// Experimental, standalone capability. Never constructed by normal gameplay.
// Unity loads serialized dependencies; donor game lifecycle does not initialize.
internal sealed class NativeContentAudit : IDisposable
{
    private static NativeContentAudit? active;
    private NativeContentRepository? repository;
    private NativeContentRepository.Lease? wallLease, warningLease;
    private readonly Action<string> log;
    private readonly Player player;
    private readonly GameLevel level;
    private readonly SaveManager save;
    private readonly Scene originalScene, originalActiveScene, canvas;
    private readonly Vector3 originalPosition;
    private readonly Transform? originalParent;
    private readonly string[] donors;
    private readonly List<GameObject> owned = new List<GameObject>();
    private PoolObject? warningPrefab, warning;
    private GameObject? wall;
    private NativeInputBridge? input;
    private Camera? camera;
    private int stage, warningRuns;
    private float runAt, nextWarning;
    private Vector3 movementStart;
    private float peakY;
    private bool captured, disposed;
    public bool Owns(Scene scene) => false;
    public static void Abort() => active?.Dispose();
    public NativeContentAudit(Player player, Action<string> log)
    {
        if (!SessionConfiguration.Current.Has("content-audit") || active != null)
            throw new InvalidOperationException("Content audit capability/ownership mismatch.");
        this.player = player; this.log = log; level = GameCore.Instance.gameLevel;
        save = Object.FindFirstObjectByType<SaveManager>() ?? throw new InvalidOperationException("Native slot context unavailable.");
        originalScene = player.gameObject.scene; originalActiveScene = SceneManager.GetActiveScene(); originalPosition = player.transform.position; originalParent = player.transform.parent;
        donors = new[] { SceneUtility.GetScenePathByBuildIndex(41), SceneUtility.GetScenePathByBuildIndex(58) }
            .Select(Path.GetFileNameWithoutExtension).ToArray();
        if (donors[0] != "A5_S5_JieChuanHall" || !donors[1].StartsWith("AG_Tutorial_Lear_S2_", StringComparison.Ordinal))
            throw new InvalidOperationException("Installed donor scene mapping changed.");
        canvas = SceneManager.CreateScene("YiGodhomeCanvasAudit");
        active = this;
        SceneManager.SetActiveScene(canvas);
        try { repository = new NativeContentRepository(player, canvas, log, NativeContentKind.JiequanWarning, NativeContentKind.ClimbArtwork); }
        catch { Dispose(); throw; }
    }
    public void SceneLoaded(Scene scene) => throw new InvalidOperationException("Repository owns donor notifications.");
    private void CheckAuthority()
    {
        if (GameCore.Instance.player != player || GameCore.Instance.gameLevel != level || !ApplicationCore.IsInBossMemoryMode
            || (int)AccessTools.Field(typeof(SaveManager), "currentSlotIndex").GetValue(save) != 3)
            throw new InvalidOperationException("Donor changed native player/level authority.");
    }
    public void Update()
    {
        CheckAuthority();
        repository!.Update();
        if (!repository.Ready) return;
        if (stage == 0) {
            warningPrefab = repository.JiequanWarning;
            wallLease = repository.Borrow(NativeContentKind.ClimbArtwork); wall = wallLease.Root;
            var siblings = new List<NativeContentRepository.Lease>();
            try {
                for (int i = 0; i < 7; i++) {
                    var sibling = repository.Borrow(NativeContentKind.ClimbArtwork); siblings.Add(sibling);
                    if (sibling.Root == wall || siblings.Take(siblings.Count - 1).Any(l => l.Root == sibling.Root))
                        throw new InvalidOperationException("Native visual leases alias.");
                }
                bool refused = false;
                try { siblings.Add(repository.Borrow(NativeContentKind.ClimbArtwork)); }
                catch (InvalidOperationException) { refused = true; }
                if (!refused) throw new InvalidOperationException("Native content exceeded its lease budget.");
                log("NS_REPOSITORY_CAPACITY independent=8 overflowRefused=True");
            } finally { foreach (var sibling in siblings) sibling.Dispose(); }
            StartCanvas(); stage = 4;
        }
        if (stage >= 4) {
            input?.Update();
            float elapsed = Time.realtimeSinceStartup - runAt;
            peakY = Mathf.Max(peakY, player.transform.position.y);
            if (elapsed >= nextWarning && warningRuns < 3) {
                if (warning != null) { warningLease?.Dispose(); warningLease = null; warning = null; }
                warningLease = repository.Borrow(NativeContentKind.JiequanWarning); warning = warningLease.Root.GetComponent<PoolObject>();
                warning.transform.position = new Vector3(20050, 20080, 0);
                log("NS_CONTENT_WARNING_PLAY run=" + (++warningRuns) + " instance=" + warning.GetInstanceID() + " nativePool=True donorLoaded=False");
                nextWarning += 2;
            }
            if (elapsed > 0.15f && !captured) {
                Capture("canvas.png", player.PlayerSprite.gameObject, wall!, warning!.gameObject);
                Capture("yi.png", player.PlayerSprite.gameObject);
                Capture("wall.png", wall!); Capture("jiequan-warning.png", warning.gameObject);
                captured = true;
            }
            if (elapsed >= 9 && stage == 4) {
                float distance = Mathf.Abs(player.transform.position.x - movementStart.x);
                var ground = (Collider2D?)AccessTools.Field(typeof(Actor), "currentGround").GetValue(player);
                if (distance < 20 || distance > 180 || peakY < 20020 || !player.IsOnGroundCheck()
                    || ground == null || ground.gameObject.name != "YiGodhomeCanvasGround")
                    throw new InvalidOperationException("Canvas movement/jump/landing failed: distance=" + distance + " peakY=" + peakY + " ground=" + ground?.name);
                if (warning != null) { warningLease?.Dispose(); warningLease = null; warning = null; }
                log("NS_CONTENT_CANVAS_VERIFIED player=" + player.GetInstanceID() + " scene=" + player.gameObject.scene.name + " moved=" + distance + " jumpRise=" + (peakY - 20000) + " nativeGround=True warningRuns=" + warningRuns + " nativeLevelUnchanged=True donorsUnloaded=True");
                input?.Dispose(); input = null; RestorePlayer(); stage = 5;
                log("NS_CONTENT_COMPLETE assets=2 nativeGameplay=True sceneCreated=True normalMigration=False");
            }
        }
    }
    private void StartCanvas()
    {
        player.transform.SetParent(null, true); SceneManager.MoveGameObjectToScene(player.gameObject, canvas);
        player.SetPosition(new Vector3(20000, 20020, 0), true); movementStart = player.transform.position; peakY = movementStart.y;
        var ground = new GameObject("YiGodhomeCanvasGround"); SceneManager.MoveGameObjectToScene(ground, canvas); owned.Add(ground);
        int solid = LayerMask.NameToLayer("Solid");
        if (solid < 0) throw new InvalidOperationException("Native Solid layer missing.");
        ground.layer = solid; ground.tag = "VRSafeGround"; ground.transform.position = new Vector3(20000, 19990, 0);
        ground.AddComponent<BoxCollider2D>().size = new Vector2(2000, 20);
        Physics2D.SyncTransforms(); player.ResetNeedToCheckGroundFlag();
        wall!.transform.position = new Vector3(20160, 20000, 0);
        wall.SetActive(true);
        var cameraObject = new GameObject("YiGodhomeCanvasCamera"); SceneManager.MoveGameObjectToScene(cameraObject, canvas); owned.Add(cameraObject);
        camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 220;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
        camera.transform.position = new Vector3(20060, 20100, -100); camera.cullingMask = 1 << 30;
        camera.nearClipPlane = 0.1f; camera.farClipPlane = 500; camera.allowHDR = false; camera.useOcclusionCulling = false;
        runAt = Time.realtimeSinceStartup;
        input = new NativeInputBridge(player, () => {
            float elapsed = Time.realtimeSinceStartup - runAt;
            return new Controls(elapsed < 0.5f ? 1 : 0, 0, elapsed > 1 && elapsed < 1.1f ? Buttons.Jump : Buttons.None);
        }, () => stage == 4 && player.actions.Enabled, log);
        log("NS_CONTENT_CANVAS_BEGIN nativePhysics=True retainedServices=True player=" + player.GetInstanceID());
        log("NS_CONTENT_PLAYER_ART renderer=" + player.PlayerSprite.name + " enabled=" + player.PlayerSprite.enabled + " bounds=" + player.PlayerSprite.bounds + " sprite=" + player.PlayerSprite.sprite?.name);
    }
    private void Capture(string name, params GameObject[] scopes) => Capture(name, scopes, true);
    private int Capture(string name, GameObject scope, bool requirePixels) => Capture(name, new[] { scope }, requirePixels);
    private int Capture(string name, GameObject[] scopes, bool requirePixels)
    {
        var texture = new RenderTexture(768, 768, 24, RenderTextureFormat.ARGB32);
        var pixels = new Texture2D(768, 768, TextureFormat.RGBA32, false);
        var previous = RenderTexture.active;
        var layers = new Dictionary<GameObject, int>();
        try {
            foreach (var renderer in scopes.SelectMany(s => s.GetComponentsInChildren<Renderer>(true)))
                if (!layers.ContainsKey(renderer.gameObject)) { layers.Add(renderer.gameObject, renderer.gameObject.layer); renderer.gameObject.layer = 30; }
            camera!.targetTexture = texture; camera.Render(); RenderTexture.active = texture;
            pixels.ReadPixels(new Rect(0, 0, 768, 768), 0, 0); pixels.Apply();
            int opaque = pixels.GetPixels32().Count(p => p.a > 10);
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!, name), pixels.EncodeToPNG());
            log("NS_CONTENT_CAPTURE file=" + name + " frame=" + Time.frameCount + " opaquePixels=" + opaque);
            if (requirePixels && opaque < 32) throw new InvalidOperationException("Acquired content did not render: " + name);
            return opaque;
        } finally { foreach (var entry in layers) if (entry.Key != null) entry.Key.layer = entry.Value; camera!.targetTexture = null; RenderTexture.active = previous; texture.Release(); Object.Destroy(texture); Object.Destroy(pixels); }
    }
    private void RestorePlayer()
    {
        SceneManager.SetActiveScene(originalActiveScene);
        SceneManager.MoveGameObjectToScene(player.gameObject, originalScene);
        player.transform.SetParent(originalParent, true); player.SetPosition(originalPosition, true);
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; input?.Dispose(); input = null;
        warningLease?.Dispose(); wallLease?.Dispose();
        if (player != null && player.gameObject.scene == canvas) RestorePlayer();
        else SceneManager.SetActiveScene(originalActiveScene);
        repository?.Dispose(); repository = null;
        foreach (var root in owned) if (root != null) Object.Destroy(root);
        SceneManager.UnloadSceneAsync(canvas);
        if (active == this) active = null;
        log("NS_CONTENT_DISPOSED ownedRoots=" + owned.Count + " restorePlayer=True");
    }
}
