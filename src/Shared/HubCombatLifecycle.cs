using System;
using UnityEngine;
using YiGodhome.Bridge;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

namespace YiGodhome;

public sealed partial class BaselinePlugin
{
    private bool hubCombat;
#if !HOLLOW_KNIGHT
    private bool pendingVictoryReset;
    private bool hubReturnPrepared;
    private bool pendingSequenceAdvance;
    private NativeTargetProbe? retiredNativeTarget;
#endif
#if HOLLOW_KNIGHT
    private long hubEncounterGeometry = -1;
    private int hubCombatEntries;
    // The paired 600-second runner gives HK 580 seconds to preserve drain time.
    private int ScriptedHubEntryLimit => smokeSeconds==580 && SessionConfiguration.Current.RunFixtures && SessionConfiguration.Current.Cheats
        && SessionConfiguration.Current.Has("trace-probe") && EncounterProfile.Selected.Family==EncounterProfile.Collector ? 8 : 2;
#endif

    private void ReleaseCombatChannels(bool keepHits = false)
    {
        contactBridge = false;
        contactEndpoint?.Dispose(); contactEndpoint = null;
        if (!keepHits) { hitEndpoint?.Dispose(); hitEndpoint = null; }
#if HOLLOW_KNIGHT
        rageMask?.Dispose(); rageMask = null;
        if (!keepHits) { hostHits?.Dispose(); hostHits = null; contactSource?.Dispose(); contactSource = null; }
#else
        if (keepHits) retiredNativeTarget = nativeTarget;
        nativeTarget?.Dispose(); nativeTarget = null;
        hostContact?.Dispose(); hostContact = null;
        if (!keepHits) { pendingHitKinds.Clear(); retiredNativeTarget = null; }
#endif
    }

    private void UpdateHubCombat()
    {
        var peer = endpoint?.FreshEncounter;
#if HOLLOW_KNIGHT
        string scene = UnitySceneManager.GetActiveScene().name;
        long geometry = hubPresence?.GeometryFrame ?? -1;
        // Native HK has already selected/loaded the next room. Its old hit
        // generation must settle before the new target can be admitted.
        if (encounterStatus?.Phase == EncounterPhase.Advancing && encounterStatus.Matches(peer)
            && peer!.Phase == EncounterPhase.Ready && geometry > encounterStatus.GeometryFrame
            && endpoint?.FreshPose?.GeometryFrame == geometry
            && visualEndpoint?.FreshFrame?.GeometryFrame == geometry && visualPresenter?.ReadyForReveal == true)
        {
            ReleaseCombatChannels(); hitCompleted = false;
            controlSource?.Release(); controlSource = null;
            SetEncounter(new EncounterStatus(encounterStatus.Id, encounterStatus.GeometryFrame, EncounterPhase.Returned));
            encounterLifetime.CompleteReturn();
            Log("HK_SEQUENCE_HANDOFF_READY scene=" + scene + " index=" + BossSequenceController.BossIndex + " geometry=" + geometry + " drained=True image=True");
            if (SessionConfiguration.Current.Has("pantheon-probe")) ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(eventLog)!, "pantheon-" + geometry + "-" + scene + ".png"));
        }
        if (scene == EncounterProfile.Selected.Scene && hubPresence?.OwnsScene(scene) == true && geometry >= 0 && geometry != hubEncounterGeometry)
        {
            if (encounterStatus?.Phase == EncounterPhase.Advancing) return; // Native scene load precedes peer image acknowledgement.
            if (encounterStatus != null && encounterStatus.Phase != EncounterPhase.Returned)
                throw new InvalidOperationException("Hub encounter entered before prior return completed.");
            ReleaseCombatChannels();
            controlSource?.Release(); controlSource = null;
            hubEncounterGeometry = geometry; channelGeneration = Guid.NewGuid(); hitCompleted = false;
            hubCombatEntries++;
            SetEncounter(new EncounterStatus(channelGeneration, geometry, EncounterPhase.Preparing));
            Log("HK_HUB_COMBAT_BEGIN scene=" + scene + " geometry=" + geometry + " nativeStatue=True");
        }
        var current = encounterStatus;
        if (current == null) return;
        // Victory closes combat channels before GPZ's authored head detaches.
        // Keep only that harmless prop interaction alive until native room return.
        if(current.Phase==EncounterPhase.Victory && EncounterProfile.Selected.Family==EncounterProfile.GreyPrinceZote
            && endpoint?.FreshPose is YiGodhome.Bridge.Pose corpsePose)contactSource?.ObservedDefeatedHead(corpsePose);
        if (current.Phase == EncounterPhase.Preparing && current.Matches(peer) && peer!.Phase == EncounterPhase.Restoring && !contactBridge)
        {
            controlSource?.Release(); controlSource = null;
            contactBridge = true;
        }
        bool victory = current.Phase == EncounterPhase.Active && godhomeProbe?.EncounterWon == true;
        bool defeat = current.Phase == EncounterPhase.Active && (debugCancel || current.Matches(peer) && peer!.Phase == EncounterPhase.Defeated);
        if (victory || defeat)
        {
            if(victory && EncounterProfile.Selected.Family==EncounterProfile.AbsoluteRadiance)visualPresenter?.HideForNativeEnd();
            if (defeat) encounterReadyGate?.CancelForNativeReturn(scene);
            hitCompleted = true; endpoint?.PublishControls(Time.frameCount, default);
            if (!victory) hubPresence?.SuspendEncounter();
            ReleaseCombatChannels(keepHits: true);
            if (!victory) { controlSource?.Release(); controlSource = null; }
            bool advance = victory && BossSequenceController.IsInSequence && !BossSequenceController.IsLastBossScene;
            SetEncounter(new EncounterStatus(current.Id, current.GeometryFrame, advance ? EncounterPhase.Advancing : victory ? EncounterPhase.Victory : EncounterPhase.Defeated));
            if (!victory) godhomeProbe!.RequestDefeat();
            debugCancel = false;
            Log("HK_HUB_COMBAT_ENDED victory=" + victory + " knightHidden=True");
        }
        if (encounterStatus!.Phase == EncounterPhase.Victory && PantheonResultsWaiting())
            SetEncounter(new EncounterStatus(current.Id, current.GeometryFrame, EncounterPhase.SequenceResults));
        if ((encounterStatus!.Phase == EncounterPhase.Victory || encounterStatus.Phase == EncounterPhase.Defeated || encounterStatus.Phase == EncounterPhase.SequenceResults)
            && ReturnPreparation.IsHub(scene) && BossSceneController.Instance == null)
            // Native scene arrival starts restoration. Waiting for the later
            // dream wake event resets an already presented lying Yi a second time.
            SetEncounter(new EncounterStatus(current.Id, current.GeometryFrame, EncounterPhase.Returned));
        if (encounterStatus.Phase == EncounterPhase.Returned && current.Matches(peer) && peer!.Phase == EncounterPhase.Ready
            && godhomeProbe?.EncounterReturned == true && hubPresence?.Ready == true
            && endpoint?.FreshPose?.GeometryFrame == geometry && hitCompleted)
        {
            if (visualPresenter?.ReadyForControl != true || visualEndpoint?.FreshFrame?.GeometryFrame != geometry) return;
            hitEndpoint?.Dispose(); hitEndpoint = null;
            hostHits?.Dispose(); hostHits = null; contactSource?.Dispose(); contactSource = null;
            hitCompleted = false;
            encounterLifetime.CompleteReturn();
            if (hubCombatEntries == 1 && SessionConfiguration.Current.Has("switch-probe")) {
                string before = EncounterProfile.Selected.Key;
                EncounterProfile.SelectScene(EncounterProfile.Selected == EncounterProfile.Vengefly ? EncounterProfile.Gruz.Scene : EncounterProfile.Vengefly.Scene);
                Log("HK_BOSS_SWITCH_FIXTURE from=" + before + " to=" + EncounterProfile.Selected.Key + " nativeStatue=True priorReturned=True");
            }
            if(hubCombatEntries<ScriptedHubEntryLimit && !SessionConfiguration.Current.Has("pantheon-probe")) hubPresence.PrepareScriptedReentry();
            Log("HK_HUB_COMBAT_RETURN_READY geometry=" + geometry + " nativeYi=True presented=True nativeWakeComplete=True");
            RecordCompletedDiagnosticCombat();
            TryBeginDebugTravel(); // Same drained transaction, before hub interaction is released.
        }
#else
        if (peer?.Phase == EncounterPhase.Preparing && peer.Id != encounterStatus?.Id)
        {
            ReleaseCombatChannels();
            hubReturnPrepared = false;
            pendingSequenceAdvance = false;
            channelGeneration = peer.Id; hitCompleted = false;
            SetEncounter(new EncounterStatus(peer.Id, peer.GeometryFrame, EncounterPhase.Restoring));
            contactBridge = true;
            Log("NS_HUB_COMBAT_BEGIN geometry=" + peer.GeometryFrame + " warmOwner=" + (memoryProbe?.ContactOwner != null));
        }
        var current = encounterStatus;
        if (current == null) return;
        if (current.Phase == EncounterPhase.Restoring && current.Matches(peer) && peer!.Phase == EncounterPhase.SequenceResults && pendingHitKinds.Count == 0)
            SetEncounter(new EncounterStatus(current.Id, current.GeometryFrame, EncounterPhase.SequenceResults));
        if (encounterStatus!.Phase == EncounterPhase.SequenceResults && current.Matches(peer) && peer!.Phase == EncounterPhase.Returned)
            SetEncounter(new EncounterStatus(current.Id, current.GeometryFrame, EncounterPhase.Restoring));
        bool dead = current.Phase == EncounterPhase.Active && memoryProbe?.ActivePlayer is Player player && player.health.IsDead;
        bool advance = current.Phase == EncounterPhase.Active && current.Matches(peer) && peer!.Phase == EncounterPhase.Advancing;
        bool victory = current.Phase == EncounterPhase.Active && current.Matches(peer)
            && (peer!.Phase == EncounterPhase.Victory || peer.Phase == EncounterPhase.Returned);
        bool cancelled = current.Phase == EncounterPhase.Active && !dead && current.Matches(peer) && peer!.Phase == EncounterPhase.Defeated;
        if (dead || victory || cancelled || advance)
        {
            hitCompleted = true; ReleaseCombatChannels(keepHits: true);
            SetEncounter(new EncounterStatus(current.Id, current.GeometryFrame, advance ? EncounterPhase.Advancing : dead ? EncounterPhase.Defeated : EncounterPhase.Restoring));
            pendingSequenceAdvance = advance;
            pendingVictoryReset = victory || cancelled;
            if (dead) memoryProbe!.PrepareNativeRevival(false, () => {
                inputBridge?.Dispose(); inputBridge = null;
                visualCapture?.Dispose(); visualCapture = null; hudCapture?.Dispose(); hudCapture = null; hudEffectsCapture?.Dispose();hudEffectsCapture=null;
            });
            Log("NS_HUB_COMBAT_RESET victory=" + victory + " nativeHealthAuthority=True");
        }
        if (pendingSequenceAdvance && current.Matches(peer) && peer!.Phase == EncounterPhase.Advancing
            && memoryProbe?.Geometry is GeometryLandingProbe next
            && ReturnPreparation.MayAdvance(pendingHitKinds.Count, sessionId, next.SourceSession, next.SourceScene, next.SourceFrame, current.GeometryFrame))
        {
            visualCapture?.RetireCombatEffects();
            hitEndpoint?.Dispose(); hitEndpoint = null; retiredNativeTarget = null;
            pendingSequenceAdvance = false; hitCompleted = false;
            SetEncounter(new EncounterStatus(current.Id, current.GeometryFrame, EncounterPhase.Ready));
            encounterLifetime.CompleteReturn();
            Log("NS_SEQUENCE_HANDOFF_READY scene=" + next.SourceScene + " geometry=" + next.SourceFrame
                + " player=" + memoryProbe.ActivePlayer!.GetInstanceID() + " health=" + memoryProbe.ActivePlayer.health.currentValue
                + " chi=" + memoryProbe.ActivePlayer.chiContainer.Value + " arrows=" + memoryProbe.ActivePlayer.ammo.Value + " potions=" + memoryProbe.ActivePlayer.potion.DrinkTimesLeft
                + " nativeReset=False pendingOutcomes=0");
        }
        if (current.Matches(peer) && ReturnPreparation.MayReset(pendingVictoryReset, pendingHitKinds.Count, peer!.Phase))
        {
            pendingVictoryReset = false;
            inputBridge?.Dispose(); inputBridge = null;
            visualCapture?.Dispose(); visualCapture = null; hudCapture?.Dispose(); hudCapture = null; hudEffectsCapture?.Dispose();hudEffectsCapture=null;
            memoryProbe!.PrepareNativeRevival(true);
            Log("NS_RETURN_PREPARE_EARLY peerPhase=" + peer.Phase + " pendingOutcomes=0 nativeFrame=" + Time.frameCount);
        }
        if ((encounterStatus!.Phase == EncounterPhase.Defeated || encounterStatus.Phase == EncounterPhase.Restoring)
            && hitCompleted && memoryProbe?.Restored == true && !hubReturnPrepared && current.Matches(peer)
            && (peer!.Phase == EncounterPhase.Defeated || peer.Phase == EncounterPhase.Victory || peer.Phase == EncounterPhase.Returned))
        {
            if (pendingHitKinds.Count != 0) return; // Drain native hit acknowledgements before retiring this generation.
            memoryProbe.ResumeHub(current.GeometryFrame);
            hubReturnPrepared = true;
            Log("NS_RETURN_GEOMETRY_WAIT previous=" + current.GeometryFrame + " peerPhase=" + peer.Phase + " nativeFrame=" + Time.frameCount);
        }
        if (hubReturnPrepared && hitCompleted && current.Matches(peer)
            && (peer!.Phase == EncounterPhase.Victory || peer.Phase == EncounterPhase.Defeated || peer.Phase == EncounterPhase.Returned)
            && ReturnPreparation.IsHub(memoryProbe?.Geometry?.SourceScene ?? "") && memoryProbe!.Geometry!.SourceFrame > current.GeometryFrame)
        {
            if (pendingHitKinds.Count != 0) return;
            hitEndpoint?.Dispose(); hitEndpoint = null;
            retiredNativeTarget = null;
            hitCompleted = false;
            SetEncounter(new EncounterStatus(current.Id, current.GeometryFrame, EncounterPhase.Ready));
            encounterLifetime.CompleteReturn();
            Log("NS_HUB_COMBAT_RETURN_BEGIN nativeRestored=True");
        }
#endif
    }
}
