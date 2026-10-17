using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using BepInEx;
using HarmonyLib;
using HollowSols.Foundation;
using UnityEngine;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

namespace YiGodhome;

public sealed partial class HollowSolsMenuPlugin
{
    private SessionToken? activeToken;
    private SavePair? activePair;
    private bool creating, nativeBegun, localLoaded, captureInFlight, returningNative, localEndReady, peerEndReady;
    private double nextEndingFixtureTrace;
    private int endingFixtureTraceCount;
    private bool finalSaveDone, pendingVanilla, sessionActivated, nativeDataRestored;
#if HOLLOW_KNIGHT
    private bool finalSaveQueued, defeatedNotice, preparationSent;
    private double nextIntroductionWait;
    private System.Threading.Tasks.Task<PairedCheckpoint>? checkpointPublication;
    private System.Threading.Tasks.Task<byte[]>? checkpointRead;
    private CheckpointRequest publicationRequest;
    private long publicationStarted;
#endif
    private int drainFrames;
    private long generation;
    private GameObject? gameplayObject;
    private BaselinePlugin? gameplay;
    private PairedSaveCoordinator? saves;
    private NativeFileExchange? exchange;
    private CheckpointRequest? checkpoint;
    private readonly Queue<(byte[] Bytes, Action<bool> Done)> saveQueue = new Queue<(byte[], Action<bool>)>();
    private Action<bool>? checkpointDone;
    private Action? vanillaContinuation;
#if !HOLLOW_KNIGHT
    private bool nsDirty, nsSaveRequested;
    private bool campaignDefeatSent;
    private long nsCapturedRevision;
    private readonly ConcurrentQueue<SessionToken> nsSaveEvents = new ConcurrentQueue<SessionToken>();
#endif
    private double nativeReturnDeadline;
    private bool nativeReturnFinished, nativeReturnFailed;
    private bool nativeReturnTimedOut;
    private double nativeReturnTraceAt;
    private int nativeReturnTraceCount;

    partial void InstallNativeLifecycle()
    {
        NativePairedGame.Install(); exchange = new NativeFileExchange(SaveRoot);
#if HOLLOW_KNIGHT
        NativeSaveContext.HollowKnightSaveRequested = NativeSave;
#else
        NativeSaveContext.NineSolsSaveRequested = token => nsSaveEvents.Enqueue(token);
#endif
    }
    partial void BeginSelectedSlot(SavePair pair, bool fresh)
    {
#if HOLLOW_KNIGHT
        ExecuteUI(() => {
            if (requestedQuit || activeToken.HasValue || controller?.Lifecycle.State != BridgeState.SaveSelection) return;
            if (fresh) { StartSelected(pair, true, null); return; }
            menuPreview = null;
            var source = store!;
            menuReads.Begin(controller.Lifecycle.Connection, () => new MenuSaveRead(source) {
                Pair = pair, Loaded = source.Load(pair.Slot, pair.Id)
            });
            menu!.Waiting("Reading Hollow Sols save...");
        });
#else
        throw new InvalidOperationException("HK selects paired saves.");
#endif
    }
#if HOLLOW_KNIGHT
    private void StartSelected(SavePair pair, bool fresh, PairedCheckpoint? loaded)
    {
        InvalidateMenuReads();
        NativePairedGame.RememberVanilla();
        activePair = pair; creating = fresh; generation = loaded?.Generation ?? 0;
        activeToken = controller!.Select(pair, Now, fresh);
        SyncReportContext();
        NativeSaveContext.Enter(Side, activeToken.Value, loaded?.HollowKnight, pair.Choice.Mode, pair.Choice.FullGodhomeAccess);
        ResetSessionFlags(); menu!.BeginLoading();
        NativePairedGame.Begin(pair, fresh, () => nativeBegun = true);
        FixtureLoadingReactivation();
        Log("PAIRED_NATIVE_LOAD_BEGIN slot=" + pair.Slot.Number + " pair=" + pair.Id + " session=" + activeToken.Value.Session + " creating=" + fresh + " mode=" + pair.Choice.Mode);
    }
    private void NativeSave(SessionToken token, byte[] bytes, Action<bool> done)
    {
        if (!activeToken.HasValue || !activeToken.Value.Equals(token)) { done(false); return; }
        if (controller!.Lifecycle.State == BridgeState.Loading) { done(true); return; } // Initial bytes join the first coordinated creation commit.
        if (!controller.Lifecycle.AcceptsSave(token)) { done(false); return; }
        if (saveQueue.Count >= 64) { done(false); GameplayFailed("PAIRED_SAVE_QUEUE_FULL", null); return; }
        saveQueue.Enqueue(((byte[])bytes.Clone(), done));
    }
#endif
    private void ResetSessionFlags()
    {
#if HOLLOW_KNIGHT
        if(checkpointPublication!=null && !checkpointPublication.IsCompleted)throw new IOException("The previous isolated checkpoint is still finishing.");
        checkpointPublication=null;checkpointRead=null;
#endif
        localLoaded = captureInFlight = returningNative = localEndReady = peerEndReady = finalSaveDone = false;
        sessionActivated = false; endingPrepared = false;
        nativeDataRestored = false;
        drainFrames = 0; checkpoint = null; checkpointDone?.Invoke(false); checkpointDone = null;
#if HOLLOW_KNIGHT
        finalSaveQueued = false;
        preparationSent = false;
#else
        nsDirty = nsSaveRequested = campaignDefeatSent = false;
        nsCapturedRevision = 0;
        while (nsSaveEvents.TryDequeue(out _)) { }
#endif
        while (saveQueue.Count > 0) saveQueue.Dequeue().Done(false);
    }
    private void AttachGameplay()
    {
        if (gameplay != null || !activeToken.HasValue) return;
        string mode = Side == GameSide.HollowKnight ? "input-hk" : "input-ns";
        YiGodhome.Bridge.SessionConfiguration.EnterPaired(mode);
        string run = Path.Combine(SaveRoot, "runtime", "runs", Side.ToString(), activeToken.Value.Session.ToString());
        Directory.CreateDirectory(run);
        Environment.SetEnvironmentVariable("YIGODHOME_SESSION", activeToken.Value.Session.ToString());
        Environment.SetEnvironmentVariable("YIGODHOME_MODE", mode);
        Environment.SetEnvironmentVariable("YIGODHOME_SMOKE_SECONDS", "0");
        Environment.SetEnvironmentVariable("YIGODHOME_BOOT_LOG", Path.Combine(run, "bootstrap.log"));
        File.WriteAllText(Path.Combine(run, "bootstrap.log"), "Paired native session; early scoped guard remains active.\n");
        YiGodhome.Geometry.Codec.ExperimentalSceneAuthorization = (session, scene) => NativeSaveContext.Session.HasValue
            && NativeSaveContext.Session.Value.Session == session && NativeSaveContext.Mode != GameMode.Godhome
            && scene != "Menu_Title" && scene != "Quit_To_Menu" && scene != "Opening_Sequence" && !scene.Contains("Intro", StringComparison.OrdinalIgnoreCase) && !scene.Contains("Cinematic", StringComparison.OrdinalIgnoreCase);
        gameplayObject = new GameObject("Hollow Sols paired gameplay"); gameplayObject.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(gameplayObject); gameplay = gameplayObject.AddComponent<BaselinePlugin>(); gameplayObject.SetActive(true);
        gameplay.ObserveCurrentScene();
        Log("PAIRED_GAMEPLAY_ADAPTER_ATTACHED session=" + activeToken.Value.Session + " legacyCombatPreserved=True");
    }
    partial void HandleNativeOperation(ControlMessage message)
    {
        switch (message.Kind)
        {
            case ControlKind.Load:
#if !HOLLOW_KNIGHT
                store ??= new PairedSaveStore(SaveRoot, leaseTrace: fixtureId == Guid.Empty ? null : Log);
                activeToken = message.Session!.Value; activePair = store.InspectPair(activeToken.Value.Slot, activeToken.Value.Pair);
                SyncReportContext();
                creating = message.Creating; var loaded = creating ? null : store.Load(activeToken.Value.Slot, activeToken.Value.Pair);
                generation = loaded?.Generation ?? 0; NativePairedGame.RememberVanilla();
                Log("NS_PAIRED_LOAD_BEGIN generation=" + generation + " creating=" + creating);
                NativeSaveContext.Enter(Side, activeToken.Value, loaded?.NineSols, activePair.Choice.Mode, activePair.Choice.FullGodhomeAccess);
                ResetSessionFlags(); nativeBegun = false; menu!.Waiting("Loading the paired Hollow Sols save. Waiting for Hollow Knight...");
#endif
                break;
            case ControlKind.PrepareNative:
#if !HOLLOW_KNIGHT
                if (!nativeBegun) { nativeBegun = true; menu!.Close(); AttachGameplay(); }
#endif
                break;
            case ControlKind.Start:
#if !HOLLOW_KNIGHT
                var committedStart = store!.Load(activeToken!.Value.Slot, activeToken.Value.Pair);
                if (committedStart.Generation != message.Generation || creating && (committedStart.Recovered || message.Generation != 1 || committedStart.Session != activeToken.Value.Session))
                    throw new IOException("The selected pair has not committed the agreed starting generation.");
                if (!creating && generation != message.Generation) throw new IOException("The loaded Nine Sols checkpoint differs from the starting generation.");
                generation = message.Generation; sessionActivated = true;
#endif
                break;
            case ControlKind.Checkpoint:
#if !HOLLOW_KNIGHT
                var request = new CheckpointRequest(message.Session!.Value, message.Transaction, message.Generation);
                if (request.PreviousGeneration != generation) throw new IOException("The paired snapshot generation does not match this Nine Sols session.");
                if (captureInFlight || checkpoint.HasValue) throw new IOException("A native snapshot is already being captured or committed.");
                checkpoint = request;
                captureInFlight = true;
                NativePairedGame.Capture(bytes => { nsCapturedRevision = NativeSaveContext.SnapshotRevision; captureInFlight = false; exchange!.Publish(request.Session, request.Transaction, bytes); controller!.Checkpoint(ControlKind.CheckpointReady, request); nsDirty = NativeSaveContext.ContentRevision > nsCapturedRevision; });
#endif
                break;
            case ControlKind.CheckpointReady:
#if HOLLOW_KNIGHT
                if (!checkpoint.HasValue || checkpoint.Value.Transaction != message.Transaction) throw new IOException("Stale native snapshot response.");
                var pending = checkpoint.Value;
                if(checkpointRead!=null || checkpointPublication!=null)throw new IOException("A checkpoint snapshot read or publication is already in flight.");
                publicationStarted=System.Diagnostics.Stopwatch.GetTimestamp();publicationRequest=pending;
                // File opening, reparse guards, read and checksum can stall on
                // storage/antivirus. No part of this immutable read needs Unity.
                checkpointRead=exchange!.ReadAsync(pending.Session,pending.Transaction);
                Log("PAIRED_CHECKPOINT_READ_STARTED frame="+Time.frameCount+" generation="+pending.PreviousGeneration+" readOnGameThread=False");
#endif
                break;
            case ControlKind.CheckpointCommitted:
#if !HOLLOW_KNIGHT
                if (!checkpoint.HasValue || checkpoint.Value.Transaction != message.Transaction) throw new IOException("Foreign checkpoint commit acknowledgement.");
                if (message.Generation <= checkpoint.Value.PreviousGeneration) throw new IOException("The paired commit did not advance its generation.");
                generation = message.Generation; checkpoint = null;
                nsSaveRequested = false;
#endif
                break;
            case ControlKind.SaveNeeded:
#if HOLLOW_KNIGHT
                if (activeToken.HasValue && controller!.Lifecycle.State == BridgeState.Active) NativePairedGame.Capture(_ => { });
#endif
                break;
            case ControlKind.NativeDefeat:
#if HOLLOW_KNIGHT
                if (NativeSaveContext.Mode == GameMode.Godhome) throw new InvalidOperationException("Godhome defeat must use its existing encounter lifecycle.");
                NativePairedGame.ExperimentalDeath(message.Session!.Value, () => {
                    if (!activeToken.HasValue || !activeToken.Value.Equals(message.Session.Value)) return;
                    defeatedNotice = GameManager.instance.playerData.permadeathMode == 2; RequestSessionEnd(false);
                });
#endif
                break;
            case ControlKind.End:
                PrepareEnding(); break;
            case ControlKind.EndReady:
                peerEndReady = true;
#if !HOLLOW_KNIGHT
                finalSaveDone = true;
#endif
                break;
            case ControlKind.Disconnect:
                pendingVanilla = true; RestoreVanillaMenu(); break;
        }
    }
    partial void UpdateNativeSession()
    {
        if (!activeToken.HasValue) return;
        var token = activeToken.Value; var state = controller!.Lifecycle.State;
#if !HOLLOW_KNIGHT
        while (nsSaveEvents.TryDequeue(out var savedToken)) if (savedToken.Equals(token)) nsDirty = NativeSaveContext.ContentRevision > nsCapturedRevision;
#endif
        if (returningNative)
        {
            if (nativeReturnFinished && Now >= nativeReturnTraceAt && nativeReturnTraceCount < 3) {
                nativeReturnTraceAt = Now + 5; nativeReturnTraceCount++;
                Log("PAIRED_NATIVE_RETURN_GATE ready="+menu!.NativeReturnReady+" fixtureInputReady="+menu.FixtureReady+" scene="+UnitySceneManager.GetActiveScene().name+" timeScale="+Time.timeScale);
                menu.FixtureEvidence();
            }
            if (nativeReturnFinished && menu!.NativeReturnReady) { if (nativeReturnTimedOut) Time.timeScale = 1; CompleteNativeReturn(); return; }
            if (Now > nativeReturnDeadline && !nativeReturnTimedOut) {
                nativeReturnTimedOut = true; nativeReturnFailed = true; Time.timeScale = 0;
                controller.Fail("Native menu return could not be confirmed."); transport?.Dispose(); transport = null;
                Log("NATIVE_MENU_RETURN_TIMEOUT nativeMenuConfirmed=False committedDataPreserved=True scopedSaveGuardRetained=True");
                try { CaptureReportFailure("NATIVE_MENU_RETURN_TIMEOUT"); } catch (Exception reportError) { Log("REPORT_CAPTURE_UNAVAILABLE " + reportError.GetType().Name); }
                ShowFailureNotice("Hollow Sols could not return safely to the main menu. Your committed saves are preserved. Close and relaunch this game to continue.", () => { finalizedQuit = true; Application.Quit(); }, shutdown: true);
            }
            return;
        }
        if (state == BridgeState.Loading)
        {
#if !HOLLOW_KNIGHT
            if(gameplay?.NativePairReady==true)GodhomeExperience.Normalize();
#endif
#if HOLLOW_KNIGHT
            if (!preparationSent && NativePairedGame.WaitingForIntroductionInput && Now >= nextIntroductionWait) { controller.IntroductionWaiting(token, Now); nextIntroductionWait = Now + 1; }
            if (nativeBegun && NativePairedGame.Ready && !preparationSent && !FixtureHoldLateCancellation()) { AttachGameplay(); controller.PrepareNative(token); preparationSent = true; }
#endif
            if (nativeBegun && !localLoaded && !captureInFlight && gameplay?.NativePairReady == true)
            {
                captureInFlight = true;
                NativePairedGame.Capture(bytes => {
                    captureInFlight = false;
                    if (!activeToken.HasValue || !activeToken.Value.Equals(token) || controller.Lifecycle.State != BridgeState.Loading) return;
#if !HOLLOW_KNIGHT
                    nsCapturedRevision = NativeSaveContext.SnapshotRevision;
                    exchange!.Publish(token, Guid.Empty, bytes);
#endif
                    localLoaded = true; controller.NativeLoaded(token, false, Now);
                });
            }
#if HOLLOW_KNIGHT
            if (controller.Lifecycle.RestorationsReady && !controller.Lifecycle.RecoveryNoticePending)
            {
                if (creating) { var initial = store!.CompleteCreation(token, NativeSaveContext.Snapshot(token), exchange!.Read(token, Guid.Empty)); generation = initial.Generation; }
                saves = new PairedSaveCoordinator(controller.Lifecycle, store!); controller.Start(token, Now, generation);
                sessionActivated = true;
                Log("PAIRED_SESSION_ACTIVE slot=" + token.Slot.Number + " pair=" + token.Pair + " generation=" + generation + " nativeIntroPreserved=True");
            }
#endif
        }
        else if (state == BridgeState.Active || state == BridgeState.Ending)
        {
#if !HOLLOW_KNIGHT
            GodhomeExperience.Normalize();
#endif
#if HOLLOW_KNIGHT
            if(checkpointRead?.IsCompleted==true) {
                var pending=publicationRequest;
                if(!checkpoint.HasValue || checkpoint.Value.Transaction!=pending.Transaction || !activeToken.HasValue || !activeToken.Value.Equals(pending.Session))throw new IOException("Stale checkpoint snapshot read completed.");
                var peerBytes=checkpointRead.GetAwaiter().GetResult();checkpointRead=null;
                long prepared=System.Diagnostics.Stopwatch.GetTimestamp();
                saves!.Submit(pending,GameSide.NineSols,peerBytes,Now);
                var publication=saves.PreparePublication(pending,Now);
                checkpointPublication=System.Threading.Tasks.Task.Run(publication);
                Log("PAIRED_CHECKPOINT_PUBLICATION_STARTED frame="+Time.frameCount+" generation="+pending.PreviousGeneration+" gameThreadPrepareMs="+((System.Diagnostics.Stopwatch.GetTimestamp()-prepared)*1000.0/System.Diagnostics.Stopwatch.Frequency).ToString("F3",System.Globalization.CultureInfo.InvariantCulture)
                    +" snapshotReadWallMs="+((prepared-publicationStarted)*1000.0/System.Diagnostics.Stopwatch.Frequency).ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+" readOnGameThread=False");
            }
            if(checkpointPublication?.IsCompleted==true) {
                var pending=publicationRequest;
                if(!checkpoint.HasValue || checkpoint.Value.Transaction!=pending.Transaction || !activeToken.HasValue || !activeToken.Value.Equals(pending.Session))throw new IOException("Stale checkpoint publication completed.");
                var committed=saves!.CompletePublication(pending,checkpointPublication.GetAwaiter().GetResult(),Now);
                checkpointPublication=null;generation=committed.Generation;checkpoint=null;
                var done=checkpointDone;checkpointDone=null;
                controller!.Checkpoint(ControlKind.CheckpointCommitted,new CheckpointRequest(pending.Session,pending.Transaction,generation));done?.Invoke(true);
                Log("PAIRED_CHECKPOINT_COMMITTED slot="+pending.Session.Slot.Number+" pair="+pending.Session.Pair+" generation="+generation+" transaction="+committed.Transaction);
                Log("PAIRED_CHECKPOINT_COST frame="+Time.frameCount+" generation="+generation+" publicationWallMs="+((System.Diagnostics.Stopwatch.GetTimestamp()-publicationStarted)*1000.0/System.Diagnostics.Stopwatch.Frequency).ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+" storeOnGameThread=False");
            }
            if (checkpoint == null && saveQueue.Count > 0)
            {
                var item = saveQueue.Dequeue(); checkpoint = saves!.Begin(token, generation, Now); checkpointDone = item.Done;
                saves.Submit(checkpoint.Value, GameSide.HollowKnight, item.Bytes, Now); controller.Checkpoint(ControlKind.Checkpoint, checkpoint.Value);
            }
            saves?.Tick(Now);
            if (checkpoint.HasValue && saves?.Busy != true) throw new TimeoutException("Paired checkpoint timed out.");
#else
            if (state == BridgeState.Active && NativeSaveContext.Mode != GameMode.Godhome && gameplay?.NativeCampaignDeath == true && !campaignDefeatSent) {
                campaignDefeatSent = true; controller.NativeDefeat(token); Log("NS_NATIVE_CAMPAIGN_DEFEAT actualNativeDeath=True slot=" + token.Slot.Number);
            }
            if (state == BridgeState.Active && nsDirty && !nsSaveRequested && !captureInFlight) { controller.RequestSave(token); nsSaveRequested = true; }
#endif
            if (state == BridgeState.Ending)
            {
                if(FixtureCommandsAllowed && endingFixtureTraceCount<12 && Now>=nextEndingFixtureTrace) {
                    nextEndingFixtureTrace=Now+1;endingFixtureTraceCount++;
                    Log("PAIRED_ENDING_GATE side="+Side+" local="+localEndReady+" peer="+peerEndReady+" saved="+finalSaveDone+" checkpoint="+checkpoint.HasValue+" callbacks="+saveQueue.Count+" drained="+(gameplay?.PairOutcomesDrained!=false)+" pause="+Time.timeScale);
                }
                drainFrames = !captureInFlight && gameplay?.PairOutcomesDrained != false ? drainFrames + 1 : 0;
                if (drainFrames >= 3 && !localEndReady)
                {
                    localEndReady = true;
#if !HOLLOW_KNIGHT
                    controller.EndReady(token);
#endif
                }
#if HOLLOW_KNIGHT
                if (localEndReady && !sessionActivated && !finalSaveDone) { finalSaveDone = true; controller.EndReady(token); }
                if (sessionActivated && localEndReady && peerEndReady && !finalSaveQueued && checkpoint == null && saveQueue.Count == 0)
                {
                    finalSaveQueued = true;
                    GameManager.instance.SaveGame(success => { if (!success) { GameplayFailed("FINAL_PAIRED_SAVE_FAILED", null); return; } finalSaveDone = true; controller.EndReady(token); });
                }
#endif
                if (finalSaveDone && localEndReady && peerEndReady && !returningNative) ReturnNative(false);
            }
        }
    }
    internal void RequestSessionEnd(bool vanilla)
    {
        if (activeToken == null) { if (vanilla) BeginDisconnect(); return; }
        pendingVanilla |= vanilla;
        if (controller!.Lifecycle.State == BridgeState.Active || controller.Lifecycle.State == BridgeState.Loading) controller.End(activeToken.Value, Now);
        if (!endingPrepared) PrepareEnding();
    }
    private bool endingPrepared;
    internal bool VanillaGameplayAllowed => !requestedQuit && !pendingVanilla && !returningNative && !activeToken.HasValue && controller?.Lifecycle.State == BridgeState.Vanilla;
    private void PrepareEnding() { if(endingPrepared)return; endingPrepared=true; Log("PAIRED_SESSION_ENDING activated=" + sessionActivated); gameplay?.SuspendPairedGameplay(); Time.timeScale = 0; drainFrames = 0; }
    internal void RequestVanilla(Action continuation)
    { if(pendingVanilla || returningNative || controller?.Lifecycle.State==BridgeState.Disconnecting)return; vanillaContinuation = continuation; RequestSessionEnd(true); }
    private void BeginDisconnect()
    {
        DisarmReports();
        if(controller!.Lifecycle.State==BridgeState.Disconnecting)return;
        if (controller!.Lifecycle.State == BridgeState.Vanilla) { vanillaContinuation?.Invoke(); vanillaContinuation = null; return; }
        controller.Disconnect(Now); pendingVanilla = true;
        // Publish the final authenticated frame before invalidating transport.
        try { while (controller.TryTakeOutgoing(out var message)) transport?.Send(message!); }
        catch (IOException error) {
            // Both fully drained sides may request vanilla concurrently. The
            // first closes after its final frame; the second no longer needs a
            // live peer to restore an already-ended native session safely.
            Log("BRIDGE_DISCONNECT_DELIVERY_UNCONFIRMED drained=True reason="+error.GetType().Name);
        }
        StartCoroutine(FinishDisconnect());
    }
    private System.Collections.IEnumerator FinishDisconnect()
    {
        double deadline = Now + 5;
        while (transport?.WritesPending == true && transport.Failure == null && Now < deadline) yield return null;
        if (transport?.WritesPending == true) Log("BRIDGE_DISCONNECT_DELIVERY_UNCONFIRMED committedDataPreserved=True");
        RestoreVanillaMenu(); var continueVanilla = vanillaContinuation; vanillaContinuation = null; pendingVanilla = false; continueVanilla?.Invoke();
    }
    private void ReturnNative(bool failed)
    {
        if (!activeToken.HasValue || returningNative) return;
        returningNative = true; nativeReturnDeadline = Now + BridgeLifecycle.LoadingSeconds;
        nativeReturnTimedOut = false;
        nativeReturnTraceAt = Now; nativeReturnTraceCount = 0;
        nativeReturnFailed = failed; nativeReturnFinished = false;
        var token = activeToken.Value;
#if !HOLLOW_KNIGHT
        // Before PrepareNative, NS still owns its original fully initialized
        // title. Reloading it is not a gameplay return and strands its native
        // startup flow. Release only the paired context/modal we actually own.
        if (!nativeBegun && gameplayObject == null && UnitySceneManager.GetActiveScene().name == "TitleScreenMenu") {
            RestoreNativeData(token); menu!.Close(); Time.timeScale = 1;
            nativeReturnFinished = true;
            Log("NS_CANCELLED_LOAD_TITLE_RETAINED nativeGameplayStarted=False nativeSceneReload=False contextRestored=True");
            return;
        }
#endif
        NativePairedGame.PrepareReturn();
        if (gameplayObject != null) UnityEngine.Object.DestroyImmediate(gameplayObject);
        gameplay = null; gameplayObject = null;
        if(FixtureCommandsAllowed)Log("PAIRED_NATIVE_RETURN_BEGIN side="+Side+" scene="+UnitySceneManager.GetActiveScene().name+" scale="+Time.timeScale);
        NativePairedGame.Return(() => nativeReturnFinished = true);
    }
    private void CompleteNativeReturn()
    {
            var token = activeToken!.Value; bool failed = nativeReturnFailed;
            RestoreNativeData(token); var endedPair = activePair;
            activeToken = null; activePair = null; returningNative = false; saves?.Cancel(); saves = null; checkpoint = null;
            if (!failed) { controller!.NativeDrained(token, Now); StateChanged(); if (pendingVanilla && controller.Lifecycle.Session == null && controller.Lifecycle.State != BridgeState.Disconnecting) BeginDisconnect();
#if HOLLOW_KNIGHT
                if (defeatedNotice && endedPair != null) { defeatedNotice = false; menu?.Notice("This Steel Soul run is defeated. Clear both paired saves only by confirming deletion from the Hollow Sols save menu.", ShowSlots); }
#endif
            }
            else { controller!.NativeVanillaRestored(Now); string notice = displayedFailure ?? "The bridge connection was lost. Your committed saves have been preserved."; displayedFailure = null; ShowFailureNotice(notice, RestoreVanillaMenu); }
            nativeReturnFinished = false;
    }
    private void RestoreDataBeforeTitle(string scene)
    {
#if HOLLOW_KNIGHT
        bool title = scene == "Menu_Title";
#else
        bool title = scene == "TitleScreenMenu";
#endif
        if (title && returningNative && activeToken.HasValue) RestoreNativeData(activeToken.Value);
    }
    private void RestoreNativeData(SessionToken token)
    {
        if (nativeDataRestored) return;
        if (NativeSaveContext.Session.HasValue && NativeSaveContext.Session.Value.Equals(token)) NativeSaveContext.Leave(token);
        NativePairedGame.RestoreVanilla(); YiGodhome.Bridge.SessionConfiguration.LeavePaired();
        YiGodhome.Geometry.Codec.ExperimentalSceneAuthorization = null;
        Environment.SetEnvironmentVariable("YIGODHOME_SESSION", null); Environment.SetEnvironmentVariable("YIGODHOME_MODE", "menu");
        nativeDataRestored = true;
    }
    partial void AbortNativeSession()
    { saves?.Cancel(); checkpoint = null; while (saveQueue.Count > 0) saveQueue.Dequeue().Done(false); if (activeToken.HasValue && !returningNative) ReturnNative(true); }
    internal void ExecuteUI(Action action)
    {
        try { action(); }
        catch (Exception error) {
            if (!activeToken.HasValue && controller?.Lifecycle.State == BridgeState.SaveSelection) {
                Log("MODDED_SAVE_OPERATION_REFUSED " + error); menu?.Notice("This Hollow Sols save operation could not be completed. Existing data has been preserved. Return to the save menu and try again.", ShowSlots);
            } else Fail(error);
        }
    }
    internal void DefeatedRunLoaded()
    {
#if HOLLOW_KNIGHT
        defeatedNotice = true; RequestSessionEnd(false);
#else
        throw new InvalidOperationException("HK owns the Steel Soul defeated-run state.");
#endif
    }
}
