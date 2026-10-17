using System;
using System.Diagnostics;
using System.IO;
using BepInEx;
using HollowSols.Foundation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

namespace YiGodhome;

[BepInPlugin(Id, "Hollow Sols native menus", YiGodhome.Bridge.BuildFeatures.ReleaseVersion)]
public sealed partial class HollowSolsMenuPlugin : BaseUnityPlugin
{
#if HOLLOW_KNIGHT
    private const GameSide Side = GameSide.HollowKnight;
    public const string Id = "local.hollowsols.menu.hk";
#else
    private const GameSide Side = GameSide.NineSols;
    public const string Id = "local.hollowsols.menu.ns";
#endif
    public static HollowSolsMenuPlugin? Instance { get; private set; }
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private MenuBridgeController? controller;
    private ControlTransport? transport;
    private PairedSaveStore? store;
    private double nextHeartbeat;
    private NativeBridgeMenu? menu;
    private string? displayedFailure;
    private bool backgroundCaptured, previousBackground;
    private ControlMessage? deferredActivation;
    private double nextPassiveRetry;
    private bool requestedQuit, finalizedQuit;
    private double quitDeadline;
#if HOLLOW_KNIGHT
    private readonly DeferredRead<MenuSaveRead> menuReads = new DeferredRead<MenuSaveRead>();
    private MenuSaveRead? menuPreview;
    private sealed class MenuSaveRead
    {
        internal readonly PairedSaveStore Store;
        internal SlotSummary[]? Slots;
        internal byte[][]? Previews;
        internal SavePair? Pair;
        internal PairedCheckpoint? Loaded;
        internal MenuSaveRead(PairedSaveStore store) { Store = store; }
    }
#endif
#if !HOLLOW_KNIGHT
    private bool locallyActivated;
    private NativeBridgeAudio? bridgeAudio;
    private bool bridgeAudioFailed;
#endif
    internal double Now => clock.Elapsed.TotalSeconds;
    internal BridgeLifecycle? Lifecycle => controller?.Lifecycle;
    internal string SaveRoot { get; private set; } = "";
    private void Awake()
    {
        if (Environment.GetEnvironmentVariable("HOLLOWSOLS_MENU") != "1") { enabled = false; return; }
        if (typeof(GameManager).Assembly.GetType("YiGodhome.GuardedPairedSavesV1") == null)
            throw new InvalidOperationException("Native menu mode requires its verified early save boundary.");
        Instance = this;
        SaveRoot = Environment.GetEnvironmentVariable("HOLLOWSOLS_SAVE_ROOT")
            ?? SavePaths.DefaultRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        controller = new MenuBridgeController(Side);
        transport = new ControlTransport(Side);
        menu = new NativeBridgeMenu(this, Log);
#if !HOLLOW_KNIGHT
        bridgeAudio = new NativeBridgeAudio(Log);
#endif
        InstallNativeLifecycle();
        CaptureBackground();
        InitializeMenuFixture();
        InitializeFailureReporting();
        UnitySceneManager.sceneLoaded += OnScene;
        Application.wantsToQuit += CanQuit;
        Application.quitting += NotifyOrderlyApplicationExit;
        Log("MENU_INFRASTRUCTURE_READY nativeSide=" + Side + " saveLoaded=False bridgeRequested=False");
    }
    internal void Log(string value) { Logger.LogInfo(value); RecordReportMenu(value); }
    private void OnScene(Scene scene, LoadSceneMode mode) { try { RestoreDataBeforeTitle(scene.name); menu?.SceneLoaded(scene); } catch (Exception error) { Log("NATIVE_MENU_BIND_PENDING " + error); } }
    internal void ActivateBridge()
    {
        if(!BridgeActivationAllowed)return;
        if (controller!.Lifecycle.State == BridgeState.Vanilla || controller.Lifecycle.State == BridgeState.Failed) BeginReportAttempt();
        try
        {
#if !HOLLOW_KNIGHT
            if (bridgeAudioFailed) throw new InvalidOperationException("Native audio could not be maintained. Restart Nine Sols before reconnecting.");
#endif
            if (controller!.Lifecycle.State == BridgeState.SaveSelection || controller.Lifecycle.State == BridgeState.Connected) { StateChanged(); return; }
            controller!.Activate(Now);
#if !HOLLOW_KNIGHT
            locallyActivated = true;
#endif
            CaptureBackground();
            transport ??= new ControlTransport(Side);
            displayedFailure = null;
            menu!.Waiting("Waiting to establish bridge connection...");
#if !HOLLOW_KNIGHT
            NativePairedGame.TryFocusHollowKnight(Log);
#endif
        }
        catch (Exception error) { Fail(error); }
    }
    internal bool BridgeActivationAllowed=>!requestedQuit && !activeToken.HasValue && controller!=null
        && (controller.Lifecycle.State==BridgeState.Vanilla || controller.Lifecycle.State==BridgeState.Failed
            || controller.Lifecycle.State==BridgeState.SaveSelection || controller.Lifecycle.State==BridgeState.Connected);
    private void CaptureBackground()
    {
        if (!backgroundCaptured) { previousBackground = Application.runInBackground; backgroundCaptured = true; }
        Application.runInBackground = true;
    }
    internal void ShowSlots() => ExecuteUI(() => {
#if HOLLOW_KNIGHT
        if (requestedQuit || activeToken.HasValue || controller?.Lifecycle.State != BridgeState.SaveSelection) return;
        menuPreview = null;
        var root = SaveRoot; var existing = store;
        Action<string>? trace = fixtureId == Guid.Empty ? null : Log;
        menuReads.Begin(controller.Lifecycle.Connection, () => {
            var source = existing ?? new PairedSaveStore(root, leaseTrace: trace);
            var slots = source.ListSlots(); var previews = new byte[slots.Length][];
            for (int i = 0; i < slots.Length; i++) {
                var slot = slots[i];
                if (slot.Pair != null && (slot.Condition == SlotCondition.Ready || slot.Condition == SlotCondition.RecoveryAvailable))
                    previews[i] = source.Load(slot.Slot, slot.Pair.Id).HollowKnight;
            }
            return new MenuSaveRead(source) { Slots = slots, Previews = previews };
        });
        menu?.Waiting("Reading Hollow Sols saves...");
#endif
    });
#if HOLLOW_KNIGHT
    internal byte[] PreviewSave(SlotSummary slot)
    {
        int index = slot.Slot.Number - 1;
        if (menuPreview?.Slots == null || !ReferenceEquals(menuPreview.Slots[index], slot) || menuPreview.Previews?[index] == null)
            throw new InvalidOperationException("This save preview has been retired.");
        return menuPreview.Previews[index];
    }
    private void UpdateMenuReads()
    {
        if (requestedQuit || activeToken.HasValue || controller?.Lifecycle.State != BridgeState.SaveSelection) {
            InvalidateMenuReads(); return;
        }
        if (!menuReads.Poll(controller.Lifecycle.Connection, out var read)) return;
        store = read!.Store;
        if (read.Slots != null) {
            menuPreview = read; menu!.Slots(read.Slots);
            Log("PAIRED_MENU_READ_READY slots=" + read.Slots.Length + " workerIO=True");
        } else {
            var pair = read.Pair!; var loaded = read.Loaded!;
            long version = menuReads.Version; Guid connection = controller.Lifecycle.Connection;
            Action start = () => ExecuteUI(() => {
                if (requestedQuit || activeToken.HasValue || menuReads.Version != version
                    || controller.Lifecycle.State != BridgeState.SaveSelection || controller.Lifecycle.Connection != connection) return;
                StartSelected(pair, false, loaded);
            });
            if (loaded.Recovered) menu!.Notice(loaded.RecoveryNotice!, start);
            else start();
        }
    }
    private void InvalidateMenuReads() { menuReads.Cancel(); menuPreview = null; }
#endif
    internal void CancelBridge()
    {
#if !HOLLOW_KNIGHT
        if (requestedQuit || controller == null || controller.Lifecycle.State == BridgeState.Active) return;
        // Back can arrive in the same frame as HK's Load. Keep the title covered
        // until that session is drained and its native save context is released.
        if (pendingVanilla || returningNative || controller.Lifecycle.State == BridgeState.Disconnecting) return;
        if (controller.Lifecycle.State == BridgeState.Connected || controller.Lifecycle.State == BridgeState.Loading || controller.Lifecycle.State == BridgeState.Ending) {
            menu?.Waiting("Disconnecting Hollow Sols Bridge...");
            RequestSessionEnd(true); return;
        }
#endif
        if(requestedQuit || activeToken.HasValue)return; // Ignore a retired menu's queued Back event.
#if HOLLOW_KNIGHT
        InvalidateMenuReads();
#endif
        if (controller?.Lifecycle.State == BridgeState.SaveSelection || controller?.Lifecycle.State == BridgeState.Connected) { menu?.Close(); return; }
        DisarmReports(); controller?.Fail("Connection cancelled."); transport?.Dispose(); transport = null; RestoreVanillaMenu();
    }
    internal void SelectSlot(SlotSummary slot)
    {
        if(requestedQuit || activeToken.HasValue || controller?.Lifecycle.State!=BridgeState.SaveSelection)return;
#if HOLLOW_KNIGHT
        if (menuReads.Busy) return;
#endif
        ExecuteUI(() => {
            if (controller?.Lifecycle.State != BridgeState.SaveSelection || activeToken.HasValue) throw new InvalidOperationException("Finish the current paired session before selecting a slot.");
            if (slot.Condition == SlotCondition.Empty) menu!.Modes(choice => ExecuteUI(() => BeginSelectedSlot(store!.ReserveCreation(slot.Slot, choice), true)));
            else if (slot.Pair != null && slot.Condition != SlotCondition.Unrecoverable) BeginSelectedSlot(slot.Pair, false);
            else menu!.Notice("This Hollow Sols save could not be recovered. Its original data has been preserved.", ShowSlots);
        });
    }
    internal void ConfirmDelete(SlotSummary slot)
    {
        if (requestedQuit || activeToken.HasValue || controller?.Lifecycle.State != BridgeState.SaveSelection) return;
#if HOLLOW_KNIGHT
        if (menuReads.Busy) return;
#endif
        if (slot.Condition == SlotCondition.Empty) return;
        ExecuteUI(() => {
        var confirmation = store!.PrepareDeletion(slot.Slot);
        menu!.Confirm("Delete Hollow Sols Slot " + slot.Slot.Number + " and its paired Nine Sols save?", "Yes", "No", () => ExecuteUI(() => {
            if (activeToken.HasValue || controller!.Lifecycle.State != BridgeState.SaveSelection || saves?.Busy == true) throw new InvalidOperationException("Finish saving before deleting a paired slot.");
            store!.DeleteConfirmed(confirmation); ShowSlots();
        }), ShowSlots);
        });
    }
    partial void BeginSelectedSlot(SavePair pair, bool creating);
    private void Update()
    {
        if (controller == null) return;
        // Cleanup failures must never starve an explicit process-close request.
        if(FinalizeQuitDeadline()){Application.Quit();return;}
        try
        {
            menu?.Update();
            try { UpdateFailureReporting(); } catch (Exception reportError) { Logger.LogWarning("REPORT_UI_UNAVAILABLE " + reportError.GetType().Name); }
            if (controller.Lifecycle.State == BridgeState.Vanilla) {
#if HOLLOW_KNIGHT
                bool title = UnitySceneManager.GetActiveScene().name == "Menu_Title";
#else
                bool title = UnitySceneManager.GetActiveScene().name == "TitleScreenMenu";
#endif
                if (title) CaptureBackground();
                else if (backgroundCaptured) { Application.runInBackground = previousBackground; backgroundCaptured = false; }
                if (transport?.Failure != null) { transport.Dispose(); transport = null; deferredActivation = null; nextPassiveRetry = Now + 1; }
                if (transport == null && Now >= nextPassiveRetry) transport = new ControlTransport(Side);
            }
            UpdateMenuFixture();
            UpdateNativeSession();
            if (requestedQuit && (Now >= quitDeadline || activeToken == null && controller.Lifecycle.State == BridgeState.Vanilla)) {
                if (Now >= quitDeadline) Log("PAIRED_QUIT_DEADLINE previousCommittedCheckpointPreserved=True finalSaveUnconfirmed=True");
                if (fixtureId != Guid.Empty && fixtureScenario.StartsWith("quit-", StringComparison.Ordinal) && activeToken == null && controller.Lifecycle.State == BridgeState.Vanilla) UpdateMenuFixture();
                finalizedQuit = true; Application.Quit(); return;
            }
            if (transport != null)
            {
                int count = 0;
                while (count++ < 64)
                {
                    ControlMessage? message;
                    if (deferredActivation != null) {
                        if (!menu!.FixtureReady) break;
                        message = deferredActivation; deferredActivation = null;
                    } else if (!transport.TryReceive(out message)) break;
#if !HOLLOW_KNIGHT
                    if (bridgeAudioFailed && message!.Kind == ControlKind.Hello) throw new InvalidOperationException("Native audio is unavailable. Restart Nine Sols before reconnecting.");
#endif
                    if (controller.Lifecycle.State == BridgeState.Vanilla && message!.Kind == ControlKind.Hello && !menu!.FixtureReady) { deferredActivation = message; break; }
                    var previous = controller.Lifecycle.State;
                    var native = controller.Receive(message!, Now);
                    if (previous == BridgeState.Vanilla && controller.Lifecycle.State != BridgeState.Vanilla) {
                        BeginReportAttempt();
                        CaptureBackground(); displayedFailure = null; menu!.Waiting("Waiting to establish bridge connection...");
                        Log("BRIDGE_ACTIVATION_ADOPTED counterpartButton=True localButton=False");
                    }
                    if (previous != controller.Lifecycle.State) StateChanged();
                    if (native != null) NativeOperation(native);
                    if (transport == null || controller.Lifecycle.State == BridgeState.Failed) break;
                }
                if (transport == null) return;
                // A final authenticated Disconnect may already be queued when
                // the reader observes EOF. Deliver it before treating pipe loss
                // as an unexpected crash.
                if (transport.Failure != null) {
                    if (controller.Lifecycle.State == BridgeState.Disconnecting && !activeToken.HasValue) return;
                    throw new IOException(transport.Failure);
                }
                if (Now >= nextHeartbeat) { nextHeartbeat = Now + .5; controller.Heartbeat(); }
                while (controller.TryTakeOutgoing(out var outgoing)) transport.Send(outgoing!);
                controller.Tick(Now);
                if (controller.Lifecycle.State == BridgeState.Failed) throw new IOException(controller.Lifecycle.Failure);
            }
#if HOLLOW_KNIGHT
            UpdateMenuReads();
#endif
        }
        catch (Exception error) { Fail(error); }
    }
#if HOLLOW_KNIGHT
    private void LateUpdate()=>menu?.EnforceLoadingHandoff();
#endif
    private void StateChanged()
    {
        Log("MENU_BRIDGE_STATE state=" + controller!.Lifecycle.State + " connection=" + controller.Lifecycle.Connection);
#if !HOLLOW_KNIGHT
        UpdateBridgeAudio();
#endif
        if (controller.Lifecycle.State == BridgeState.Active) sessionActivated = true;
        if (pendingVanilla && activeToken == null && (controller.Lifecycle.State == BridgeState.SaveSelection || controller.Lifecycle.State == BridgeState.Connected)) { BeginDisconnect(); return; }
        if (controller.Lifecycle.State == BridgeState.SaveSelection)
        {
            ShowSlots();
        }
        else if (controller.Lifecycle.State == BridgeState.Connected) {
            menu!.Waiting("Bridge connected. Waiting for Hollow Knight to select a save...");
#if !HOLLOW_KNIGHT
            if (locallyActivated) NativePairedGame.TryFocusHollowKnight(Log);
#endif
        }
    }
    private void NativeOperation(ControlMessage message) { HandleNativeOperation(message); }
    partial void HandleNativeOperation(ControlMessage message);
    private void Fail(Exception error)
    {
#if HOLLOW_KNIGHT
        InvalidateMenuReads();
#endif
#if !HOLLOW_KNIGHT
        RestoreFixtureBindings();
#endif
        Log("MENU_BRIDGE_FAILURE " + error);
        bool peerChannelFailure = error is IOException && transport?.Failure != null
            || error.Message == "VISUAL_CHANNEL_STOPPED" || error.Message == "PEER_DISCONNECTED" || error.Message == "CONTACT_CHANNEL_STOPPED";
        try { CaptureReportFailure(error.ToString(), peerChannelFailure); } catch (Exception reportError) { Logger.LogWarning("REPORT_CAPTURE_UNAVAILABLE " + reportError.GetType().Name); }
        if (fixtureId != Guid.Empty && fixtureScenario.StartsWith("crash-", StringComparison.Ordinal)) {
            string crashMarker = Path.Combine(Path.GetDirectoryName(fixtureFolder)!, "fixture-crash.trigger");
            // Gameplay IPC can observe EOF before the next fixture Update.
            // An exact authorized crash marker must allow native survivor
            // cleanup, rather than the harness's fail-fast Application.Quit.
            if (File.Exists(crashMarker) && File.ReadAllText(crashMarker) == fixtureId.ToString()) fixtureCrashObserved = true;
        }
        if (fixtureId != Guid.Empty && fixtureStage >= 0 && !fixtureCrashObserved) { File.WriteAllText(Path.Combine(fixtureFolder,"menu-fixture.failed"),fixtureId+"\n"+error.GetType().Name); fixtureStage = -1; finalizedQuit = true; Application.Quit(); }
        string detail = error.ToString();
        string notice = detail.Contains("Incompatible Hollow Sols control protocol", StringComparison.Ordinal)
            ? "The Hollow Sols bridge components use incompatible versions. Install matching components in both games, then try again."
            : activeToken.HasValue ? "The bridged session stopped because its connection or game state could not be maintained. Your committed saves have been preserved."
            : "The bridge connection could not be established or was interrupted. Return to the menu and try again.";
#if !HOLLOW_KNIGHT
        if (bridgeAudioFailed) notice = "Nine Sols' audio could not be synchronized. Your committed saves have been preserved. Restart Nine Sols, then reconnect.";
#endif
        if (displayedFailure == null) displayedFailure = notice;
        controller?.Fail("The bridge connection could not be established or was interrupted.");
        transport?.Dispose(); transport = null;
        AbortNativeSession();
        if (!activeToken.HasValue) ShowFailureNotice(displayedFailure, RestoreVanillaMenu);
    }
    internal void GameplayFailed(string reason, Exception? error) => Fail(new InvalidOperationException(reason, error));
    partial void InstallNativeLifecycle();
    partial void UpdateNativeSession();
    partial void AbortNativeSession();
    partial void InitializeMenuFixture();
    partial void UpdateMenuFixture();
    internal void RestoreVanillaMenu()
    {
#if HOLLOW_KNIGHT
        InvalidateMenuReads();
#endif
        DisarmReports();
        transport?.Dispose(); transport = null;
        if (controller?.Lifecycle.State == BridgeState.Failed || controller?.Lifecycle.State == BridgeState.Disconnecting)
            controller.NativeVanillaRestored(Now);
        deferredActivation = null;
        // Remote Disconnect also takes this path. Its vanilla intent is fully
        // consumed now, not an instruction to end the next fresh handshake.
        pendingVanilla = false;
#if !HOLLOW_KNIGHT
        locallyActivated = false;
        RestoreBridgeAudio();
#endif
        menu?.Close(); displayedFailure = null;
        if (backgroundCaptured) { Application.runInBackground = previousBackground; backgroundCaptured = false; }
    }
    private void OnDestroy()
    {
#if HOLLOW_KNIGHT
        InvalidateMenuReads();
        enduranceHooks?.UnpatchSelf();enduranceHooks=null;
#endif
#if !HOLLOW_KNIGHT
        RestoreBridgeAudio();
#endif
        Application.wantsToQuit -= CanQuit;
        Application.quitting -= NotifyOrderlyApplicationExit;
        UnitySceneManager.sceneLoaded -= OnScene; transport?.Dispose(); menu?.Dispose();
        if (Instance == this) Instance = null;
    }
    internal bool NativeQuitRequested() => CanQuit();
#if !HOLLOW_KNIGHT
    private void LateUpdate()
    {
        try { UpdateBridgeAudio(); }
        catch (Exception error) { Fail(error); }
    }
    private void UpdateBridgeAudio()
    {
        if (bridgeAudioFailed || controller == null) return;
        try { bridgeAudio?.Update(controller.Lifecycle.State); }
        catch { bridgeAudioFailed = true; throw; }
    }
    private void RestoreBridgeAudio()
    {
        try { bridgeAudio?.Restore(); }
        catch (Exception error) { bridgeAudioFailed = true; Log("NS_BRIDGE_AUDIO_RESTORE_UNCONFIRMED restartRequired=True preferencesUnchanged=True " + error); }
    }
#endif
    private bool CanQuit()
    {
        if(FinalizeQuitDeadline())return true;
        if (finalizedQuit || fixtureId != Guid.Empty && !fixtureScenario.StartsWith("quit-", StringComparison.Ordinal) || controller == null || activeToken == null && controller.Lifecycle.State == BridgeState.Vanilla) return true;
        if (!requestedQuit) {
            requestedQuit = true; quitDeadline = Now + 10;
            try {
                if (activeToken.HasValue) { if (!returningNative) RequestSessionEnd(true); }
                else if (controller.Lifecycle.State == BridgeState.Failed) RestoreVanillaMenu();
                else BeginDisconnect();
            } catch (Exception error) { Fail(error); }
        }
        return false;
    }
    private bool FinalizeQuitDeadline()
    {
        if(!requestedQuit || finalizedQuit || Now<quitDeadline)return false;
        finalizedQuit=true;
        Log("PAIRED_QUIT_DEADLINE previousCommittedCheckpointPreserved=True finalSaveUnconfirmed=True cleanupDeadlineSeconds=10");
        return true;
    }
    private void OnApplicationQuit() {
        NotifyOrderlyApplicationExit();
#if !HOLLOW_KNIGHT
        RestoreFixtureBindings();
#endif
        Log("MENU_NATIVE_APPLICATION_QUIT fixtureStage=" + fixtureStage + " time=" + Now + " state=" + controller?.Lifecycle.State);
    }
}
