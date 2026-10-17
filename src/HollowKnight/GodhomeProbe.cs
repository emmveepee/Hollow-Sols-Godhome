using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

namespace YiGodhome;

// Read-only native Godhome inspection. No Yi simulation or encounter interaction yet.
internal sealed class GodhomeProbe : IDisposable
{
    private readonly Action<string> log;
    private readonly bool humanTest;
    private readonly HashSet<string> inspected = new HashSet<string>();
    private bool started;
    private bool failed;
    private string pendingScene = "";
    private float inspectAfter;
    private float nextPresentationLog;
    private int availablePantheons;
    private int availableVariants;
    private EncounterAudit? encounterAudit;
    private readonly bool arenaProbe;
    private readonly bool hubProbe;
    public bool EncounterReturned => encounterAudit?.Returned == true;
    public bool EncounterWon => encounterAudit?.Won == true;
    public long GeometryFrame { get; private set; } = -1;
    public void RetryEncounter() => (encounterAudit ?? throw new InvalidOperationException("No native encounter is active.")).Retry();
    public void RequestDefeat() => (encounterAudit ?? throw new InvalidOperationException("No native encounter is active.")).RequestDefeat();

    public GodhomeProbe(Action<string> log, bool humanTest = false, bool preloaded = false) {
        started = preloaded;
        this.log = log; this.humanTest = humanTest;
        hubProbe = YiGodhome.Bridge.SessionConfiguration.Current.Has("hub-probe");
        arenaProbe = YiGodhome.Bridge.SessionConfiguration.Current.Has("arena");
        bool hubCombat = YiGodhome.Bridge.SessionConfiguration.Current.Has("hub-combat");
        if (hubCombat) encounterAudit = new EncounterAudit(log, false, passive: !YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures || YiGodhome.Bridge.SessionConfiguration.Current.Has("pantheon-probe"), hubRoute: true);
        else if (arenaProbe) encounterAudit = new EncounterAudit(log, false);
        else if (hubProbe && YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures)
            encounterAudit = new EncounterAudit(log, false, true);
        if (YiGodhome.Bridge.SessionConfiguration.Current.Has("encounter-audit"))
            encounterAudit = new EncounterAudit(log);
    }

    public void SceneLoaded(Scene scene)
    {
        encounterAudit?.SceneLoaded(scene.name);
        if (scene.name == "Menu_Title" && !started)
        {
            started = true;
            log("HK_GODHOME_PROBE_BEGIN slot=4 writes=disabled input=" + (humanTest ? "human" : "disabled"));
            GameManager.instance.LoadGame(4, success =>
            {
                try
                {
                    var gm = GameManager.instance;
                    if (!success) throw new InvalidOperationException("Slot 4 could not be loaded.");
                    if (!GodseekerProfile.HasRequiredAccess(new SaveGameData(gm.playerData, gm.sceneData)))
                        throw new InvalidOperationException("Slot 4 is not a validated Godseeker profile; run preparation first.");
                    if (gm.playerData.respawnScene == "GG_Entrance_Cutscene")
                    {
                        // User-authorized bench start, first verified in memory with saving still blocked.
                        GodseekerProfile.UseGodhomeBench(gm.playerData);
                        log("HK_GODHOME_BENCH_OVERRIDE scene=GG_Atrium marker=RestBench type=1 memoryOnly=True");
                    }
                    var continuation = typeof(GameManager).GetMethod("RunContinueGame", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
                    log("HK_GODHOME_CONTINUE profile=" + gm.profileID + " spawn=" + gm.playerData.respawnScene);
                    var input = UnityEngine.Object.FindFirstObjectByType<InputHandler>();
                    object actions = typeof(InputHandler).GetField("inputActions")!.GetValue(input);
                    foreach (string name in new[] { "jump", "attack", "menuSubmit", "textSpeedup", "skipCutscene" })
                    {
                        object action = actions.GetType().GetField(name)!.GetValue(actions);
                        var bindings = (IEnumerable)action.GetType().GetProperty("Bindings")!.GetValue(action);
                        foreach (var binding in bindings)
                            log("HK_GODHOME_INPUT action=" + name + " binding=" + binding.GetType().GetProperty("Name")!.GetValue(binding));
                    }
                    gm.StartCoroutine((IEnumerator)continuation.Invoke(gm, null));
                }
                catch (Exception error) { Fail(error); }
            });
        }
        else if (started && !(HollowSols.Foundation.NativeSaveContext.Enabled && HollowSols.Foundation.NativeSaveContext.Mode != HollowSols.Foundation.GameMode.Godhome) && !scene.name.StartsWith("GG_", StringComparison.Ordinal)
            && scene.name != "Menu_Title" && scene.name != "Knight_Pickup" && scene.name != "Quit_To_Menu")
            Fail(new InvalidOperationException("Unexpected probe scene: " + scene.name));
        if (scene.name.StartsWith("GG_", StringComparison.Ordinal))
        {
            if (arenaProbe && scene.name == YiGodhome.Bridge.EncounterProfile.Selected.Scene) inspected.Remove(scene.name);
            pendingScene = scene.name;
            inspectAfter = Time.realtimeSinceStartup + (arenaProbe && scene.name == YiGodhome.Bridge.EncounterProfile.Selected.Scene ? 0 : 5);
        }
    }

    public void Update()
    {
        if (failed) return;
        try
        {
            if (!hubProbe && !humanTest && HeroController.instance != null && !EncounterReturned) HeroController.instance.IgnoreInput();
            encounterAudit?.Update();
            if (HollowSols.Foundation.NativeSaveContext.Enabled) return; // Production choices include intentionally locked native Godhome access.
            if (UnitySceneManager.GetActiveScene().name == "GG_Atrium" && Time.realtimeSinceStartup >= nextPresentationLog)
            {
                nextPresentationLog = Time.realtimeSinceStartup + 10;
                LogPresentation("hub");
            }
            // A completed readiness request has no work on subsequent frames.
            if (pendingScene.Length != 0 && inspected.Contains(pendingScene)) pendingScene = "";
            if (pendingScene.Length == 0 || Time.realtimeSinceStartup < inspectAfter) return;
            if (UnitySceneManager.GetActiveScene().name != pendingScene) return;
            if (arenaProbe && pendingScene == YiGodhome.Bridge.EncounterProfile.Selected.Scene
                && (BossSceneController.Instance == null || !BossSceneController.Instance.HasTransitionedIn
                    || !GameManager.instance.HasFinishedEnteringScene || HeroController.instance == null
                    || HeroController.instance.controlReqlinquished || GameManager.instance.playerData.atBench)) return;
            if (humanTest && GameManager.instance.gameState.ToString() != "PLAYING") return;
            // Imported Yi can hold the native fade while its first image arrives.
            // Audit the completed presentation, not a fixed point inside FadeIn.
            var pendingFade = Resources.FindObjectsOfTypeAll<PlayMakerFSM>().FirstOrDefault(f => f.gameObject.activeInHierarchy && f.FsmName == "CameraFade");
            if (pendingFade == null || pendingFade.ActiveStateName != "Normal") return;
            if (inspected.Add(pendingScene)) { Audit(pendingScene); pendingScene = ""; }
        }
        catch (Exception error) { Fail(error); }
    }

    private void LogPresentation(string stage)
    {
        var gm = GameManager.instance;
        log("HK_PRESENTATION stage=" + stage + " gameState=" + gm.gameState + " loading=" + typeof(GameManager).GetField("isLoading", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(gm)
            + " respawning=" + gm.RespawningHero + " bench=" + gm.playerData.atBench + " respawnMarker=" + gm.playerData.respawnMarkerName + " respawnType=" + gm.playerData.respawnType);
        foreach (var camera in Camera.allCameras)
            log("HK_CAMERA name=" + camera.name + " position=" + camera.transform.position.ToString("F2") + " enabled=" + camera.enabled + " mask=" + camera.cullingMask + " target=" + (camera.targetTexture == null ? "screen" : camera.targetTexture.name));
        foreach (var fsm in Resources.FindObjectsOfTypeAll<PlayMakerFSM>().Where(f => f.gameObject.activeInHierarchy
            && (f.name.IndexOf("Fade", StringComparison.OrdinalIgnoreCase) >= 0 || f.FsmName.IndexOf("Fade", StringComparison.OrdinalIgnoreCase) >= 0)))
            log("HK_FADE object=" + fsm.name + " fsm=" + fsm.FsmName + " state=" + fsm.ActiveStateName);
        foreach (var renderer in Resources.FindObjectsOfTypeAll<SpriteRenderer>().Where(r => r.gameObject.activeInHierarchy
            && (r.name.IndexOf("black", StringComparison.OrdinalIgnoreCase) >= 0 || r.name.IndexOf("fade", StringComparison.OrdinalIgnoreCase) >= 0)))
            log("HK_OVERLAY name=" + renderer.name + " enabled=" + renderer.enabled + " color=" + renderer.color + " position=" + renderer.transform.position.ToString("F2"));
    }

    private void Audit(string scene)
    {
        var hero = HeroController.instance;
        if (YiGodhome.Bridge.SessionConfiguration.Current.Has("native-perf")) {
            if (hero == null || GameManager.instance.profileID != 4) throw new InvalidOperationException("Native benchmark profile/hero invalid.");
            log("HK_GODHOME_PRESENTATION scene=" + scene + " fade=Normal state=" + GameManager.instance.gameState);
            return;
        }
        log("HK_GODHOME_NATIVE_READY scene=" + scene + " profile=" + GameManager.instance.profileID
            + " hero=" + (hero != null) + " position=" + (hero == null ? "none" : hero.transform.position.ToString("F2")));
        if (hero != null)
        {
            log("HK_COLLISION_MOTION runSpeed=" + hero.RUN_SPEED + " jumpSpeed=" + hero.JUMP_SPEED);
            CollisionAudit.Dump(log, "HK", hero);
            if (!hubProbe && scene == (arenaProbe ? YiGodhome.Bridge.EncounterProfile.Selected.Scene : "GG_Atrium") && YiGodhome.Bridge.SessionConfiguration.Current.Has("export-geometry"))
            {
                GeometryFrame = GeometryExport.Save(log, scene, hero);
                if (!arenaProbe) BenchProbe.Audit(log);
            }
        }
        var statues = Resources.FindObjectsOfTypeAll<BossStatue>().Where(s => s.gameObject.scene.name == scene && (s.bossScene != null || s.dreamBossScene != null)).ToArray();
        int variants = 0, unlocked = 0;
        foreach (var statue in statues.OrderBy(s => s.statueStatePD))
        {
            void Variant(BossScene boss, BossStatue.Completion completion, string field)
            {
                if (boss == null) return;
                variants++;
                // Native getters already apply fallback prerequisites when the persisted unlock is absent.
                bool available = completion.isUnlocked;
                bool radiant = statue.hasNoTiers || completion.completedTier2;
                bool sceneExists = Application.CanStreamedLevelBeLoaded(boss.sceneName);
                if (available) unlocked++;
                log("HK_GODHOME_STATUE field=" + field + " boss=" + boss.name + " scene=" + boss.sceneName
                    + " available=" + available + " radiantAvailable=" + radiant + " sceneExists=" + sceneExists);
                if (!available || !radiant || !sceneExists) throw new InvalidOperationException("Unavailable native statue: " + field);
            }
            Variant(statue.bossScene, statue.StatueState, statue.statueStatePD);
            Variant(statue.dreamBossScene, statue.DreamStatueState, statue.dreamStatueStatePD);
            if (statue.statueStatePD == "statueStateRadiance")
            {
                bool gateUnlocked = statue.bossScene.IsUnlocked(BossSceneCheckSource.Statue);
                log("HK_RADIANCE_GATE_NATIVE unlocked=" + gateUnlocked);
                if (!gateUnlocked) throw new InvalidOperationException("Absolute Radiance cage prerequisite is locked.");
            }
            bool interactive = statue.bossUIControlFSM != null && statue.bossUIControlFSM.enabled && statue.bossUIControlFSM.gameObject.activeInHierarchy;
            log("HK_GODHOME_STATUE_UI field=" + statue.statueStatePD + " enabled=" + interactive);
            if (!interactive && !statue.isHidden) throw new InvalidOperationException("Statue interaction is disabled: " + statue.statueStatePD);
        }
        var doors = Resources.FindObjectsOfTypeAll<BossSequenceDoor>().Where(d => d.gameObject.scene.name == scene && d.bossSequence != null && !string.IsNullOrEmpty(d.playerDataString)).ToArray();
        foreach (var door in doors.OrderBy(d => d.playerDataString))
        {
            bool available = (bool)typeof(BossSequenceDoor).GetMethod("IsUnlocked", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(door, null);
            log("HK_GODHOME_PANTHEON field=" + door.playerDataString + " unlocked=" + available
                + " sequenceCount=" + (door.bossSequence == null ? 0 : door.bossSequence.Count));
            for (int i = 0; door.bossSequence != null && i < door.bossSequence.Count; i++)
                log("HK_PANTHEON_ROSTER sequence=" + door.bossSequence.name + " field=" + door.playerDataString + " index=" + i
                    + " scene=" + door.bossSequence.GetSceneAt(i) + " canLoad=" + door.bossSequence.CanLoad(i));
            if (!available || door.bossSequence == null || door.bossSequence.Count == 0) throw new InvalidOperationException("Unavailable native Pantheon: " + door.playerDataString);
            availablePantheons++;
        }
        foreach (var gate in Resources.FindObjectsOfTypeAll<TransitionPoint>().Where(g => g.gameObject.scene.name == scene))
            log("HK_GODHOME_GATE name=" + gate.name + " destination=" + gate.targetScene + " entry=" + gate.entryPoint);
        foreach (var point in Resources.FindObjectsOfTypeAll<RespawnMarker>().Where(p => p.gameObject.scene.name == scene))
            log("HK_GODHOME_RESPAWN marker=" + point.name + " position=" + point.transform.position.ToString("F2") + " facingRight=" + point.respawnFacingRight);
        log("HK_GODHOME_AUDIT scene=" + scene + " statues=" + statues.Length + " variants=" + variants + " available=" + unlocked + " pantheons=" + doors.Length);
        var fade = Resources.FindObjectsOfTypeAll<PlayMakerFSM>().Single(f => f.gameObject.activeInHierarchy && f.FsmName == "CameraFade");
        log("HK_GODHOME_PRESENTATION scene=" + scene + " fade=" + fade.ActiveStateName + " state=" + GameManager.instance.gameState);
        if (fade.ActiveStateName != "Normal" || hero == null) throw new InvalidOperationException("Godhome presentation is not ready.");
        if (scene == "GG_Workshop")
        {
            LogWorkshopBarriers(scene);
            if (variants != 45 || unlocked != 45) throw new InvalidOperationException("Incomplete Hall of Gods audit.");
            availableVariants = unlocked;
        }
        if (availableVariants == 45 && availablePantheons == 5) log("HK_GODHOME_ACCESS_VERIFIED variants=45 pantheons=5 writes=disabled");
    }

    private void LogWorkshopBarriers(string scene)
    {
        var cage = Resources.FindObjectsOfTypeAll<PlayMakerFSM>().Single(f => f.gameObject.scene.name == scene && f.FsmName == "Unlock"
            && f.name == "Cage" && f.transform.parent != null && f.transform.parent.name == "Radiance Statue Gate");
        log("HK_RADIANCE_CAGE state=" + cage.ActiveStateName + " active=" + cage.gameObject.activeInHierarchy);
        if (cage.ActiveStateName == "Idle") throw new InvalidOperationException("Absolute Radiance cage is still locked.");
    }

    private void Fail(Exception error)
    {
        failed = true;
        log("HK_GODHOME_PROBE_FAILED " + error);
        if (HollowSols.Foundation.NativeSaveContext.Enabled && HollowSolsMenuPlugin.Instance != null) HollowSolsMenuPlugin.Instance.GameplayFailed("HK_GODHOME_PROBE_FAILED", error);
        else Application.Quit();
    }
    public void Dispose() => encounterAudit?.Dispose();
}
