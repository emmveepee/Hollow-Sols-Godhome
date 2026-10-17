using System;
using System.Linq;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

namespace YiGodhome;

internal sealed class EncounterAudit : IDisposable
{
    private readonly Action<string> log;
    private readonly HeroProxy benchExit;
    private HeroProxy? workshopBench;
    private bool audited;
    private bool positioned, entered, workshop;
    private PlayMakerFSM? door;
    private float deadline;
    private BossStatue? statue;
    private PlayMakerFSM? inspect;
    private bool inspecting, selected, arena;
    private float enterAfter, nextSample, arenaAt;
    private HealthManager? boss;
    private bool returned;
    private readonly bool syntheticDamage;
    private readonly bool benchmark = YiGodhome.Bridge.SessionConfiguration.Current.Has("native-perf");
    private PlayMakerFSM? benchmarkControl;
    private HeroController? protectedHero;
    private GlobalEnums.DamageMode priorDamageMode;
    private bool priorInvincible;
    private readonly TargetIsolation? isolation;
    private readonly bool defeatProbe;
    private bool defeatRequested, bossDied, returnInspected;
    private BossSceneController? completionOwner;
    private void Completed() { bossDied = true; log("HK_NATIVE_BOSSES_DEAD scene=" + UnitySceneManager.GetActiveScene().name + " frame=" + Time.frameCount); }
    private void ReleaseCompletion() { if (completionOwner != null) completionOwner.OnBossesDead -= Completed; completionOwner = null; }
    private BossStatue.Completion completionBefore;
    private string returnState = "";
    private bool wakeRequested;
    private bool volleyRequested;
    private HostDamageRegistry? damageRegistry;
    private readonly System.Collections.Generic.HashSet<int> damageAudited = new System.Collections.Generic.HashSet<int>();
    private readonly bool traversalOnly;
    private readonly bool passive, hubRoute;
    public bool Returned => returned;
    public bool Won => bossDied;
    public void Retry()
    {
        if (!returned || UnitySceneManager.GetActiveScene().name != "GG_Workshop") throw new InvalidOperationException("Native retry requires completed Hall return.");
        // Automated fixture only: select through the native statue UI. The same
        // scene-entry observer used by a player's normal selection resets state.
        workshop = selected = inspecting = false;
        statue = null; inspect = null;
        log("HK_RETRY_BEGIN nativeStatueSelection=True");
    }
    public void SceneLoaded(string scene)
    {
        if (scene != YiGodhome.Bridge.EncounterProfile.Selected.Scene || !returned && !BossSequenceController.IsInSequence) return;
        ReleaseCompletion();
        if (protectedHero != null) { protectedHero.damageMode = priorDamageMode; GameManager.instance.playerData.isInvincible = priorInvincible; protectedHero = null; }
        arena = returned = defeatRequested = bossDied = returnInspected = wakeRequested = false;
        boss = null; returnState = "";
        log("HK_NATIVE_STATUE_REENTRY scene=" + scene);
    }
    public void RequestDefeat()
    {
        if (defeatRequested) return;
        if (!arena || returned || bossDied || UnitySceneManager.GetActiveScene().name != YiGodhome.Bridge.EncounterProfile.Selected.Scene || GameManager.instance.profileID != 4)
            throw new InvalidOperationException("Native defeat requested outside the active audited encounter.");
        defeatRequested = true;
        var hero = HeroController.instance;
        log("HK_DEFEAT_REQUEST nativeDie=True bossHP=" + (boss == null ? -1 : boss.hp) + " knightHP=" + GameManager.instance.playerData.health);
        hero.StartCoroutine((System.Collections.IEnumerator)typeof(HeroController).GetMethod("Die", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(hero, null));
    }
    public EncounterAudit(Action<string> log, bool syntheticDamage = true, bool traversalOnly = false, bool passive = false, bool hubRoute = false)
    {
        this.log = log;
        this.traversalOnly = traversalOnly;
        this.passive = passive; this.hubRoute = hubRoute;
        defeatProbe = YiGodhome.Bridge.SessionConfiguration.Current.Has("defeat-probe");
        this.syntheticDamage = syntheticDamage && !defeatProbe;
        benchExit = new HeroProxy(log); if (benchmark || !this.syntheticDamage || YiGodhome.Bridge.EncounterProfile.Selected == YiGodhome.Bridge.EncounterProfile.PureVessel) isolation = new TargetIsolation(log);
    }
    public void Update()
    {
        string scene = UnitySceneManager.GetActiveScene().name;
        if (syntheticDamage && (scene == YiGodhome.Bridge.EncounterProfile.Mawlek.Scene || scene == YiGodhome.Bridge.EncounterProfile.PureVessel.Scene))
        {
            damageRegistry ??= new HostDamageRegistry(scene, d => YiGodhome.Bridge.EncounterProfile.Selected == YiGodhome.Bridge.EncounterProfile.PureVessel);
            foreach (var source in damageRegistry.Snapshot())
                if (damageAudited.Add(source.GetInstanceID()))
                {
                    log("HK_POOLED_DAMAGE_AUDIT name=" + source.name + " id=" + source.GetInstanceID() + " damage=" + source.damageDealt + " hazard=" + source.hazardType);
                    log("HK_POOLED_COMPONENTS name=" + source.name + " types=" + string.Join(",", source.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().FullName)));
                    foreach (var shape in source.GetComponents<Collider2D>())
                        log("HK_POOLED_SHAPE type=" + shape.GetType().Name + " bounds=" + shape.bounds + " trigger=" + shape.isTrigger);
                    foreach (var fsm in source.GetComponents<PlayMakerFSM>()) Dump(fsm);
                }
        }
        if (traversalOnly && scene != "GG_Atrium") return;
        if (arena && YiGodhome.Bridge.ReturnPreparation.IsHub(scene) && !returned)
        {
            HeroController.instance.AcceptInput();
            if (!returnInspected)
            {
                returnInspected = true;
                foreach (var fsm in HeroController.instance.GetComponentsInChildren<PlayMakerFSM>(true).Where(f => f.FsmName.IndexOf("Dream", StringComparison.OrdinalIgnoreCase) >= 0)) Dump(fsm);
            }
            var dream = HeroController.instance.GetComponents<PlayMakerFSM>().Single(f => f.FsmName == "Dream Return");
            if (returnState != dream.ActiveStateName) { returnState = dream.ActiveStateName; log("HK_RETURN_STATE state=" + returnState + " relinquished=" + HeroController.instance.controlReqlinquished); }
            if (!wakeRequested && dream.ActiveStateName == "Ready")
            {
                if (!dream.FsmStates.Single(s => s.Name == "Ready").Transitions.Any(t => t.EventName == "GET UP" && t.ToState == "Get Up")) throw new InvalidOperationException("Native dream wake contract changed.");
                wakeRequested = true; log("HK_RETURN_WAKE_REQUEST event=GET UP"); dream.SendEvent("GET UP");
            }
            if (GameManager.instance.gameState.ToString() != "PLAYING" || HeroController.instance.controlReqlinquished || dream.ActiveStateName != "Idle") return;
            returned = true; log("HK_ENCOUNTER_RETURN native=True bossDead=" + bossDied);
            if (protectedHero != null) { protectedHero.damageMode = priorDamageMode; GameManager.instance.playerData.isInvincible = priorInvincible; protectedHero = null; }
            if (defeatRequested && scene == "GG_Workshop")
            {
                var after = GameManager.instance.GetPlayerDataVariable<BossStatue.Completion>(YiGodhome.Bridge.EncounterProfile.Selected.Statue);
                if (bossDied || !after.Equals(completionBefore)) throw new InvalidOperationException("Defeat incorrectly completed the native boss challenge.");
                log("HK_DEFEAT_VERIFIED nativeReturn=True bossCompleted=False completionUnchanged=True controlRestored=True knightHP=" + GameManager.instance.playerData.health);
            }
        }
        if (arena && scene == YiGodhome.Bridge.EncounterProfile.Selected.Scene && Time.realtimeSinceStartup >= nextSample)
        {
            nextSample = Time.realtimeSinceStartup + 1;
            if (YiGodhome.Bridge.EncounterProfile.Selected.AuditOnly)
                foreach (var receiver in Resources.FindObjectsOfTypeAll<HealthManager>().Where(h => h.gameObject.scene.name == scene))
                    log("HK_PHASE_AUDIT name=" + receiver.name + " hp=" + receiver.hp + " dead=" + receiver.isDead + " invincible=" + receiver.IsInvincible + " active=" + receiver.gameObject.activeInHierarchy
                        + " states=" + string.Join(";", receiver.GetComponents<PlayMakerFSM>().Select(f => f.FsmName + ":" + f.ActiveStateName)));
            if (boss != null)
            {
                if (benchmark) {
                    log("HK_NATIVE_BENCHMARK_SAMPLE hp=" + boss.hp + " state=" + benchmarkControl!.ActiveStateName + " frame=" + Time.frameCount);
                    if (!boss.isDead && Time.realtimeSinceStartup - arenaAt >= 8) {
                        int before = boss.hp;
                        boss.Hit(new HitInstance { Source = HeroController.instance.gameObject, AttackType = AttackTypes.Nail, DamageDealt = 65, Multiplier = 1, MagnitudeMultiplier = 1, Direction = 0 });
                        log("HK_NATIVE_BENCHMARK_HIT before=" + before + " after=" + boss.hp + " frame=" + Time.frameCount);
                    }
                    return;
                }
                log("HK_ENCOUNTER_SAMPLE hp=" + boss.hp + " dead=" + boss.isDead + " position=" + boss.transform.position.ToString("F3") + " knightHP=" + GameManager.instance.playerData.health);
                if (defeatProbe && !defeatRequested && Time.realtimeSinceStartup - arenaAt >= 8)
                {
                    var hero = HeroController.instance;
                    var prefab = (GameObject)typeof(HeroController).GetField("heroDeathPrefab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(hero);
                    foreach (var fsm in prefab.GetComponentsInChildren<PlayMakerFSM>(true)) Dump(fsm);
                    RequestDefeat();
                }
                bool mawlekAudit = YiGodhome.Bridge.EncounterProfile.Selected == YiGodhome.Bridge.EncounterProfile.Mawlek;
                if (syntheticDamage && mawlekAudit && !volleyRequested && Time.realtimeSinceStartup - arenaAt >= 12)
                {
                    volleyRequested = true;
                    boss.GetComponents<PlayMakerFSM>().Single(f => f.FsmName == "Mawlek Control").SetState("Super Spit");
                    log("HK_VOLLEY_FIXTURE nativeState=SuperSpit");
                }
                if (syntheticDamage && !boss.isDead && Time.realtimeSinceStartup - arenaAt >= (mawlekAudit || YiGodhome.Bridge.EncounterProfile.Selected == YiGodhome.Bridge.EncounterProfile.PureVessel ? 30 : 8))
                {
                    int before = boss.hp;
                    boss.Hit(new HitInstance { Source = HeroController.instance.gameObject, AttackType = AttackTypes.Nail,
                        DamageDealt = 65, Multiplier = 1, MagnitudeMultiplier = 1, Direction = 0 });
                    log("HK_ENCOUNTER_FIXTURE_HIT before=" + before + " after=" + boss.hp + " dead=" + boss.isDead + " nativeHit=True");
                }
                foreach (var c in boss.GetComponentsInChildren<Collider2D>())
                    log("HK_ENCOUNTER_BOSS_COLLIDER name=" + c.name + " layer=" + c.gameObject.layer + " type=" + c.GetType().Name + " enabled=" + c.enabled + " trigger=" + c.isTrigger + " bounds=" + c.bounds
                        + (c is BoxCollider2D box ? " offset=" + box.offset + " size=" + box.size : "")
                        + (c is PolygonCollider2D polygon && YiGodhome.Bridge.EncounterProfile.Selected == YiGodhome.Bridge.EncounterProfile.MassiveMossCharger
                            ? " offset=" + polygon.offset + " points=" + string.Join(";", polygon.points.Select(p => p.ToString("F4"))) : ""));
                if (YiGodhome.Bridge.EncounterProfile.Selected == YiGodhome.Bridge.EncounterProfile.MassiveMossCharger)
                {
                    foreach (var renderer in boss.GetComponents<Renderer>())
                        log("HK_MOSS_NATIVE_ART position=" + boss.transform.position + " renderer=" + renderer.enabled + " bounds=" + renderer.bounds
                            + " state=" + boss.GetComponent<PlayMakerFSM>()?.ActiveStateName + " frame=" + Time.frameCount);
                    var sprite = boss.GetComponent<tk2dBaseSprite>();
                    if (sprite != null) {
                        var definition = sprite.GetCurrentSpriteDef();
                        log("HK_MOSS_NATIVE_SPRITE definition=" + definition.name + " scale=" + sprite.scale + " collider=" + definition.colliderType
                            + " vertices=" + string.Join(";", definition.colliderVertices.Select(p => p.ToString("F4"))) + " frame=" + Time.frameCount);
                    }
                }
            }
            return;
        }
        // HubPresence owns both hub rooms, including native bench departure.
        // Root travel may arrive already standing; the standalone bench probe
        // must not reacquire that room or require an active resting bench.
        if (hubRoute && YiGodhome.Bridge.ReturnPreparation.IsHub(scene)) return;
        if (scene == "GG_Workshop" && !workshop && (GameManager.instance.playerData.atBench
            || workshopBench != null && !workshopBench.Dismounted))
        {
            workshopBench ??= new HeroProxy(log, benchScene: "GG_Workshop");
            workshopBench.Update(null);
            if (!workshopBench.Dismounted) return;
        }
        if (scene == "GG_Workshop" && !workshop && GameManager.instance.gameState.ToString() == "PLAYING")
        {
            workshop = true;
            statue = Resources.FindObjectsOfTypeAll<BossStatue>().Single(s => s.gameObject.scene.name == scene && (s.statueStatePD == YiGodhome.Bridge.EncounterProfile.Selected.Statue || s.dreamStatueStatePD == YiGodhome.Bridge.EncounterProfile.Selected.Statue));
            if (YiGodhome.Bridge.EncounterProfile.Selected == YiGodhome.Bridge.EncounterProfile.FailedChampionAudit) statue.SetDreamVersion(true, false, false);
            if (benchmark && statue.dreamStatueStatePD == YiGodhome.Bridge.EncounterProfile.Selected.Statue) statue.SetDreamVersion(true, false, false);
            log("HK_ENCOUNTER_WORKSHOP boss=" + statue.bossScene.sceneName);
            foreach (var fsm in statue.GetComponentsInChildren<PlayMakerFSM>(true)) Dump(fsm);
            inspect = statue.GetComponentsInChildren<PlayMakerFSM>().Single(f => f.name == "Inspect" && f.FsmName == "npc_control");
            HeroController.instance.transform.position = inspect.transform.position;
            Physics2D.SyncTransforms(); deadline = Time.realtimeSinceStartup + 12;
            return;
        }
        if (scene == "GG_Workshop" && workshop && !selected)
        {
            var hero = HeroController.instance; hero.AcceptInput();
            if (!inspecting && inspect!.ActiveStateName.StartsWith("In Range", StringComparison.Ordinal) && hero.CanInteract())
            { inspecting = true; log("HK_ENCOUNTER_INSPECT event=UP PRESSED"); inspect.SendEvent("UP PRESSED"); }
            var ui = UnityEngine.Object.FindFirstObjectByType<BossChallengeUI>();
            if (ui != null && statue!.bossUIControlFSM.ActiveStateName == "Open UI")
            {
                var bound = typeof(BossChallengeUI).GetField("bossStatue", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(ui);
                if (!ReferenceEquals(bound, statue)) throw new InvalidOperationException("Unexpected challenge UI statue.");
                selected = true; log("HK_ENCOUNTER_SELECT tier=" + YiGodhome.Bridge.DiagnosticDifficulty.Tier + " boss=" + YiGodhome.Bridge.EncounterProfile.Selected.Scene); ui.LoadBoss(YiGodhome.Bridge.DiagnosticDifficulty.Tier);
            }
            else if (Time.realtimeSinceStartup > deadline) throw new InvalidOperationException("Statue selection timed out: " + inspect!.ActiveStateName);
            return;
        }
        if (scene == YiGodhome.Bridge.EncounterProfile.Selected.Scene && !arena && BossSceneController.Instance != null && BossSceneController.Instance.HasTransitionedIn)
        {
            arena = true;
            completionBefore = GameManager.instance.GetPlayerDataVariable<BossStatue.Completion>(YiGodhome.Bridge.EncounterProfile.Selected.Statue);
            completionOwner = BossSceneController.Instance; completionOwner.OnBossesDead += Completed;
            arenaAt = Time.realtimeSinceStartup;
            if (benchmark || !syntheticDamage || YiGodhome.Bridge.EncounterProfile.Selected == YiGodhome.Bridge.EncounterProfile.PureVessel)
            {
                protectedHero = HeroController.instance; priorDamageMode = protectedHero.damageMode;
                priorInvincible = GameManager.instance.playerData.isInvincible;
                GameManager.instance.playerData.isInvincible = true;
                protectedHero.damageMode = GlobalEnums.DamageMode.NO_DAMAGE;
                log("HK_ARENA_TARGET_PROTECTED damageMode=NO_DAMAGE");
            }
            log("HK_ENCOUNTER_ARENA tier=" + BossSceneController.Instance.BossLevel + " transitioned=True return=" + BossSceneController.Instance.DreamReturnEvent);
            if (benchmark) {
                boss = GameObject.Find(YiGodhome.Bridge.EncounterProfile.Selected.Boss)?.GetComponent<HealthManager>() ?? throw new InvalidOperationException("Native benchmark boss missing.");
                benchmarkControl = boss.GetComponents<PlayMakerFSM>().Single(f => f.FsmName == "Control");
                log("HK_NATIVE_BENCHMARK_READY boss=" + boss.name + " hp=" + boss.hp + " nativeHitOnly=True Yi=False IPC=False aura=False");
                return;
            }
            foreach (var fsm in BossSceneController.Instance.GetComponents<PlayMakerFSM>()) Dump(fsm);
            foreach (var fsm in Resources.FindObjectsOfTypeAll<PlayMakerFSM>().Where(f => f.name.Contains("Title") && f.gameObject.scene.IsValid())) Dump(fsm);
            foreach (var health in Resources.FindObjectsOfTypeAll<HealthManager>().Where(h => h.gameObject.scene.name == scene))
            {
                log("HK_ENCOUNTER_HEALTH id=" + health.GetInstanceID() + " name=" + health.name + " hp=" + health.hp + " active=" + health.gameObject.activeInHierarchy);
                if (health.name == YiGodhome.Bridge.EncounterProfile.Selected.Boss) boss = health;
                foreach (var fsm in health.GetComponents<PlayMakerFSM>()) Dump(fsm);
            }
            foreach (var damage in Resources.FindObjectsOfTypeAll<DamageHero>().Where(d => d.gameObject.scene.name == scene))
            {
                log("HK_ENCOUNTER_DAMAGE id=" + damage.GetInstanceID() + " name=" + damage.name + " damage=" + damage.damageDealt + " hazard=" + damage.hazardType);
                if (YiGodhome.Bridge.EncounterProfile.Selected == YiGodhome.Bridge.EncounterProfile.Xero)
                    foreach (var fsm in damage.GetComponents<PlayMakerFSM>()) Dump(fsm);
                foreach (var collider in damage.GetComponents<Collider2D>())
                    log("HK_ENCOUNTER_COLLIDER type=" + collider.GetType().Name + " enabled=" + collider.enabled + " trigger=" + collider.isTrigger + " bounds=" + collider.bounds);
            }
            return;
        }
        if (passive || scene != "GG_Atrium" || GameManager.instance.gameState.ToString() != "PLAYING") return;
        if (audited)
        {
            if (entered) return;
            if (Time.realtimeSinceStartup < enterAfter) return;
            if (!positioned)
            {
                positioned = true; deadline = Time.realtimeSinceStartup + 8;
                HeroController.instance.transform.position = door!.transform.position;
                Physics2D.SyncTransforms();
                log("HK_ENCOUNTER_FIXTURE positioned=workshop-door");
            }
            var hero = HeroController.instance;
            hero.AcceptInput();
            if (door!.ActiveStateName == "In Range" && hero.CanInteract())
            {
                entered = true; log("HK_ENCOUNTER_ENTER event=UP PRESSED"); door.SendEvent("UP PRESSED");
            }
            else if (Time.realtimeSinceStartup > deadline) throw new InvalidOperationException("Workshop interaction timed out: " + door.ActiveStateName + " grounded=" + hero.cState.onGround);
            return;
        }
        benchExit.Update(null);
        if (!benchExit.Dismounted) return;
        audited = true;
        enterAfter = Time.realtimeSinceStartup + 6;
        var gate = Resources.FindObjectsOfTypeAll<TransitionPoint>().Single(g => g.gameObject.scene.name == "GG_Atrium" && g.name == "Door_Workshop");
        log("HK_ENCOUNTER_DOOR target=" + gate.targetScene + " entry=" + gate.entryPoint + " door=" + gate.isADoor);
        foreach (var fsm in gate.GetComponentsInChildren<PlayMakerFSM>(true)) Dump(fsm);
        door = gate.GetComponents<PlayMakerFSM>().Single(f => f.FsmName == "Door Control");
        if (!door.FsmStates.Single(s => s.Name == "In Range").Transitions.Any(t => t.EventName == "UP PRESSED" && t.ToState == "Can Enter?"))
            throw new InvalidOperationException("Native workshop interaction contract changed.");
    }
    private void Dump(PlayMakerFSM fsm)
    {
        if (benchmark) return;
        log("HK_ENCOUNTER_FSM object=" + fsm.name + " fsm=" + fsm.FsmName + " state=" + fsm.ActiveStateName);
        foreach (var state in fsm.FsmStates)
        {
            log("HK_ENCOUNTER_STATE name=" + state.Name + " transitions=" + string.Join(",", state.Transitions.Select(t => t.EventName + "->" + t.ToState)));
            foreach (var action in state.Actions)
                log("HK_ENCOUNTER_ACTION state=" + state.Name + " type=" + action.GetType().FullName + " fields=" + string.Join(";", action.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => f.Name + "=" + Describe(f.GetValue(action)))));
        }
    }
    private static string Describe(object? value)
    {
        if (value == null) return "null";
        if (value is FsmOwnerDefault owner) return owner.OwnerOption + ":" + owner.GameObject?.Name + "=" + owner.GameObject?.Value?.name;
        if (value is FsmString s) return s.Value ?? "null";
        if (value is FsmEvent e) return e.Name;
        if (value is FsmGameObject g) return g.Value == null ? "null" : g.Value.name;
        if (value is UnityEngine.Object o) return o.name;
        return value.ToString() ?? "null";
    }
    public void Dispose() { ReleaseCompletion(); damageRegistry?.Dispose(); isolation?.Dispose(); if (protectedHero != null) { protectedHero.damageMode = priorDamageMode; GameManager.instance.playerData.isInvincible = priorInvincible; protectedHero = null; } benchExit.Dispose(); workshopBench?.Dispose(); }
}
