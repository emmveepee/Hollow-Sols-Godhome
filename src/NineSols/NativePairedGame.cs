using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using HarmonyLib;
using HollowSols.Foundation;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace YiGodhome;

internal static class NativePairedGame
{
    private static JSONObject? oldFlags;
    private static int oldSlot;
    private static bool oldForcedLocation;
    internal static bool Returning;
    private static bool nativeMenuPrepared;
    private static UnityEngine.SceneManagement.Scene? endingServices;
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
    internal static void TryFocusNineSols(Action<string> log)
    {
        try {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;
            using var process = Process.GetCurrentProcess();
            if (process.MainWindowHandle == IntPtr.Zero) return;
            ShowWindowAsync(process.MainWindowHandle, 9);
            log("NS_CONTROLS_FOCUS_ATTEMPT accepted=" + SetForegroundWindow(process.MainWindowHandle));
        } catch (Exception error) { log("NS_CONTROLS_FOCUS_ATTEMPT unavailable=" + error.GetType().Name); }
    }
    internal static void TryFocusHollowKnight(Action<string> log)
    {
        try {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) { log("HK_FOCUS_ATTEMPT supported=False"); return; }
            var candidates = Process.GetProcessesByName("hollow_knight");
            try {
                if (candidates.Length != 1 || candidates[0].MainWindowHandle == IntPtr.Zero) { log("HK_FOCUS_ATTEMPT available=False"); return; }
                IntPtr window = candidates[0].MainWindowHandle; ShowWindowAsync(window, 9);
                log("HK_FOCUS_ATTEMPT requested=True accepted=" + SetForegroundWindow(window));
            } finally { foreach (var process in candidates) process.Dispose(); }
        } catch (Exception error) { log("HK_FOCUS_ATTEMPT unavailable=" + error.GetType().Name); }
    }
    internal static void Install()
    {
        GodhomeExperience.Install();
        new Harmony("local.hollowsols.ns.quit").Patch(AccessTools.Method(typeof(ApplicationCore), "ExitGame"), prefix: new HarmonyMethod(typeof(NativePairedGame), nameof(InterceptQuit)));
        var harmony = new Harmony("local.hollowsols.ns.lifecycle");
        harmony.Patch(AccessTools.Method(typeof(UIControlGroup), "PushShow"), prefix: new HarmonyMethod(typeof(NativePairedGame), nameof(VanillaSelection)));
        harmony.Patch(AccessTools.Method(typeof(ApplicationCore), "ChangeSceneClean"), prefix: new HarmonyMethod(typeof(NativePairedGame), nameof(InterceptReturn)));
        harmony.Patch(AccessTools.Method(typeof(GameCore), "SetStateChangeToMainMenu"), prefix: new HarmonyMethod(typeof(NativePairedGame), nameof(InterceptMenuPreparation)));
        harmony.Patch(AccessTools.Method(typeof(StartMenuLogic), "CreateOrLoadSaveSlotAndPlay"), prefix: new HarmonyMethod(typeof(NativePairedGame), nameof(ValidateNativeGameplay)));
        harmony.Patch(AccessTools.Method(typeof(StartMenuLogic), "StartMemoryChallenge"), prefix: new HarmonyMethod(typeof(NativePairedGame), nameof(VanillaMemories)));
        harmony.Patch(AccessTools.PropertySetter(typeof(GameCore), "currentCoreState"), prefix: new HarmonyMethod(typeof(NativePairedGame), nameof(TraceCoreState)));
        harmony.Patch(AccessTools.Method(typeof(GameCore), "SceneLoaded"), prefix: new HarmonyMethod(typeof(NativePairedGame), nameof(TraceLevelEntry)));
        harmony.Patch(AccessTools.Method(typeof(ApplicationCore), "StartGameGoTo"), prefix: new HarmonyMethod(typeof(NativePairedGame), nameof(UseNativeMemoryEntry)));
        harmony.Patch(AccessTools.Method(typeof(SaveManager), "LoadSaveAtSlot"), postfix: new HarmonyMethod(typeof(NativePairedGame), nameof(RequireNativeLoadSuccess)));
    }
    private static void RequireNativeLoadSuccess(ref UniTask<bool> __result)
    { if (NativeSaveContext.Session.HasValue) __result = ValidateNativeLoad(__result, NativeSaveContext.Session.Value); }
    private static async UniTask<bool> ValidateNativeLoad(UniTask<bool> loading, SessionToken token)
    {
        bool loaded = await loading;
        if (!NativeSaveContext.Session.HasValue || !NativeSaveContext.Session.Value.Equals(token)) throw new OperationCanceledException("Native load belongs to an ended paired session.");
        if (!loaded) throw new IOException("The selected native Nine Sols checkpoint could not be loaded. A replacement baseline is not permitted.");
        return true;
    }
    private static bool InterceptQuit() => HollowSolsMenuPlugin.Instance?.NativeQuitRequested() != false;
    private static bool pairedLoadEntry;
    private static bool pairedLoadRunning;
    private static bool nativeEntryPending;
    private static GameLevel? enteringHub;
    private static UnityEngine.Events.UnityAction? hubStartedHandler;
    private static bool waitForHubStart, hubStarted;
    private static bool NativeSceneLoading => pairedLoadRunning || nativeEntryPending || GameCore.IsAvailable()
        && GameCore.Instance.currentCoreState == GameCore.GameCoreState.ChangingScene;
    internal static async UniTask WaitNativeEntryReady()
    {
        // A fresh core's hub deliberately remains Init until the first combat
        // teleport. A reused core runs the hub's normal delayed level-start
        // callback; that exact callback must finish before another transition.
        if (!waitForHubStart) return;
        nativeEntryPending = true;
        try {
            double deadline = Time.realtimeSinceStartupAsDouble + BridgeLifecycle.LoadingSeconds;
            while (!hubStarted) {
                if (Time.realtimeSinceStartupAsDouble > deadline) throw new TimeoutException("Native memory hub initialization did not finish.");
                await UniTask.Yield();
            }
        } finally { nativeEntryPending = false; ClearHubStartWatch(); }
    }
    private static void ClearHubStartWatch()
    {
        if (enteringHub != null && hubStartedHandler != null) enteringHub.OnLevelStartPlaying.RemoveListener(hubStartedHandler);
        enteringHub=null;hubStartedHandler=null;waitForHubStart=false;
    }
    private static void TraceCoreState(GameCore __instance, GameCore.GameCoreState __0)
    {
        if (HollowSolsMenuPlugin.Instance?.Lifecycle?.State != BridgeState.Loading) return;
        HollowSolsMenuPlugin.Instance.Log("NS_NATIVE_CORE_TRANSITION from="+__instance.currentCoreState+" to="+__0+" playerActive="+__instance.player?.gameObject.activeSelf+" scene="+UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
    }
    private static void TraceLevelEntry(GameCore __instance, GameLevel __0)
    {
        if (HollowSolsMenuPlugin.Instance?.Lifecycle?.State != BridgeState.Loading) return;
        HollowSolsMenuPlugin.Instance.Log("NS_NATIVE_LEVEL_ENTRY state="+__instance.currentCoreState+" level="+__0.SceneName+" playerActive="+__instance.player?.gameObject.activeSelf);
        if (__0.SceneName == "VR_Challenge_Hub") {
            ClearHubStartWatch();hubStarted=false;
            waitForHubStart=__instance.currentCoreState!=GameCore.GameCoreState.Init;
            if (waitForHubStart) {
                enteringHub=__0;var token=NativeSaveContext.Session;
                hubStartedHandler=()=> { if (Nullable.Equals(token,NativeSaveContext.Session)) { hubStarted=true;HollowSolsMenuPlugin.Instance?.Log("NS_NATIVE_HUB_STARTED nativeCallback=True"); } };
                enteringHub.OnLevelStartPlaying.AddListener(hubStartedHandler);
            }
        }
    }
    internal static async UniTask LoadPairedNative(StartMenuLogic menu)
    {
        if (pairedLoadRunning || Returning || !NativeSaveContext.Enabled
            || HollowSolsMenuPlugin.Instance?.Lifecycle?.State != BridgeState.Loading)
            throw new InvalidOperationException("Paired native loading is unavailable.");
        pairedLoadRunning = true;
        try {
            // The persistent core keeps its title-exit latch between levels.
            // Native ChangeScene exits immediately while it is set, so clearing
            // it only after awaiting the new load is too late.
            if (GameCore.IsAvailable()) {
                var exit = AccessTools.Field(typeof(GameCore), "BackToDeveloperMenu");
                bool latched = (bool)exit.GetValue(GameCore.Instance); exit.SetValue(GameCore.Instance, false);
                HollowSolsMenuPlugin.Instance!.Log("NS_PAIRED_LOAD_ENTRY priorMenuExit=" + latched + " clearedBeforeNativeLoad=True");
            }
            UniTask loading;
            pairedLoadEntry = true;
            try { loading = menu.CreateOrLoadSaveSlotAndPlay(3, false, false, true); }
            finally { pairedLoadEntry = false; }
            await loading;
        } finally { pairedLoadRunning = false; }
    }
    private static bool VanillaMemories()
    {
        // This direct title entry bypasses UIControlGroup's save selector.
        // Ignore spam while bridge mode/cleanup owns the native loader.
        return !Returning && !pairedLoadRunning && !NativeSaveContext.ProtectingWrites
            && HollowSolsMenuPlugin.Instance?.VanillaGameplayAllowed != false;
    }
    private static bool ValidateNativeGameplay(int __0, ref UniTask __result)
    {
        if (NativeSaveContext.Enabled) {
            if (pairedLoadEntry && __0 == NativeSaveContext.SerializerIndex(GameSide.NineSols)) return true;
            __result = default; return false;
        }
        var state = HollowSolsMenuPlugin.Instance?.Lifecycle?.State;
        if (Returning || pairedLoadRunning || HollowSolsMenuPlugin.Instance?.VanillaGameplayAllowed == false) { __result = default; return false; }
        if (NativeSaveContext.ReadOnlyVanilla || state.HasValue && state != BridgeState.Vanilla)
            throw new InvalidOperationException("Native gameplay requires the selected isolated paired context. Return through vanilla selection before playing a vanilla save.");
        return true;
    }
    internal static void RememberVanilla()
    {
        var save = SaveManager.Instance;
        if (save == null || save.allFlags == null) throw new InvalidOperationException("Native NS save services unavailable.");
        if (AccessTools.Field(typeof(SaveManager), "_saveLock").GetValue(save) is bool busy && busy) throw new InvalidOperationException("Wait for the native save operation to finish.");
        // The native full serializer includes gameplay metadata and requires a
        // live level. Preserve resident flags at the title using its flag codec;
        // gameplay snapshots still use the complete native binary serializer.
        oldFlags = new JSONObject();
        var dictionary = (Dictionary<string, GameFlagBase>)AccessTools.Field(typeof(GameFlagCollection), "flagDict").GetValue(save.allFlags);
        foreach (var entry in dictionary) { var value = new JSONObject(); entry.Value.FlagToJSON(value); oldFlags.AddField(entry.Value.FinalSaveID, value); }
        oldSlot = (int)AccessTools.Field(typeof(SaveManager), "currentSlotIndex").GetValue(save);
        oldForcedLocation = save.forceSaveFlag.CurrentValue;
        ((Dictionary<string, bool>)AccessTools.Field(typeof(SaveManager), "ExistPathsCache").GetValue(save)).Clear();
        if (AccessTools.Field(typeof(SaveManager), "_fileSystem").GetValue(save)?.GetType().FullName != "SaveManager+UnityFileSystem") throw new InvalidOperationException("An unaudited native file provider is active.");
    }
    internal static void RestoreVanilla()
    {
        var save = SaveManager.Instance;
        if (oldFlags != null) AccessTools.Method(typeof(GameFlagManager), "LoadFlagsFromJOBJ").Invoke(null, new object[] { oldFlags, save.allFlags, default(TestMode) });
        AccessTools.Field(typeof(SaveManager), "currentSlotIndex").SetValue(save, oldSlot);
        AccessTools.Field(typeof(SaveManager), "isSlotLoaded").SetValue(save, false);
        ((Dictionary<string, bool>)AccessTools.Field(typeof(SaveManager), "ExistPathsCache").GetValue(save)).Clear();
        oldFlags = null;
        save.forceSaveFlag.CurrentValue = oldForcedLocation;
    }
    internal static async void Capture(Action<byte[]> done)
    {
        var token = NativeSaveContext.Session;
        if (!token.HasValue) throw new InvalidOperationException("No native NS session to save.");
        try
        {
            long started=Stopwatch.GetTimestamp();int collections=GC.CollectionCount(2);
            if (!await SaveManager.Instance.SaveAllFlagsAndMeta(NativeSaveContext.SlotPath(NativeSaveContext.SerializerIndex(GameSide.NineSols)))) throw new IOException("Native NS serialization failed.");
            if (NativeSaveContext.Session.HasValue && NativeSaveContext.Session.Value.Equals(token.Value)) {
                var snapshot=NativeSaveContext.Snapshot(token.Value);
                HollowSolsMenuPlugin.Instance!.Log(FormattableString.Invariant($"NS_PAIRED_CAPTURE ms={(Stopwatch.GetTimestamp()-started)*1000d/Stopwatch.Frequency:F3} gc2={GC.CollectionCount(2)-collections} state={HollowSolsMenuPlugin.Instance.Lifecycle?.State} gesture={NativeGesturePresentation.FrameKey} bytes={snapshot.Length}"));
                done(snapshot);
            }
        }
        catch (Exception error) { HollowSolsMenuPlugin.Instance!.GameplayFailed("PAIRED_NS_SAVE_FAILED", error); }
    }
    internal static async void Return(Action completed)
    {
        Returning = true; Time.timeScale = 1;
        try {
            // A Unity scene load cannot be aborted by destroying its requester.
            // Serialize title return after it, keeping the save guard installed.
            double deadline = Time.realtimeSinceStartupAsDouble + BridgeLifecycle.LoadingSeconds;
            while (NativeSceneLoading) {
                if (Time.realtimeSinceStartupAsDouble > deadline) throw new TimeoutException("Cancelled native load did not finish before title return.");
                await UniTask.Yield();
            }
            PrepareNativeMenuState();
            await ApplicationCore.Instance.ChangeSceneClean("TitleScreenMenu", true, true, true);
            if (endingServices.HasValue && endingServices.Value.IsValid() && endingServices.Value.isLoaded) {
                var unload = UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(endingServices.Value);
                if (unload != null) await UniTask.WaitUntil(() => unload.isDone);
            }
            // Native gameplay closes this in GameCore.SceneLoaded(GameLevel).
            // The title has no GameLevel, so use the authored cleanup here.
            var loading = (LoadingScreenPanel)AccessTools.Field(typeof(ApplicationCore), "loadingScreen").GetValue(ApplicationCore.Instance);
            loading.HideLoading();
            await UniTask.Yield();
            if (!LoadingClosed) throw new InvalidOperationException("Native title loading screen did not close.");
            HollowSolsMenuPlugin.Instance!.Log("NS_PAIRED_TITLE_LOADING_CLOSED nativeHide=True tipsAndSpinnerHidden=True");
            endingServices = null; completed();
        }
        catch (Exception error) { HollowSolsMenuPlugin.Instance!.GameplayFailed("NS_MENU_RETURN_FAILED", error); }
        finally { Returning = false; }
    }
    internal static void PrepareReturn()
    {
        Returning = true;
        endingServices = NativeCanvas.Current?.SessionServiceScene;
        nativeMenuPrepared = false;
        if (!NativeSceneLoading) PrepareNativeMenuState();
    }
    private static void PrepareNativeMenuState()
    {
        if (nativeMenuPrepared) return;
        nativeMenuPrepared = true;
        var core = GameCore.Instance;
        if (core != null && AccessTools.Field(typeof(GameCore), "player").GetValue(core) is Player player && player != null
            && AccessTools.Field(typeof(GameCore), "gameLevel").GetValue(core) is GameLevel level && level != null) core.SetStateChangeToMainMenu();
    }
    internal static bool LoadingClosed
    {
        get {
            var loading = (LoadingScreenPanel)AccessTools.Field(typeof(ApplicationCore), "loadingScreen").GetValue(ApplicationCore.Instance);
            return loading != null && !(bool)AccessTools.Field(typeof(LoadingScreenPanel), "OnMission").GetValue(loading)
                && !((Canvas)AccessTools.Field(typeof(LoadingScreenPanel), "_canvas").GetValue(loading)).gameObject.activeInHierarchy;
        }
    }
    private static void UseNativeMemoryEntry(TeleportPointData __0)
    {
        if (!NativeSaveContext.Enabled || HollowSolsMenuPlugin.Instance?.Lifecycle?.State != BridgeState.Loading) return;
        if (__0 != GameConfig.Instance.memoryChallengeStartPoint) throw new InvalidOperationException("Paired Yi loading must use the native memory entry point.");
        // Native metadata stores donor-world position with forceSaveFlag. That
        // coordinate is runtime scaffolding, not this HK-controlled session's
        // destination. Keep every native loadout flag; allow the game's authored
        // memory entry point to initialize a fresh level/player instead.
        bool forced = SaveManager.Instance.forceSaveFlag.CurrentValue;
        SaveManager.Instance.forceSaveFlag.CurrentValue = false;
        HollowSolsMenuPlugin.Instance.Log("NS_PAIRED_MEMORY_ENTRY ignoredDonorPosition=" + forced);
    }
    internal static void PrepareNewNativeLevel(Action<string> log)
    {
        if (!NativeSaveContext.Enabled || Returning) throw new InvalidOperationException("A new native level requires its active paired loading context.");
        var core = GameCore.Instance;
        if (core == null || AccessTools.Field(typeof(GameCore), "player").GetValue(core) is not Player player || player == null
            || AccessTools.Field(typeof(GameCore), "gameLevel").GetValue(core) is not GameLevel level || level == null) throw new InvalidOperationException("Native level initialization did not finish.");
        // Native menu exit latches this on the persistent core. A normal process
        // launch starts with false; our retained core needs the same level-entry
        // condition after native creation/load, before requesting any teleport.
        var field = AccessTools.Field(typeof(GameCore), "BackToDeveloperMenu");
        bool returning = (bool)field.GetValue(core); field.SetValue(core, false);
        // Title input leaves a binder-owned UI vote on the persistent service.
        // Request the native gameplay state once for this new level. This API
        // revokes only the binder's own vote; real menu/cutscene votes survive.
        var input=ApplicationCore.Instance.playerInput;
        if(input.currentStateType==PlayerInputStateType.UI) {
            input.GotoActionState();
            log("NS_PAIRED_NATIVE_INPUT_ENTRY nativeRequest=True remainingState="+input.currentStateType+" directActionEnable=False");
        }
        log("NS_PAIRED_NATIVE_LEVEL_ENTRY priorMenuExit=" + returning + " nativeLevel=True");
    }
    private static bool InterceptMenuPreparation()
    {
        if (Returning || !NativeSaveContext.Enabled) return true;
        HollowSolsMenuPlugin.Instance!.RequestSessionEnd(false); return false;
    }
    private static bool InterceptReturn(string __0, ref UniTask __result)
    {
        if (Returning || !NativeSaveContext.Enabled || __0 != "TitleScreenMenu") return true;
        HollowSolsMenuPlugin.Instance!.RequestSessionEnd(false); __result = default; return false;
    }
    private static bool VanillaSelection(UIControlGroup __instance)
    {
        var owner = HollowSolsMenuPlugin.Instance;
        // The native title group also owns inactive save-slot descendants.
        // Start() must show it during return, before bridge teardown can finish.
        if (NativeTitleMenuIdentity.IsTitleGroup(__instance)) return true;
        if (__instance.GetComponentsInChildren<SaveSlotUIButton>(true).Length == 0) return true;
        if (Returning || pairedLoadRunning) return false;
        if (owner?.Lifecycle == null || owner.VanillaGameplayAllowed && !NativeSaveContext.ProtectingWrites) return true;
        owner.RequestVanilla(() => __instance.PushShow()); return false;
    }
}
