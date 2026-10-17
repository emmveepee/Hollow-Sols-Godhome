using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace YiGodhome;

internal sealed class RoomEntryPresentation : IDisposable
{
    private static RoomEntryPresentation? current;
    private readonly Harmony harmony = new Harmony("local.yigodhome.room-entry");
    private readonly Action<string> log;
    private string pendingScene = "";
    private bool bypass;
    private bool dreamReturnPending;
    private string cameraEvent = "";
    private string revealedScene = "";
    private int returnSceneHandle = -1;
    private int transitionTraceHandle = -1, transitionTraceCount;
    private int statueDepartureHandle=-1;
    internal static bool StatueDeparting=>current!=null && current.statueDepartureHandle==UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
    private static readonly System.Reflection.FieldInfo transitionPrefab = AccessTools.Field(typeof(BossSceneController), "transitionPrefab");
    public static bool Returning => current != null && current.dreamReturnPending;
    public bool Revealed { get; private set; }
    public RoomEntryPresentation(Action<string> log)
    {
        this.log = log; current = this;
        // RequestFadeSceneIn and the native failsafe bypass both public fade
        // wrappers. Scope the common event boundary to the actual camera FSM.
        harmony.Patch(AccessTools.Method(typeof(HutongGames.PlayMaker.Fsm), "ProcessEvent"), prefix: new HarmonyMethod(typeof(RoomEntryPresentation), nameof(BeforeCameraEvent)));
        harmony.Patch(AccessTools.Method(typeof(HutongGames.PlayMaker.Actions.SendEventByName), "OnEnter"), prefix: new HarmonyMethod(typeof(RoomEntryPresentation), nameof(BeforeReturnFade)));
        harmony.Patch(AccessTools.Method(typeof(BossChallengeUI),"LoadBoss",new[]{typeof(int),typeof(bool)}),postfix:new HarmonyMethod(typeof(RoomEntryPresentation),nameof(StatueSelected)));
    }
    private static void StatueSelected(){if(current!=null && HollowSols.Foundation.NativeSaveContext.Enabled && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name=="GG_Workshop")current.statueDepartureHandle=UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;}
    private static bool BeforeCameraEvent(HutongGames.PlayMaker.Fsm __instance, HutongGames.PlayMaker.FsmEvent __0)
    {
        string name = __0 == null ? "" : __0.Name;
        var traceOwner = current;
        if (traceOwner != null && YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures && BossSceneController.Instance != null)
        {
            int handle = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            if (traceOwner.transitionTraceHandle != handle) { traceOwner.transitionTraceHandle = handle; traceOwner.transitionTraceCount = 0; }
            var prefab = transitionPrefab.GetValue(BossSceneController.Instance) as GameObject;
            var root = __instance.GameObject == null ? null : __instance.GameObject.transform.root.gameObject;
            if (prefab != null && root != null && root.name == prefab.name + "(Clone)" && traceOwner.transitionTraceCount < 32)
            {
                traceOwner.transitionTraceCount++;
                var sprite = root.GetComponentInChildren<tk2dBaseSprite>(true);
                traceOwner.log("HK_NATIVE_BOSS_FADE_EVENT scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + " nativeFrame=" + Time.frameCount
                    + " owner=" + root.name + " fsm=" + __instance.Name + " state=" + __instance.ActiveStateName + " event=" + name
                    + " spriteAlpha=" + (sprite == null ? "unknown" : sprite.color.a.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
            }
        }
        if (name != "FADE SCENE IN" && name != "FADE SCENE IN SLOWLY" && name != "FADE SCENE IN INSTANT") return true;
        var owner = current;
        if (owner == null || owner.bypass || GameCameras.instance?.cameraFadeFSM?.Fsm != __instance) return true;
        bool allowed = BeforeFade();
        if (YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures)
            owner.log("HK_CAMERA_REVEAL_REQUEST scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + " event=" + name + " allowed=" + allowed + " state=" + __instance.ActiveStateName);
        if (allowed) return true;
        // Preserve the requested speed; a later failsafe cannot bypass the hold.
        if (owner.cameraEvent.Length == 0) owner.cameraEvent = name;
        owner.log("HK_CAMERA_FADE_DEFERRED event=" + name + " nativeFsm=True");
        return false;
    }
    private static bool BeforeReturnFade(HutongGames.PlayMaker.Actions.SendEventByName __instance)
    {
        var owner = current;
        if (owner == null || GameManager.instance?.profileID != 4 || !YiGodhome.Bridge.ReturnPreparation.IsHub(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)
            || __instance.Fsm.Name != "Dream Return" || (__instance.State.Name != "Prostrate" && __instance.State.Name != "GG Return") || __instance.sendEvent.Value != "FADE OUT") return true;
        if (__instance.delay.Value != 0 || __instance.everyFrame) throw new InvalidOperationException("Native return fade contract changed.");
        if (owner.returnSceneHandle == UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle) return true;
        owner.returnSceneHandle = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
        owner.pendingScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; owner.Revealed = false;
        owner.dreamReturnPending = true;
        var target = __instance.Fsm.GetOwnerDefaultTarget(__instance.eventTarget.gameObject);
        var sprite = target == null ? null : target.GetComponent<tk2dBaseSprite>();
        owner.log("HK_RETURN_PRESENTATION_BEGIN nativeFadeUnchanged=True state=" + __instance.State.Name + " target=" + (target == null ? "none" : target.name)
            + " alpha=" + (sprite == null ? "unknown" : sprite.color.a.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)) + " nativeFrame=" + Time.frameCount);
        return true;
    }
    private static bool BeforeFade()
    {
        var owner = current; var hero = HeroController.instance;
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (owner == null || owner.bypass || (owner.Revealed && owner.revealedScene == scene) || GameManager.instance?.profileID != 4 || !scene.StartsWith("GG_", StringComparison.Ordinal)
            || BossSceneController.Instance != null || hero == null) return true;
        var gate = hero.sceneEntryGate;
        string dream = Array.Find(hero.GetComponents<PlayMakerFSM>(), f => f.FsmName == "Dream Return")?.ActiveStateName ?? "";
        bool returning = YiGodhome.Bridge.ReturnPreparation.IsHub(scene) && (Returning || gate != null && gate.name.StartsWith("door_dreamReturn", StringComparison.Ordinal)
            || dream == "Wait until after" || dream == "Pause" || dream == "GC Pause" || dream == "Prostrate" || dream == "Ready" || dream == "Get Up");
        if (returning) return true; // Native return timing is not delayed for presentation.
        if (gate == null) return true;
        int position = gate == null ? 0 : (int)gate.GetGatePosition();
        if (!returning && position != 0 && position != 1 && position != 2 && position != 3 && position != 4) return true;
        owner.pendingScene = scene; owner.Revealed = false;
        owner.dreamReturnPending |= returning;
        owner.log("HK_ROOM_FADE_HELD scene=" + scene + " gate=" + (gate == null ? "none" : gate.name) + " dreamReturn=" + returning + " nativeCamera=True state=" + dream);
        return false;
    }
    public void Reset()
    {
        if (pendingScene != UnityEngine.SceneManagement.SceneManager.GetActiveScene().name) { pendingScene = ""; dreamReturnPending = false; cameraEvent = ""; }
        Revealed = false;
    }
    public void Reveal(long geometry)
    {
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (pendingScene != scene)
        {
            // Paired gameplay attaches after the native introduction/entry fade.
            // The caller already verified the current geometry and real image.
            // There is no held camera event to replay, but NS still needs the
            // presentation acknowledgement before its native entrance can run.
            if (pendingScene.Length != 0 || Revealed || !HollowSols.Foundation.NativeSaveContext.Enabled
                || GameManager.instance?.HasFinishedEnteringScene != true || GameManager.instance.gameState.ToString() != "PLAYING"
                || HeroController.instance == null) return;
            pendingScene = scene;
            log("HK_PAIRED_INITIAL_IMAGE_READY geometry=" + geometry + " scene=" + scene + " nativeFadeAlreadyCompleted=True");
        }
        bool cameraHeld = cameraEvent.Length != 0;
        bypass = true;
        try
        {
            if (cameraEvent.Length != 0) GameCameras.instance.cameraFadeFSM.Fsm.Event(cameraEvent);
        }
        finally { bypass = false; }
        dreamReturnPending = false; cameraEvent = "";
        revealedScene = pendingScene; pendingScene = ""; Revealed = true;
        log("HK_ROOM_FADE_RELEASED geometry=" + geometry + " nativeImage=True cameraHeld=" + cameraHeld + " nativeReturnTiming=True");
    }
    public void Dispose() { harmony.UnpatchSelf(); if (current == this) current = null; }
}
