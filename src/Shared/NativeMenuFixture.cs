using System;
using System.IO;
using System.Globalization;
using System.Linq;
using HollowSols.Foundation;
using UnityEngine;

namespace YiGodhome;

public sealed partial class HollowSolsMenuPlugin
{
    private Guid fixtureId;
    internal bool FixtureCommandsAllowed => fixtureId != Guid.Empty && fixtureStage >= 0 && NativeSaveContext.ReadOnlyVanilla;
    private bool ControlsFixture=>fixtureScenario=="controls" || fixtureScenario=="controls-controller";
    internal bool FixtureInitialImageGap=>FixtureCommandsAllowed && fixtureScenario=="reload";
    internal bool FixtureControllerControls=>FixtureCommandsAllowed && fixtureScenario=="controls-controller";
    internal bool FixtureRadianceIdle=>FixtureCommandsAllowed && (fixtureScenario=="combat-RadianceIdlePerf" || fixtureScenario=="combat-RadianceRainVisual");
    internal bool FixtureRadianceHoming=>FixtureCommandsAllowed && fixtureScenario=="combat-RadianceArrowPerf";
    internal bool FixtureRosterEndurance=>FixtureCommandsAllowed && fixtureScenario.StartsWith("combat-Endurance-",StringComparison.Ordinal);
    internal bool FixtureEnduranceAscended=>FixtureRosterEndurance && EnduranceWorkload=="VengeflyKingAscended";
    internal bool FixtureRadiancePerformance=>FixtureCommandsAllowed && (fixtureScenario=="combat-RadianceRainPerf" || fixtureScenario=="combat-RadianceArrowPerf" || fixtureScenario=="combat-RadianceIdlePerf" || fixtureScenario=="combat-RadianceRainVisual");
    internal bool FixtureActorAsync=>FixtureCommandsAllowed && (fixtureScenario=="capture-stress" || fixtureScenario=="combat-RadianceFinish" || FixtureRadiancePerformance || FixtureRosterEndurance);
    internal bool FixtureGestureScreenshots=>FixtureCommandsAllowed && !ControlsFixture;
    private string fixtureFolder = "", fixtureScenario = "";
    private int fixtureStage, fixtureSeconds;
    private double fixtureAt, fixtureReadyAt = -1, fixtureDelay;
    private bool fixtureCrashObserved;
    private bool fixtureSawActive;
    private long fixtureQuitGeneration;
    private int fixtureEngineErrors;
    private int fixturePass = 0;
#if HOLLOW_KNIGHT
    private void FixtureLoadingReactivation()
    {
        if(!FixtureCommandsAllowed)return;
        var token=activeToken;var connection=controller!.Lifecycle.Connection;var link=transport;
        ActivateBridge();ActivateBridge();
        if(!Nullable.Equals(activeToken,token) || controller.Lifecycle.Connection!=connection || !ReferenceEquals(link,transport) || controller.Lifecycle.State!=BridgeState.Loading)
            throw new InvalidOperationException("Duplicate bridge callback replaced an active loading session.");
        menu!.FixtureCheckLoadingHandoff();
        StartCoroutine(FixtureLoadingFrames());
        Log("HK_LOADING_DUPLICATE_ACTIVATION_IGNORED sameSession=True sameTransport=True");
    }
    private System.Collections.IEnumerator FixtureLoadingFrames()
    {
        for(int frame=0;frame<6;frame++) {
            yield return new WaitForEndOfFrame();
            menu?.FixtureCheckLoadingHandoff();
            if(frame==0)menu?.FixturePixels(Path.Combine(fixtureFolder,"save-load-handoff.png"));
        }
        Log("HK_LOADING_TITLE_GUARD_PASS frames=6 nativeFade=True");
    }
    internal void FixtureLoadingTrace(string detail){if(FixtureCommandsAllowed)Log("HK_BRIDGING_GATE "+detail);}
    internal void FixtureLoadingPresentation(string phase,int room) {
        if(!FixtureCommandsAllowed)return;
        if(!HudGlobalHide.IsHidden)throw new InvalidOperationException("HK HUD leaked into paired entry.");
        var knight=HeroController.instance?.GetComponent<MeshRenderer>();
        if(knight!=null && !knight.forceRenderingOff)throw new InvalidOperationException("Knight artwork leaked into paired entry.");
        if(phase=="reveal" && VisualPresenter.ActorRenderer?.enabled!=true)throw new InvalidOperationException("Loading faded before native Yi pixels.");
        if(BridgeLoadingPresentation.Blocking && VisualPresenter.RootRenderer?.enabled==true)throw new InvalidOperationException("Root artwork leaked through the initial loading caption.");
        ScreenCapture.CaptureScreenshot(Path.Combine(fixtureFolder,"bridging-"+room+"-"+phase+".png"));
        Log("HK_BRIDGING_PRESENTATION phase="+phase+" room="+room+" nativeHudHidden=True knightHidden=True yiVisible="+(VisualPresenter.ActorRenderer?.enabled==true)+" blocksInput="+BridgeLoadingPresentation.Blocking);
    }
    internal void FixtureGameplayBoundary(bool scoped) => Log("NATIVE_MENU_FIXTURE_GAMEPLAY_BOUNDARY scoped=" + scoped + " state=" + controller?.Lifecycle.State + " trace=" + Environment.StackTrace);
    private double fixtureNextLoadProbe;
    private double fixtureIntroInputAt;
    private PlayerData? fixtureGodhomeBaseline;
    private Guid fixtureConnection;
    private int fixtureControlStage,fixtureControlsPass;
    private bool fixtureHiddenCancel;
    private double fixtureControlAt;
    private float fixtureControlX;
    private float fixtureControlY;
#else
    private double fixturePeerActiveAt = -1;
    private bool fixturePeerExit;
    private Guid fixtureEquipmentSession;
    private bool fixtureEquipmentExpected, fixtureEquipmentOriginal;
    private int fixtureEquipmentPass;
    private bool fixtureNativeDeathRequested;
    private Guid fixtureAudioSession;
    private double fixtureAudioActiveAt;
    private double fixtureAudioReturnAt = -1;
    private double fixtureControlsAt = -1;
    private bool fixtureControlsNavigation,fixtureControlsResumed,fixtureControlsPreFocused;
    private bool fixtureBindingStarted,fixtureBindingHeld,fixtureReopened,fixtureBindingAccepted;
    private double fixtureBindingAt;
    private GameObject? fixtureControlsSelection;
    private bool fixtureSelectionCleared;
    private bool fixtureRootControlsSeen;
    private int fixtureLoadoutStage,fixturePostControlsRoot;
    private double fixtureRootAt;
    private byte[] fixtureInputBindings=Array.Empty<byte>();
    private double fixtureLoadoutAt;
    private bool fixtureAudioReturnChecked;
    private double fixtureInputWaitAt;
    private string fixtureBowOriginal = "", fixtureBowExpected = "", fixtureTalismanOriginal = "", fixtureTalismanExpected = "";
#endif
#if !HOLLOW_KNIGHT
    private int rootChoiceIndex,rootChoicePhase,rootChoiceBefore;
    private double rootChoiceAt;
    private bool FixtureRootChoices() {
        var root=GameCore.Instance.savePanelUiController;
        var buttons=new[]{root.StatusButton,root.jadeButton,root.SkillTreeButton,root.RestoreButton};
        var pages=new[]{PlayerInfoPanelType.Status,PlayerInfoPanelType.Jade,PlayerInfoPanelType.SkillTree};
        if(rootChoiceIndex==4)return true;
        if(rootChoicePhase==0) {
            if(NativeMenu.InventoryOpen || !NativeMenu.NavigationInteractive)return false;
            var button=buttons[rootChoiceIndex];
            if(!button.gameObject.activeInHierarchy)throw new InvalidOperationException("Visible Root choice is inactive: "+button.name);
            button.GetComponent<UnityEngine.UI.Selectable>().Select();
            rootChoiceBefore=rootChoiceIndex==3?NativeMenuBoundary.RootRestoreRequests:NativeMenuBoundary.RootPageRequests;
            Log("NS_ROOT_CHOICE_SELECTED button="+button.name+" label="+string.Join(";",button.GetComponentsInChildren<TMPro.TMP_Text>().Select(t=>t.text)));
            rootChoicePhase=3;rootChoiceAt=Now;return false;
        }
        if(rootChoicePhase==3) {
            if(Now-rootChoiceAt<.25)return false;
            ScreenCapture.CaptureScreenshot(Path.Combine(fixtureFolder,"root-choice-"+rootChoiceIndex+".png"));
            buttons[rootChoiceIndex].Submit();rootChoicePhase=1;rootChoiceAt=Now;return false;
        }
        if(rootChoicePhase==1) {
            bool invoked=rootChoiceIndex==3?NativeMenuBoundary.RootRestoreRequests>rootChoiceBefore
                :NativeMenuBoundary.RootPageRequests>rootChoiceBefore && NativeMenuBoundary.LastRootPage==pages[rootChoiceIndex];
            if(!invoked){if(Now-rootChoiceAt>3)throw new InvalidOperationException("Root choice invoked a wrong/missing native callback: "+buttons[rootChoiceIndex].name);return false;}
            if(rootChoiceIndex<3) {
                if(!NativeMenu.InventoryOpen || !NativeMenu.NavigationInteractive)return false;
                NativeMenu.Toggle(Log);
            }
            Log("NS_ROOT_CHOICE_CALLBACK_PASS button="+buttons[rootChoiceIndex].name+" native="+(rootChoiceIndex==3?"Restore":pages[rootChoiceIndex].ToString()));
            rootChoicePhase=2;rootChoiceAt=Now;return false;
        }
        if(rootChoicePhase==2 && Now-rootChoiceAt>.5) {
            if(!NativeRestPoint.Active && Player.i.CurrentStateType==PlayerStateType.Normal){NativeRestPoint.Interact();return false;}
            if(!NativeMenu.Open || NativeMenu.InventoryOpen || !NativeMenu.NavigationInteractive)return false;
            rootChoiceIndex++;rootChoicePhase=0;
        }
        if(Now-rootChoiceAt>6)throw new InvalidOperationException("Root choice did not return natively: "+buttons[rootChoiceIndex].name);
        return false;
    }
    internal void FixtureBowDensitySatisfied() {
        if(FixtureCommandsAllowed && fixtureScenario=="reload")File.WriteAllText(Path.Combine(fixtureFolder,"bow-density.receipt"),fixtureId.ToString());
    }
    internal bool FixtureCaptureStressRequested {
        get {
            if(!FixtureCommandsAllowed || fixtureScenario!="capture-stress")return false;
            string request=Path.Combine(Path.GetDirectoryName(fixtureFolder)!,"HollowKnight","capture-stress.request");
            return File.Exists(request) && File.ReadAllText(request)==fixtureId.ToString();
        }
    }
    internal void FixtureCaptureStressComplete() {
        if(!FixtureCaptureStressRequested)throw new InvalidOperationException("Capture stress lost its scoped authorization.");
        File.WriteAllText(Path.Combine(fixtureFolder,"capture-stress.receipt"),fixtureId.ToString());
    }
    private void RestoreFixtureBindings() {
        if(radianceIdleImmunity!=null)radianceIdleImmunity.Release();
        if(fixtureInputBindings.Length==0)return;
        var original=fixtureInputBindings;fixtureInputBindings=Array.Empty<byte>();
        ApplicationCore.Instance.playerInput.gameplayActions.LoadData(original);
        Log("NS_FIXTURE_BINDINGS_RESTORED runtimeOnly=True");
    }
#endif
    partial void InitializeMenuFixture()
    {
        if (!YiGodhome.Bridge.BuildFeatures.DevelopmentTools) {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HOLLOWSOLS_MENU_FIXTURE"))) throw new InvalidOperationException("Public builds do not accept automated menu fixtures.");
            return;
        }
        if (!Guid.TryParse(Environment.GetEnvironmentVariable("HOLLOWSOLS_MENU_FIXTURE"), out fixtureId)) return;
        fixtureFolder = Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!;
        var fields = File.ReadAllLines(Path.Combine(fixtureFolder, "native-menu.fixture"));
        fixtureScenario = fields[2]; fixtureDelay = double.Parse(fields[3], CultureInfo.InvariantCulture);
        int.TryParse(Environment.GetEnvironmentVariable("YIGODHOME_SMOKE_SECONDS"), out fixtureSeconds);
        if (fields[0] != fixtureId.ToString() || fields[4] != Side.ToString() || fixtureSeconds < 10 || fixtureSeconds > (fixtureScenario=="combat-Endurance-Sweep"?10980:600)
            || fixtureDelay < 0 || fixtureDelay > 20 || !ValidCombatFixture(fixtureScenario) && !new[] { "cancel-loading-late", "cancel-loading", "native-ui", "ui-inspect", "ui-flows", "menus", "godhome-yes", "godhome-no", "classic", "steel", "classic-death", "steel-death", "reload", "capture-stress", "controls", "controls-controller", "loadout", "switch", "vanilla-hk", "vanilla-ns", "vanilla-active-hk", "vanilla-active-ns", "crash-hk", "crash-ns", "quit-hk", "quit-ns", "delete", "recovery", "unrecoverable" }.Contains(fixtureScenario))
            throw new InvalidOperationException("Invalid bounded menu fixture.");
        InitializeEndurancePlan();
        NativeSaveContext.ReadOnlyVanilla = true;
        Application.runInBackground = true;


        Application.logMessageReceived += FixtureNativeLog;
        menu!.FixtureDiagnostics();
#if HOLLOW_KNIGHT
        new HarmonyLib.Harmony("local.hollowsols.fixture.intro-input").Patch(HarmonyLib.AccessTools.Method(typeof(HutongGames.PlayMaker.Actions.ListenForPromptContinue), "OnUpdate"), postfix: new HarmonyLib.HarmonyMethod(typeof(HollowSolsMenuPlugin), nameof(FixtureIntroContinue)));
        new HarmonyLib.Harmony("local.hollowsols.fixture.intro-input").Patch(HarmonyLib.AccessTools.PropertyGetter(typeof(NextSceneOnInput), "ButtonPressed"), postfix: new HarmonyLib.HarmonyMethod(typeof(HollowSolsMenuPlugin), nameof(FixtureIntroButton)));
#endif
        Log("NATIVE_MENU_FIXTURE_AUTHORIZED id=" + fixtureId + " scenario=" + fixtureScenario + " seconds=" + fixtureSeconds + " vanillaWrites=False");
    }
    private void FixtureNativeLog(string message, string trace, LogType type)
    {
        if ((type == LogType.Exception || type == LogType.Error) && fixtureEngineErrors++ < 64) Log("NATIVE_MENU_FIXTURE_ENGINE_ERROR " + message + " " + trace);
    }
#if HOLLOW_KNIGHT
    // Disposable fixture only: take the same authored dialogue-button event after
    // a full reading interval. Never call SkipCutscene or change animation timing.
    private static void FixtureIntroContinue(HutongGames.PlayMaker.Actions.ListenForPromptContinue __instance)
    {
        if (__instance.ContinuePressed != null && FixtureIntroPress(__instance)) __instance.Fsm.Event(__instance.ContinuePressed);
    }
    private static bool FixtureIntroPress(HutongGames.PlayMaker.FsmStateAction action)
    {
        var owner = Instance;
        if (owner == null || owner.fixtureId == Guid.Empty || owner.fixtureStage != 4
            || UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "GG_Entrance_Cutscene" || owner.Now < owner.fixtureIntroInputAt
            || owner.Now - owner.fixtureAt < 20) return false;
        owner.fixtureIntroInputAt = owner.Now + 15;
        owner.Log("NATIVE_MENU_FIXTURE_INTRO_INPUT fsm=" + action.Fsm.Name + " state=" + action.Fsm.ActiveStateName + " listener=" + action.GetType().Name);
        return true;
    }
    private static void FixtureIntroButton(NextSceneOnInput __instance, ref bool __result)
    {
        var owner = Instance;
        if (__result || owner == null || owner.fixtureId == Guid.Empty || owner.fixtureStage != 4
            || (__instance.gameObject.scene.name != "GG_Entrance_Cutscene" && !((owner.fixtureScenario.StartsWith("classic", StringComparison.Ordinal) || owner.fixtureScenario.StartsWith("steel", StringComparison.Ordinal)) && __instance.gameObject.scene.name.Contains("Intro", StringComparison.OrdinalIgnoreCase))) || !__instance.AcceptingInput
            || owner.Now < owner.fixtureIntroInputAt || owner.Now - owner.fixtureAt < 20) return;
        owner.fixtureIntroInputAt = owner.Now + 15; __result = true;
        owner.Log("NATIVE_MENU_FIXTURE_INTRO_INPUT listener=NextSceneOnInput accepting=True simulatedButton=True");
    }
#endif
#if HOLLOW_KNIGHT
    private double lateCancelHoldAt;
    private bool FixtureHoldLateCancellation()
    {
        if (!FixtureCommandsAllowed || fixtureScenario != "cancel-loading-late" || fixtureStage != 2) return false;
        if (lateCancelHoldAt == 0) {
            lateCancelHoldAt = Now;
            File.WriteAllText(Path.Combine(fixtureFolder,"late-cancel-ready.receipt"),fixtureId.ToString());
            Log("CANCEL_LOADING_LATE_BOUNDARY hkSceneReady=True nativePreparationNotSent=True");
        }
        if (Now-lateCancelHoldAt>5) throw new TimeoutException("Late native cancellation did not arrive at its bounded gate.");
        return true;
    }
#endif

    private void UpdateCancelledLoadFixture()
    {
        if (fixtureStage == 99) {
            string peerDone = Path.Combine(Path.GetDirectoryName(fixtureFolder)!, Side == GameSide.HollowKnight ? "NineSols" : "HollowKnight", "menu-fixture.complete");
            if (File.Exists(peerDone)) { finalizedQuit=true;Application.Quit(); }
            return;
        }
        if (Now > fixtureSeconds - 5) throw new TimeoutException("Cancelled-load menu/reconnect did not complete.");
        string peerFolder = Path.Combine(Path.GetDirectoryName(fixtureFolder)!, Side == GameSide.HollowKnight ? "NineSols" : "HollowKnight");
#if HOLLOW_KNIGHT
        if (fixtureStage == 0 && menu!.FixtureReady && Now > 5) { menu.FixtureSubmitBridge(); fixtureStage = 1; }
        if (fixtureStage == 1 && controller!.Lifecycle.State == BridgeState.SaveSelection) {
            menu!.FixtureSelectSlot(store!.ListSlots().First(s => s.Pair != null)); fixtureStage = 2;
        }
#else
        if (fixtureStage == 0 && controller!.Lifecycle.State == BridgeState.Loading
            && (fixtureScenario != "cancel-loading-late" || File.Exists(Path.Combine(peerFolder,"late-cancel-ready.receipt")))) {
            menu!.FixtureBack(); fixtureStage = 2;
            Log("CANCEL_LOADING_NATIVE_BACK state=" + controller.Lifecycle.State);
        }
#endif
        if (fixtureStage == 2 && VanillaGameplayAllowed && !NativeSaveContext.Enabled && menu!.NativeReturnReady && menu.FixtureReady) {
            menu.FixtureEvidence(); menu.FixturePixels(Path.Combine(fixtureFolder,"cancelled-title.png"));
            File.WriteAllText(Path.Combine(fixtureFolder,"cancelled-title.receipt"),fixtureId.ToString());
            fixtureStage = 3; fixtureAt = Now;
            Log("CANCEL_LOADING_NATIVE_TITLE_READY input=True isolatedContextReleased=True");
        }
#if HOLLOW_KNIGHT
        if (fixtureStage == 3 && Now-fixtureAt>1 && File.Exists(Path.Combine(peerFolder,"cancelled-title.receipt"))) {
            menu!.FixtureSubmitBridge();fixtureStage = 4;
        }
        if (fixtureStage == 4 && controller!.Lifecycle.State == BridgeState.SaveSelection && File.Exists(Path.Combine(peerFolder,"reconnected.receipt"))) {
            if (fixtureScenario == "cancel-loading-late") {
                menu!.FixtureSelectSlot(store!.ListSlots().First(s => s.Pair != null)); fixtureStage = 6;fixtureAt = Now;
            } else { BeginDisconnect();fixtureStage = 5; }
        }
#else
        if (fixtureStage == 3 && controller!.Lifecycle.State == BridgeState.Connected) {
            File.WriteAllText(Path.Combine(fixtureFolder,"reconnected.receipt"),fixtureId.ToString());fixtureStage = 5;
        }
#endif
#if HOLLOW_KNIGHT
        if (fixtureStage == 6 && controller!.Lifecycle.State == BridgeState.Active && gameplay?.NativePairReady == true && !BridgeLoadingPresentation.Blocking) {
            if (Now-fixtureAt>8) {
                fixturePass++;
                string repeat = Path.Combine(Path.GetDirectoryName(fixtureFolder)!,"reload-again.request");
                if(fixturePass==1)File.WriteAllText(repeat,fixtureId.ToString());else File.Delete(repeat);
                Log("CANCEL_LOADING_RELOAD_GAMEPLAY_READY pairedActive=True loadingPresentationClosed=True pass="+fixturePass);
                menu!.FixturePixels(Path.Combine(fixtureFolder,"reloaded-gameplay-"+fixturePass+".png"));
                RequestSessionEnd(true);fixtureStage = 7;
            }
        }
        if (fixtureStage == 7 && VanillaGameplayAllowed && menu!.FixtureReady) {
            if(fixturePass<2){menu.FixtureSubmitBridge();fixtureStage=4;}else fixtureStage=5;
        }
#endif
        if (fixtureStage == 5 && VanillaGameplayAllowed && menu!.FixtureReady
            && !File.Exists(Path.Combine(Path.GetDirectoryName(fixtureFolder)!,"reload-again.request"))) {
            menu.FixturePixels(Path.Combine(fixtureFolder,"reconnected-title.png"));
            File.WriteAllText(Path.Combine(fixtureFolder,"menu-fixture.complete"),fixtureId.ToString());
            fixtureStage = 99; Log("NATIVE_MENU_FIXTURE_COMPLETE cancelledLoad=True nativeTitles=True reconnected=True vanillaWrites=False");
        }
        if (fixtureStage == 99 && File.Exists(Path.Combine(peerFolder,"menu-fixture.complete"))) { finalizedQuit=true;Application.Quit(); }
    }

    partial void UpdateMenuFixture()
    {
        if (fixtureId == Guid.Empty) return;
        Application.runInBackground = true;
        if (controller?.Lifecycle.State == BridgeState.Active) { fixtureSawActive = true; if (fixtureScenario.StartsWith("quit-", StringComparison.Ordinal) && fixtureQuitGeneration == 0) fixtureQuitGeneration = generation; }
        if (fixtureScenario.StartsWith("quit-", StringComparison.Ordinal) && fixtureSawActive && activeToken == null && !NativeSaveContext.Enabled && controller?.Lifecycle.State == BridgeState.Vanilla && fixtureStage != 99) {
            string expectedScene = Side == GameSide.HollowKnight ? "Menu_Title" : "TitleScreenMenu";
            bool source = (fixtureScenario == "quit-hk") == (Side == GameSide.HollowKnight);
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != expectedScene || controller.Lifecycle.Connection != Guid.Empty) throw new InvalidOperationException("Graceful quit did not restore native menu and clear authorization.");
            if (!source && !menu!.FixtureReady) return; // Native modal fade still owns input for this frame.
#if HOLLOW_KNIGHT
            if (!finalSaveDone || store!.ListSlots()[0].Generation <= fixtureQuitGeneration) throw new InvalidOperationException("Graceful quit did not commit its final paired checkpoint.");
#endif
            if ((fixtureScenario == "quit-hk") == (Side == GameSide.HollowKnight) && !requestedQuit) throw new InvalidOperationException("Graceful quit bypassed the production quit guard.");
            Log("NATIVE_MENU_FIXTURE_COMPLETE gracefulQuit=True finalSave=True nativeMenu=True connectionReset=True"); File.WriteAllText(Path.Combine(fixtureFolder, "menu-fixture.complete"), fixtureId.ToString()); fixtureStage = 99;
            if (!requestedQuit) { finalizedQuit = true; Application.Quit(); } return;
        }
        if (controller?.Lifecycle.State == BridgeState.Failed || displayedFailure != null) {
            string trigger = Path.Combine(Path.GetDirectoryName(fixtureFolder)!, "fixture-crash.trigger");
            if (fixtureScenario.StartsWith("crash-", StringComparison.Ordinal) && File.Exists(trigger) && File.ReadAllText(trigger) == fixtureId.ToString()) fixtureCrashObserved = true;
            else { Log("NATIVE_MENU_FIXTURE_FAILED stage=" + fixtureStage); Application.Quit(); return; }
        }
        if (fixtureScenario == "cancel-loading" || fixtureScenario == "cancel-loading-late") { UpdateCancelledLoadFixture(); return; }
        if (fixtureCrashObserved && activeToken == null && !NativeSaveContext.Enabled && controller?.Lifecycle.State == BridgeState.Vanilla) {
            if (fixtureStage < 76) { fixtureStage = 76; fixtureAt = Now; }
            if (fixtureStage == 76 && Now - fixtureAt > 3) {
                string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (scene != (Side == GameSide.HollowKnight ? "Menu_Title" : "TitleScreenMenu") || controller.Lifecycle.Connection != Guid.Empty) throw new InvalidOperationException("Crash survivor did not return to its native menu with authorization reset.");
#if !HOLLOW_KNIGHT
                if (!NativePairedGame.LoadingClosed) throw new InvalidOperationException("Crash survivor retained native loading artwork.");
#endif
                menu!.FixturePixels(Path.Combine(fixtureFolder, "crash-survivor-menu.png")); fixtureStage = 77; fixtureAt = Now;
            }
        }
        if (fixtureStage == 77 && Now - fixtureAt > 1) { Log("NATIVE_MENU_FIXTURE_COMPLETE crashSurvivor=True nativeMenu=True committedDataPreserved=True"); File.WriteAllText(Path.Combine(fixtureFolder, "menu-fixture.complete"), fixtureId.ToString()); fixtureStage = 99; Application.Quit(); return; }
#if HOLLOW_KNIGHT
        if (fixtureScenario == "native-ui") {
            if (controller!.Lifecycle.State != BridgeState.Vanilla || NativeSaveContext.Enabled) throw new InvalidOperationException("Native UI comparison must remain outside gameplay.");
            if (fixtureStage == 0 && menu!.FixtureReady && Now > 5) {
                menu.FixturePixels(Path.Combine(fixtureFolder, "vanilla-main.png")); UIManager.instance.UIGoToProfileMenu(); fixtureStage = 1; fixtureAt = Now;
            } else if (fixtureStage == 1 && Now - fixtureAt > 4) {
                menu!.FixturePixels(Path.Combine(fixtureFolder, "vanilla-save-selection.png")); UIManager.instance.UIGoToPlayModeMenu(); fixtureStage = 2; fixtureAt = Now;
            } else if (fixtureStage == 2 && Now - fixtureAt > 4) {
                menu!.FixturePixels(Path.Combine(fixtureFolder, "vanilla-mode-selection.png"));
                var ui = UIManager.instance;
                var mode = (MenuScreen)HarmonyLib.AccessTools.Field(typeof(UIManager), "playModeMenuScreen").GetValue(ui); mode.gameObject.SetActive(false);
                ui.UIShowReturnMenuPrompt(); fixtureStage = 3; fixtureAt = Now;
            } else if (fixtureStage == 3 && Now - fixtureAt > 4) {
                var prompt = (MenuScreen)HarmonyLib.AccessTools.Field(typeof(UIManager), "returnMainMenuPrompt").GetValue(UIManager.instance);
                foreach (var rect in prompt.GetComponentsInChildren<RectTransform>(true).Where(r => r.GetComponent<UnityEngine.UI.Text>() != null || r.GetComponent<UnityEngine.UI.MenuSelectable>() != null))
                    Log("HK_NATIVE_PROMPT_LAYOUT name=" + rect.name + " parent=" + rect.parent.name + " position=" + rect.anchoredPosition + " size=" + rect.sizeDelta + " pivot=" + rect.pivot);
                menu!.FixturePixels(Path.Combine(fixtureFolder, "vanilla-yes-no.png")); fixtureStage = 4; fixtureAt = Now;
            } else if (fixtureStage == 4 && Now - fixtureAt > 2) {
                File.WriteAllText(Path.Combine(fixtureFolder, "menu-fixture.complete"), fixtureId.ToString()); Log("NATIVE_MENU_FIXTURE_COMPLETE nativeUiComparison=True gameplayLoaded=False"); Application.Quit();
            }
            if (Now > fixtureSeconds) Application.Quit(); return;
        }
#endif
#if !HOLLOW_KNIGHT
        if(CombatFixture && controller!.Lifecycle.State==BridgeState.Active)UpdateCombatFixture();
        if(fixtureScenario=="loadout" && controller!.Lifecycle.State==BridgeState.Loading && gameplay?.FixturePairedPlayer is Player loadingYi && Now>=fixtureInputWaitAt) {
            fixtureInputWaitAt=Now+2;
            var binder=ApplicationCore.Instance.playerInput;
            var votes=(System.Collections.Generic.List<RuntimeConditionVote>)HarmonyLib.AccessTools.Field(typeof(PlayerInputBinder),"conditionVoteList").GetValue(binder);
            Log("NS_ROOT_TEST_LOADING_GATE player="+loadingYi.CurrentStateType+" actions="+loadingYi.actions.Enabled+" inputState="+binder.currentStateType
                +" inputFlags="+binder.stateFlag+" pause="+NativeMenu.PauseOpen+" inventory="+NativeMenu.InventoryOpen
                +" votes="+string.Join(",",votes.Take(12).Select(v=>v.Result.ToString()))
                +" uiVoters="+string.Join(";",((System.Collections.IDictionary)HarmonyLib.AccessTools.Field(typeof(RuntimeConditionVote),"votes").GetValue(votes[1])).Keys.Cast<UnityEngine.Object>().Take(12).Select(v=>v==null?"destroyed":v.name+":"+v.GetType().Name)));
        }
        if(fixtureScenario=="loadout" && controller!.Lifecycle.State==BridgeState.Active && gameplay?.NativePairReady==true
            && !NativeRoomEntry.Active && Now-fixtureAt>10
            && File.Exists(Path.Combine(Path.GetDirectoryName(fixtureFolder)!,"HollowKnight","paired-active.receipt"))
            && File.ReadAllText(Path.Combine(Path.GetDirectoryName(fixtureFolder)!,"HollowKnight","paired-active.receipt"))==fixtureId.ToString()) {
            if(fixtureLoadoutStage==0) {
                NativeMenu.Toggle(Log);fixtureLoadoutStage=1;fixtureLoadoutAt=Now;
            } else if(fixtureLoadoutStage==1 && Now-fixtureLoadoutAt>1) {
                NativeMenuBoundary.TraceConfiguration(Log);NativeMenu.Toggle(Log);
                gameplay!.FixturePairedRootPlacement();fixtureLoadoutStage=2;fixtureLoadoutAt=Now;
            } else if(fixtureLoadoutStage==2 && Now-fixtureLoadoutAt>1) {
                NativeRestPoint.Interact();
                if(!NativeRestPoint.Active)throw new InvalidOperationException("Configured native root did not admit rest.");
                fixtureLoadoutStage=3;fixtureLoadoutAt=Now;
            } else if(fixtureLoadoutStage==3 && Now-fixtureLoadoutAt>3) {
                NativeMenu.Toggle(Log);fixtureLoadoutStage=4;fixtureLoadoutAt=Now;
            } else if(fixtureLoadoutStage==4 && Now-fixtureLoadoutAt>2) {
                if(!NativeMenu.InventoryOpen || !GameCore.Instance.savePanelUiController.ToMenu)
                    throw new InvalidOperationException("Native Root Status failed to reach the retained gameplay menu.");
                Log("NS_ROOT_STATUS_OWNER_PASS retainedMenu=True nativeToMenu=True bridgePreserved=True");
                NativeMenuBoundary.TraceConfiguration(Log);
                Log("NS_LOADOUT_FIXTURE_ROOT nativeRoot="+gameplay!.FixturePairedPlayer!.IsSitAtSavePoint+" bridgeRoot="+NativeRestPoint.Active);
                ScreenCapture.CaptureScreenshot(Path.Combine(fixtureFolder,"native-root-loadout.png"));
                NativeMenu.Toggle(Log);fixtureLoadoutStage=5;fixtureLoadoutAt=Now;
            } else if(fixtureLoadoutStage==5 && Now-fixtureLoadoutAt>1) {
                File.WriteAllText(Path.Combine(fixtureFolder,"loadout-probe.complete"),fixtureId.ToString());fixtureLoadoutStage=6;
            }
        }
        if(ControlsFixture && !fixtureControlsPreFocused && controller!.Lifecycle.State==BridgeState.Active
            && File.Exists(Path.Combine(Path.GetDirectoryName(fixtureFolder)!,"HollowKnight","controls-prefocus.request"))) {
            NativePairedGame.TryFocusHollowKnight(Log);fixtureControlsPreFocused=true;
            File.WriteAllText(Path.Combine(fixtureFolder,"controls-prefocus.receipt"),fixtureId.ToString());
        }
        if(ControlsFixture && !fixtureReopened && File.Exists(Path.Combine(Path.GetDirectoryName(fixtureFolder)!,"HollowKnight","controls-cycle2.request"))) {
            fixtureReopened=true;fixtureControlsAt=-1;fixtureControlsSelection=null;
            fixtureControlsNavigation=fixtureBindingStarted=fixtureBindingHeld=fixtureBindingAccepted=false;
        }
        if (ControlsFixture && NativeMenu.ControlsOpen) {
            if(NativeSaveContext.Mode==GameMode.Godhome && GameCore.Instance.playerGameData!=null) {
                var data=GameCore.Instance.playerGameData;
                if(data.CurrentExp!=0 || data.CurrentExpPercent!=0)throw new InvalidOperationException("Godhome native EXP progress is not locked to zero.");
                if(fixtureControlsAt<0)Log("NS_GODHOME_EXP_ZERO nativeTotal=0 nativeProgress=0 combatLevel="+data.CurrentLevel);
            }
            if (fixtureControlsAt < 0) fixtureControlsAt = Now;
            if(fixtureControlsSelection==null && Now-fixtureControlsAt>.6) {
                if(fixtureReopened && !fixtureSelectionCleared && NativeMenu.ControlsInteractive) {
                    UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);fixtureSelectionCleared=true;
                    Log("NS_CONTROLS_REOPEN_FOCUS_LOSS_INJECTED nativeSelectionCleared=True");return;
                }
                if(!NativeMenu.ControlsVisible || NativeMenu.ControlsSelection==null) {
                    if(Now-fixtureControlsAt>3)throw new InvalidOperationException("Controls native reveal never unlocked: "+NativeMenu.ControlsTrace);
                    return;
                }
                fixtureControlsSelection=NativeMenu.ControlsSelection;
                gameplay!.FixturePairedAudio();
                File.WriteAllText(Path.Combine(fixtureFolder,"controls-ready.receipt"),fixtureId.ToString());
                Log("NS_NATIVE_CONTROLS_SELECTION_READY "+NativeMenu.ControlsTrace);
            }
            if(fixtureControlsSelection!=null && Now-fixtureControlsAt>1.7 && !fixtureControlsNavigation) {
                var yi=gameplay!.FixturePairedPlayer!;var ui=yi.playerInput.uiActions;
                if(ui.MoveDown.Value<.5f || !NativeInputBridge.HostPaused || yi.actions.FooAttack.Value!=0
                    || NativeMenu.ControlsSelection==null || NativeMenu.ControlsSelection==fixtureControlsSelection)
                    throw new InvalidOperationException("Native GUI selection/pause isolation failed: down="+ui.MoveDown.Value+" pause="+NativeInputBridge.HostPaused+" talisman="+yi.actions.FooAttack.Value+" "+NativeMenu.ControlsTrace);
                fixtureControlsNavigation=true;
                if(NativeRoomIsolation.ControlsUiSounds==0)throw new InvalidOperationException("Native Controls navigation emitted no playing UI sound.");
                Log("NS_NATIVE_GUI_SELECTION_PASS before="+fixtureControlsSelection.name+" after="+NativeMenu.ControlsSelection.name+" nativeEventDispatch=True paused=True");
                File.WriteAllText(Path.Combine(fixtureFolder,"controls-navigated.receipt"),fixtureId.ToString());
                ScreenCapture.CaptureScreenshot(Path.Combine(fixtureFolder, "native-controls.png"));
            }
        }
        if(ControlsFixture && fixtureControlsNavigation && !fixtureControlsResumed && !NativeMenu.Open && !NativeInputBridge.HostPaused) {
            if(Time.timeScale<=0 || gameplay!.FixturePairedPlayer!.animator.speed<=0)throw new InvalidOperationException("Native NS controls return retained a zero gameplay clock: scale="+Time.timeScale+" animation="+gameplay!.FixturePairedPlayer!.animator.speed);
            fixtureControlsResumed=true;File.WriteAllText(Path.Combine(fixtureFolder,"controls-resumed.receipt"),fixtureId.ToString());
            Log("NS_NATIVE_CONTROLS_RESUMED scale="+Time.timeScale+" animator="+gameplay.FixturePairedPlayer.animator.speed);
        }
        if(ControlsFixture && fixtureControlsNavigation && !fixtureBindingStarted
            && File.Exists(Path.Combine(Path.GetDirectoryName(fixtureFolder)!,"HollowKnight","binding-authorize.receipt"))) {
            // Native EventSystem must receive the forwarded host Submit; do
            // not call the row directly and bypass the host ownership conflict.
            if(!NativeMenu.BindingActive)return;
            fixtureBindingStarted=true;fixtureBindingAt=Now;
            File.WriteAllText(Path.Combine(fixtureFolder,"binding-started.receipt"),fixtureId.ToString());
        }
        if(ControlsFixture && fixtureBindingStarted && !fixtureBindingHeld && Now-fixtureBindingAt>.5) {
            if(!fixtureBindingAccepted){NativeMenu.FixtureAcceptBack(Log);fixtureBindingAccepted=true;fixtureBindingAt=Now;return;}
            if(!NativeMenu.BindingActive || NativeMenu.ListenerActive || !NativeMenu.ControlsOpen || !NativeInputBridge.HostPaused || gameplay!.FixturePairedPlayer!.actions.FooAttack.Value!=0)
                throw new InvalidOperationException("Ordinary host Back cancelled native capture or unpaused gameplay.");
            fixtureBindingHeld=true;
            Log("NS_NATIVE_BINDING_BACK_PRESERVED assignmentCompleted=True heldBackBlocked=True gameplayPaused=True");
            File.WriteAllText(Path.Combine(fixtureFolder,"binding-held.receipt"),fixtureId.ToString());
        }
        if(ControlsFixture && fixtureBindingHeld && NativeMenu.ControlsInteractive && !NativeMenu.BindingActive)
            File.WriteAllText(Path.Combine(fixtureFolder,"binding-released.receipt"),fixtureId.ToString());
        if(ControlsFixture && File.Exists(Path.Combine(Path.GetDirectoryName(fixtureFolder)!,"HollowKnight","restore-bindings.request"))) {
            RestoreFixtureBindings();File.WriteAllText(Path.Combine(fixtureFolder,"restored-bindings.receipt"),fixtureId.ToString());
        }
        if(ControlsFixture && File.Exists(Path.Combine(Path.GetDirectoryName(fixtureFolder)!,"HollowKnight","post-controls-root.request"))) {
            if(fixturePostControlsRoot==0 && fixtureScenario=="controls-controller") {
                var actions=ApplicationCore.Instance.playerInput.gameplayActions;
                fixtureInputBindings=actions.SaveData();
                actions.Hack.ClearBindings();
                actions.Hack.AddBinding(new InControl.DeviceBindingSource(InControl.InputControlType.Action4));
                actions.WeaponAttack.ClearBindings();actions.WeaponAttack.AddBinding(new InControl.DeviceBindingSource(InControl.InputControlType.Action2));
                fixturePostControlsRoot=-1;fixtureRootAt=Now;
            } else if(fixturePostControlsRoot==-1) {
                string expected=ApplicationCore.Instance.playerInput.gameplayActions.Hack.Bindings.OfType<InControl.DeviceBindingSource>().First(b=>b.Control==InControl.InputControlType.Action4).Name;
                if(NativePromptCapture.CurrentBinding!=expected || !NativePromptCapture.CurrentOpaque) {
                    if(Now-fixtureRootAt>3)throw new InvalidOperationException("Rebound native Y glyph was not captured.");return;
                }
                if(!File.Exists(Path.Combine(fixtureFolder,"interaction-glyph-ready.receipt")))
                    File.WriteAllLines(Path.Combine(fixtureFolder,"interaction-glyph-ready.receipt"),new[]{fixtureId.ToString(),InControl.InputControlType.Action4.ToString()});
                if(!File.Exists(Path.Combine(Path.GetDirectoryName(fixtureFolder)!,"HollowKnight","interaction-glyph-checked.receipt")))return;
                NativePromptCapture.FixtureNativeVariants(Log);
                Log("NS_REBOUND_INTERACTION_GLYPH_PASS nativeY=True hostBindingMirrored=True originalBindingsRestoredBeforeSave=True");
                fixturePostControlsRoot=0;
                gameplay!.FixturePairedRootPlacement();NativeRestPoint.Interact();
                if(!NativeRestPoint.Active)throw new InvalidOperationException("Post-controls Root did not admit rest.");
                fixturePostControlsRoot=1;fixtureRootAt=Now;
            } else if(fixturePostControlsRoot==0) {
                gameplay!.FixturePairedRootPlacement();NativeRestPoint.Interact();
                if(!NativeRestPoint.Active)throw new InvalidOperationException("Post-controls Root did not admit rest.");
                fixturePostControlsRoot=1;fixtureRootAt=Now;
            } else if(fixturePostControlsRoot==1 && Now-fixtureRootAt>1) {
                if(rootChoicePhase>0 || rootChoiceIndex>0) {
                    if(!FixtureRootChoices()){fixtureRootAt=Now-1.1;return;}
                }
                if(!NativeMenu.Open)throw new InvalidOperationException("Post-controls Root options were not visible.");
                var manager=ApplicationUIGroupManager.Instance;
                var selected=UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject?.GetComponent<UIControlButton>();
                var admitted=selected==null?null:HarmonyLib.AccessTools.Field(typeof(UIControlButton),"belongGroup").GetValue(selected) as UIControlGroup;
                var parent=selected?.GetComponentInParent<UIControlGroup>();
                Log("NS_ROOT_GROUP_IDENTITIES current="+manager.currentControlGroup?.GetInstanceID()+" belonging="+admitted?.GetInstanceID()+" parent="+parent?.GetInstanceID()+" belongingActive="+admitted?.gameObject.activeInHierarchy+" interactive="+admitted?.interactable);
                if(admitted?.interactable!=true) {
                    if(Now-fixtureRootAt>4)throw new InvalidOperationException("Root button belongs to a permanently locked native group.");return;
                }
                if(!FixtureRootChoices()){fixtureRootAt=Now;return;}
                NativeMenu.FixtureRootBackGlyph(Log);
                var stack=(System.Collections.Generic.List<UIControlGroup>)HarmonyLib.AccessTools.Field(typeof(ApplicationUIGroupManager),"controlGroupStack").GetValue(manager);
                Log("NS_POST_CONTROLS_ROOT_NAV current="+manager.currentControlGroup?.name+" focused="+Application.isFocused
                    +" stack="+string.Join(";",stack.Take(8).Select(g=>g==null?"none":g.name+":"+g.gameObject.activeInHierarchy+":"+g.bindPanel?.state)));
                File.WriteAllText(Path.Combine(fixtureFolder,"post-controls-root-ready.receipt"),fixtureId.ToString());
                fixturePostControlsRoot=2;fixtureRootAt=Now;
            } else if(fixturePostControlsRoot==2 && NativeMenu.ControlsVisible && !fixtureRootControlsSeen) {
                if(!NativeRestPoint.Active)throw new InvalidOperationException("Controls unexpectedly ended the underlying Root session.");
                fixtureRootControlsSeen=true;fixtureRootAt=Now;
                File.WriteAllText(Path.Combine(fixtureFolder,"root-controls-ready.receipt"),fixtureId.ToString());
                Log("NS_ROOT_CONTROLS_OVERLAP_READY nativeRootPreserved=True");
            } else if(fixturePostControlsRoot==2 && fixtureRootControlsSeen && !NativeMenu.ControlsOpen && NativeRestPoint.Active) {
                if(!File.Exists(Path.Combine(fixtureFolder,"root-controls-closed.receipt"))) {
                    if(!NativeMenu.Open)throw new InvalidOperationException("Closing Controls lost its underlying Root menu.");
                    File.WriteAllText(Path.Combine(fixtureFolder,"root-controls-closed.receipt"),fixtureId.ToString());fixtureRootAt=Now;
                    Log("NS_ROOT_CONTROLS_CLOSED rootMenuStillVisible=True controlsVisible=False");
                }
            } else if(fixturePostControlsRoot==2 && !NativeMenu.Open && !NativeRestPoint.Active) {
                if(!gameplay!.FixturePairedPlayer!.actions.Enabled)throw new InvalidOperationException("Root exit retained an input vote.");
                File.WriteAllText(Path.Combine(fixtureFolder,"post-controls-root-exit.receipt"),fixtureId.ToString());
                Log("NS_POST_CONTROLS_ROOT_EXIT_PASS nativeStandup=True nativeControl=True");fixturePostControlsRoot=3;
            }
            if(fixturePostControlsRoot==3 && NativeMenu.ControlsVisible && !File.Exists(Path.Combine(fixtureFolder,"post-root-controls-visible.receipt"))) {
                ScreenCapture.CaptureScreenshot(Path.Combine(fixtureFolder,"post-root-controls.png"));
                File.WriteAllText(Path.Combine(fixtureFolder,"post-root-controls-visible.receipt"),fixtureId.ToString());
                Log("NS_POST_ROOT_CONTROLS_VISIBLE nativePixels=True nativeAlpha=True");
            }
            if(fixturePostControlsRoot==2 && Now-fixtureRootAt>10)throw new InvalidOperationException("Post-controls Root Cancel soft locked: "+NativeMenu.ControlsTrace);
        }
        if (fixtureSawActive && controller!.Lifecycle.State == BridgeState.Connected && !fixtureAudioReturnChecked) {
            if (fixtureAudioReturnAt < 0) fixtureAudioReturnAt = Now;
            if (Now - fixtureAudioReturnAt > .5) { bridgeAudio!.AssertMuted(); fixtureAudioReturnChecked = true; }
        }
        if (controller!.Lifecycle.State == BridgeState.Active && activeToken.HasValue) {
            if (fixtureAudioSession != activeToken.Value.Session) { fixtureAudioSession = activeToken.Value.Session; fixtureAudioActiveAt = Now; }
            if (Now - fixtureAudioActiveAt >= 3 && fixtureAudioActiveAt >= 0 && gameplay?.FixturePairedAudioReady==true) { gameplay.FixturePairedAudio(); fixtureAudioActiveAt = -1; }
        }
        if (fixtureScenario.EndsWith("-death", StringComparison.Ordinal) && !fixtureNativeDeathRequested && controller!.Lifecycle.State == BridgeState.Active) {
            if (fixturePeerActiveAt < 0) fixturePeerActiveAt = Now;
            if (Now - fixturePeerActiveAt > 10) {
                fixtureNativeDeathRequested = true; var player = gameplay!.FixturePairedPlayer!;
                if (player.health.IsDead) throw new InvalidOperationException("Disposable death fixture was already defeated.");
                player.health.Die(); if (!player.health.IsDead) throw new InvalidOperationException("Native Health.Die did not produce death.");
                Log("NS_FIXTURE_NATIVE_DEATH actualNativeHealth=True mode=" + NativeSaveContext.Mode);
            }
        }
        if (fixtureScenario == "quit-ns" && !fixturePeerExit && controller!.Lifecycle.State == BridgeState.Active) {
            if (fixturePeerActiveAt < 0) fixturePeerActiveAt = Now;
            if (Now - fixturePeerActiveAt > 10) { fixturePeerExit = true; ApplicationCore.Instance.ExitGame(); }
        }
        if (fixtureScenario == "crash-ns" && controller!.Lifecycle.State == BridgeState.Active) {
            if (fixturePeerActiveAt < 0) fixturePeerActiveAt = Now;
            if (Now - fixturePeerActiveAt > 10) FixtureAbruptExit();
        }
        if (fixtureScenario == "switch" && controller!.Lifecycle.State == BridgeState.Active && activeToken!.Value.Session != fixtureEquipmentSession) {
            var player = gameplay?.FixturePairedPlayer ?? throw new InvalidOperationException("Equipment fixture requires active native Yi.");
            var jade = player.mainAbilities.MustReflectProjectileJade;
            var styles = new[] { player.mainAbilities.FooExplodeAllStyle.AbilityData, player.mainAbilities.FooExplodeConsecutiveStyle.AbilityData, player.mainAbilities.FooExplodeAutoStyle.AbilityData };
            string TalismanSelection() => string.Join(";", styles.Where(s => s.IsEquipped).Select(s => s.FinalSaveID));
            string BowSelection() => player.weaponDataCollection.currentWeapon?.FinalSaveID ?? throw new InvalidOperationException("Native bow selection is missing.");
            if (!jade.IsAcquired || jade.IsTestForceEquip) throw new InvalidOperationException("Equipment fixture requires native ownership without force equipment.");
            fixtureEquipmentSession = activeToken.Value.Session;
            if (fixtureEquipmentPass == 0) {
                fixtureEquipmentOriginal = jade.IsEquipped; fixtureEquipmentExpected = !fixtureEquipmentOriginal;
                if (fixtureEquipmentExpected) jade.EquipCheck(false); else jade.UnEquipCheck();
                if (jade.IsEquipped != fixtureEquipmentExpected) throw new InvalidOperationException("Native jade selection could not be changed.");
                fixtureBowOriginal = BowSelection(); player.weaponDataCollection.Next(); fixtureBowExpected = BowSelection();
                if (fixtureBowOriginal == fixtureBowExpected) throw new InvalidOperationException("Native bow cycling did not select another acquired weapon.");
                fixtureTalismanOriginal = TalismanSelection();
                var next = styles.FirstOrDefault(s => s.IsAcquired && !s.IsEquipped && !s.IsTestForceEquip) ?? throw new InvalidOperationException("Another acquired native talisman style is unavailable.");
                var flags = (System.Collections.Generic.Dictionary<string, GameFlagBase>)HarmonyLib.AccessTools.Field(typeof(GameFlagCollection), "flagDict").GetValue(SaveManager.Instance.allFlags);
                var members = HarmonyLib.AccessTools.Field(typeof(GameFlagBaseCollection<PlayerAbilityData>), "gameFlagDataList");
                var collection = flags.Values.OfType<PlayerAbilitySingleChoiceCollection>().Single(c => ((System.Collections.Generic.List<PlayerAbilityData>)members.GetValue(c)).Contains(next));
                collection.SetCurrent(next); fixtureTalismanExpected = TalismanSelection();
                if (fixtureTalismanOriginal == fixtureTalismanExpected) throw new InvalidOperationException("Native talisman configuration did not change.");
                Log("NS_FIXTURE_LOADOUT_CHANGED jade=Ricochet original=" + fixtureEquipmentOriginal + " expected=" + fixtureEquipmentExpected);
                Log("NS_FIXTURE_LOADOUT_SELECTION bow=" + fixtureBowExpected + " talisman=" + fixtureTalismanExpected);
            } else if (fixtureEquipmentPass == 1) {
                if (jade.IsEquipped != fixtureEquipmentOriginal) throw new InvalidOperationException("Fresh slot inherited the other slot's jade selection.");
                if (BowSelection() != fixtureBowOriginal || TalismanSelection() != fixtureTalismanOriginal) throw new InvalidOperationException("Fresh slot inherited another slot's bow/talisman configuration.");
                Log("NS_FIXTURE_LOADOUT_ISOLATED slot=2 jade=Ricochet equipped=" + jade.IsEquipped);
            } else {
                Log("NS_FIXTURE_LOADOUT_COMPARE bowActual=" + BowSelection() + " bowExpected=" + fixtureBowExpected + " talismanActual=" + TalismanSelection() + " talismanExpected=" + fixtureTalismanExpected);
                if (jade.IsEquipped != fixtureEquipmentExpected) throw new InvalidOperationException("Reloaded slot lost the saved jade selection.");
                if (BowSelection() != fixtureBowExpected || TalismanSelection() != fixtureTalismanExpected) throw new InvalidOperationException("Reloaded slot lost saved bow/talisman selections.");
                Log("NS_FIXTURE_LOADOUT_RESTORED slot=1 jade=Ricochet equipped=" + jade.IsEquipped);
                Log("NS_FIXTURE_LOADOUT_SELECTION_RESTORED bow=" + BowSelection() + " talisman=" + TalismanSelection());
            }
            fixtureEquipmentPass++;
        }
        if ((fixtureScenario == "switch" || fixtureScenario == "vanilla-active-ns") && !fixturePeerExit && controller!.Lifecycle.State == BridgeState.Active) {
            if (fixturePeerActiveAt < 0) fixturePeerActiveAt = Now;
            if (Now - fixturePeerActiveAt > 10) { fixturePeerExit = true; GameCore.Instance.SetStateChangeToMainMenu(); Log("NATIVE_MENU_FIXTURE_NS_ORIGIN_EXIT nativeMenuPreparation=True"); }
        }
#endif
        if (fixtureScenario.StartsWith("vanilla-active-", StringComparison.Ordinal) && fixtureStage != 8 && fixtureStage != 99 && activeToken == null
            && (controller!.Lifecycle.State == BridgeState.SaveSelection || controller.Lifecycle.State == BridgeState.Connected)) {
            bool wasActive;
#if HOLLOW_KNIGHT
            wasActive = fixtureSawActive && (fixtureStage == 6 || fixtureStage == 60 || fixtureStage == 61);
#else
            wasActive = fixtureSawActive;
#endif
            if (wasActive) { fixtureStage = 8; fixtureAt = Now; if ((fixtureScenario == "vanilla-active-hk") == (Side == GameSide.HollowKnight)) menu!.FixtureVanillaSelection(); }
        }
#if HOLLOW_KNIGHT
        if (fixtureStage == 4 && Now >= fixtureNextLoadProbe) {
            fixtureNextLoadProbe = Now + 15;
            var gm = GameManager.instance;
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            Log("NATIVE_MENU_FIXTURE_LOAD_PROBE scene=" + scene + " scale=" + Time.timeScale + " game=" + gm.gameState + " entered=" + gm.HasFinishedEnteringScene + " hero=" + (HeroController.instance != null));
            menu!.FixturePixels(Path.Combine(fixtureFolder, "loading-" + ((int)Now).ToString(CultureInfo.InvariantCulture) + ".png"));
            if (scene == "GG_Entrance_Cutscene") {
                // This standalone UI fixture inspects only current input-action
                // contracts in this one authored introduction, never scene assets
                // or dependency graphs and never during bridged combat.
                foreach (var fsm in PlayMakerFSM.FsmList.Where(f => f != null && f.gameObject.scene.name == scene).Take(16))
                    Log("NATIVE_MENU_FIXTURE_INTRO_CONTROL object=" + fsm.name + " fsm=" + fsm.FsmName + " state=" + fsm.ActiveStateName + " actions=" + string.Join(",", fsm.Fsm.ActiveState.Actions.Where(a => a != null).Select(a => a.GetType().Name).Take(24)));
                var nextScene = UnityEngine.Object.FindFirstObjectByType<NextSceneOnInput>();
                Log("NATIVE_MENU_FIXTURE_INTRO_OWNER nextScene=" + (nextScene != null) + (nextScene == null ? "" : " accepting=" + nextScene.AcceptingInput + " object=" + nextScene.name));
                var dialogue = UnityEngine.Object.FindFirstObjectByType<DialogueBox>();
                if (dialogue != null) foreach (string field in new[] { "currentConversation", "currentPage", "typing", "hidden", "proxyFSM" }) Log("NATIVE_MENU_FIXTURE_DIALOGUE " + field + "=" + HarmonyLib.AccessTools.Field(typeof(DialogueBox), field).GetValue(dialogue));
                var cinematic = UnityEngine.Object.FindFirstObjectByType<CinematicPlayer>();
                if (cinematic != null) foreach (var name in new[] { "isPlaying", "didFinish", "isWaitingToPlay", "videoTriggered", "loadingLevel" })
                    Log("NATIVE_MENU_FIXTURE_INTRO " + name + "=" + HarmonyLib.AccessTools.Field(typeof(CinematicPlayer), name).GetValue(cinematic));
            }
        }
#endif
        if (fixtureScenario == "ui-inspect")
        {
            if (Now > 5 && (menu!.FixtureReady || Now > fixtureSeconds - 5) && fixtureStage == 0) { menu!.FixtureEvidence(); menu.FixturePixels(Path.Combine(fixtureFolder, "scoped-native-menu.png")); fixtureStage = 1; fixtureAt = Now; }
            if (fixtureStage == 1 && Now - fixtureAt > 2) { Log("NATIVE_MENU_FIXTURE_COMPLETE scopedInspection=True"); File.WriteAllText(Path.Combine(fixtureFolder, "menu-fixture.complete"), fixtureId.ToString()); Application.Quit(); }
            return;
        }
        if (Now >= fixtureSeconds) { Log("NATIVE_MENU_FIXTURE_TIMEOUT stage=" + fixtureStage); Application.Quit(); return; }
#if HOLLOW_KNIGHT
        // A connected control channel precedes asynchronous save-card readiness.
        // Keep the fixture's existing overall deadline, but do not click or read
        // the store while its menu read still owns the lease.
        if (controller!.Lifecycle.State == BridgeState.SaveSelection && menuReads.Busy) return;
#endif
        if ((fixtureScenario == "menus" || fixtureScenario.StartsWith("vanilla-", StringComparison.Ordinal)) && fixtureStage == 99) {
            string counterpart = Path.Combine(Path.GetDirectoryName(fixtureFolder)!, Side == GameSide.HollowKnight ? "NineSols" : "HollowKnight", "menu-fixture.complete");
            if (File.Exists(counterpart) && File.ReadAllText(counterpart) == fixtureId.ToString()) Application.Quit();
            return;
        }
        if (fixtureStage == 0 && fixtureDelay > 0 && controller!.Lifecycle.State != BridgeState.Vanilla) {
            fixtureStage = 1; fixtureAt = Now; Log("NATIVE_MENU_FIXTURE_COUNTERPART localButton=False adopted=True");
        }
        if (!menu!.FixtureReady && fixtureStage == 0) return;
        if (fixtureStage == 0)
        {
            if (fixtureDelay > 0) { File.WriteAllText(Path.Combine(fixtureFolder, "native-menu.ready"), fixtureId.ToString()); return; }
            if (fixtureReadyAt < 0) fixtureReadyAt = Now;
            if (Now - fixtureReadyAt < 2 + fixtureDelay) return;
            menu.FixturePixels(Path.Combine(fixtureFolder, "main-menu.png"));
            menu.FixtureSubmitBridge(); fixtureStage = 1; fixtureAt = Now;
            File.WriteAllText(Path.Combine(fixtureFolder, "native-menu.ready"), fixtureId.ToString());
        }
        else if (fixtureStage == 1 && Now - fixtureAt > 2)
        {
            menu.FixturePixels(Path.Combine(fixtureFolder, "waiting.png")); fixtureStage = 2;
        }
        if (fixtureStage == 2 && (controller!.Lifecycle.State == BridgeState.SaveSelection || controller.Lifecycle.State == BridgeState.Connected))
        { menu.FixturePixels(Path.Combine(fixtureFolder, "connected.png")); fixtureStage = 3; fixtureAt = Now; }
        if (fixtureStage == 3 && Now - fixtureAt > 2)
        {
#if !HOLLOW_KNIGHT
            bridgeAudio!.AssertMuted();
#endif
            menu.FixturePixels(Path.Combine(fixtureFolder, "connected-settled.png"));
            menu.FixtureEvidence();
            fixtureStage = 300; fixtureAt = Now;
        }
        if (fixtureStage == 300 && Now - fixtureAt > 1)
        {
#if HOLLOW_KNIGHT
            if (fixtureScenario == "ui-flows" || fixtureScenario == "delete") menu.FixtureNavigation();
#endif
            if (fixtureScenario == "vanilla-hk" || fixtureScenario == "vanilla-ns") {
                fixtureStage = 8; fixtureAt = Now;
                if ((fixtureScenario == "vanilla-hk") == (Side == GameSide.HollowKnight)) menu.FixtureVanillaSelection();
            }
            else if (fixtureScenario == "menus")
            {
                Log("NATIVE_MENU_FIXTURE_COMPLETE side=" + Side + " connected=True gameplayLoaded=False localButton=" + (fixtureDelay == 0)); fixtureStage = 99;
                File.WriteAllText(Path.Combine(fixtureFolder, "menu-fixture.complete"), fixtureId.ToString());
            }
#if HOLLOW_KNIGHT
            else {
                var slot = store!.ListSlots()[0];
                if (fixtureScenario == "delete") { if (slot.Condition != SlotCondition.Ready) throw new InvalidOperationException("Deletion fixture requires an occupied disposable pair."); ConfirmDelete(slot); fixtureStage = 20; fixtureAt = Now; }
                else if (fixtureScenario == "recovery" || fixtureScenario == "unrecoverable") { if (slot.Condition != (fixtureScenario == "recovery" ? SlotCondition.RecoveryAvailable : SlotCondition.Unrecoverable)) throw new InvalidOperationException("Corrupted fixture checkpoint condition differs from its expected recovery result."); menu.FixtureSelectSlot(slot); fixtureStage = 20; fixtureAt = Now; }
                else if (CombatFixture || fixtureScenario == "reload" || fixtureScenario == "capture-stress" || ControlsFixture || fixtureScenario == "loadout" || fixtureScenario == "switch" || fixtureScenario.StartsWith("crash-", StringComparison.Ordinal) || fixtureScenario.StartsWith("quit-", StringComparison.Ordinal) || fixtureScenario.StartsWith("vanilla-active-", StringComparison.Ordinal)) { if (slot.Condition != SlotCondition.Ready) throw new InvalidOperationException("Reload fixture requires a valid existing disposable pair."); fixtureConnection = controller!.Lifecycle.Connection; menu.FixtureSelectSlot(slot); fixtureStage = 4; fixtureAt = Now; }
                else { if (slot.Condition != SlotCondition.Empty) throw new InvalidOperationException("Creation fixture requires an empty disposable slot."); menu.FixtureSelectSlot(slot); fixtureStage = 30; fixtureAt = Now; }
            }
#endif
#if !HOLLOW_KNIGHT
            else { fixtureStage = 7; } // Observe once; NS does not select saves.
#endif
        }
        if (fixtureStage == 8 && controller!.Lifecycle.State == BridgeState.Vanilla && Now - fixtureAt > 3) {
#if !HOLLOW_KNIGHT
            bridgeAudio!.AssertRestored();
#endif
            if (NativeSaveContext.Enabled || activeToken.HasValue || controller.Lifecycle.Connection != Guid.Empty) throw new InvalidOperationException("Vanilla selection retained a paired session or authorization.");
            menu.FixturePixels(Path.Combine(fixtureFolder, "vanilla-after-disconnect.png"));
            Log("NATIVE_MENU_FIXTURE_COMPLETE vanillaSelection=True side=" + Side + " requiresNewActivation=True");
            File.WriteAllText(Path.Combine(fixtureFolder, "menu-fixture.complete"), fixtureId.ToString()); fixtureStage = 99;
        }
#if HOLLOW_KNIGHT
        if (fixtureStage == 20 && Now - fixtureAt > 3) {
            menu!.FixturePixels(Path.Combine(fixtureFolder, "paired-operation-notice.png"));
            fixtureStage = 200; fixtureAt = Now;
        }
        if (fixtureStage == 200 && Now - fixtureAt > 1) {
            if (activeToken.HasValue || NativeSaveContext.Enabled) throw new InvalidOperationException("A confirmation started gameplay before consent.");
            if (fixtureScenario == "delete") { menu.FixtureChoose("No"); if (store!.ListSlots()[0].Condition != SlotCondition.Ready) throw new InvalidOperationException("Cancelled deletion changed its save."); ConfirmDelete(store.ListSlots()[0]); fixtureStage = 21; fixtureAt = Now; }
            else { menu.FixtureChoose("Continue"); fixtureStage = fixtureScenario == "recovery" ? 4 : 22; fixtureAt = Now; }
        }
        if (fixtureStage == 21 && Now - fixtureAt > 3) { menu!.FixtureChoose("Yes"); if (store!.ListSlots()[0].Condition != SlotCondition.Empty || store.ListSlots().Skip(1).Any(s => s.Condition != SlotCondition.Empty)) throw new InvalidOperationException("Confirmed deletion affected the wrong slot."); fixtureStage = 22; fixtureAt = Now; }
        if (fixtureStage == 22 && Now - fixtureAt > 3) { menu!.FixturePixels(Path.Combine(fixtureFolder, "paired-operation-result.png")); Log("NATIVE_MENU_FIXTURE_COMPLETE scenario=" + fixtureScenario + " gameplayLoaded=False originalDataRetained=True"); File.WriteAllText(Path.Combine(fixtureFolder, "menu-fixture.complete"), fixtureId.ToString()); fixtureStage = 99; Application.Quit(); }
#endif
        if (fixtureStage == 30 && Now - fixtureAt > 2) {
#if HOLLOW_KNIGHT
            if (fixtureScenario == "ui-flows") menu.FixtureNavigation();
#endif
            menu.FixturePixels(Path.Combine(fixtureFolder, "mode-selection.png"));
            fixtureStage = 32; fixtureAt = Now;
        }
        if (fixtureStage == 32 && Now - fixtureAt > 1) {
            menu.FixtureChoose(fixtureScenario.StartsWith("classic", StringComparison.Ordinal) || fixtureScenario == "ui-flows" && fixturePass == 0 ? "Classic" : fixtureScenario.StartsWith("steel", StringComparison.Ordinal) || fixtureScenario == "ui-flows" && fixturePass == 1 ? "Steel Soul" : "Godhome"); fixtureStage = 31; fixtureAt = Now;
        }
        if (fixtureStage == 31 && Now - fixtureAt > 2) {
            menu.FixturePixels(Path.Combine(fixtureFolder, fixtureScenario == "ui-flows" ? "confirmation-" + fixturePass + ".png" : "creation-confirmation.png"));
#if HOLLOW_KNIGHT
            if (fixtureScenario == "ui-flows") menu.FixtureNavigation();
#endif
            fixtureStage = 33; fixtureAt = Now;
        }
        if (fixtureStage == 33 && Now - fixtureAt > 1) {
#if HOLLOW_KNIGHT
            if (fixtureScenario == "ui-flows") {
                if (fixturePass < 2) { menu.FixtureChoose("Go Back"); fixturePass++; fixtureStage = 30; fixtureAt = Now; }
                else { menu.FixtureCancel(); if (store!.ListSlots().Any(s => s.Condition != SlotCondition.Empty) || NativeSaveContext.Enabled) throw new InvalidOperationException("Menu cancellation created a paired save."); fixtureStage = 34; fixtureAt = Now; }
                return;
            }
            if (fixtureScenario == "godhome-no") { fixtureGodhomeBaseline = (PlayerData)Activator.CreateInstance(typeof(PlayerData), true); fixtureGodhomeBaseline.AddGGPlayerDataOverrides(); }
#endif
            menu.FixtureChoose(fixtureScenario.StartsWith("classic", StringComparison.Ordinal) || fixtureScenario.StartsWith("steel", StringComparison.Ordinal) ? "I Understand" : fixtureScenario == "godhome-yes" ? "Yes" : "No"); fixtureStage = 4; fixtureAt = Now;
        }
        if (fixtureStage == 34 && Now - fixtureAt > 3) { menu.FixturePixels(Path.Combine(fixtureFolder, "cancelled-creation.png")); fixtureStage = 35; fixtureAt = Now; }
        if (fixtureStage == 35 && Now - fixtureAt > 1) { Log("NATIVE_MENU_FIXTURE_COMPLETE nativeUiFlows=True cancelCreatesNothing=True"); File.WriteAllText(Path.Combine(fixtureFolder, "menu-fixture.complete"), fixtureId.ToString()); fixtureStage = 99; Application.Quit(); }
        if (fixtureStage == 4 && controller!.Lifecycle.State == BridgeState.Active)
        {
#if HOLLOW_KNIGHT
            bool hudReady=gameplay!.FixturePairedReady;
            if (!hudReady && !((fixtureScenario=="loadout" || CombatFixture) && gameplay.FixturePairedWorldReady)) {
                if (Now - fixtureAt > 60) throw new InvalidOperationException("Paired gameplay never released input or presented its native HUD.");
                return;
            }
            Log(hudReady?"PAIRED_INPUT_HUD_READY actualNativeEntryComplete=True inputGate=True nonemptyHudTexture=True nativeCanvas=True"
                :"PAIRED_ROOT_TEST_WORLD_READY actualNativeEntryComplete=True hudVisible=False separateHudAcceptancePending=True");
            File.WriteAllText(Path.Combine(fixtureFolder,"paired-active.receipt"),fixtureId.ToString());
            fixtureControlStage = 0;
            if (FixtureTestsMovement) gameplay.FixturePairedControl(default(YiGodhome.Bridge.Controls));
            if (fixtureScenario == "switch" && (activeToken!.Value.Connection != fixtureConnection || activeToken.Value.Slot.Number != (fixturePass == 1 ? 2 : 1))) throw new InvalidOperationException("Connected switching changed connection or selected the wrong pair.");
            if (fixtureScenario == "godhome-yes" || fixtureScenario == "godhome-no") FixtureGodhomeState();
#endif
            menu.FixturePixels(Path.Combine(fixtureFolder, "active-gameplay.png")); Log("NATIVE_MENU_FIXTURE_ACTIVE paired=True creation=True");
            fixtureStage = 5; fixtureAt = Now;
        }
        if (fixtureStage == 5 && Now - fixtureAt > 10) {
#if HOLLOW_KNIGHT
            if(CombatFixture){UpdateCombatFixture();if(!CombatFixtureDrained)return;}
#endif
#if HOLLOW_KNIGHT
            if(fixtureScenario=="loadout" && (!File.Exists(Path.Combine(Path.GetDirectoryName(fixtureFolder)!,"NineSols","loadout-probe.complete"))
                || File.ReadAllText(Path.Combine(Path.GetDirectoryName(fixtureFolder)!,"NineSols","loadout-probe.complete"))!=fixtureId.ToString()))return;
            if (ControlsFixture) {
                GameManager.instance.StartCoroutine(GameManager.instance.PauseGameToggle());
                fixtureStage = 41; fixtureAt = Now; return;
            }
            if (FixtureTestsMovement && !FixturePairedMovement()) return;
#endif
            if (fixtureScenario.StartsWith("quit-", StringComparison.Ordinal)) {
#if HOLLOW_KNIGHT
                if (fixtureScenario == "quit-hk") GameManager.instance.StartCoroutine(GameManager.instance.QuitGame());
#endif
                fixtureStage = 6; return;
            }
            if (fixtureScenario == "vanilla-active-ns") { fixtureStage = 6; return; }
#if HOLLOW_KNIGHT
            if (fixtureScenario == "vanilla-active-hk") { GameManager.instance.StartCoroutine(GameManager.instance.ReturnToMainMenu(GameManager.ReturnToMainMenuSaveModes.DontSave, _ => { })); fixtureStage = 6; return; }
#endif
            if (fixtureScenario.EndsWith("-death", StringComparison.Ordinal)) { fixtureStage = 6; return; }
            if (fixtureScenario.StartsWith("crash-", StringComparison.Ordinal)) {
                if ((fixtureScenario == "crash-hk") == (Side == GameSide.HollowKnight)) FixtureAbruptExit();
                fixtureStage = 75; return;
            }
#if HOLLOW_KNIGHT
            if (fixtureScenario != "switch" || fixturePass != 0) RequestSessionEnd(false);
#else
            RequestSessionEnd(false);
#endif
            fixtureStage = 6;
        }
#if HOLLOW_KNIGHT
        if (fixtureStage == 41 && GameManager.instance.IsGamePaused() && gameplay!.FixturePairedUiPolish(fixtureFolder)) {
            File.WriteAllText(Path.Combine(fixtureFolder,"controls-prefocus.request"),fixtureId.ToString());fixtureStage=47;fixtureAt=Now;
        }
        if(fixtureStage==47 && Application.isFocused
            && File.Exists(Path.Combine(Path.GetDirectoryName(fixtureFolder)!,"NineSols","controls-prefocus.receipt"))) {
            gameplay!.FixturePairedControlsPanel();fixtureStage=42;fixtureAt=Now;
        }
        if (fixtureStage == 42 && HollowSolsNativePause.PeerVisible && Now - fixtureAt > .5
            && File.Exists(Path.Combine(Path.GetDirectoryName(fixtureFolder)!,"NineSols","controls-ready.receipt"))) {
            gameplay!.FixturePairedControl(new YiGodhome.Bridge.Controls(0,-1,YiGodhome.Bridge.Buttons.HostPaused|YiGodhome.Bridge.Buttons.Keyboard));
            menu!.FixturePixels(Path.Combine(fixtureFolder, "forwarded-controls.png"));
            Log("NATIVE_CONTROLS_FIXTURE_PRESENTED nativePeerPixels=True hostPause=True"); fixtureStage = 43; fixtureAt = Now;
        }
        if(fixtureStage==43 && File.Exists(Path.Combine(Path.GetDirectoryName(fixtureFolder)!,"NineSols","controls-navigated.receipt"))) {
            if(!Application.isFocused || !GameManager.instance.IsGamePaused())throw new InvalidOperationException("Opening Controls lost HK focus or native pause: focused="+Application.isFocused+" paused="+GameManager.instance.IsGamePaused());
            File.WriteAllText(Path.Combine(fixtureFolder,"binding-authorize.receipt"),fixtureId.ToString());
            gameplay!.FixturePairedControl(new YiGodhome.Bridge.Controls(0,0,YiGodhome.Bridge.Buttons.Jump|YiGodhome.Bridge.Buttons.Confirm|YiGodhome.Bridge.Buttons.HostPaused|YiGodhome.Bridge.Buttons.Keyboard));
            // Also exercise the actual native hidden Resume callback. It must
            // not turn the same Submit into a forced close of the NS listener.
            UIManager.instance.TogglePauseGame();
            if(HollowSolsNativePause.ForwardClose)throw new InvalidOperationException("Native HK menu Submit requested peer closure.");
            fixtureHiddenCancel=false;fixtureStage=48;fixtureAt=Now;
        }
        if(fixtureStage==48 && File.Exists(Path.Combine(Path.GetDirectoryName(fixtureFolder)!,"NineSols","binding-started.receipt"))) {
            if(!fixtureHiddenCancel){gameplay!.FixturePairedControlsPanel(cancel:true);fixtureHiddenCancel=true;}
            gameplay!.FixturePairedControl(new YiGodhome.Bridge.Controls(0,0,YiGodhome.Bridge.Buttons.Cancel|YiGodhome.Bridge.Buttons.HostPaused|YiGodhome.Bridge.Buttons.Keyboard));
            if(!File.Exists(Path.Combine(Path.GetDirectoryName(fixtureFolder)!,"NineSols","binding-held.receipt")))return;
            gameplay!.FixturePairedControl(default(YiGodhome.Bridge.Controls));fixtureStage=49;fixtureAt=Now;
        }
        if(fixtureStage==49 && File.Exists(Path.Combine(Path.GetDirectoryName(fixtureFolder)!,"NineSols","binding-released.receipt"))) {
            gameplay!.FixturePairedControl(new YiGodhome.Bridge.Controls(0,0,YiGodhome.Bridge.Buttons.Cancel|YiGodhome.Bridge.Buttons.HostPaused));
            Log("HK_NATIVE_CONTROLS_FRESH_BACK afterAssignmentRelease=True");fixtureStage=46;fixtureAt=Now;
        }
        if (fixtureStage == 46 && !HollowSolsNativePause.Active) {
            if(fixtureControlsPass==0) {
                fixtureControlsPass++;
                foreach(var receipt in new[]{"controls-ready.receipt","controls-navigated.receipt","binding-started.receipt","binding-held.receipt","binding-released.receipt"})
                    File.Delete(Path.Combine(Path.GetDirectoryName(fixtureFolder)!,"NineSols",receipt));
                File.Delete(Path.Combine(fixtureFolder,"binding-authorize.receipt"));
                File.WriteAllText(Path.Combine(fixtureFolder,"controls-cycle2.request"),fixtureId.ToString());
                gameplay!.FixturePairedControl(default(YiGodhome.Bridge.Controls));
                gameplay.FixturePairedControlsPanel();fixtureStage=42;fixtureAt=Now;
                Log("HK_NATIVE_CONTROLS_REOPEN_REQUEST samePause=True");return;
            }
            Log("HK_NATIVE_CONTROLS_TWO_CYCLES_PASS forwardedSubmit=True nativeMenuResumeRejected=True listenerHeld=True");
            if(!GameManager.instance.IsGamePaused())throw new InvalidOperationException("Native controls Back did not return to paused HK menu.");
            gameplay!.FixturePairedControl(default(YiGodhome.Bridge.Controls));
            GameManager.instance.StartCoroutine(GameManager.instance.PauseGameToggle()); fixtureStage = 44; fixtureAt = Now;
        }
        if (fixtureStage == 44 && !GameManager.instance.IsGamePaused() && Now - fixtureAt > .5
            && File.Exists(Path.Combine(Path.GetDirectoryName(fixtureFolder)!,"NineSols","controls-resumed.receipt"))) {
            if(Time.timeScale<=0)throw new InvalidOperationException("Controls return retained a zero HK gameplay clock.");
            fixtureControlX=gameplay!.FixturePairedPose!.X;
            gameplay.FixturePairedControl(new YiGodhome.Bridge.Controls(1,0,YiGodhome.Bridge.Buttons.Keyboard));fixtureStage=45;fixtureAt=Now;
        }
        if(fixtureStage==45 && Now-fixtureAt>.3) {
            float moved=gameplay!.FixturePairedPose!.X-fixtureControlX;
            if(moved<.5f)throw new InvalidOperationException("Native Yi failed to move after controls return: distance="+moved);
            gameplay.FixturePairedControl(default(YiGodhome.Bridge.Controls));
            Log("HK_NATIVE_CONTROLS_RETURN_MOVEMENT distance="+moved+" hostScale="+Time.timeScale+" nativePauseReleased=True");
            File.WriteAllText(Path.Combine(fixtureFolder,"post-controls-root.request"),fixtureId.ToString());fixtureStage=51;fixtureAt=Now;
        }
        if(fixtureStage==51 && File.Exists(Path.Combine(Path.GetDirectoryName(fixtureFolder)!,"NineSols","interaction-glyph-ready.receipt"))) {
            var expected=File.ReadAllLines(Path.Combine(Path.GetDirectoryName(fixtureFolder)!,"NineSols","interaction-glyph-ready.receipt"));
            if(expected.Length!=2 || expected[0]!=fixtureId.ToString())throw new InvalidOperationException("Foreign glyph fixture receipt.");
            if(gameplay!.FixtureInteractionControllerBinding==expected[1] && !File.Exists(Path.Combine(fixtureFolder,"interaction-glyph-checked.receipt"))) {
                File.WriteAllText(Path.Combine(fixtureFolder,"interaction-glyph-checked.receipt"),fixtureId.ToString());
            }
        }
        if(fixtureStage==51 && File.Exists(Path.Combine(Path.GetDirectoryName(fixtureFolder)!,"NineSols","post-controls-root-ready.receipt"))) {
            gameplay!.FixturePairedControl(default(YiGodhome.Bridge.Controls));
            GameManager.instance.StartCoroutine(GameManager.instance.PauseGameToggle());fixtureStage=53;fixtureAt=Now;
        }
        if(fixtureStage==53 && GameManager.instance.IsGamePaused() && Now-fixtureAt>.5) {
            gameplay!.FixturePairedControlsPanel();fixtureStage=54;fixtureAt=Now;
        }
        if(fixtureStage==54 && HollowSolsNativePause.PeerVisible
            && File.Exists(Path.Combine(Path.GetDirectoryName(fixtureFolder)!,"NineSols","root-controls-ready.receipt"))) {
            GameManager.instance.StartCoroutine(GameManager.instance.PauseGameToggle());fixtureStage=55;fixtureAt=Now;
        }
        if(fixtureStage==55 && !HollowSolsNativePause.Active
            && File.Exists(Path.Combine(Path.GetDirectoryName(fixtureFolder)!,"NineSols","root-controls-closed.receipt"))) {
            if(!GameManager.instance.IsGamePaused())throw new InvalidOperationException("Controls over Root unpaused HK before player resume.");
            Log("HK_ROOT_CONTROLS_CLOSE_PASS rootMenuPreserved=True controlsLeaseReleased=True");
            GameManager.instance.StartCoroutine(GameManager.instance.PauseGameToggle());fixtureStage=56;fixtureAt=Now;
        }
        if(fixtureStage==56 && !GameManager.instance.IsGamePaused() && Now-fixtureAt>.5) {
            gameplay!.FixturePairedControl(new YiGodhome.Bridge.Controls(0,0,YiGodhome.Bridge.Buttons.Interact|YiGodhome.Bridge.Buttons.Keyboard));
            fixtureStage=52;fixtureAt=Now;
        }
        if(fixtureStage==52 && Now-fixtureAt>.15) {
            gameplay!.FixturePairedControl(default(YiGodhome.Bridge.Controls));
            if(!File.Exists(Path.Combine(Path.GetDirectoryName(fixtureFolder)!,"NineSols","post-controls-root-exit.receipt")))return;
            Log("HK_POST_CONTROLS_ROOT_EXIT_PASS forwardedCancel=True");
            GameManager.instance.StartCoroutine(GameManager.instance.PauseGameToggle());fixtureStage=57;fixtureAt=Now;
        }
        if(fixtureStage==57 && GameManager.instance.IsGamePaused() && Now-fixtureAt>.5) {
            gameplay!.FixturePairedControlsPanel();fixtureStage=58;fixtureAt=Now;
        }
        if(fixtureStage==58 && HollowSolsNativePause.PeerVisible && Now-fixtureAt>1
            && File.Exists(Path.Combine(Path.GetDirectoryName(fixtureFolder)!,"NineSols","post-root-controls-visible.receipt"))) {
            menu!.FixturePixels(Path.Combine(fixtureFolder,"post-root-controls.png"));
            gameplay!.FixturePairedControl(new YiGodhome.Bridge.Controls(0,0,YiGodhome.Bridge.Buttons.Cancel));fixtureStage=59;fixtureAt=Now;
            Log("HK_POST_ROOT_CONTROLS_REOPEN_PASS swappedBindingsRetained=True");
        }
        if(fixtureStage==59 && !HollowSolsNativePause.Active) {
            gameplay!.FixturePairedControl(default(YiGodhome.Bridge.Controls));
            GameManager.instance.StartCoroutine(GameManager.instance.PauseGameToggle());fixtureStage=84;fixtureAt=Now;
        }
        if(fixtureStage==84 && !GameManager.instance.IsGamePaused() && Now-fixtureAt>.5) {
            fixtureControlY=gameplay!.FixturePairedPose!.Y;
            gameplay.FixturePairedControl(new YiGodhome.Bridge.Controls(0,0,YiGodhome.Bridge.Buttons.Jump));fixtureStage=85;fixtureAt=Now;
        }
        if(fixtureStage==85 && Now-fixtureAt>.25) {
            if(gameplay!.FixturePairedPose!.Y-fixtureControlY<.5f)throw new InvalidOperationException("Post-root/reopen native jump failed.");
            gameplay.FixturePairedControl(default(YiGodhome.Bridge.Controls));
            Log("HK_POST_ROOT_CONTROLS_JUMP_PASS nativeJump=True");
            File.WriteAllText(Path.Combine(fixtureFolder,"restore-bindings.request"),fixtureId.ToString());fixtureStage=86;fixtureAt=Now;
        }
        if(fixtureStage==86 && File.Exists(Path.Combine(Path.GetDirectoryName(fixtureFolder)!,"NineSols","restored-bindings.receipt"))) {RequestSessionEnd(false);fixtureStage=6;fixtureAt=Now;}
        if ((fixtureStage is >=41 and <=59 || fixtureStage is >=84 and <=86) && Now - fixtureAt > 12)
            throw new InvalidOperationException("Native controls fixture did not complete its bounded open/close path.");
#endif
        if (fixtureStage == 6 && controller!.Lifecycle.State == BridgeState.SaveSelection)
        {
            fixtureStage = 60; fixtureAt = Now;
        }
        if (fixtureStage == 60 && Now - fixtureAt > 2) {
#if HOLLOW_KNIGHT
            if (fixtureScenario == "steel-death" && NativeSavePreview.Read(SavedCheckpoint()).permadeathMode != 2) throw new InvalidOperationException("Steel Soul death did not retain its native defeated-run state.");
            byte[] SavedCheckpoint() { var slot = store!.ListSlots()[0]; return store.Load(slot.Slot, slot.Pair!.Id).HollowKnight; }
#endif
            menu.FixturePixels(Path.Combine(fixtureFolder, "returned-save-menu.png")); Log("NATIVE_MENU_FIXTURE_RETURN side=" + Side + " saved=True nativeReturn=True");
            fixtureStage = 61; fixtureAt = Now;
        }
        if (fixtureStage == 61 && Now - fixtureAt > 1) {
#if HOLLOW_KNIGHT
            if (fixtureScenario == "switch" && fixturePass < 2) {
                fixturePass++;
                var slot = store!.ListSlots()[fixturePass == 1 ? 1 : 0]; menu.FixtureSelectSlot(slot);
                fixtureStage = fixturePass == 1 ? 30 : 4; fixtureAt = Now;
                Log("NATIVE_MENU_FIXTURE_SWITCH pass=" + fixturePass + " slot=" + slot.Slot.Number + " connection=" + fixtureConnection); return;
            }
#endif
            Log("NATIVE_MENU_FIXTURE_COMPLETE side=" + Side + " scenario=" + fixtureScenario + " saved=True nativeReturn=True");
            fixtureStage = 99; File.WriteAllText(Path.Combine(fixtureFolder, "menu-fixture.complete"), fixtureId.ToString()); Application.Quit();
        }
    }
#if HOLLOW_KNIGHT
    private bool FixturePairedMovement()
    {
        if (fixtureControlStage == 7) return true;
        var pose = gameplay!.FixturePairedPose ?? throw new InvalidOperationException("Input fixture lost Yi's native pose.");
        if(fixtureScenario=="capture-stress" && fixtureControlStage>=4) {
            if(fixtureControlStage==4) {
                gameplay.FixturePairedControl(default(YiGodhome.Bridge.Controls));
                File.WriteAllText(Path.Combine(fixtureFolder,"capture-stress.request"),fixtureId.ToString());fixtureControlStage=5;fixtureControlAt=Now;
            }
            string receipt=Path.Combine(Path.GetDirectoryName(fixtureFolder)!,"NineSols","capture-stress.receipt");
            if(File.Exists(receipt) && File.ReadAllText(receipt)==fixtureId.ToString()){gameplay.FixturePairedControl(null);fixtureControlStage=7;return true;}
            if(Now-fixtureControlAt>15)throw new InvalidOperationException("Native capture stress exceeded its finite bound.");
            return false;
        }
        if(fixtureScenario=="reload" && fixtureControlStage>=4) {
            if(fixtureControlStage==4) {
                if(!pose.Grounded)return false;
                gameplay.FixturePairedControl(new YiGodhome.Bridge.Controls(0,0,YiGodhome.Bridge.Buttons.Bow));fixtureControlStage=5;fixtureControlAt=Now;return false;
            }
            if(fixtureControlStage==5 && Now-fixtureControlAt>1) {
                gameplay.FixturePairedControl(default(YiGodhome.Bridge.Controls));fixtureControlStage=6;fixtureControlAt=Now;
            }
            if(fixtureControlStage==6 && Now-fixtureControlAt>2) {
                if(!File.Exists(Path.Combine(Path.GetDirectoryName(fixtureFolder)!,"NineSols","bow-density.receipt")))
                    throw new InvalidOperationException("Bow fixture did not exercise an expanded native arrow capture.");
                menu!.FixturePixels(Path.Combine(fixtureFolder,"bow-density-host.png"));
                gameplay.FixturePairedControl(null);fixtureControlStage=7;return true;
            }
            return false;
        }
        if(fixtureControlStage==4)return true;
        if (fixtureControlStage == 0) {
            fixtureControlX = pose.X; fixtureControlAt = Now; fixtureControlStage = 1;
            gameplay.FixturePairedControl(new YiGodhome.Bridge.Controls(1, 0, YiGodhome.Bridge.Buttons.Keyboard)); return false;
        }
        if (Now - fixtureControlAt < (fixtureControlStage == 1 ? .2 : .4)) return false;
        if (fixtureControlStage == 1) {
            if (pose.X - fixtureControlX < .15f) throw new InvalidOperationException("Forwarded right input did not move native Yi.");
            Log("PAIRED_INPUT_NATIVE_MOVEMENT direction=Right delta=" + (pose.X - fixtureControlX));
            fixtureControlX = pose.X; fixtureControlAt = Now; fixtureControlStage = 2;
            gameplay.FixturePairedControl(new YiGodhome.Bridge.Controls(-1, 0, YiGodhome.Bridge.Buttons.None)); return false;
        }
        if (fixtureControlStage == 2) {
            if (fixtureControlX - pose.X < .15f) throw new InvalidOperationException("Forwarded left input did not move native Yi.");
            Log("PAIRED_INPUT_NATIVE_MOVEMENT direction=Left delta=" + (fixtureControlX - pose.X));
            fixtureControlY = pose.Y; fixtureControlAt = Now; fixtureControlStage = 3;
            gameplay.FixturePairedControl(new YiGodhome.Bridge.Controls(0, 0, YiGodhome.Bridge.Buttons.Jump)); return false;
        }
        if (pose.Y - fixtureControlY < .15f && pose.VelocityY <= .1f) throw new InvalidOperationException("Forwarded jump did not activate native Yi movement.");
        gameplay.FixturePairedControl(null); fixtureControlStage = 4;
        Log("PAIRED_INPUT_NATIVE_JUMP actualNativePose=True delta=" + (pose.Y - fixtureControlY));
        menu!.FixturePixels(Path.Combine(fixtureFolder, "active-input-hud.png"));
        return fixtureScenario!="reload" && fixtureScenario!="capture-stress";
    }
    private bool FixtureTestsMovement => fixtureScenario == "reload" || fixtureScenario == "capture-stress" || fixtureScenario == "godhome-yes" || fixtureScenario == "godhome-no"
        || fixtureScenario == "crash-hk" || fixtureScenario == "quit-hk";
#endif
    private void FixtureAbruptExit()
    {
        if (fixtureStage == 78) return;
        if (fixtureId == Guid.Empty || !NativeSaveContext.ReadOnlyVanilla || controller?.Lifecycle.State != BridgeState.Active) throw new InvalidOperationException("Abrupt termination requires an authorized active disposable fixture.");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(fixtureFolder)!, "fixture-crash.trigger"), fixtureId.ToString());
        Log("NATIVE_MENU_FIXTURE_ABRUPT_TERMINATION_REQUEST side=" + Side + " saveRoot=Disposable parentOwnsExactProcess=True"); fixtureStage = 78;
    }
#if HOLLOW_KNIGHT
    private void FixtureGodhomeState()
    {
        var pd = GameManager.instance.playerData;
        int statues = 0, unlocked = 0, statueClears = 0, doors = 0, doorAccess = 0, records = 0;
        foreach (var field in typeof(PlayerData).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)) {
            if (field.FieldType == typeof(BossStatue.Completion)) {
                var value = (BossStatue.Completion)field.GetValue(pd); statues++; if (value.isUnlocked) unlocked++;
                if (value.completedTier1 || value.completedTier2 || value.completedTier3) statueClears++;
                if (fixtureGodhomeBaseline != null) {
                    var expected = (BossStatue.Completion)field.GetValue(fixtureGodhomeBaseline);
                    if (value.isUnlocked != expected.isUnlocked || value.completedTier1 != expected.completedTier1 || value.completedTier2 != expected.completedTier2 || value.completedTier3 != expected.completedTier3) throw new InvalidOperationException("Godhome No differs from native starting statue access/progression.");
                }
            }
            else if (field.FieldType == typeof(BossSequenceDoor.Completion)) {
                var value = (BossSequenceDoor.Completion)field.GetValue(pd); doors++; if (value.unlocked) doorAccess++;
                if (value.completed || value.allBindings || value.noHits || value.boundNail || value.boundShell || value.boundCharms || value.boundSoul) records++;
                if (fixtureGodhomeBaseline != null) {
                    var expected = (BossSequenceDoor.Completion)field.GetValue(fixtureGodhomeBaseline);
                    if (value.unlocked != expected.unlocked || value.canUnlock != expected.canUnlock || value.completed != expected.completed || value.allBindings != expected.allBindings || value.noHits != expected.noHits || value.boundNail != expected.boundNail || value.boundShell != expected.boundShell || value.boundCharms != expected.boundCharms || value.boundSoul != expected.boundSoul) throw new InvalidOperationException("Godhome No differs from native starting Pantheon access/progression.");
                }
            }
        }
        bool full = fixtureScenario == "godhome-yes";
        Log("NATIVE_MENU_FIXTURE_GODHOME_STATE fullAccess=" + full + " statues=" + statues + " unlocked=" + unlocked + " doors=" + doors + " doorAccess=" + doorAccess + " statueClears=" + statueClears + " pantheonRecords=" + records + " completion=" + pd.completionPercentage);
        if (!pd.bossRushMode || full && (unlocked != statues || doors != 5 || doorAccess != 5 || statueClears != 0 || records != 0
            || !pd.unlockedBossScenes.Contains(GodseekerProfile.RadianceSceneUnlock))) throw new InvalidOperationException("Fresh Godhome access or progression oracle failed.");
    }
#endif
}
