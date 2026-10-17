#if HOLLOW_KNIGHT
using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace YiGodhome;

public sealed partial class BaselinePlugin
{
    private float presentationFixtureAt = -1;
    private bool presentationResumed;
    private int combatPauseStage;
    private float combatPauseAt = -1;
    private bool combatPauseResuming;
    private float combatPauseCompletedAt = -1;
    private bool combatPauseCancelled;
    private bool CombatPauseFixture()
    {
        if (controlSource != null) controlSource.PauseProbeStage = () => combatPauseStage;
        var gm = GameManager.instance;
        if (combatPauseStage >= 3) {
            if (!combatPauseCancelled && Time.realtimeSinceStartup - combatPauseCompletedAt > 1 && CanDebugTravel()) {
                RequestDebugTravel("GG_Atrium"); combatPauseCancelled = true;
            }
            if (combatPauseStage == 3 && combatPauseCancelled && hubPresence?.Ready == true && debugTravel == null
                && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "GG_Atrium") {
                combatPauseStage = 4; Log("HK_COMBAT_PAUSE_RETURNED nativeCancellation=True pendingOutcomes=0");
            }
            return true;
        }
        if (combatPauseAt >= 0) {
            if (Time.realtimeSinceStartup - combatPauseAt >= 22 && !combatPauseResuming) {
                if (!gm.IsGamePaused()) throw new InvalidOperationException("Combat pause never engaged.");
                combatPauseResuming = true; gm.StartCoroutine(gm.PauseGameToggle());
                Log("HK_COMBAT_PAUSE_RESUME stage=" + combatPauseStage + " frame=" + Time.frameCount);
            }
            if (combatPauseResuming && !gm.IsGamePaused()) {
                combatPauseStage++; combatPauseAt = -1; combatPauseResuming = false;
                if (combatPauseStage == 3) { combatPauseCompletedAt = Time.realtimeSinceStartup; Log("HK_COMBAT_PAUSE_COMPLETE nativeToggle=True"); }
            }
            return true;
        }
        var pose = endpoint?.FreshPose;
        if (combatPauseStage >= 3 || encounterStatus?.Phase != Bridge.EncounterPhase.Active || pose == null) return false;
        // Ammo is consumed before the native shooting continuation creates the
        // arrow. Use this bounded fixture's actual projectile capture instead.
        string run = Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!;
        string runs = Path.GetDirectoryName(Path.GetDirectoryName(run))!;
        bool bow = File.Exists(Path.Combine(runs, "NineSols", sessionId.ToString(), "yi-projectile-" + pose.GeometryFrame + ".png"));
        bool produced = combatPauseStage == 0 ? pose.Melee.Length > 0 : combatPauseStage == 1 ? bow : pose.Vitals?.TalismanActive == true;
        if (!produced) return false;
        combatPauseAt = Time.realtimeSinceStartup;
        gm.StartCoroutine(gm.PauseGameToggle());
        Log("HK_COMBAT_PAUSE_BEGIN stage=" + combatPauseStage + " encounter=" + encounterStatus.Id + " geometry=" + pose.GeometryFrame + " nativeProduced=True frame=" + Time.frameCount);
        return true;
    }
    private bool PresentationFixture()
    {
        if (!hubCombat || !YiGodhome.Bridge.SessionConfiguration.Current.Has("cheat-probe")) return false;
        if(Bridge.SessionConfiguration.Current.Has("configuration-probe")) return configurationMenu?.FixtureComplete!=true;
        if (Bridge.SessionConfiguration.Current.Has("pause-probe")) return CombatPauseFixture();
        var gm = GameManager.instance;
        if (presentationFixtureAt < 0)
        {
            if (hubPresence?.Ready != true || UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "GG_Workshop") return false;
            presentationFixtureAt = Time.realtimeSinceStartup;
            foreach (var fsm in Resources.FindObjectsOfTypeAll<PlayMakerFSM>().Where(f => f.gameObject.scene.IsValid()))
                foreach (var state in fsm.FsmStates)
                    foreach (var action in state.Actions.Where(a => a.GetType().Name.IndexOf("Audio", StringComparison.OrdinalIgnoreCase) >= 0))
                        foreach (var field in action.GetType().GetFields())
                            if (field.GetValue(action) is HutongGames.PlayMaker.FsmObject obj && obj.Value is AudioClip clip
                                && (clip.name.IndexOf("nail", StringComparison.OrdinalIgnoreCase) >= 0 || clip.name.IndexOf("draw", StringComparison.OrdinalIgnoreCase) >= 0 || clip.name.IndexOf("sword", StringComparison.OrdinalIgnoreCase) >= 0))
                                Log("HK_HERO_AUDIO_AUDIT fsm=" + fsm.FsmName + " state=" + state.Name + " clip=" + clip.name + " action=" + action.GetType().Name);
            gm.StartCoroutine(gm.PauseGameToggle());
            Log("HK_PAUSE_FIXTURE requested=True");
        }
        float t = Time.realtimeSinceStartup - presentationFixtureAt;
        if (t >= 3 && !presentationResumed)
        {
            presentationResumed = true;
            if (!gm.IsGamePaused()) throw new InvalidOperationException("Native host pause did not engage.");
            gm.StartCoroutine(gm.PauseGameToggle());
            Log("HK_PAUSE_FIXTURE resumed=True");
        }
        return t < 5;
    }
}
#endif
