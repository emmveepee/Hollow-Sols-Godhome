using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using HollowSols.Foundation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace YiGodhome;

internal static class NativePairedGame
{
    private static PlayerData? oldData;
    private static SceneData? oldScenes;
    private static int oldProfile;
    internal static bool Returning;
    private static bool nativeLoadPending;
    private static int introductionInputFrame = -10;
    private static Guid experimentalDeathSession;
    internal static bool WaitingForIntroductionInput => Time.frameCount - introductionInputFrame <= 1
        && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "GG_Entrance_Cutscene";
    internal static void Install()
    {
        var harmony = new Harmony("local.hollowsols.hk.lifecycle");
        harmony.Patch(AccessTools.Method(typeof(TransitionPoint),"TryDoTransition"),prefix:new HarmonyMethod(typeof(HubPresence),nameof(HubPresence.AllowGateTransition)));
        harmony.Patch(AccessTools.Method(typeof(GameManager), "QuitGame"), prefix: new HarmonyMethod(typeof(NativePairedGame), nameof(InterceptQuit)));
        harmony.Patch(AccessTools.Method(typeof(GameManager), "ReturnToMainMenu"), prefix: new HarmonyMethod(typeof(NativePairedGame), nameof(InterceptReturn)));
        harmony.Patch(AccessTools.Method(typeof(UIManager), "UIGoToProfileMenu"), prefix: new HarmonyMethod(typeof(NativePairedGame), nameof(InterceptVanillaSelection)));
        harmony.Patch(AccessTools.Method(typeof(HutongGames.PlayMaker.Actions.BoolTest), "OnEnter"), prefix: new HarmonyMethod(typeof(NativePairedGame), nameof(RoofAccess)));
        harmony.Patch(AccessTools.Method(typeof(PlayerData), "AddGGPlayerDataOverrides"), postfix: new HarmonyMethod(typeof(NativePairedGame), nameof(FullAccess)));
        harmony.Patch(AccessTools.Method(typeof(BossChallengeUI), "Setup"), postfix: new HarmonyMethod(typeof(NativePairedGame), nameof(ChallengeAccess)));
        harmony.Patch(AccessTools.Method(typeof(HutongGames.PlayMaker.Actions.ListenForPromptContinue), "OnUpdate"), prefix: new HarmonyMethod(typeof(NativePairedGame), nameof(ObserveIntroductionInput)));
        harmony.Patch(AccessTools.PropertyGetter(typeof(NextSceneOnInput), "AcceptingInput"), postfix: new HarmonyMethod(typeof(NativePairedGame), nameof(ObserveIntroductionContinue)));
        harmony.Patch(AccessTools.Method(typeof(GameManager), "StartNewGame"), prefix: new HarmonyMethod(typeof(NativePairedGame), nameof(ValidateNativeGameplay)));
        harmony.Patch(AccessTools.Method(typeof(GameManager), "RunContinueGame"), prefix: new HarmonyMethod(typeof(NativePairedGame), nameof(ValidateNativeGameplay)));
        harmony.Patch(AccessTools.Method(typeof(UIManager), "StartNewGame"), prefix: new HarmonyMethod(typeof(NativePairedGame), nameof(AllowNativeMenuCreation)));
        harmony.Patch(AccessTools.Method(typeof(HeroController),"Awake"),postfix:new HarmonyMethod(typeof(BridgeLoadingPresentation),nameof(BridgeLoadingPresentation.SuppressKnight)));
        foreach(var method in new[]{"PlaySelect","PlaySubmit","PlayCancel","PlaySlider"})
            harmony.Patch(AccessTools.Method(typeof(MenuAudioController),method),prefix:new HarmonyMethod(typeof(NativePairedGame),nameof(AllowHostMenuSound)));
        harmony.Patch(AccessTools.Method(typeof(GameManager),"PauseGameToggleByMenu"),prefix:new HarmonyMethod(typeof(NativePairedGame),nameof(BlockPeerMenuResume)));
        harmony.Patch(AccessTools.Method(typeof(GameManager),"PauseGameToggle"),prefix:new HarmonyMethod(typeof(NativePairedGame),nameof(BlockLoadingPause)));
        harmony.Patch(AccessTools.Method(typeof(LoadingSpinner),"Update"),postfix:new HarmonyMethod(typeof(BridgeLoadingPresentation),nameof(BridgeLoadingPresentation.HideNativeLoadingGraphic)));
        harmony.Patch(AccessTools.Method(typeof(StandaloneLoadingSpinner),"LateUpdate"),postfix:new HarmonyMethod(typeof(BridgeLoadingPresentation),nameof(BridgeLoadingPresentation.HideNativeLoadingGraphic)));
        UnityEngine.SceneManagement.SceneManager.sceneLoaded+=BridgeLoadingPresentation.SceneLoaded;
        UnityEngine.SceneManagement.SceneManager.activeSceneChanged+=(oldScene,newScene)=>BridgeLoadingPresentation.SceneEntered(newScene);
    }
    private static bool InterceptQuit(ref IEnumerator __result) { if (HollowSolsMenuPlugin.Instance?.NativeQuitRequested() != false) return true; __result = NoQuit(); return false; }
    private static IEnumerator NoQuit() { yield break; }
    internal static long MutedHostMenuSounds;
    private static bool AllowHostMenuSound() {
        if(!NativeSaveContext.Enabled || !HollowSolsNativePause.Active)return true;
        MutedHostMenuSounds++;return false;
    }
    private static bool BlockPeerMenuResume(ref IEnumerator __result) {
        if(!NativeSaveContext.Enabled || !HollowSolsNativePause.Active)return true;
        // The hidden HK pause menu still receives native Submit/Cancel. Its
        // Resume callback must not become an explicit peer capture cancellation.
        HollowSolsMenuPlugin.Instance?.Log("HK_PEER_MENU_RESUME_IGNORED nativeSubmitOwnedByNineSols=True");
        __result=NoQuit();return false;
    }
    private static bool BlockLoadingPause(ref IEnumerator __result) {
        if(NativeSaveContext.Enabled && NativeMenuPresenter.InterceptInventoryEscape()) {__result=NoQuit();return false;}
        if(NativeSaveContext.Enabled && HollowSolsNativePause.Active) {
            HollowSolsNativePause.RequestClose();__result=NoQuit();return false;
        }
        if(!BridgeLoadingPresentation.Blocking)return true;
        __result=NoQuit();return false;
    }
    private static void ValidateNativeGameplay()
    {
        if (NativeSaveContext.ReadOnlyVanilla) HollowSolsMenuPlugin.Instance?.FixtureGameplayBoundary(NativeSaveContext.Enabled);
        if (NativeSaveContext.Enabled) return;
        var state = HollowSolsMenuPlugin.Instance?.Lifecycle?.State;
        if (NativeSaveContext.ReadOnlyVanilla || state.HasValue && state != BridgeState.Vanilla)
            throw new InvalidOperationException("Native gameplay requires the selected isolated paired context. Return through vanilla selection before playing a vanilla save.");
    }
    private static bool AllowNativeMenuCreation()
    {
        var state = HollowSolsMenuPlugin.Instance?.Lifecycle?.State;
        if (NativeSaveContext.ReadOnlyVanilla || state.HasValue && state != BridgeState.Vanilla) {
            if (NativeSaveContext.ReadOnlyVanilla) HollowSolsMenuPlugin.Instance?.FixtureGameplayBoundary(false);
            return false;
        }
        return true;
    }
    private static void ObserveIntroductionInput(HutongGames.PlayMaker.FsmStateAction __instance)
    {
        if (NativeSaveContext.Enabled && GameManager.instance.gameState.ToString() == "CUTSCENE"
            && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "GG_Entrance_Cutscene") introductionInputFrame = Time.frameCount;
    }
    private static void ObserveIntroductionContinue(NextSceneOnInput __instance, bool __result)
    {
        if (__result && NativeSaveContext.Enabled && __instance.gameObject.scene.name == "GG_Entrance_Cutscene") introductionInputFrame = Time.frameCount;
    }
    internal static void RememberVanilla()
    { var gm = GameManager.instance; oldData = gm.playerData; oldScenes = gm.sceneData; oldProfile = gm.profileID; }
    internal static void RestoreVanilla()
    {
        BridgeLoadingPresentation.End();
        var gm = GameManager.instance;
        if (oldData != null) { gm.playerData = oldData; AccessTools.Field(typeof(PlayerData), "_instance").SetValue(null, oldData); }
        if (oldScenes != null) { gm.sceneData = oldScenes; AccessTools.Field(typeof(SceneData), "_instance")?.SetValue(null, oldScenes); }
        gm.profileID = oldProfile; oldData = null; oldScenes = null;
    }
    internal static void Begin(SavePair pair, bool creating, Action loaded)
    {
        BridgeLoadingPresentation.Begin();
        var gm = GameManager.instance; gm.profileID = NativeSaveContext.SerializerIndex(GameSide.HollowKnight);
        if (creating)
        {
            var data = (PlayerData)Activator.CreateInstance(typeof(PlayerData), true); var scenes = (SceneData)Activator.CreateInstance(typeof(SceneData), true);
            var setter = typeof(GameManager).GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Single(m => m.Name == "SetLoadedGameData" && m.GetParameters()[0].ParameterType == typeof(SaveGameData));
            setter.Invoke(gm, new object[] { new SaveGameData(data, scenes), gm.profileID });
            gm.StartNewGame(pair.Choice.Mode == GameMode.SteelSoul, pair.Choice.Mode == GameMode.Godhome);
            loaded();
        }
        else {
            var token = NativeSaveContext.Session!.Value;
            nativeLoadPending = true;
            gm.LoadGame(gm.profileID, success => {
            if (!NativeSaveContext.Session.HasValue || !NativeSaveContext.Session.Value.Equals(token)
                || Returning || HollowSolsMenuPlugin.Instance?.Lifecycle?.State != BridgeState.Loading) { nativeLoadPending = false; return; }
            if (!success) { nativeLoadPending = false; HollowSolsMenuPlugin.Instance!.GameplayFailed("MODDED_HK_LOAD_FAILED", null); return; }
            if (gm.playerData.permadeathMode == 2) { nativeLoadPending = false; HollowSolsMenuPlugin.Instance!.DefeatedRunLoaded(); return; }
            gm.StartCoroutine(ContinueNativeLoad(gm)); loaded();
        });
        }
    }
    private static IEnumerator ContinueNativeLoad(GameManager gm)
    {
        try {
            var continuation = gm.RunContinueGame();
            while (continuation.MoveNext()) yield return continuation.Current;
        } finally { nativeLoadPending = false; }
    }
    internal static bool Ready {
        get {
            var gm = GameManager.instance; string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (gm == null || gm.gameState.ToString() != "PLAYING" || !gm.HasFinishedEnteringScene || scene == "GG_Entrance_Cutscene" || scene == "Opening_Sequence"
                || scene.Contains("Intro", StringComparison.OrdinalIgnoreCase) || scene.Contains("Cinematic", StringComparison.OrdinalIgnoreCase)) return false;
            var hero = HeroController.instance;
            return hero != null && (NativeSaveContext.Mode == GameMode.Godhome || hero.cState.onGround);
        }
    }
    internal static void Capture(Action<byte[]> done) => GameManager.instance.SaveGame(success => {
        if (!success) { HollowSolsMenuPlugin.Instance!.GameplayFailed("PAIRED_HK_SAVE_FAILED", null); return; }
        if (NativeSaveContext.Session.HasValue) done(NativeSaveContext.Snapshot(NativeSaveContext.Session.Value));
    });
    internal static void Return(Action completed)
    {
        // Combat adapters are already drained/disposed. Release the early
        // scene-entry presentation lease before native menu restoration.
        BridgeLoadingPresentation.End();
        Returning = true; Time.timeScale = 1;
        GameManager.instance.StartCoroutine(ReturnAfterLoad(completed));
    }
    private static IEnumerator ReturnAfterLoad(Action completed)
    {
        double deadline = Time.realtimeSinceStartupAsDouble + BridgeLifecycle.LoadingSeconds;
        while (nativeLoadPending) {
            if (Time.realtimeSinceStartupAsDouble > deadline) { HollowSolsMenuPlugin.Instance!.GameplayFailed("CANCELLED_HK_LOAD_DID_NOT_DRAIN", null); yield break; }
            yield return null;
        }
        yield return GameManager.instance.ReturnToMainMenu(GameManager.ReturnToMainMenuSaveModes.DontSave, success => { Returning = false; completed(); });
    }
    internal static void PrepareReturn() { }
    internal static void ExperimentalDeath(SessionToken token, Action completed)
    {
        if (!NativeSaveContext.Session.HasValue || !NativeSaveContext.Session.Value.Equals(token) || NativeSaveContext.Mode == GameMode.Godhome) throw new InvalidOperationException("Native death requires its active experimental paired context.");
        if (experimentalDeathSession == token.Session) return;
        var hero = HeroController.instance ?? throw new InvalidOperationException("Native HK hero unavailable for defeat.");
        experimentalDeathSession = token.Session;
        hero.StartCoroutine(CompleteExperimentalDeath(hero, token, completed));
    }
    private static IEnumerator CompleteExperimentalDeath(HeroController hero, SessionToken token, Action completed)
    {
        yield return (IEnumerator)AccessTools.Method(typeof(HeroController), "Die").Invoke(hero, null);
        if (NativeSaveContext.Session.HasValue && NativeSaveContext.Session.Value.Equals(token)) completed();
    }
    private static bool InterceptReturn(ref IEnumerator __result)
    {
        if (Returning || !NativeSaveContext.Enabled) return true;
        HollowSolsMenuPlugin.Instance!.RequestSessionEnd(false); __result = WaitForReturn(); return false;
    }
    private static IEnumerator WaitForReturn() { while (NativeSaveContext.Enabled) yield return null; }
    private static bool InterceptVanillaSelection()
    {
        var owner = HollowSolsMenuPlugin.Instance;
        if (Returning || owner?.Lifecycle == null || owner.Lifecycle.State == BridgeState.Vanilla) return true;
        owner.RequestVanilla(() => UIManager.instance.UIGoToProfileMenu()); return false;
    }
    private static bool RoofAccess(HutongGames.PlayMaker.Actions.BoolTest __instance)
    {
        var owner=__instance.Owner;
        if(owner==null || !GodhomeRoofAccess.Applies(NativeSaveContext.Enabled,NativeSaveContext.FullAccess,
            NativeSaveContext.Mode==GameMode.Godhome,owner.scene.name,owner.name,__instance.Fsm.Name,
            __instance.State.Name,__instance.boolVariable.Name))return true;
        // Native Activated configures the roof exits and activates the hidden path.
        // Do not enter the hit/Player Data? branch or write Activated/clear flags.
        __instance.Fsm.Event(__instance.isTrue);
        __instance.Finish();
        HollowSolsMenuPlugin.Instance?.Log("HK_GODHOME_ROOF_ACCESS nativeActivatedBranch=True progressionWrites=False");
        return false;
    }
    private static void FullAccess(PlayerData __instance)
    {
        if (!NativeSaveContext.Enabled || !NativeSaveContext.FullAccess || !__instance.bossRushMode) return;
        foreach (var field in typeof(PlayerData).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
        {
            if (field.FieldType == typeof(BossStatue.Completion)) { var value = (BossStatue.Completion)field.GetValue(__instance); value.isUnlocked = true; field.SetValue(__instance, value); }
            if (field.FieldType == typeof(BossSequenceDoor.Completion)) { var value = (BossSequenceDoor.Completion)field.GetValue(__instance); value.canUnlock = value.unlocked = true; field.SetValue(__instance, value); }
        }
        __instance.bossDoorCageUnlocked = __instance.finalBossDoorUnlocked = true;
        __instance.unlockedBossScenes ??= new System.Collections.Generic.List<string>();
        if (!__instance.unlockedBossScenes.Contains(GodseekerProfile.RadianceSceneUnlock)) __instance.unlockedBossScenes.Add(GodseekerProfile.RadianceSceneUnlock);
        // No completion, binding, trophy, reward, achievement or respawn writes.
    }
    private static void ChallengeAccess(BossChallengeUI __instance)
    {
        if (!NativeSaveContext.Enabled || !NativeSaveContext.FullAccess) return;
        __instance.tier1Button.SetupNavigation(true, __instance.tier3Button, __instance.tier2Button);
        __instance.tier2Button.SetupNavigation(true, __instance.tier1Button, __instance.tier3Button);
        __instance.tier3Button.SetupNavigation(true, __instance.tier2Button, __instance.tier1Button);
    }
}
