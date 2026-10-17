using System;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using YiGodhome.Bridge;
using Pose = YiGodhome.Bridge.Pose;

namespace YiGodhome;

// Room presence does not imply combat compatibility or restrict statue selection.
internal sealed class HubPresence : IDisposable
{
    private static readonly System.Reflection.FieldInfo deathPrefabField = HarmonyLib.AccessTools.Field(typeof(HeroController), "heroDeathPrefab");
    private PlayMakerFSM? dreamReturn;
    private HeroController? dreamHero;
    private readonly Action<string> log;
    private readonly EntryFloorAdmission entryFloorAdmission=new();
    private readonly VerticalEntryGateLease verticalEntryGate=new();
    private static HubPresence? current;
    private bool gateWaitLogged;
    private readonly Func<float>? nativeInteract;
    private HeroProxy? proxy;
    private HeroProxy? benchExit;
    private MeshRenderer? knight;
    private bool originalVisible;
    private GameObject? deathPresentation;
    private readonly System.Collections.Generic.Dictionary<Renderer, bool> hiddenDeathRenderers = new System.Collections.Generic.Dictionary<Renderer, bool>();
    private readonly System.Collections.Generic.Dictionary<Renderer, bool> hiddenKnightParticles = new System.Collections.Generic.Dictionary<Renderer, bool>();
    private string scene = "";
    private int sceneHandle = -1;
    private HallClimbingWalls? climbingWalls;
    private HallStatueLevers? statueLevers;
    internal bool LeverFixtureComplete=>statueLevers?.FixtureComplete==true;
    private HubShallowWater? shallowWater;
    private bool nativeInteraction;
    private float nextTrace;
    private long visualGeometry = -1;
    private YiGodhome.Geometry.Snapshot? anchor;
    private float nextGeometryCheck;
    private long geometryAllocated;
    private double geometryMs;
    private int geometrySamples;
    private bool automatedHallReached;
    private bool waterAudited;
    private bool statuesAudited;
    private PlayMakerFSM? knightWater;
    private bool waterWasEnabled;
    private readonly System.Collections.Generic.Dictionary<PlayMakerFSM, bool> knightActions = new System.Collections.Generic.Dictionary<PlayMakerFSM, bool>();
    private readonly System.Collections.Generic.Dictionary<GameObject, bool> restBenches = new System.Collections.Generic.Dictionary<GameObject, bool>();
    private readonly System.Collections.Generic.Dictionary<PlayMakerFSM, bool> voidCatchers = new System.Collections.Generic.Dictionary<PlayMakerFSM, bool>();
    private bool voidOwnershipAcquired;
    private float nextInteractionTrace;
    private bool scriptedMenuClosed;
    private string auditedAudioScene = "";
    private readonly NativeSpellIsolation spells;
    private readonly NativeBossTitles titles;
    private readonly NativeBossMusic music;
    private readonly RoomEntryPresentation entryPresentation;
    private bool entryActive;
    public bool EntryRevealed => entryPresentation.Revealed;
    public bool DoorEntering => nativeInteraction && HeroController.instance != null
        && HeroController.instance.GetComponent<tk2dSpriteAnimator>()?.CurrentClip?.name == "Enter";
    public void RevealEntry(long geometry) { if (geometry == GeometryFrame) entryPresentation.Reveal(geometry); }
    private InteractionBinding? interactionBinding;
    private bool encounterSuspended;
    private readonly bool combat = YiGodhome.Bridge.SessionConfiguration.Current.Has("hub-combat");
    private GameObject? terrainFixture;
    private int terrainFixtureStage;
    private float terrainFixtureAt;
    private readonly bool scripted = YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures;
    public long GeometryFrame { get; private set; } = -1;
    public bool OwnsScene(string name) => scene == name && anchor?.Scene == name;
    private bool rootArrival;
    public void RequestRootArrival() => rootArrival = true;
    internal Vector2? FirstRestPoint => anchor?.RestPoints.Length > 0 ? new Vector2(anchor.RestPoints[0].X, anchor.RestPoints[0].Y) : (Vector2?)null;
    public void UpdateWallArt(VisualFrame? frame, Pose? pose) => climbingWalls?.Update(frame, pose);
    public bool Ready => proxy?.Ready == true && !nativeInteraction && !entryActive;
    public Vector3? RestPrompt(Pose? pose)
    {
        if (!combat || !Ready || pose == null || !pose.Grounded || anchor == null) return null;
        foreach (var point in anchor.RestPoints)
            if (Math.Abs(point.X - pose.X) <= 1.8f && Math.Abs(point.Y - pose.Y) <= 1.5f)
                return new Vector3(point.X, point.Y + 3, 0);
        return null;
    }
    private bool MenuIsOpen;
    public bool MenuOpen { set { MenuIsOpen = value; if (interactionBinding != null) interactionBinding.MenuOpen = value; } }
    public static bool Allowed(string name) => name.StartsWith("GG_", StringComparison.Ordinal)
        || HollowSols.Foundation.NativeSaveContext.Enabled && HollowSols.Foundation.NativeSaveContext.Mode != HollowSols.Foundation.GameMode.Godhome
            && name != "Menu_Title" && name != "Quit_To_Menu" && name != "Opening_Sequence" && !name.Contains("Intro", StringComparison.OrdinalIgnoreCase) && !name.Contains("Cinematic", StringComparison.OrdinalIgnoreCase);

    public HubPresence(Action<string> log, Func<float>? nativeInteract=null)
    {
        this.nativeInteract=nativeInteract;
        this.log = log;current=this;
        titles = new NativeBossTitles(log);
        music = new NativeBossMusic(log);
        entryPresentation = new RoomEntryPresentation(log);
        spells = new NativeSpellIsolation(log, () => Allowed(scene));
        Camera.onPreCull+=HideKnightBeforeRender;
        log("HK_HUB_DIAGNOSTIC bossLoading=True nativeMenus=True combatCompatibilityUnverified=True");
    }
    public void SuspendEncounter()
    {
        encounterSuspended = true;
        proxy?.Release(false); proxy = null;
        HideKnight();
    }
    public void PrepareScriptedReentry() { if (scripted) scriptedMenuClosed = false; }

    public void Update(Pose? pose)
    {
        try { UpdateCore(pose); }
        catch (YiGodhome.Geometry.SnapshotPublicationDeferredException) { /* Retry next frame; capture/readiness has not committed. */ }
    }
    private void UpdateCore(Pose? pose)
    {
        titles.Update();
        spells.Update();
        var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        string current = activeScene.name;
        if (current != scene || activeScene.handle != sceneHandle)
        {
            entryPresentation.Reset(); entryActive = false;entryFloorAdmission.Reset();verticalEntryGate.Reset();gateWaitLogged=false;
            climbingWalls?.Dispose(); climbingWalls = null;
            statueLevers?.Dispose(); statueLevers = null;
            shallowWater?.Dispose(); shallowWater = null;
            RestoreVoidCatchers();
            proxy?.Release(false); proxy = null; benchExit?.Dispose(); benchExit = null;
            GeometryFrame = visualGeometry = -1; anchor = null; nativeInteraction = false; encounterSuspended = false; scene = current;
            sceneHandle = activeScene.handle;
            log("HK_HUB_ROOM scene=" + scene + " handle=" + sceneHandle + " oldGeometryInvalidated=True");
        }
        if (!Allowed(scene))
        {
            RestoreWater();
            return;
        }
        var hero = HeroController.instance;
        var gm = GameManager.instance;
        if (hero == null || gm == null) return;
        if (gm.profileID != 4) throw new InvalidOperationException("Hub profile boundary changed.");
        // Root travel can export immediately; its snapshot must include the
        // same native solids as every later room-entry path.
        if (YiGodhome.Geometry.RoomFeatures.ClimbingWalls(scene).Length > 0) climbingWalls ??= new HallClimbingWalls(log, scene);
        if (scene == "GG_Atrium" || scene == "GG_Uumuu" || scene == "GG_Flukemarm") shallowWater ??= new HubShallowWater(log, scene); // Alternate Uumuu arena has no native water.
        if (rootArrival && scene == "GG_Atrium" && gm.HasFinishedEnteringScene && !hero.controlReqlinquished) {
            var bench = Resources.FindObjectsOfTypeAll<PlayMakerFSM>().Single(f => f.gameObject.scene.name == scene && f.name == "RestBench" && f.FsmName == "Bench Control");
            var floor = Physics2D.RaycastAll(bench.transform.position + Vector3.up * 2, Vector2.down, 6, 1 << 8)
                .Where(h => h.collider != null && !h.collider.isTrigger && h.normal.y > .99f).OrderBy(h => h.distance).FirstOrDefault();
            if (floor.collider == null) throw new InvalidOperationException("Debug root arrival lacks native ground.");
            hero.transform.position = new Vector3(bench.transform.position.x, floor.point.y + 1, hero.transform.position.z);
            hero.GetComponent<Rigidbody2D>().position = hero.transform.position;
            Physics2D.SyncTransforms(); rootArrival = false;
            proxy?.Release(false); proxy = null;
            GeometryFrame = visualGeometry = -1; anchor = null;
            CaptureGeometry(hero, false);
            log("HK_DEBUG_ROOT_ARRIVAL nativeRoot=True position=" + hero.transform.position);
        }
        if (GeometryFrame < 0 && combat && BossSceneController.Instance != null && hero.sceneEntryGate != null
            && hero.sceneEntryGate.gameObject.scene.name == scene
            && Vector2.Distance(hero.transform.position, hero.sceneEntryGate.transform.position) < 8
            && Physics2D.RaycastAll(hero.transform.position, Vector2.down, 20, 1 << 8).Any(h => h.collider != null && !h.collider.isTrigger && h.normal.y > .99f)) {
            CaptureGeometry(hero, true);
            log("HK_ARENA_GEOMETRY_EARLY geometry=" + GeometryFrame + " transitioned=" + BossSceneController.Instance.HasTransitionedIn);
        }
        if (scene == "GG_Workshop")
        {
            if (combat) statueLevers ??= new HallStatueLevers(log);
        }
        if (scripted && gm.HasFinishedEnteringScene && auditedAudioScene != scene)
        {
            auditedAudioScene = scene;
            foreach (var fsm in Resources.FindObjectsOfTypeAll<PlayMakerFSM>().Where(f => f.gameObject.scene.name == scene))
                foreach (var state in fsm.FsmStates)
                    foreach (var action in state.Actions.Where(a => a.GetType().Name.Contains("Audio")))
                        foreach (var field in action.GetType().GetFields())
                            if (field.GetValue(action) is HutongGames.PlayMaker.FsmObject obj && obj.Value is AudioClip clip)
                                log("HK_SCENE_AUDIO_AUDIT object=" + fsm.name + " fsm=" + fsm.FsmName + " state=" + state.Name + " clip=" + clip.name + " action=" + action.GetType().Name);
        }
        // InputManager.Setup clears registrations made during plugin startup.
        interactionBinding ??= new InteractionBinding(() => Ready && Allowed(scene)
            && GameManager.instance != null && GameManager.instance.profileID == 4
            && GameManager.instance.gameState.ToString() == "PLAYING" && HeroController.instance != null
            && !HeroController.instance.controlReqlinquished, log,
            () => Allowed(scene) && GameManager.instance?.profileID == 4 && !Ready && !nativeInteraction, nativeInteract);
        HideKnight();
        if (encounterSuspended) return;
        // Swimming is Knight locomotion, not a door/menu interaction. Let native
        // Yi movement continue against imported terrain; water effects stay native.
        if (knightWater == null)
        {
            knightWater = hero.GetComponents<PlayMakerFSM>().Single(f => f.FsmName == "Surface Water");
            if (knightWater.ActiveStateName != "Inactive")
                throw new InvalidOperationException("Cannot acquire Knight water locomotion while already swimming.");
            waterWasEnabled = knightWater.enabled;
            knightWater.enabled = false;
            log("HK_HUB_WATER_OWNERSHIP knightSwimming=False nativeYiMovement=True waterEffectsUnchanged=True");
        }
        if (dreamReturn == null || dreamHero != hero) { dreamHero = hero; dreamReturn = hero.GetComponents<PlayMakerFSM>().Single(f => f.FsmName == "Dream Return"); }
        if (GeometryFrame < 0 && ReturnPreparation.IsHub(scene) && RoomEntryPresentation.Returning
            && (dreamReturn.ActiveStateName == "GG Return" || dreamReturn.ActiveStateName == "Prostrate"
                || dreamReturn.ActiveStateName == "Ready" || dreamReturn.ActiveStateName == "Get Up"))
        {
            CaptureGeometry(hero, true);
            log("HK_RETURN_GEOMETRY_EARLY geometry=" + GeometryFrame + " state=" + dreamReturn.ActiveStateName + " nativeFrame=" + Time.frameCount);
        }
        if (ReturnPreparation.IsHub(scene) && dreamReturn.ActiveStateName == "Ready")
        {
            if (!dreamReturn.FsmStates.Single(s => s.Name == "Ready").Transitions.Any(t => t.EventName == "GET UP" && t.ToState == "Get Up"))
                throw new InvalidOperationException("Native dream wake contract changed.");
            hero.AcceptInput(); dreamReturn.SendEvent("GET UP");
            log("HK_HUB_RETURN_WAKE event=GET UP");
        }
        if (!waterAudited && scene == "GG_Atrium")
        {
            waterAudited = true;
            foreach (var bench in Resources.FindObjectsOfTypeAll<PlayMakerFSM>().Where(f => f.gameObject.scene.name == scene && f.FsmName == "Bench Control"))
                log("HK_BENCH_PLACEMENT name=" + bench.name + " position=" + bench.transform.position);
            foreach (var fsm in Resources.FindObjectsOfTypeAll<PlayMakerFSM>().Where(f => f.gameObject.scene.name == scene
                && (f.name.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0 || f.FsmName.IndexOf("swim", StringComparison.OrdinalIgnoreCase) >= 0 || f.name.IndexOf("acid", StringComparison.OrdinalIgnoreCase) >= 0)))
            {
                log("HK_HUB_WATER object=" + fsm.name + " fsm=" + fsm.FsmName + " position=" + fsm.transform.position);
                foreach (var state in fsm.FsmStates)
                    log("HK_HUB_WATER_STATE name=" + state.Name + " transitions=" + string.Join(",", state.Transitions.Select(t => t.EventName + "->" + t.ToState))
                        + " actions=" + string.Join(",", state.Actions.Select(a => a.GetType().FullName)));
            }
        }
        // The automated route exercises the existing native door fixture before
        // attaching Yi in the Hall. Physical tests start with Yi at the hub bench.
        if (scene == "GG_Workshop") automatedHallReached = true;
        if (scripted && scene == "GG_Workshop" && !statuesAudited)
        {
            statuesAudited = true;
            foreach (var statue in Resources.FindObjectsOfTypeAll<BossStatue>().Where(s => s.gameObject.scene.name == scene).OrderBy(s => s.transform.position.y).ThenBy(s => s.transform.position.x))
                log("HK_STATUE_PLACEMENT state=" + statue.statueStatePD + " x=" + statue.transform.position.x + " y=" + statue.transform.position.y);
        }
        if (!automatedHallReached && scene == "GG_Atrium" && scripted && !SessionConfiguration.Current.Has("pantheon-probe")) return;
        if ((gm.playerData.atBench || benchExit != null && !benchExit.Dismounted) && GeometryFrame < 0
            && (scene == "GG_Atrium" || scene == "GG_Workshop") && !nativeInteraction)
        {
            benchExit ??= new HeroProxy(log, benchScene: scene);
            benchExit.Update(null);
            if (!benchExit.Dismounted) return;
        }
        if (proxy?.Ready == true && hero.controlReqlinquished)
        {
            nativeInteraction = true;
            proxy.Release(false); proxy = null;
            HideKnight();
            log("HK_HUB_INTERACTION nativeControl=True");
            log("HK_HUB_INTERACTION_POSITION hero=" + hero.transform.position + " body=" + hero.GetComponent<Rigidbody2D>().position);
            foreach (var fsm in hero.GetComponents<PlayMakerFSM>()) log("HK_HUB_NATIVE_STATE fsm=" + fsm.FsmName + " state=" + fsm.ActiveStateName);
        }
        if (nativeInteraction && Time.realtimeSinceStartup >= nextInteractionTrace)
        {
            nextInteractionTrace = Time.realtimeSinceStartup + 0.25f;
            log("HK_HUB_INTERACTION_SAMPLE hero=" + hero.transform.position + " body=" + hero.GetComponent<Rigidbody2D>().position + " velocity=" + hero.GetComponent<Rigidbody2D>().linearVelocity);
            foreach (var fsm in UnityEngine.Object.FindObjectsByType<PlayMakerFSM>(FindObjectsSortMode.None).Where(f => f.FsmName == "npc_control" && f.gameObject.scene.name == scene && f.ActiveStateName != "Idle"))
                log("HK_HUB_STATUE_STATE object=" + fsm.transform.parent.name + " state=" + fsm.ActiveStateName
                    + " floats=" + string.Join(",", fsm.FsmVariables.FloatVariables.Select(v => v.Name + "=" + v.Value)));
        }
        if (scripted && nativeInteraction && !scriptedMenuClosed && scene == "GG_Workshop")
        {
            var ui = UnityEngine.Object.FindFirstObjectByType<BossChallengeUI>();
            if (ui != null && Resources.FindObjectsOfTypeAll<BossStatue>().Any(s => s.gameObject.scene.name == scene && s.bossUIControlFSM.ActiveStateName == "Open UI"))
            {
                scriptedMenuClosed = true; log("HK_HUB_MENU_FIXTURE opened=True nativeCancel=True hero=" + hero.transform.position);
                if (combat && (!YiGodhome.Bridge.SessionConfiguration.Current.Has("rest-probe") || ControlSource.DiagnosticRestComplete)) { log("HK_HUB_COMBAT_STATUE_SELECT tier=" + DiagnosticDifficulty.Tier); ui.LoadBoss(DiagnosticDifficulty.Tier); }
                else ui.Hide();
            }
        }
        if (!gm.HasFinishedEnteringScene || gm.gameState.ToString() != "PLAYING" || hero.controlReqlinquished || gm.playerData.atBench) return;
        if (nativeInteraction)
        {
            nativeInteraction = false;
            log("HK_HUB_INTERACTION_RESUMED geometry=" + GeometryFrame + " mappingPreserved=True");
        }
        if (GeometryFrame < 0) {
            bool wasWaiting=entryFloorAdmission.Waiting;
            var floor=GeometryExport.EntryFloor(scene,hero);
            var admission=entryFloorAdmission.Observe(Time.realtimeSinceStartupAsDouble,floor.collider!=null);
            if(admission==EntryFloorAdmission.Verdict.Expired)
                throw new InvalidOperationException("Native room entry did not reach a valid same-room floor within eight seconds.");
            if(admission==EntryFloorAdmission.Verdict.Waiting) {
                hero.IgnoreInput(); // Leave native gravity/entry callbacks running.
                if(!wasWaiting)log("HK_ENTRY_FLOOR_WAIT scene="+scene+" gate="+hero.sceneEntryGate?.name+" position="+hero.transform.position+" limitSeconds=8 nativeGravity=True");
                return;
            }
            if(wasWaiting)log("HK_ENTRY_FLOOR_READY scene="+scene+" position="+hero.transform.position+" floor="+floor.point);
            CaptureGeometry(hero,false);
        }
        if (pose?.GeometryFrame != GeometryFrame) { proxy?.Update(null); return; }
        if (combat && scene == "GG_Workshop" && !MenuIsOpen) {
            statueLevers ??= new HallStatueLevers(log);
            statueLevers.Update(pose);
        }
        entryActive = pose.Vitals?.EntryActive == true;
        if(pose.Vitals!=null && verticalEntryGate.Observe(pose.GeometryFrame,entryActive,pose.Grounded))
            log("HK_VERTICAL_ENTRY_LANDED geometry="+GeometryFrame+" arrivalGateReleased=True");
        proxy ??= new HeroProxy(log, arena: combat && scene == EncounterProfile.Selected.Scene, hubScene: scene);
        proxy.Update(pose);
        if (combat && proxy.Ready && anchor!.RestPoints.Length != 0)
            foreach (var bench in Resources.FindObjectsOfTypeAll<PlayMakerFSM>().Where(f => f.gameObject.scene.name == scene && f.FsmName == "Bench Control"))
                if (!restBenches.ContainsKey(bench.gameObject))
                { restBenches.Add(bench.gameObject, bench.gameObject.activeSelf); bench.gameObject.SetActive(false); log("HK_REST_BENCH_REPLACED nativeKnightDismounted=True"); }
        if (proxy.Ready && !voidOwnershipAcquired)
        {
            foreach (var catcher in DreamVoidSource.Find(scene))
                if (!voidCatchers.ContainsKey(catcher))
                {
                    DreamVoidSource.Capture(catcher);
                    voidCatchers.Add(catcher, catcher.enabled); catcher.enabled = false;
                    log("HK_DREAM_VOID_OWNERSHIP nativeYiRecovery=True geometry=" + GeometryFrame);
                }
            voidOwnershipAcquired = true;
        }
        if (proxy.Ready && knightActions.Count == 0)
        {
            var action = hero.GetComponents<PlayMakerFSM>().Single(f => f.FsmName == "Dream Nail");
            knightActions.Add(action, action.enabled); action.enabled = false;
            log("HK_HUB_ACTION_ISOLATED fsm=Dream Nail");
            if (scripted)
            {
                if (hero.CanCast()) throw new InvalidOperationException("Native spell permission escaped Yi ownership.");
                log("HK_NATIVE_SPELL_CHECK canCast=False nativeMethod=True");
            }
        }
        HideKnight();
        if (scripted && scene == "GG_Workshop" && proxy.Ready)
        {
            if (terrainFixtureStage == 0)
            {
                terrainFixtureStage = 1; terrainFixtureAt = Time.realtimeSinceStartup;
                terrainFixture = new GameObject("YiGodhome Live Terrain Fixture") { layer = 8 };
                terrainFixture.transform.position = hero.transform.position + Vector3.up * 100;
                terrainFixture.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                terrainFixture.AddComponent<BoxCollider2D>().size = new Vector2(2, 1);
                log("HK_HUB_TERRAIN_FIXTURE added=True authoredObjectsChanged=False");
            }
            else if (terrainFixtureStage == 1 && Time.realtimeSinceStartup - terrainFixtureAt > 0.6f)
            {
                terrainFixtureStage = 2; UnityEngine.Object.Destroy(terrainFixture); terrainFixture = null;
                log("HK_HUB_TERRAIN_FIXTURE removed=True authoredObjectsChanged=False");
            }
        }
        if (anchor != null && Time.realtimeSinceStartup >= nextGeometryCheck)
        {
            nextGeometryCheck = Time.realtimeSinceStartup + 1f / 30;
            long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            long beforeTick = System.Diagnostics.Stopwatch.GetTimestamp();
            GeometryExport.Save(log, scene, hero, true, anchor);
            geometryAllocated += GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
            geometryMs += (System.Diagnostics.Stopwatch.GetTimestamp() - beforeTick) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            geometrySamples++;
        }
        if (Time.realtimeSinceStartup >= nextTrace)
        {
            nextTrace = Time.realtimeSinceStartup + 1;
            if (geometrySamples > 0) log("HK_GEOMETRY_COST samples=" + geometrySamples + " allocatedKB=" + geometryAllocated / 1024 + " totalMs=" + geometryMs.ToString("F3"));
            geometryAllocated = 0; geometryMs = 0; geometrySamples = 0;
            log("HK_HUB_STATE scene=" + scene + " geometry=" + GeometryFrame + " ready=" + Ready + " canInteract=" + hero.CanInteract()
                + " knightVisible=" + (knight != null && knight.enabled) + " nativeMovement=" + hero.enabled);
        }
    }
    private void CaptureGeometry(HeroController hero, bool earlyReturn)
    {
        nativeInteraction = false;
        if (!earlyReturn && scripted && combat && scene == "GG_Workshop")
        {
            var statue = Resources.FindObjectsOfTypeAll<BossStatue>().Single(s => s.gameObject.scene.name == scene && (s.statueStatePD == EncounterProfile.Selected.Statue || s.dreamStatueStatePD == EncounterProfile.Selected.Statue));
            if (EncounterProfile.Selected.Roster) statue.SetDreamVersion(statue.dreamStatueStatePD == EncounterProfile.Selected.Statue, false, false);
            var inspect = statue.GetComponentsInChildren<PlayMakerFSM>().Single(f => f.name == "Inspect" && f.FsmName == "npc_control");
            hero.transform.position = inspect.transform.position;
            Physics2D.SyncTransforms();
            log("HK_STATUE_PLACEMENT_FIXTURE profile=" + EncounterProfile.Selected.Key + " scriptedOnly=True");
        }
        if (!earlyReturn && scripted && scene == "GG_Workshop" && SessionConfiguration.Current.Has("lever-probe")) {
            statueLevers ??= new HallStatueLevers(log);
            hero.transform.position = statueLevers.FixturePlacement(); Physics2D.SyncTransforms();
        }
        if (!earlyReturn) hero.IgnoreInput();
        GeometryFrame = GeometryExport.Save(log, scene, hero, true);
        string path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!, "godhome-geometry.bin");
        using (var file = new System.IO.MemoryStream(YiGodhome.Geometry.Codec.ReadFileBytes(path))) anchor = YiGodhome.Geometry.Codec.Read(file);
        visualGeometry = GeometryFrame;
        if(anchor.EntryStyle>=5 && hero.sceneEntryGate!=null)
            verticalEntryGate.Arm(sceneHandle,GeometryFrame,hero.sceneEntryGate.GetInstanceID());
        log("HK_HUB_GEOMETRY scene=" + scene + " frame=" + GeometryFrame);
    }
    public Pose? Current(Pose? pose) => Ready && pose?.GeometryFrame == GeometryFrame ? pose : null;
    public Pose? Visible(Pose? pose) => pose?.GeometryFrame == visualGeometry ? (proxy?.Ready == true ? proxy.AppliedPose ?? pose : pose) : null;
    internal static bool AllowGateTransition(TransitionPoint __instance) {
        var owner=current;
        if(owner==null || !HollowSols.Foundation.NativeSaveContext.Enabled
            || !owner.verticalEntryGate.Blocks(__instance.gameObject.scene.handle,__instance.GetInstanceID()))return true;
        if(!owner.gateWaitLogged){owner.gateWaitLogged=true;owner.log("HK_VERTICAL_ENTRY_GATE_HELD gate="+__instance.name+" geometry="+owner.GeometryFrame+" untilNativeYiLanding=True");}
        return false;
    }
    private void HideKnightBeforeRender(Camera camera)
    {
        // EnterScene can enable its root renderer after our Update. Suppression
        // belongs immediately before drawing too, without stopping native entry.
        if(GameManager.instance?.profileID==4 && Allowed(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name))HideKnight();
    }
    public void HideKnight()
    {
        var renderer = HeroController.instance == null ? null : HeroController.instance.GetComponent<MeshRenderer>();
        if (renderer == null) return;
        if (knight != renderer)
        {
            if (knight != null) knight.enabled = originalVisible;
            knight = renderer; originalVisible = renderer.enabled;
            foreach (var r in renderer.GetComponentsInChildren<Renderer>(true).Where(r => r.GetType().Name == "ParticleSystemRenderer"))
                if (!hiddenKnightParticles.ContainsKey(r)) hiddenKnightParticles.Add(r, r.forceRenderingOff);
            log("HK_KNIGHT_PARTICLES_HIDDEN count=" + hiddenKnightParticles.Count + " cameraMasksUnchanged=True");
        }
        foreach (var entry in hiddenKnightParticles) if (entry.Key != null) entry.Key.forceRenderingOff = true;
        renderer.enabled = false;
        var hero = HeroController.instance;
        if (hero != null)
        {
            var death = (GameObject?)deathPrefabField.GetValue(hero);
            if (death != null && death.activeInHierarchy && death != deathPresentation)
            {
                deathPresentation = death;
                foreach (var r in death.GetComponentsInChildren<Renderer>(true))
                { if (!hiddenDeathRenderers.ContainsKey(r)) hiddenDeathRenderers.Add(r, r.forceRenderingOff); r.forceRenderingOff = true; }
                log("HK_DEATH_PRESENTATION_HIDDEN nativeReturnLogic=True renderers=" + hiddenDeathRenderers.Count);
            }
        }
    }
    public void Dispose()
    {
        Camera.onPreCull-=HideKnightBeforeRender;
        verticalEntryGate.Reset();if(current==this)current=null;
        statueLevers?.Dispose(); statueLevers = null;
        climbingWalls?.Dispose(); climbingWalls = null;
        shallowWater?.Dispose(); shallowWater = null;
        titles.Dispose();
        music.Dispose();
        entryPresentation.Dispose();
        spells.Dispose();
        interactionBinding?.Dispose();
        proxy?.Dispose(); benchExit?.Dispose();
        RestoreWater();
        if (terrainFixture != null) UnityEngine.Object.Destroy(terrainFixture);
        if (knight != null) knight.enabled = originalVisible;
        foreach (var entry in hiddenDeathRenderers) if (entry.Key != null) entry.Key.forceRenderingOff = entry.Value;
        hiddenDeathRenderers.Clear();
        foreach (var entry in hiddenKnightParticles) if (entry.Key != null) entry.Key.forceRenderingOff = entry.Value;
        hiddenKnightParticles.Clear();
    }
    private void RestoreWater()
    {
        foreach (var entry in restBenches) if (entry.Key != null) entry.Key.SetActive(entry.Value);
        restBenches.Clear();
        RestoreVoidCatchers();
        if (knightWater != null) knightWater.enabled = waterWasEnabled;
        knightWater = null;
        foreach (var entry in knightActions) if (entry.Key != null) entry.Key.enabled = entry.Value;
        knightActions.Clear();
    }
    private void RestoreVoidCatchers()
    {
        foreach (var pair in voidCatchers) if (pair.Key != null) pair.Key.enabled = pair.Value;
        voidCatchers.Clear();
        voidOwnershipAcquired = false;
    }
}
