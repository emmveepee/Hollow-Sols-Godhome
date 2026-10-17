#if HOLLOW_KNIGHT
using System;
using System.Linq;
using UnityEngine;
using YiGodhome.Bridge;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

namespace YiGodhome;

public sealed partial class BaselinePlugin
{
    private static readonly System.Reflection.FieldInfo resultsWaiting = typeof(BossDoorChallengeCompleteUI).GetField("waitingForInput", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
    private static BossDoorChallengeCompleteUI? NativeResults() => UnitySceneManager.GetActiveScene().name == "GG_End_Sequence" && BossSequenceController.WasCompleted
        ? UnityEngine.Object.FindFirstObjectByType<BossDoorChallengeCompleteUI>() : null;
    private static bool PantheonResultsWaiting() { var ui = NativeResults(); return ui != null && (bool)resultsWaiting.GetValue(ui); }
    private float resultsFixtureAt = -1;
    private bool resultsFixtureAcknowledged;
    private float interludeFixtureAt = -1;
    private bool interludeFixtureCancelled;
    private bool pantheonFixtureStarted;
    private int pantheonTraceHandle = -1;
    private int pantheonReadyHandle = -1;
    private float pantheonLateCaptureAt = -1;
    private float pantheonRoomInspectAt = -1;
    private void UpdatePantheonFixture()
    {
        var config = SessionConfiguration.Current;
        if (config.Has("pantheon-probe") && config.PantheonStart is 5 or 10 && !interludeFixtureCancelled
            && encounterStatus?.Phase == EncounterPhase.Active && hubPresence?.Ready == true && visualPresenter?.ReadyForControl == true) {
            if (interludeFixtureAt < 0) {
                interludeFixtureAt = Time.realtimeSinceStartup;
                Log("HK_PANTHEON_INTERLUDE_REACHED next=" + UnitySceneManager.GetActiveScene().name + " geometry=" + hubPresence.GeometryFrame + " nativeActive=True fixtureCancelAfterSeconds=8");
            }
            if (Time.realtimeSinceStartup - interludeFixtureAt >= 8) {
                interludeFixtureCancelled = true; debugCancel = true;
                Log("HK_PANTHEON_INTERLUDE_CANCEL diagnosticOnly=True nativeDefeat=True victoryClaim=False");
            }
        }
        if (config.Has("pantheon-probe") && encounterStatus?.Phase == EncounterPhase.SequenceResults && !resultsFixtureAcknowledged) {
            if (resultsFixtureAt < 0) { resultsFixtureAt = Time.realtimeSinceStartup; Log("HK_PANTHEON_RESULTS_WAIT nativeCompleted=True fixtureHoldSeconds=35"); }
            if (Time.realtimeSinceStartup - resultsFixtureAt >= 35 && PantheonResultsWaiting()) {
                // Exact installed native Update response to anyKeyDown: clear
                // this latch. The native coroutine owns all subsequent return work.
                resultsWaiting.SetValue(NativeResults(), false); resultsFixtureAcknowledged = true;
                Log("HK_PANTHEON_RESULTS_ACK diagnosticOnly=True nativeContinuation=True");
            }
        }
        if (config.Has("pantheon-probe") && pantheonFixtureStarted && hubPresence?.Ready == true && visualPresenter?.ReadyForControl == true
            && pantheonReadyHandle != UnitySceneManager.GetActiveScene().handle) {
            pantheonReadyHandle = UnitySceneManager.GetActiveScene().handle;
            pantheonLateCaptureAt = Time.realtimeSinceStartup + 5;
            var exit = BossSceneController.Instance?.customExitPoint;
            if (exit != null) Log("HK_PANTHEON_EXIT name=" + exit.name + " position=" + exit.transform.position + " target=" + exit.targetScene + " colliders=" + string.Join(";", exit.GetComponents<Collider2D>().Select(c => c.bounds.ToString())));
            Log("HK_PANTHEON_ROOM_PRESENTED scene=" + UnitySceneManager.GetActiveScene().name + " geometry=" + hubPresence.GeometryFrame + " index=" + BossSequenceController.BossIndex);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(eventLog)!, "pantheon-ready-" + hubPresence.GeometryFrame + ".png"));
        }
        if (pantheonLateCaptureAt > 0 && Time.realtimeSinceStartup >= pantheonLateCaptureAt) {
            pantheonLateCaptureAt = -1;
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(eventLog)!, "pantheon-late-" + hubPresence?.GeometryFrame + ".png"));
        }
        if (BossSequenceController.IsInSequence && pantheonTraceHandle != UnitySceneManager.GetActiveScene().handle) {
            pantheonTraceHandle = UnitySceneManager.GetActiveScene().handle;
            if (config.Has("pantheon-probe")) pantheonRoomInspectAt = Time.realtimeSinceStartup + 7;
            Log("HK_SEQUENCE_ROOM index=" + BossSequenceController.BossIndex + " count=" + BossSequenceController.BossCount
                + " scene=" + UnitySceneManager.GetActiveScene().name + " last=" + BossSequenceController.IsLastBossScene
                + " bindings=" + BossSequenceController.BoundNail + "/" + BossSequenceController.BoundShell + "/" + BossSequenceController.BoundCharms + "/" + BossSequenceController.BoundSoul);
        }
        if (pantheonRoomInspectAt > 0 && Time.realtimeSinceStartup >= pantheonRoomInspectAt) {
            pantheonRoomInspectAt = -1; var scene = UnitySceneManager.GetActiveScene().name;
            Log("HK_PANTHEON_NATIVE_ROOM scene=" + scene + " hero=" + HeroController.instance?.transform.position + " entryFinished=" + GameManager.instance.HasFinishedEnteringScene + " relinquished=" + HeroController.instance?.controlReqlinquished + " camera=" + GameCameras.instance.cameraFadeFSM.ActiveStateName);
            foreach (var f in Resources.FindObjectsOfTypeAll<PlayMakerFSM>().Where(f => f.gameObject.scene.name == scene && (f.gameObject.name.Contains("fader") || f.FsmName.Contains("Scene") || f.FsmName.Contains("Dream") || f.FsmName.Contains("Control"))).Take(32))
                Log("HK_PANTHEON_NATIVE_OWNER name=" + f.name + " fsm=" + f.FsmName + " active=" + f.gameObject.activeInHierarchy + " state=" + f.ActiveStateName);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(eventLog)!, "pantheon-native-" + scene + ".png"));
        }
        if (!config.Has("pantheon-probe") || pantheonFixtureStarted || hubPresence?.Ready != true
            || UnitySceneManager.GetActiveScene().name != "GG_Atrium" || visualPresenter?.ReadyForControl != true) return;
        if (!config.Scripted || !hubCombat || GameManager.instance.profileID != 4)
            throw new InvalidOperationException("Unauthorized native Pantheon fixture.");
        var door = Resources.FindObjectsOfTypeAll<BossSequenceDoor>().Single(d => d.gameObject.scene.name == "GG_Atrium"
            && d.playerDataString == "bossDoorStateTier" + config.Pantheon);
        foreach (var f in door.GetComponentsInChildren<PlayMakerFSM>(true))
            foreach (var state in f.FsmStates)
                foreach (var a in state.Actions.OfType<HutongGames.PlayMaker.Actions.BeginSceneTransition>())
                    Log("HK_PANTHEON_NATIVE_LOAD fsm=" + f.FsmName + " state=" + state.Name + " visualization=" + a.visualization.Value + " preventFade=" + a.preventCameraFadeOut);
        var sequence = door.bossSequence;
        if (sequence == null || config.PantheonStart >= sequence.Count || !sequence.CanLoad(config.PantheonStart))
            throw new InvalidOperationException("Native sequence index unavailable.");
        for (int i = 0; i < sequence.Count; i++)
            Log("HK_PANTHEON_ROSTER sequence=" + sequence.name + " field=" + door.playerDataString + " index=" + i
                + " scene=" + sequence.GetSceneAt(i) + " canLoad=" + sequence.CanLoad(i));
        var pd = GameManager.instance.playerData;
        pd.bossReturnEntryGate = door.dreamReturnGate.name;
        pd.dreamReturnScene = "GG_Atrium";
        BossSequenceController.SetupNewSequence(sequence, (BossSequenceController.ChallengeBindings)0, door.playerDataString, config.PantheonStart);
        pantheonFixtureStarted = true;
        Log("HK_PANTHEON_FIXTURE_BEGIN sequence=" + sequence.name + " index=" + config.PantheonStart + " nativeSetup=True bindings=None fullClearClaim=False");
        GameManager.instance.BeginSceneTransition(new GameManager.SceneLoadInfo {
            SceneName = sequence.GetSceneAt(config.PantheonStart), EntryGateName = "door_dreamEnter", EntryDelay = 0,
            PreventCameraFadeOut = true, WaitForSceneTransitionCameraFade = true, Visualization = GameManager.SceneLoadVisualizations.GodsAndGlory });
    }
}
#endif
