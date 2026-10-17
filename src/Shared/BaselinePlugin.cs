using System;
using System.Linq;
using System.Diagnostics;
using System.IO;
using BepInEx;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;
using YiGodhome.Bridge;

#if HOLLOW_KNIGHT && HOLLOWSOLS_PUBLIC_RELEASE
using PlayerDebugMenu = YiGodhome.DisabledPlayerTools;
#endif

namespace YiGodhome;

[BepInPlugin(Id, "Yi in Godhome — protected baseline", "0.1.0")]
public sealed partial class BaselinePlugin : BaseUnityPlugin
{
#if HOLLOW_KNIGHT
    public const string Id = "local.yigodhome.hollowknight";
    private const string Game = "HollowKnight";
#else
    public const string Id = "local.yigodhome.ninesols";
    private const string Game = "NineSols";
    private Action<string>? nativeCostLog;
    private Action<string> NativeCostLog => nativeCostLog ??= Log;
#endif
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private double nextLog;
    private int smokeSeconds;
    private bool quitting;
    private bool returnGrace = false;
    private string eventLog = "";
    private BoundedEventLog? eventWriter;
    private readonly object logLock = new object();
    private Endpoint? endpoint;
    private bool inspecting;
    private bool previewing;
    private bool humanTest;
    private bool memoryPreview;
    private bool geometryPreview;
    private bool poseProbe;
    private bool inputProbe;
    private bool visualProbe;
    private bool proxyProbe;
    private bool arenaProbe;
    private bool hubProbe;
    private bool contactBridge;
    private bool targetProbe;
    private bool hitProbe, hitCompleted;
    private bool lifecycleProbe;
    private EncounterStatus? encounterStatus;
    private readonly EncounterLifetime encounterLifetime = new EncounterLifetime();
    private bool LifecyclePaused =>
#if HOLLOW_KNIGHT
        GameManager.instance?.IsGamePaused() == true || menuPresenter?.Open == true;
#else
        NativeInputBridge.HostPaused || NativeMenu.Open;
#endif
    private bool retryProbe;
#if HOLLOW_KNIGHT
    private bool retryStarted;
#endif
#if HOLLOW_KNIGHT
    private bool retryAnnounced;
#endif
    private Guid channelGeneration;
    private HitEndpoint? hitEndpoint;
    private ContactEndpoint? contactEndpoint;
    private VisualEndpoint? visualEndpoint;
    private VisualEndpoint? arrowEndpoint;
    #if HOLLOW_KNIGHT
    private LocalBossAura? rageMask;
#endif
    private VisualEndpoint? hudEndpoint;
    private VisualEndpoint? hudEffectsEndpoint;
    private VisualEndpoint? menuEndpoint;
    private VisualEndpoint? promptEndpoint;
    private VisualEndpoint? bossHudEndpoint;
    private VisualEndpoint? restEndpoint;
    private VisualEndpoint? wallEndpoint;
    private VisualEndpoint? paletteEndpoint;
    private VisualEndpoint? summonEndpoint;
#if HOLLOW_KNIGHT
#else
#endif
#if HOLLOW_KNIGHT
    private HostAttackPresentation? attackPresentation;
    private PlayerDebugMenu? playerDebugMenu;
    private HollowSolsConfiguration? configurationMenu;
    private string? debugTravel;
    private bool debugCancel;
    private bool diagnosticDrain;
    private long arenaImageGeometry = -1;
    private float arenaFadeTraceUntil, arenaFadeTraceNext;
    private bool CanDebugTravel() => hubCombat && GameManager.instance?.profileID == 4 && HubPresence.Allowed(UnitySceneManager.GetActiveScene().name)
        && GameManager.instance.gameState.ToString() == "PLAYING" && menuPresenter?.Open != true
        && (encounterStatus == null || encounterStatus.Phase == EncounterPhase.Active || encounterStatus.Phase == EncounterPhase.Returned && !hitCompleted);
    private void RequestDebugTravel(string destination)
    {
        if (!BuildFeatures.DevelopmentTools) return;
        if (!CanDebugTravel() || destination != "GG_Atrium" && destination != "GG_Workshop") return;
        if (UnitySceneManager.GetActiveScene().name == destination) return;
        debugTravel = destination; debugCancel = encounterStatus?.Phase == EncounterPhase.Active;
        Log("HK_DEBUG_TRAVEL_REQUEST destination=" + destination + " cancelEncounter=" + debugCancel);
    }
    private void TryBeginDebugTravel()
    {
        if (debugTravel == null || debugCancel || hitCompleted || hubPresence?.Ready != true
            || encounterStatus != null && encounterStatus.Phase != EncounterPhase.Returned || !CanDebugTravel()) return;
        string destination = debugTravel;
        // Cancellation may already have returned to the requested Hall. Do not
        // queue a redundant second room load after the player wakes at the statue.
        if (UnitySceneManager.GetActiveScene().name == destination) {
            debugTravel = null; Log("HK_DEBUG_TRAVEL_COMPLETE destination=" + destination + " nativeReturnAlreadyArrived=True"); return;
        }
        if (BossSequenceController.IsInSequence) { BossSequenceController.Reset(); BossSceneController.SetupEvent = null; Log("HK_SEQUENCE_CANCELLED manualTravel=True nativeReset=True pendingOutcomes=0"); }
        if (destination == "GG_Atrium") hubPresence.RequestRootArrival();
        endpoint?.PublishControls(Time.frameCount, default);
        GameManager.instance.BeginSceneTransition(new GameManager.SceneLoadInfo { SceneName = destination,
            EntryGateName = destination == "GG_Workshop" ? "left1" : "Door_Workshop", EntryDelay = 0,
            Visualization = GameManager.SceneLoadVisualizations.Default });
        debugTravel = null;
        Log("HK_DEBUG_TRAVEL_BEGIN destination=" + destination + " nativeTransition=True pendingOutcomes=0");
    }
    private NativeMenuPresenter? menuPresenter;
    private InteractionPromptPresenter? promptPresenter;
    private BossHudPresenter? bossHudPresenter;
    private VisualPresenter? restPresenter;
    private NativeWorldHudPresenter? summonPresenter;
#else
    private NativeAttackPalette? attackPalette;
    private NativeMenuCapture? menuCapture;
    private NativePromptCapture? promptCapture;
    private NativeBossCapture? bossHudCapture;
    private NativeRestPoint? restPoint;
    private NativeRestCapture? restCapture;
    private NativeWallCapture? wallCapture;
    private NativeSummonCapture? summonCapture;
#endif
    private Guid sessionId;
#if HOLLOW_KNIGHT
    private long poseSamples;
    private float poseMinY = float.PositiveInfinity, poseMaxY = float.NegativeInfinity;
    private GodhomeProbe? godhomeProbe;
    private ControlSource? controlSource;
    private VisualPresenter? visualPresenter;
    private VisualPresenter? arrowPresenter;
    private readonly VisualMotionContinuity visualMotion = new();
    private HeroProxy? heroProxy;
    private EncounterReadyGate? encounterReadyGate;
    private HostContactSource? contactSource;
    private HostHitReceiver? hostHits;
    private YiVitalsHud? yiHud;
    private YiVitalsHud? yiEffectsHud;
    private HubPresence? hubPresence;
#else
    private BattleMemoriesProbe? memoryProbe;
    private NativeContentAudit? contentAudit;
    private NativeAmbienceIsolation? ambience;
    private NativeRoomIsolation? roomIsolation;
    private NativeInputBridge? inputBridge;
    private NativeVisualCapture? visualCapture;
    private NativeVisualCapture? arrowCapture;
    private NativeHudCapture? hudCapture;
    private NativeHudCapture? hudEffectsCapture;
    private NativeHostContact? hostContact;
    private NativeTargetProbe? nativeTarget;
    private NativeAudioMirror? audioMirror;
    private readonly System.Collections.Generic.Dictionary<long, HitKind> pendingHitKinds = new System.Collections.Generic.Dictionary<long, HitKind>();
#endif

    private void Awake()
    {
        if (Environment.GetEnvironmentVariable("HOLLOWSOLS_MENU") == "1" && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("YIGODHOME_SESSION")))
        { enabled = false; return; }
        if (!Guid.TryParse(Environment.GetEnvironmentVariable("YIGODHOME_SESSION"), out var session))
        { Environment.Exit(75); return; }
        sessionId = session;
        eventLog = Path.Combine(Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!, "plugin.log");
        // Keep a single exclusive writer: external file reconciliation previously split a live log.
        eventWriter = new BoundedEventLog(eventLog,retainPerformance:HollowSolsMenuPlugin.Instance?.FixtureRosterEndurance==true);
        if (BuildFeatures.DevelopmentTools) detailedCapture = new DetailedEventCapture(Path.GetDirectoryName(eventLog)!, "session=" + sessionId + " role=" + Game);
        SessionConfiguration configuration;
        try { configuration = SessionConfiguration.Current; }
        catch (Exception error) { FailSession("SESSION_CONFIGURATION_FAILED", error); return; }
        Log("SESSION_CONFIGURATION capabilities=" + configuration);
        if (Environment.GetEnvironmentVariable("HOLLOWSOLS_MENU") != "1") NativeStartupScreens.Install(Log);
        Log("SESSION_BUILD module=" + GetType().Assembly.ManifestModule.ModuleVersionId + " width=" + Screen.width + " height=" + Screen.height
            + " vsync=" + QualitySettings.vSyncCount + " frameCap=" + Application.targetFrameRate + " quality=" + QualitySettings.GetQualityLevel());
        inspecting = Environment.GetEnvironmentVariable("YIGODHOME_MODE") == "inspect-hk";
        humanTest = Environment.GetEnvironmentVariable("YIGODHOME_MODE") == "human-hk";
        inputProbe = Environment.GetEnvironmentVariable("YIGODHOME_MODE") == "input-hk" || Environment.GetEnvironmentVariable("YIGODHOME_MODE") == "input-ns";
        poseProbe = inputProbe || Environment.GetEnvironmentVariable("YIGODHOME_MODE") == "pose-hk" || Environment.GetEnvironmentVariable("YIGODHOME_MODE") == "pose-ns";
        previewing = humanTest || Environment.GetEnvironmentVariable("YIGODHOME_MODE") == "preview-hk" || Environment.GetEnvironmentVariable("YIGODHOME_MODE") == "pose-hk" || Environment.GetEnvironmentVariable("YIGODHOME_MODE") == "input-hk";
        memoryPreview = Environment.GetEnvironmentVariable("YIGODHOME_MODE") == "memory-ns" || Environment.GetEnvironmentVariable("YIGODHOME_MODE") == "pose-ns" || Environment.GetEnvironmentVariable("YIGODHOME_MODE") == "input-ns";
        geometryPreview = memoryPreview && File.Exists(YiGodhome.Geometry.Codec.RuntimePath(Path.Combine(Path.GetDirectoryName(eventLog)!, "godhome-geometry.bin")));
        visualProbe = configuration.Visual;
        proxyProbe = configuration.Proxy;
        arenaProbe = configuration.Arena;
        hubProbe = configuration.Hub;
        contactBridge = configuration.Contacts;
        targetProbe = configuration.Targets;
        hitProbe = configuration.Hits;
        lifecycleProbe = hitProbe;
        hubCombat = configuration.HubCombat;
        if (hubCombat) contactBridge = false;
        retryProbe = lifecycleProbe && YiGodhome.Bridge.SessionConfiguration.Current.Has("retry-probe");
        if (visualProbe) visualEndpoint = new VisualEndpoint(session, Game == "HollowKnight" ? Role.HollowKnight : Role.NineSols);
        if (visualProbe) arrowEndpoint = new VisualEndpoint(session, Game == "HollowKnight" ? Role.HollowKnight : Role.NineSols, arrows: true);
        if (visualProbe) hudEndpoint = new VisualEndpoint(session, Game == "HollowKnight" ? Role.HollowKnight : Role.NineSols, true);
        if (visualProbe) hudEffectsEndpoint = new VisualEndpoint(session, Game == "HollowKnight" ? Role.HollowKnight : Role.NineSols, hudEffects:true);
        if (visualProbe) bossHudEndpoint = new VisualEndpoint(session, Game == "HollowKnight" ? Role.HollowKnight : Role.NineSols, boss: true);
        if (targetProbe) summonEndpoint = new VisualEndpoint(session, Game == "HollowKnight" ? Role.HollowKnight : Role.NineSols, summons: true);
        if (hubProbe) wallEndpoint = new VisualEndpoint(session, Game == "HollowKnight" ? Role.HollowKnight : Role.NineSols, walls: true);
        if (hubProbe) restEndpoint = new VisualEndpoint(session, Game == "HollowKnight" ? Role.HollowKnight : Role.NineSols, rest: true);
        if (hubProbe) promptEndpoint = new VisualEndpoint(session, Game == "HollowKnight" ? Role.HollowKnight : Role.NineSols, prompt: true);
        if (hubProbe) paletteEndpoint = new VisualEndpoint(session, Game == "HollowKnight" ? Role.HollowKnight : Role.NineSols, palette: true);
        if (hubProbe) menuEndpoint = new VisualEndpoint(session, Game == "HollowKnight" ? Role.HollowKnight : Role.NineSols, menu: true);
#if HOLLOW_KNIGHT
        HollowSolsSettings.Initialize(Config);
        configurationMenu=new HollowSolsConfiguration(Log);
        if (arenaProbe) encounterReadyGate = new EncounterReadyGate(Log);
        if (hubProbe) hubPresence = new HubPresence(Log,()=>controlSource?.InteractionValue??0);
        if (previewing) godhomeProbe = new GodhomeProbe(Log, humanTest, HollowSols.Foundation.NativeSaveContext.Enabled);
        if (HostFrameProbe.Enabled) hostFrameProbe = new HostFrameProbe(Log);
#else
        if (memoryPreview) { memoryProbe = new BattleMemoriesProbe(Log); memoryProbe.DetailedCaptureId = () => detailedCapture?.ActiveId ?? Guid.Empty; }
#endif
        int.TryParse(Environment.GetEnvironmentVariable("YIGODHOME_SMOKE_SECONDS"), out smokeSeconds);
        Log("SESSION_DURATION seconds=" + smokeSeconds + " manualExit=" + (smokeSeconds == 0));
        UnitySceneManager.sceneLoaded += SceneLoaded;
        Log("YIGODHOME_PLUGIN_READY game=" + Game + " session=" + session + " unity=" + Application.unityVersion + " saveMode=" + (HollowSols.Foundation.NativeSaveContext.Enabled ? "mod-owned-paired" : inspecting || previewing ? "hk-slot4-read-only" : "disabled"));
        if (Environment.GetEnvironmentVariable("YIGODHOME_BRIDGE") == "1")
            endpoint = new Endpoint(session, Game == "HollowKnight" ? Role.HollowKnight : Role.NineSols);
    }

    private void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Log("SCENE_READY game=" + Game + " scene=" + scene.name + " frame=" + Time.frameCount);
#if HOLLOW_KNIGHT
        if (inspecting && scene.name == "Menu_Title") SlotFourInspection.Begin(Log);
        if (hubCombat && EncounterProfile.SelectScene(scene.name)) Log("HK_ENCOUNTER_PROFILE scene=" + scene.name + " key=" + EncounterProfile.Selected.Key);
        godhomeProbe?.SceneLoaded(scene);
#else
        if (contentAudit?.Owns(scene) == true) { contentAudit.SceneLoaded(scene); return; }
        if (NativeContentRepository.RouteSceneLoaded(scene)) return;
        if (Environment.GetEnvironmentVariable("YIGODHOME_MODE") == "inspect-ns-template" && scene.name == "TitleScreenMenu") BattleMemoriesTemplate.Inspect(Log);
        memoryProbe?.SceneLoaded(scene);
#endif
    }
    internal void ObserveCurrentScene() => SceneLoaded(UnitySceneManager.GetActiveScene(), LoadSceneMode.Single);
#if HOLLOW_KNIGHT
    private Controls? pairedFixtureControls;
    internal bool FixturePairedInputReady { get; private set; }
    internal bool FixturePairedReady => hubPresence?.Ready == true && yiHud?.FixtureVisible == true;
    internal bool FixturePairedWorldReady => hubPresence?.Ready==true && endpoint?.FreshPose?.Vitals?.EntryActive==false;
    internal bool FixtureCombatActive=>encounterStatus?.Phase==EncounterPhase.Active;
    internal ContactFrame? FixtureContact=>contactSource?.Latest;
    internal PeerState? FixturePairedPeer=>endpoint?.FreshPose==null?null:endpoint.Peer;
    internal void FixtureTravelToHall(){if(HollowSolsMenuPlugin.Instance?.FixtureCommandsAllowed!=true)throw new InvalidOperationException("Combat travel is not authorized.");RequestDebugTravel("GG_Workshop");}
    internal YiGodhome.Bridge.Pose? FixturePairedPose => endpoint?.FreshPose;
    internal void FixturePairedControlsPanel(bool cancel=false) {
        if (HollowSolsMenuPlugin.Instance?.FixtureCommandsAllowed != true) throw new InvalidOperationException("Native controls fixture is not authorized.");
        configurationMenu?.FixtureNativeControls(cancel);
        if(cancel) {
            menuPresenter?.FixtureMissingPixels(endpoint?.FreshPose);
            Log("HK_CONTROLS_PIXEL_GAP_PASS pauseLeaseRetained=True explicitCloseStillRequired=True");
        }
    }
    internal string FixtureInteractionControllerBinding=>controlSource?.InteractionControllerBinding??"unavailable";
    internal bool FixturePairedUiPolish(string folder)=>configurationMenu?.FixturePolish(folder)==true;
    internal void FixturePairedControl(Controls? controls)
    {
        if (HollowSolsMenuPlugin.Instance?.FixtureCommandsAllowed != true || !HollowSols.Foundation.NativeSaveContext.Enabled)
            throw new InvalidOperationException("Paired input fixture is not authorized.");
        pairedFixtureControls = controls;
    }
#endif
#if !HOLLOW_KNIGHT
    internal Player? FixturePairedPlayer => memoryProbe?.ActivePlayer;
    internal bool FixtureRoomReady(string scene)=>memoryProbe?.Geometry?.SourceScene==scene && memoryProbe.ContentReady && !NativeRoomEntry.Active;
    internal bool FixtureHallReady=>FixtureRoomReady("GG_Workshop");
    internal void FixtureNoskCorner(Action<string> detail)=>memoryProbe?.Geometry?.TraceNoskCorner(detail);
    internal void FixturePlaceInHall(float x,float y)=>FixturePlaceInRoom("GG_Workshop",x,y);
    internal void FixturePlaceInRoom(string scene,float x,float y) {
        if(HollowSolsMenuPlugin.Instance?.FixtureCommandsAllowed!=true || (scene!="GG_Workshop" && scene!="GG_Nosk" && !(scene=="GG_Radiance" && HollowSolsMenuPlugin.Instance.FixtureRadiancePerformance)) || !FixtureRoomReady(scene))throw new InvalidOperationException("Combat placement left its exact native fixture room scope.");
        var player=FixturePairedPlayer??throw new InvalidOperationException("Combat placement has no Yi.");
        var geometry=memoryProbe?.Geometry??throw new InvalidOperationException("Combat placement lost its geometry.");var p=geometry.Map.ToTarget(new YiGodhome.Geometry.Point(x,y));
        player.Velocity=Vector2.zero;player.SetPosition(new Vector3(p.X,p.Y+.1f,player.transform.position.z),true);player.ResetNeedToCheckGroundFlag();Physics2D.SyncTransforms();
        Log("COMBAT_FIXTURE_HALL_PLACED sourceX="+x+" sourceY="+y+" nativePlayer=True");
    }
    internal bool FixturePairedAudioReady => audioMirror?.FixtureReady==true;
    internal void FixturePairedRootPlacement() {
        if(HollowSolsMenuPlugin.Instance?.FixtureCommandsAllowed!=true || !HollowSols.Foundation.NativeSaveContext.Enabled)
            throw new InvalidOperationException("Native root fixture is not authorized.");
        var geometry=memoryProbe?.Geometry??throw new InvalidOperationException("Native root fixture has no imported geometry.");
        if(geometry.RestPoints.Length==0)throw new InvalidOperationException("Existing fixture room has no configured root.");
        var player=FixturePairedPlayer??throw new InvalidOperationException("Native root fixture has no Yi.");
        var location=geometry.Map.ToTarget(geometry.RestPoints[0]);
        player.Velocity=Vector2.zero;player.SetPosition(new Vector3(location.X,location.Y+.1f,player.transform.position.z),true);
        player.ResetNeedToCheckGroundFlag();Physics2D.SyncTransforms();
        Log("NS_LOADOUT_FIXTURE_PLACED configuredRoot=True originalSaveUntouched=True scene="+geometry.SourceScene);
    }
    internal void FixturePairedAudio() => (audioMirror ?? throw new InvalidOperationException("Native audio mirror unavailable.")).FixtureAssert();
    internal bool NativeCampaignDeath => encounterStatus == null && memoryProbe?.ActivePlayer?.health.IsDead == true;
#endif
    internal bool NativePairReady
    {
        get {
#if HOLLOW_KNIGHT
            return HeroController.instance != null && GameManager.instance != null && GameManager.instance.HasFinishedEnteringScene
                && GameManager.instance.gameState.ToString() == "PLAYING" && UnitySceneManager.GetActiveScene().name != "GG_Entrance_Cutscene";
#else
            return memoryProbe?.ActivePlayer != null && memoryProbe.Geometry != null && memoryProbe.ContentReady;
#endif
        }
    }
    internal bool PairOutcomesDrained => (hitEndpoint?.PendingCount ?? 0) == 0
#if !HOLLOW_KNIGHT
        && pendingHitKinds.Count == 0
#endif
        ;
    internal void SuspendPairedGameplay()
    {
#if !HOLLOW_KNIGHT
        NativeInputBridge.PairEnding = true;
#else
        endpoint?.PublishControls(Time.frameCount, default);
        controlSource?.Release(); controlSource = null;
#endif
        pairedEnding = true;
    }
    private bool pairedEnding;
    private void DrainPairedOutcomes()
    {
        endpoint?.PublishFrame(Time.frameCount);
#if HOLLOW_KNIGHT
        if (hostHits != null) hitEndpoint?.ProcessPending(hostHits.Process);
#else
        inputBridge?.Update();
        while (hitEndpoint != null && hostContact != null && hostContact.TryGetCounter(out var counter)) { long id = hitEndpoint.Submit(counter); pendingHitKinds.Add(id, counter.Kind); }
        while (hitEndpoint != null && nativeTarget != null && nativeTarget.TryGetOutcome(out var outcome)) { long id = hitEndpoint.Submit(outcome); nativeTarget.Submitted(id, outcome); pendingHitKinds.Add(id, outcome.Kind); }
        while (hitEndpoint != null && hitEndpoint.TryGetAck(out var ack)) {
            if (!pendingHitKinds.Remove(ack.Sequence)) throw new InvalidOperationException("Unknown drain acknowledgement.");
            (nativeTarget ?? retiredNativeTarget)?.Acknowledged(ack);
        }
#endif
    }

    private void Log(string message)
    {
#if HOLLOW_KNIGHT
        HollowSolsMenuPlugin.Instance?.FixtureObserveGameplay(message);
#endif
        // BepInEx's buffered log can lose its tail when the native game quits.
        lock (logLock) { var line = DateTime.UtcNow.ToString("O") + " " + message; eventWriter?.WriteLine(line); detailedCapture?.Observe(line); }
        // Do not duplicate unlimited telemetry into BepInEx's unbounded log.
        if (message.StartsWith("YIGODHOME_PLUGIN_READY", StringComparison.Ordinal) || message.StartsWith("SESSION_TERMINAL", StringComparison.Ordinal)) Logger.LogInfo(message);
    }

    private void Update()
    {
#if !HOLLOW_KNIGHT
        using var updateCost = new NativeWorkCost.Scope(NativeWorkCost.Update, "plugin_update", NativeCostLog);
#endif
        if (quitting) return;
        if (pairedEnding) { try { DrainPairedOutcomes(); } catch (Exception error) { FailSession("PAIR_DRAIN_FAILED", error); } return; }
        try { if(FinishCompletedDiagnosticCombat())return; }
        catch(Exception error) { FailSession("DIAGNOSTIC_COMPLETION_FAILED",error);return; }
#if HOLLOW_KNIGHT
        using var hostUpdateCost = HostFrameProbe.Measure(12);
        configurationMenu?.Update();
        try { configurationMenu?.Fixture(hubPresence?.Ready==true && UnitySceneManager.GetActiveScene().name=="GG_Workshop"); }
        catch(Exception error) { FailSession("HK_CONFIGURATION_FIXTURE_FAILED",error); return; }
        if (hostFrameProbe?.PixelWindow() == true) {
            hostFrameProbe.Pixels("actor", visualEndpoint); hostFrameProbe.Pixels("hud", hudEndpoint);
            hostFrameProbe.Pixels("boss", bossHudEndpoint); hostFrameProbe.Pixels("menu", menuEndpoint);
            hostFrameProbe.Pixels("rest", restEndpoint); hostFrameProbe.Pixels("prompt", promptEndpoint);
            hostFrameProbe.Pixels("palette", paletteEndpoint); hostFrameProbe.Pixels("summon", summonEndpoint);
            hostFrameProbe.Pixels("wall", wallEndpoint);
        }
        if (BuildFeatures.DevelopmentTools && (SessionConfiguration.Current.ManualTools || SessionConfiguration.Current.RunFixtures && SessionConfiguration.Current.Has("trace-probe") && EncounterProfile.Selected.Family == EncounterProfile.NightmareGrimm) && hubPresence?.Ready == true) playerDebugMenu ??= new PlayerDebugMenu(RequestDebugTravel, CanDebugTravel, SetDetailedLogging, () => detailedCapture?.Active == true, () => detailedCapture?.Status ?? "Unavailable", Log);
        if (SessionConfiguration.Current.ManualTools) playerDebugMenu?.Update();
        try { UpdatePlayerToolsFixture(); }
        catch (Exception error) { FailSession("HK_TRAVEL_FIXTURE_FAILED", error); return; }
        try { UpdatePantheonFixture(); }
        catch (Exception error) { FailSession("HK_PANTHEON_FIXTURE_FAILED", error); return; }
        try { TryBeginDebugTravel(); }
        catch (Exception error) { FailSession("HK_DEBUG_TRAVEL_FAILED", error); return; }
#endif
        framePerformance.Sample(Log);
        if (retryProbe && FinishRetryFixture()) return;
        try {
            string? failure = encounterLifetime.Check(clock.Elapsed.TotalSeconds, false, 0, hitCompleted, LifecyclePaused);
            if (failure != null) throw new InvalidOperationException(failure);
#if HOLLOW_KNIGHT
            using (HostFrameProbe.Measure(17)) UpdateLifecycle();
#else
            UpdateLifecycle();
#endif
#if HOLLOW_KNIGHT
            string boundary = SessionConfiguration.Current.FailureBoundary;
            if (boundary.Length != 0 && encounterStatus?.Phase.ToString() == boundary) {
                FailSession("DIAGNOSTIC_DISCONNECT_" + boundary, null); return;
            }
#endif
        }
        catch (Exception error) { FailSession("LIFECYCLE_FAILED", error); return; }
#if HOLLOW_KNIGHT
        if (!hubCombat && hitProbe && !hitCompleted && godhomeProbe?.EncounterWon == true)
        {
            hitCompleted = true; endpoint?.PublishControls(Time.frameCount, default);
            heroProxy?.Update(null); visualPresenter?.Update(null, null);
            heroProxy?.Dispose(); heroProxy = null; proxyProbe = false;
            visualPresenter?.Dispose(); arrowPresenter?.Dispose(); arrowPresenter = null; visualPresenter = null; visualProbe = false;
            yiHud?.Dispose(); yiEffectsHud?.Dispose();yiEffectsHud=null; yiHud = null;
            contactBridge = false; contactEndpoint?.Dispose();
            if (encounterStatus != null) SetEncounter(new EncounterStatus(encounterStatus.Id, encounterStatus.GeometryFrame, EncounterPhase.Victory));
            Log("HK_HIT_ENCOUNTER_COMPLETE nativeBossDead=True proxyReleased=True");
        }
        using (HostFrameProbe.Measure(13)) godhomeProbe?.Update();
        using (var actorImage = visualEndpoint?.AcquireFrame())
        {
        var actorFrame = actorImage?.Frame;
        var actorPeer = endpoint?.Peer;
        var actorControl = endpoint?.IsPeerFresh == true ? actorPeer?.Pose : null;
        var actorPose = visualProbe ? visualMotion.Sample(actorFrame, actorControl, actorPeer?.Frame ?? -1) : actorControl;
        if (hubPresence != null)
        {
            try { using (HostFrameProbe.Measure(18)) hubPresence.Update(actorPose); }
            catch (Exception error) { FailSession("HK_HUB_FAILED", error); return; }
        }
        if (poseProbe && endpoint == null && File.Exists(YiGodhome.Geometry.Codec.RuntimePath(Path.Combine(Path.GetDirectoryName(eventLog)!, "godhome-geometry.bin"))))
            endpoint = new Endpoint(sessionId, Role.HollowKnight, inputProbe ? 7u : 3u);
        var hostSettings = GameManager.instance?.gameSettings;
        endpoint?.PublishFrame(Time.frameCount, encounter: encounterStatus, audio: inputProbe && hostSettings != null
            ? new HostAudioState(HostAudioSource.Gain(hostSettings.masterVolume), HostAudioSource.Gain(hostSettings.soundVolume), GameManager.instance?.gameState.ToString() == "PAUSED") : null, detailCapture: detailedCapture?.ActiveId ?? Guid.Empty);
        if (proxyProbe && endpoint != null)
        {
            try
            {
                if (!hubProbe) { if (heroProxy == null) heroProxy = new HeroProxy(Log, arenaProbe); heroProxy.Update(actorPose); }
                if ((hubProbe ? hubPresence?.Ready == true : arenaProbe && heroProxy?.Ready == true) && yiHud == null) yiHud = new YiVitalsHud(Log);
            }
            catch (Exception error) { FailSession("HK_PROXY_FAILED", error); return; }
        }
        if (visualProbe)
        {
            using var presentationCost = HostFrameProbe.Measure(19);
            try
            {
                if (visualPresenter == null) visualPresenter = new VisualPresenter(Log, victoryHold: hubCombat, actor: true);
                visualPresenter.BindBodies(contactSource?.PresentationBodies);
                var visiblePose = hubProbe ? hubPresence?.Visible(actorPose) : UnitySceneManager.GetActiveScene().name == (arenaProbe ? YiGodhome.Bridge.EncounterProfile.Selected.Scene : "GG_Atrium") ? actorPose : null;
                if (!hubProbe && heroProxy?.Ready == true && heroProxy.AppliedPose?.GeometryFrame == visiblePose?.GeometryFrame) visiblePose = heroProxy.AppliedPose;
                if (arrowEndpoint != null) { arrowPresenter ??= new VisualPresenter(message => Log(message.Replace("VISUAL", "ARROW_VISUAL")), worldEffects: true); using var arrowImage = arrowEndpoint.AcquireFrame(); arrowPresenter.Update(arrowImage.Frame, visiblePose); }
                if(contactSource?.HidePlayer==true)visualPresenter.HideForNativeEnd();
                if (visualEndpoint != null) visualPresenter.Update(actorFrame, visiblePose);
                else visualPresenter.Update(null, visiblePose);
                if (visiblePose != null && actorFrame?.GeometryFrame == visiblePose.GeometryFrame && visualPresenter.ReadyForReveal
                    && (!RoomEntryPresentation.Returning || ActorPixels.Visible(actorFrame.Rgba))) {
                    if (RoomEntryPresentation.Returning) Log("HK_RETURN_IMAGE_READY geometry=" + actorFrame.GeometryFrame + " nativeFrame=" + actorFrame.NativeFrame + " sequence=" + actorFrame.Sequence + " fade=" + actorFrame.ScreenFade + " renderer=True opaquePixels=True");
                    if (BossSceneController.Instance != null && arenaImageGeometry != actorFrame.GeometryFrame && ActorPixels.Visible(actorFrame.Rgba)) {
                        arenaImageGeometry = actorFrame.GeometryFrame;
                        arenaFadeTraceUntil = Time.realtimeSinceStartup + 2; arenaFadeTraceNext = 0;
                        Log("HK_ARENA_IMAGE_READY geometry=" + actorFrame.GeometryFrame + " nativeFrame=" + actorFrame.NativeFrame + " sequence=" + actorFrame.Sequence
                            + " opaquePixels=True entered=" + (GameManager.instance?.HasFinishedEnteringScene == true) + " transitioned=" + BossSceneController.Instance.HasTransitionedIn);
                    }
                    hubPresence?.RevealEntry(visiblePose.GeometryFrame);
                }
                if (SessionConfiguration.Current.RunFixtures && BossSceneController.Instance != null && arenaImageGeometry == visiblePose?.GeometryFrame
                    && Time.realtimeSinceStartup <= arenaFadeTraceUntil && Time.realtimeSinceStartup >= arenaFadeTraceNext)
                {
                    arenaFadeTraceNext = Time.realtimeSinceStartup + .1f;
                    var nativeFade = GameCameras.instance?.cameraFadeFSM;
                    var fadeSprite = nativeFade?.GetComponent<tk2dBaseSprite>();
                    var fadeRenderer = nativeFade?.GetComponent<UnityEngine.SpriteRenderer>();
                    Log("HK_ARENA_FADE_SAMPLE geometry=" + arenaImageGeometry + " nativeFrame=" + Time.frameCount
                        + " state=" + (nativeFade?.ActiveStateName ?? "none")
                        + " alpha=" + (fadeSprite != null ? fadeSprite.color.a.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                            : fadeRenderer != null ? fadeRenderer.color.a.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : "unknown")
                        + " imageReady=" + visualPresenter.ReadyForReveal);
                }
                if (summonEndpoint != null) { summonPresenter ??= new NativeWorldHudPresenter(Log); using var image = summonEndpoint.AcquireFrame(); summonPresenter.Update(image.Frame, visiblePose); }
                if (wallEndpoint != null && hubPresence != null) { using var wallImage = wallEndpoint.AcquireFrame(); hubPresence.UpdateWallArt(wallImage.Frame, visiblePose); }
                if (restEndpoint != null) { restPresenter ??= new VisualPresenter(message => Log(message.Replace("VISUAL", "REST")),behindActor:true); using var restImage = restEndpoint.AcquireFrame(); restPresenter.Update(restImage.Frame, visiblePose); }
                if (bossHudEndpoint != null) { bossHudPresenter ??= new BossHudPresenter(); using var bossImage = bossHudEndpoint.AcquireFrame(); bossHudPresenter.Update(bossImage.Frame, visiblePose); }
                if (hudEffectsEndpoint != null && yiHud!=null) { yiEffectsHud??=new YiVitalsHud(Log,effectsOnly:true);using var fx=hudEffectsEndpoint.AcquireFrame();yiEffectsHud.Update(fx.Frame,visiblePose); }
                if (hudEndpoint != null) { using var image = hudEndpoint.AcquireFrame(); yiHud?.Update(image.Frame, visiblePose); }
                else yiHud?.Update(null, visiblePose);
                if(yiHud!=null)yiEffectsHud?.PlaceBehind(yiHud);
                if (menuEndpoint != null) { menuPresenter ??= new NativeMenuPresenter(); using var image = menuEndpoint.AcquireFrame(); menuPresenter.Update(image.Frame, visiblePose); }
                if (promptEndpoint != null) { promptPresenter ??= new InteractionPromptPresenter(Log); using var glyph = promptEndpoint.AcquireFrame(); promptPresenter.Update(glyph.Frame, visiblePose, menuPresenter?.Open == true, hubPresence?.RestPrompt(visiblePose)); }
                if (hubPresence != null) hubPresence.MenuOpen = menuPresenter?.Open == true;
                hubPresence?.HideKnight();
                if (paletteEndpoint != null)
                {
                    attackPresentation ??= new HostAttackPresentation(Log);
                    using var colors = paletteEndpoint.AcquireFrame();
                    attackPresentation.Update(contactSource?.Latest?.CrimsonSequence > 0 ? null : contactSource?.Boss, contactSource?.Latest, colors.Frame);
                }
            }
            catch (Exception error) { FailSession("HK_VISUAL_FAILED", error); return; }
        }
        }
        if (inputProbe && endpoint != null)
        {
            using var controlCost = HostFrameProbe.Measure(20);
            bool presentationFixture;
            try { presentationFixture = PresentationFixture(); }
            catch (Exception error) { FailSession("HK_PRESENTATION_FIXTURE_FAILED", error); return; }
            if (controlSource == null && !hitCompleted) controlSource = new ControlSource(Log, YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures, arenaProbe, contactBridge, hubProbe);
            if (controlSource != null) controlSource.HubReentryFixture = !diagnosticDrain && !SessionConfiguration.Current.Has("invulnerable-probe") && hubCombat && hubCombatEntries > 0 && hubCombatEntries < ScriptedHubEntryLimit && encounterStatus?.Phase == EncounterPhase.Returned && (!SessionConfiguration.Current.Has("travel-probe") || travelFixtureRetryReleased) && !SessionConfiguration.Current.Has("pantheon-probe");
            if (controlSource != null) controlSource.MenuOpen = menuPresenter?.Open == true || playerDebugMenu?.Open == true;
            bool victoryMotion = hubCombat && (encounterStatus?.Phase == EncounterPhase.Victory || encounterStatus?.Phase == EncounterPhase.Advancing) && hubPresence?.OwnsScene(EncounterProfile.Selected.Scene) == true;
            bool controlsReady = debugTravel == null && (!hitCompleted || victoryMotion) && endpoint.FreshPose != null && (hubProbe ? (victoryMotion ? hubPresence?.Visible(endpoint.FreshPose) != null : hubPresence?.Current(endpoint.FreshPose) != null) : !proxyProbe || heroProxy?.Ready == true)
                && (!visualProbe || visualEndpoint?.FreshFrame?.GeometryFrame == endpoint.FreshPose.GeometryFrame);
            if (HollowSols.Foundation.NativeSaveContext.Enabled && HollowSolsMenuPlugin.Instance?.Lifecycle?.State != HollowSols.Foundation.BridgeState.Active) controlsReady = false;
            if(HollowSols.Foundation.NativeSaveContext.Enabled)
                BridgeLoadingPresentation.CaptureEntryGesture(endpoint.FreshPose?.Vitals?.EntryActive==true,visualPresenter?.ReadyForReveal==true && visualEndpoint?.FreshFrame?.GeometryFrame==endpoint.FreshPose?.GeometryFrame);
            if(HollowSols.Foundation.NativeSaveContext.Enabled) {
                // Pixels/initialization may be ready while the native entrance
                // gesture still owns control. Let players see that animation;
                // HubPresence and the encounter gate retain the input hold.
                bool entryVisible=endpoint.FreshPose?.Vitals?.EntryActive==true && endpoint.FreshPose.Vitals.Dead==false
                    && HollowSolsMenuPlugin.Instance?.Lifecycle?.State==HollowSols.Foundation.BridgeState.Active
                    && GameManager.instance?.HasFinishedEnteringScene==true;
                bool loadingReleased=BridgeLoadingPresentation.PlayerReady((controlsReady || entryVisible) && !presentationFixture,
                    visualPresenter?.ReadyForReveal==true && visualEndpoint?.FreshFrame?.GeometryFrame==endpoint.FreshPose?.GeometryFrame);
                controlsReady &= loadingReleased;
            }
            try { encounterReadyGate?.Update(controlsReady && (!lifecycleProbe || encounterStatus?.Matches(endpoint.FreshEncounter) == true && endpoint.FreshEncounter?.Phase == EncounterPhase.Active)); }
            catch (Exception error) { FailSession("HK_ENCOUNTER_GATE_FAILED", error); return; }
            controlsReady &= encounterReadyGate?.Waiting != true && !presentationFixture;
            FixturePairedInputReady=controlsReady;
            Controls sampled;
            bool peerPauseMenu=GameManager.instance?.IsGamePaused()==true && HollowSolsNativePause.Active;
            if(peerPauseMenu && endpoint.FreshPose!=null)controlsReady=true;
            controlSource?.ObserveAvailability(controlsReady && playerDebugMenu?.Open != true);
            try { sampled = controlsReady && controlSource != null && playerDebugMenu?.Open != true ? controlSource.Sample(endpoint) : default; }
            catch (Exception error) { FailSession("HK_INPUT_FAILED",error); return; }
            if (pairedFixtureControls.HasValue && controlsReady && HollowSolsMenuPlugin.Instance?.FixtureCommandsAllowed == true) sampled = pairedFixtureControls.Value;
            if(peerPauseMenu && HollowSolsNativePause.ForwardClose)sampled=new Controls(0,0,Buttons.Cancel|Buttons.NativePauseMenu);
            bool paused = GameManager.instance != null && GameManager.instance.gameState.ToString() == "PAUSED";
            var metadata = (paused ? Buttons.HostPaused : Buttons.None)
                | (HollowSolsNativePause.Requested ? Buttons.NativePauseMenu : Buttons.None)
                | (playerDebugMenu?.Collision == true ? Buttons.ParryDebug : Buttons.None)
                | (controlSource == null || controlSource.LastInputType != InControl.BindingSourceType.DeviceBindingSource ? Buttons.Keyboard : Buttons.None)
                | (hubPresence?.DoorEntering == true ? Buttons.DoorEnter : Buttons.None)
                | (RoomEntryPresentation.StatueDeparting ? Buttons.StatueEnter : Buttons.None)
                | (hubPresence?.EntryRevealed == true ? Buttons.EntryRevealed : Buttons.None);
            if(HollowSolsMenuPlugin.Instance?.FixtureControllerControls==true) {
                metadata&=~Buttons.Keyboard;sampled=new Controls(sampled.X,sampled.Y,sampled.Buttons&~Buttons.Keyboard);
            }
            if (presentationFixture && Time.realtimeSinceStartup - presentationFixtureAt >= 1.5f
                && Time.realtimeSinceStartup - presentationFixtureAt < 4) metadata &= ~Buttons.Keyboard;
            endpoint.PublishControls(Time.frameCount, new Controls(paused && !peerPauseMenu ? 0 : sampled.X, paused && !peerPauseMenu ? 0 : sampled.Y,
                (paused && !peerPauseMenu ? Buttons.None : sampled.Buttons) | metadata), renewNeutralEntry: HollowSols.Foundation.NativeSaveContext.Enabled
                    && endpoint.FreshPose?.Vitals?.EntryActive==true && (metadata&Buttons.EntryRevealed)!=0
                    && sampled.X==0 && sampled.Y==0 && (sampled.Buttons&~Buttons.Keyboard)==0
                    && (metadata&~(Buttons.EntryRevealed|Buttons.HostPaused|Buttons.Keyboard|Buttons.ParryDebug))==0,
                renewMenuRequest: peerPauseMenu && HollowSolsNativePause.Requested && sampled.X==0 && sampled.Y==0
                    && (sampled.Buttons&~(Buttons.NativePauseMenu|Buttons.HostPaused|Buttons.Keyboard|Buttons.ParryDebug))==0);
        }
        else if (encounterReadyGate?.Waiting == true)
        {
            try { encounterReadyGate.Update(false); }
            catch (Exception error) { FailSession("HK_ENCOUNTER_GATE_FAILED", error); return; }
        }
#else
        if (inputProbe) { ambience ??= new NativeAmbienceIsolation(Log); ambience.Update(); roomIsolation ??= new NativeRoomIsolation(Log); }
        try { if (inputProbe) { audioMirror ??= new NativeAudioMirror(Log); audioMirror.Update(endpoint?.IsPeerFresh == true ? endpoint.Peer?.Audio : null); } }
        catch (Exception error) { FailSession("NS_AUDIO_FAILED", error); return; }
        using (new NativeWorkCost.Scope(NativeWorkCost.Memory, "memory_update", NativeCostLog)) memoryProbe?.Update();
        try {
            if (SessionConfiguration.Current.Has("content-audit") && memoryProbe?.ActivePlayer is Player canvasPlayer && canvasPlayer.actions.Enabled)
                (contentAudit ??= new NativeContentAudit(canvasPlayer, Log)).Update();
        } catch (Exception error) { NativeContentAudit.Abort(); FailSession("NS_CONTENT_FAILED", error); return; }
        try { if (hubCombat && memoryProbe?.ActivePlayer != null && memoryProbe.Geometry != null) { restPoint ??= new NativeRestPoint(Log); restPoint.Update(memoryProbe.Geometry); } }
        catch (Exception error) { FailSession("NS_REST_FAILED", error); return; }
        try { if (memoryProbe?.ActivePlayer is Player roomPlayer && memoryProbe.ContactOwner is MonsterBase roomOwner) roomIsolation?.Survey(roomPlayer, roomOwner); }
        catch (Exception error) { FailSession("NS_ROOM_FAILED", error); return; }
        // Start the connection deadline when native loading and terrain checks finish.
        // Native arena entry can take longer than the transport connection budget.
        if (poseProbe && endpoint == null && memoryProbe?.SamplePose() != null)
            endpoint = new Endpoint(sessionId, Role.NineSols, inputProbe ? 7u : 3u);
        var publishedPose = poseProbe ? memoryProbe?.SamplePose() : null;
        if (hubCombat && encounterStatus?.Phase == EncounterPhase.Restoring && publishedPose?.GeometryFrame != encounterStatus.GeometryFrame)
            publishedPose = null; // Terrain import runs at a lower cadence; never pair a new encounter with the previous room.
        endpoint?.PublishFrame(Time.frameCount, publishedPose, encounterStatus, detailCapture: detailedCapture?.ActiveId ?? Guid.Empty);
        if (inputProbe && endpoint != null && memoryProbe?.ActivePlayer is Player player)
        {
            try
            {
                if (inputBridge == null && memoryProbe.SamplePose() != null) inputBridge = new NativeInputBridge(player, endpoint,
                    () => (!HollowSols.Foundation.NativeSaveContext.Enabled || HollowSolsMenuPlugin.Instance?.Lifecycle?.State == HollowSols.Foundation.BridgeState.Active)
                        && memoryProbe.SamplePose() != null && memoryProbe.ContentReady && (!hubCombat || (!hitCompleted || pendingSequenceAdvance || pendingVictoryReset && endpoint.FreshEncounter?.Phase == EncounterPhase.Victory) && visualCapture != null), Log);
                inputBridge?.Update();
            }
            catch (Exception error) { FailSession("NS_INPUT_FAILED", error); return; }
        }
#endif
#if HOLLOW_KNIGHT
        var contactPose = endpoint?.FreshPose;
#else
        var contactPose = memoryProbe?.SamplePose();
#endif
        if (contactBridge && contactPose != null && (!hubCombat || encounterStatus?.GeometryFrame == contactPose.GeometryFrame)
#if !HOLLOW_KNIGHT
            && memoryProbe?.ContentReady == true
#endif
            )
        {
            try
            {
                if (contactEndpoint == null) contactEndpoint = new ContactEndpoint(sessionId, Game == "HollowKnight" ? Role.HollowKnight : Role.NineSols, channelGeneration);
                if (hitProbe && hitEndpoint == null) hitEndpoint = new HitEndpoint(sessionId, Game == "HollowKnight" ? Role.HollowKnight : Role.NineSols, channelGeneration);
#if HOLLOW_KNIGHT
                if (contactSource == null) contactSource = new HostContactSource(sessionId, channelGeneration, Log);
                long contactCostStart = System.Diagnostics.Stopwatch.GetTimestamp();
                using (HostFrameProbe.Measure(21)) contactEndpoint.Publish(contactSource.Capture(contactPose.GeometryFrame, contactPose));
                if (EncounterProfile.Selected.Family == EncounterProfile.NightmareGrimm) HostWorkCost.Contacts.Record(contactCostStart, contactSource.Latest?.Volumes.Length ?? 0, "contacts", Log);
                if (contactSource.Latest?.CrimsonSequence > 0) {
                    rageMask ??= new LocalBossAura(Log);
                    // Pose contours and animated ribbons belong to the current HK renderer.
                    using (HostFrameProbe.Measure(22)) rageMask.Update(contactSource.RageBodies, contactSource.Latest, contactSource.RageParts, contactSource.AuraTraits);
                } else rageMask?.Update(null, null);
                if (lifecycleProbe && (encounterStatus == null || encounterStatus.Phase == EncounterPhase.Preparing)) SetEncounter(new EncounterStatus(contactSource.Latest!.Encounter, contactPose.GeometryFrame, EncounterPhase.Active));
                if (hitProbe && hostHits == null) { hostHits = new HostHitReceiver(contactSource, Log); if (controlSource != null) { controlSource.HasResolvedHit = hostHits.HasApplied; controlSource.HasSummonHit = hostHits.HasSummonHit; controlSource.HasSummons = () => contactSource?.Latest?.Summons.Length > 0; controlSource.DiagnosticTargets = () => contactSource?.Latest; controlSource.DiagnosticCounterPrecharge = () => contactSource?.ScriptedCounterPrecharge==true; controlSource.DiagnosticSkipCounter = () => contactSource?.ScriptedSkipCounter==true; controlSource.DiagnosticEligible = target => contactSource?.DiagnosticEligible(target) ?? false; controlSource.DiagnosticProgress = () => hostHits?.AppliedDamageCount ?? 0; } }
                if(hostHits!=null) contactSource.ObservedMelee(contactPose,hostHits);
#else
                if (hostContact == null && memoryProbe?.ContactOwner is MonsterBase owner && memoryProbe.Geometry != null)
                {
                    memoryProbe.PrepareContactLease();
                    hostContact = new NativeHostContact(memoryProbe.ActivePlayer!, owner, memoryProbe.Geometry, Log);
                }
                if (targetProbe && nativeTarget == null && memoryProbe?.ContactOwner is MonsterBase target && memoryProbe.Geometry != null)
                    nativeTarget = new NativeTargetProbe(target, memoryProbe.Geometry, Log, hitProbe);
#endif
            }
            catch (Exception error) { FailSession("CONTACT_FAILED", error); return; }
        }
#if !HOLLOW_KNIGHT
        try { hostContact?.Update(hitCompleted ? null : contactEndpoint?.FreshFrame); using (new NativeWorkCost.Scope(NativeWorkCost.Target, "target_update", NativeCostLog)) nativeTarget?.Update(hitCompleted ? null : contactEndpoint?.FreshFrame); }
        catch (Exception error) { FailSession("CONTACT_FAILED", error); return; }
        if (lifecycleProbe && (encounterStatus == null || encounterStatus.Phase == EncounterPhase.Restoring) && contactEndpoint?.FreshFrame is ContactFrame initialContact)
            SetEncounter(new EncounterStatus(initialContact.Encounter, initialContact.GeometryFrame, EncounterPhase.Active));
#endif
        if (hitEndpoint != null)
        {
            try
            {
#if HOLLOW_KNIGHT
                if (hostHits != null) hitEndpoint.ProcessPending(hostHits.Process);
#else
                while (hostContact != null && hostContact.TryGetCounter(out var counter))
                {
                    if (hitCompleted) continue;
                    long counterId = hitEndpoint.Submit(counter); pendingHitKinds.Add(counterId, counter.Kind);
                    Log("NS_COUNTER_SUBMITTED sequence=" + counterId + " attack=" + counter.AttackId);
                }
                while (nativeTarget != null && nativeTarget.TryGetOutcome(out var outcome))
                {
                    if (hitCompleted) continue;
                    long id = hitEndpoint.Submit(outcome);
                    nativeTarget.Submitted(id, outcome);
                    pendingHitKinds.Add(id, outcome.Kind);
                    Log("NS_HIT_OUTCOME sequence=" + id + " frame=" + outcome.NativeFrame + " entity=" + outcome.Entity + " kind=" + outcome.Kind + " damage=" + outcome.Damage + " encounter=" + outcome.Encounter + " activation=" + outcome.TargetActivation + " attack=" + outcome.AttackId);
                }
                while (hitEndpoint.TryGetAck(out var ack))
                {
                    Log("NS_HOST_HIT_RESULT sequence=" + ack.Sequence + " result=" + ack.Disposition + " health=" + ack.Health);
                    if (!pendingHitKinds.TryGetValue(ack.Sequence, out var kind)) throw new InvalidOperationException("Unknown native hit acknowledgement.");
                    pendingHitKinds.Remove(ack.Sequence);
                    (nativeTarget ?? retiredNativeTarget)?.Acknowledged(ack);
                    if (kind == HitKind.Talisman && ack.Disposition == HitDisposition.Applied && hostContact != null) hostContact.OutgoingValidated = true;
                    // Receiver HP is not encounter completion (armor, phases, summons).
                }
#endif
#if HOLLOW_KNIGHT
                const int pendingCount = 0;
#else
                int pendingCount = pendingHitKinds.Count;
#endif
                string? hitFailure = encounterLifetime.Check(clock.Elapsed.TotalSeconds, hitEndpoint.Stopped, pendingCount, hitCompleted, LifecyclePaused);
                if (hitFailure != null) throw new InvalidOperationException(hitFailure);
            }
            catch (Exception error) { FailSession("HIT_FAILED", error); return; }
            while (hitEndpoint.TryGetEvent(out var hitEvent)) Log(hitEvent);
        }
        if (contactEndpoint != null) {
            while (contactEndpoint.TryGetEvent(out var contactEvent)) Log(contactEvent);
        }
        if (encounterLifetime.CheckContact(clock.Elapsed.TotalSeconds, contactBridge && !hitCompleted && contactEndpoint?.Stopped == true) != null)
        { FailSession("CONTACT_CHANNEL_STOPPED", null); return; }
        endpoint?.CheckHealth();
        if (visualProbe && (visualEndpoint?.Stopped == true || arrowEndpoint?.Stopped == true)) { FailSession("VISUAL_CHANNEL_STOPPED", null); return; }
        if (endpoint?.State == "disconnected") { FailSession("PEER_DISCONNECTED", null); return; }
#if HOLLOW_KNIGHT
        if (poseProbe && endpoint?.FreshPose is Bridge.Pose receivedPose)
        {
            poseSamples++; poseMinY = Math.Min(poseMinY, receivedPose.Y); poseMaxY = Math.Max(poseMaxY, receivedPose.Y);
        }
#endif
        if (endpoint != null) while (endpoint.TryGetEvent(out var message)) Log(message);
        if (visualEndpoint != null) while (visualEndpoint.TryGetEvent(out var visualMessage)) Log(visualMessage);
        if (arrowEndpoint != null) while (arrowEndpoint.TryGetEvent(out var arrowMessage)) Log(arrowMessage);
        if (hudEndpoint != null) while (hudEndpoint.TryGetEvent(out var hudMessage)) Log(hudMessage);
        if (hudEffectsEndpoint != null) while (hudEffectsEndpoint.TryGetEvent(out var fxMessage)) Log(fxMessage);
        if (summonEndpoint != null) while (summonEndpoint.TryGetEvent(out var message)) Log(message);
        if (wallEndpoint != null) while (wallEndpoint.TryGetEvent(out var wallMessage)) Log(wallMessage);
        if (restEndpoint != null) while (restEndpoint.TryGetEvent(out var restMessage)) Log(restMessage);
        if (bossHudEndpoint != null) while (bossHudEndpoint.TryGetEvent(out var bossMessage)) Log(bossMessage);
        if (promptEndpoint != null) while (promptEndpoint.TryGetEvent(out var promptMessage)) Log(promptMessage);
        if (paletteEndpoint != null) while (paletteEndpoint.TryGetEvent(out var paletteMessage)) Log(paletteMessage);
        if (menuEndpoint != null) while (menuEndpoint.TryGetEvent(out var menuMessage)) Log(menuMessage);
        try {
#if HOLLOW_KNIGHT
            using var detailCost = HostFrameProbe.Measure(24);
#endif
            UpdateDetailedLogging();
        }
        catch (Exception error) { lock(logLock) detailedCapture?.Stop("snapshot-failure"); Log("DETAIL_FAILED error=" + error.GetType().Name); }
        var seconds = clock.Elapsed.TotalSeconds;
#if HOLLOW_KNIGHT
        if(!diagnosticDrain && SessionConfiguration.Current.RunFixtures && SessionConfiguration.Current.Has("extended-diagnostic")
            && hubCombat && !SessionConfiguration.Current.Has("pantheon-probe") && smokeSeconds>=180 && seconds>=smokeSeconds-30) {
            diagnosticDrain=true;
            Log("HK_DIAGNOSTIC_DRAIN_BEGIN phase="+encounterStatus?.Phase+" nativeReturn=True maxSeconds=30");
        }
        if(diagnosticDrain && encounterStatus?.Phase==EncounterPhase.Active && debugTravel==null && CanDebugTravel()
            && contactSource?.AwaitingNativeBossCompletion!=true) RequestDebugTravel("GG_Workshop");
#endif
        if (seconds >= nextLog)
        {
            nextLog = seconds + 5;
            Log("BASELINE_TICK game=" + Game + " frame=" + Time.frameCount + " elapsedMs=" + clock.ElapsedMilliseconds + " scene=" + UnitySceneManager.GetActiveScene().name);
            var peer = endpoint?.Peer;
            if (peer != null) Log("IPC_SAMPLE peerFrame=" + peer.Frame + " sequence=" + peer.Sequence
                + " rttMs=" + peer.RoundTripMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
                + " senderSampleAgeMs=" + peer.SenderSampleAgeMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
                + " receiveAgeMs=" + ((Stopwatch.GetTimestamp() - peer.ReceivedTick) * 1000.0 / Stopwatch.Frequency).ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
        }
        if (!quitting && smokeSeconds > 0 && seconds >= smokeSeconds)
        {
#if HOLLOW_KNIGHT
            if (lifecycleProbe && encounterStatus != null && seconds < smokeSeconds + 15
                && (encounterStatus.Phase == EncounterPhase.Victory || encounterStatus.Phase == EncounterPhase.Defeated || encounterStatus.Phase == EncounterPhase.Restoring)) {
                if (!returnGrace) { returnGrace = true; Log("SMOKE_RETURN_GRACE maxExtraSeconds=15"); }
                return;
            }
#endif
            quitting = true;
#if HOLLOW_KNIGHT
            if(diagnosticDrain) Log("HK_DIAGNOSTIC_DRAIN_EXIT phase="+encounterStatus?.Phase+" peerPhase="+endpoint?.FreshEncounter?.Phase+" scene="+UnitySceneManager.GetActiveScene().name);
#endif
            Log("SMOKE_EXIT_REQUEST game=" + Game + " frame=" + Time.frameCount);
            Application.Quit();
        }
    }

    private void SetEncounter(EncounterStatus status)
    {
        encounterLifetime.Observe(status, clock.Elapsed.TotalSeconds);
        encounterStatus = status;
        Log("ENCOUNTER_STATE role=" + Game + " encounter=" + status.Id + " geometryFrame=" + status.GeometryFrame + " phase=" + status.Phase);
    }

    private void FailSession(string reason, Exception? error)
    {
        if (quitting) return;
        quitting = true;
        Log("SESSION_TERMINAL reason=" + reason + " session=" + sessionId + " encounter=" + encounterStatus?.Id
            + " phase=" + encounterStatus?.Phase + " frame=" + Time.frameCount + " elapsedMs=" + clock.ElapsedMilliseconds + " replay=False " + error);
        Log("SMOKE_EXIT_REQUEST game=" + Game + " reason=" + reason);
        // A hung Unity thread cannot quit itself. Its protected launcher observes
        // this exact-session request from the healthy peer and bounds shutdown.
        try { System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!, "session-stop.request"), sessionId.ToString()); }
        catch (Exception stopError) { Log("SESSION_STOP_SIGNAL_FAILED " + stopError.Message); }
        encounterLifetime.Cancel();
        if (HollowSols.Foundation.NativeSaveContext.Enabled && HollowSolsMenuPlugin.Instance != null)
        { endpoint?.Stop(reason); ReleaseCombatChannels(); HollowSolsMenuPlugin.Instance.GameplayFailed(reason, error); return; }
        try { endpoint?.PublishControls(Time.frameCount, default); endpoint?.Stop(reason); ReleaseCombatChannels(); }
        finally { Application.Quit(); }
    }

    private void UpdateLifecycle()
    {
        if (hubCombat) { UpdateHubCombat(); return; }
        if (!lifecycleProbe || encounterStatus == null) return;
        var current = encounterStatus; var peer = endpoint?.FreshEncounter;
#if HOLLOW_KNIGHT
        if (current.Phase == EncounterPhase.Returned && current.Matches(peer) && peer!.Phase == EncounterPhase.Ready)
            encounterLifetime.CompleteReturn();
        if (current.Phase == EncounterPhase.Returned && current.Matches(peer) && peer!.Phase == EncounterPhase.Ready
            && (retryProbe && !retryStarted || !retryProbe && godhomeProbe!.GeometryFrame > current.GeometryFrame))
        {
            retryStarted = true; retryAnnounced = false;
            contactEndpoint?.Dispose(); contactEndpoint = null; hitEndpoint?.Dispose(); hitEndpoint = null;
            hostHits?.Dispose(); hostHits = null; contactSource?.Dispose(); contactSource = null;
            rageMask?.Dispose(); rageMask = null;
            controlSource?.Release(); controlSource = null;
            if (retryProbe) godhomeProbe!.RetryEncounter();
        }
        if (retryStarted && !retryAnnounced && godhomeProbe!.GeometryFrame > current.GeometryFrame)
        {
            retryAnnounced = true; channelGeneration = Guid.NewGuid();
            SetEncounter(new EncounterStatus(channelGeneration, godhomeProbe.GeometryFrame, EncounterPhase.Preparing));
            hitCompleted = false; contactBridge = proxyProbe = visualProbe = true;
        }
        if (retryAnnounced && controlSource != null) controlSource.AllowLifecycleAttack = true;
        if (current.Phase == EncounterPhase.Active && current.Matches(peer) && peer!.Phase == EncounterPhase.Defeated)
        {
            // HK owns the terminal boss verdict. A native boss death already accepted
            // on this main thread wins a same-frame exchange; Update handles victory.
            if (godhomeProbe?.EncounterWon == true) return;
            hitCompleted = true; endpoint?.PublishControls(Time.frameCount, default);
            heroProxy?.Update(null); visualPresenter?.Update(null, null);
            heroProxy?.Dispose(); heroProxy = null; proxyProbe = false;
            visualPresenter?.Dispose(); arrowPresenter?.Dispose(); arrowPresenter = null; visualPresenter = null; visualProbe = false;
            yiHud?.Dispose(); yiEffectsHud?.Dispose();yiEffectsHud=null; yiHud = null;
            contactBridge = false; contactEndpoint?.Dispose(); hitEndpoint?.Dispose();
            SetEncounter(new EncounterStatus(current.Id, current.GeometryFrame, EncounterPhase.Defeated));
            godhomeProbe!.RequestDefeat();
            Log("HK_YI_DEFEAT_ACCEPTED nativeHealthAuthority=True proxyReleased=True");
        }
        if ((encounterStatus.Phase == EncounterPhase.Defeated || encounterStatus.Phase == EncounterPhase.Victory) && godhomeProbe?.EncounterReturned == true)
            SetEncounter(new EncounterStatus(current.Id, current.GeometryFrame, EncounterPhase.Returned));
#else
        if (current.Phase == EncounterPhase.Ready && peer?.Phase == EncounterPhase.Preparing
            && peer.Id != current.Id && peer.GeometryFrame > current.GeometryFrame)
        {
            channelGeneration = peer.Id;
            inputBridge?.Dispose(); inputBridge = null; visualCapture?.Dispose(); visualCapture = null; arrowCapture?.Dispose(); arrowCapture = null; hudCapture?.Dispose(); hudCapture = null; hudEffectsCapture?.Dispose(); hudEffectsCapture=null;
            contactEndpoint?.Dispose(); contactEndpoint = null; hitEndpoint?.Dispose(); hitEndpoint = null;
            pendingHitKinds.Clear();
            memoryProbe!.PrepareRetry(peer.GeometryFrame);
            SetEncounter(new EncounterStatus(peer.Id, peer.GeometryFrame, EncounterPhase.Restoring));
            contactBridge = true; hitCompleted = false;
        }
        if (current.Phase == EncounterPhase.Active && memoryProbe?.ActivePlayer is Player player && player.health.IsDead)
        {
            SetEncounter(new EncounterStatus(current.Id, current.GeometryFrame, EncounterPhase.Defeated));
            hitCompleted = true; contactBridge = false;
            nativeTarget?.Dispose(); nativeTarget = null; hostContact?.Dispose(); hostContact = null;
            visualCapture?.Dispose(); visualCapture = null; arrowCapture?.Dispose(); arrowCapture = null; hudCapture?.Dispose(); hudCapture = null; hudEffectsCapture?.Dispose(); hudEffectsCapture=null;
            contactEndpoint?.Dispose();
            inputBridge?.Dispose(); inputBridge = null;
            memoryProbe.PrepareNativeRevival();
            Log("NS_YI_DEFEAT_PUBLISHED health=" + player.health.currentValue + " oldAdaptersReleased=True");
        }
        if (encounterStatus.Phase == EncounterPhase.Active && current.Matches(peer)
            && (peer!.Phase == EncounterPhase.Victory || peer.Phase == EncounterPhase.Returned))
        {
            SetEncounter(new EncounterStatus(current.Id, current.GeometryFrame, EncounterPhase.Restoring));
            hitCompleted = true; contactBridge = false;
            nativeTarget?.Dispose(); nativeTarget = null; hostContact?.Dispose(); hostContact = null;
            visualCapture?.Dispose(); visualCapture = null; arrowCapture?.Dispose(); arrowCapture = null; hudCapture?.Dispose(); hudCapture = null; hudEffectsCapture?.Dispose(); hudEffectsCapture=null; contactEndpoint?.Dispose();
            inputBridge?.Dispose(); inputBridge = null;
            memoryProbe!.PrepareNativeRevival(true);
        }
        if ((encounterStatus.Phase == EncounterPhase.Defeated || encounterStatus.Phase == EncounterPhase.Restoring)
            && memoryProbe?.Restored == true && current.Matches(peer)
            && (peer!.Phase == EncounterPhase.Defeated || peer.Phase == EncounterPhase.Victory || peer.Phase == EncounterPhase.Returned))
        {
            if (pendingHitKinds.Count != 0) return;
            SetEncounter(new EncounterStatus(current.Id, current.GeometryFrame, EncounterPhase.Ready));
            encounterLifetime.CompleteReturn();
        }
#endif
    }

    private bool FinishRetryFixture()
    {
        string run = Path.GetDirectoryName(eventLog)!;
        string root = Path.GetDirectoryName(Path.GetDirectoryName(run))!;
        string signal = Path.Combine(root, "HollowKnight", sessionId.ToString(), "retry-complete.signal");
#if HOLLOW_KNIGHT
        if (retryAnnounced && hostHits?.HasApplied(HitKind.Melee) == true && hostHits.HasApplied(HitKind.Bow) && !File.Exists(signal))
        {
            // Give the native result time to cross the acknowledged event pipe.
            if (retryFinishAt < 0) { retryFinishAt = Time.realtimeSinceStartup + 1; Log("HK_RETRY_HIT_VERIFIED encounter=" + encounterStatus!.Id); }
            if (Time.realtimeSinceStartup >= retryFinishAt) File.WriteAllText(signal, encounterStatus!.Id.ToString());
        }
#endif
        if (!File.Exists(signal)) return false;
        if (!quitting) { quitting = true; Log("SMOKE_EXIT_REQUEST game=" + Game + " reason=retry-fixture-complete"); Application.Quit(); }
        return true;
    }
#if HOLLOW_KNIGHT
    private float retryFinishAt = -1;
#endif

    private void Start() => Log("PLUGIN_START game=" + Game + " frame=" + Time.frameCount + " active=" + gameObject.activeInHierarchy);
    private readonly FramePerformance framePerformance = new FramePerformance();
#if HOLLOW_KNIGHT
    private HostFrameProbe? hostFrameProbe;
#endif

    private void LateUpdate()
    {
#if !HOLLOW_KNIGHT
        using var lateCost = new NativeWorkCost.Scope(NativeWorkCost.LateUpdate, "plugin_late_update", NativeCostLog);
#endif
#if HOLLOW_KNIGHT
        hubPresence?.HideKnight();
        visualPresenter?.BindBodies(contactSource?.PresentationBodies);
        visualPresenter?.RefreshOrder();
        HostPooledAttackVolumes.RefreshForeground();
        try { rageMask?.Render(); } catch (Exception error) { FailSession("LOCAL_AURA_FAILED", error); }
#else
        if (visualProbe && visualEndpoint != null && memoryProbe?.ActivePlayer is Player player && memoryProbe.SamplePose() is Bridge.Pose pose)
        {
            try
            {
                if (visualCapture == null) { visualCapture = new NativeVisualCapture(player, visualEndpoint, sessionId, Log, memoryProbe!.ContactOwner, separateArrows: true); visualCapture.DetailedCaptureId = () => detailedCapture?.ActiveId ?? Guid.Empty; visualCapture.AdditionalRenderers = () => NativeGesturePresentation.Renderers; visualCapture.WorldRenderers = () => hostContact?.WarningRenderers ?? Array.Empty<Renderer>(); visualCapture.AdditionalContentSequence = () => hostContact?.RageVisualSequence ?? 0; visualCapture.AdditionalContentFrame = () => hostContact?.RageHostFrame ?? 0; }
                using (new NativeWorkCost.Scope(NativeWorkCost.ActorCapture, "actor_capture", NativeCostLog)) visualCapture.Capture(pose);
                if (arrowEndpoint != null) { if(arrowCapture==null) { arrowCapture=new NativeVisualCapture(player, arrowEndpoint, sessionId, message => Log(message.Replace("NS_VISUAL", "NS_ARROW_VISUAL")), memoryProbe.ContactOwner, separateArrows: true, arrowLayer: true); arrowCapture.WorldRenderers=()=>hostContact?.WarningRenderers??Array.Empty<Renderer>(); } arrowCapture.Capture(pose); }
                memoryProbe.AfterVisualCapture(visualCapture.CapturedGeometry);
                if (hudEffectsEndpoint!=null) { hudEffectsCapture??=new NativeHudCapture(hudEffectsEndpoint,sessionId,Log,effectsOnly:true);hudEffectsCapture.Capture(pose); }
                if (hudEndpoint != null) { if (hudCapture == null) hudCapture = new NativeHudCapture(hudEndpoint, sessionId, Log); hudCapture.Capture(pose); }
                if (summonEndpoint != null && memoryProbe?.Geometry != null) { summonCapture ??= new NativeSummonCapture(summonEndpoint); using (new NativeWorkCost.Scope(NativeWorkCost.SummonCapture, "summon_capture", NativeCostLog)) summonCapture.Capture(pose, memoryProbe.Geometry.Map, nativeTarget?.SummonRenderers ?? Array.Empty<Renderer>()); }
                if (wallEndpoint != null && memoryProbe?.Geometry != null) {
                    if (YiGodhome.Geometry.RoomFeatures.ClimbingWalls(memoryProbe.Geometry.SourceScene).Length > 0) { if (memoryProbe.ContentReady) { wallCapture ??= new NativeWallCapture(wallEndpoint, sessionId, Log); wallCapture.Capture(pose, memoryProbe.Geometry); } }
                    else { wallCapture?.Dispose(); wallCapture = null; }
                }
                if (restEndpoint != null && restPoint != null) { restCapture ??= new NativeRestCapture(restEndpoint, sessionId, Log); using (new NativeWorkCost.Scope(NativeWorkCost.RestCapture, "rest_capture", NativeCostLog)) restCapture.Capture(pose, restPoint); }
                if (bossHudEndpoint != null && hudCapture != null) { bossHudCapture ??= new NativeBossCapture(bossHudEndpoint, sessionId, Log,hudCapture.SourceCamera); bossHudCapture.Capture(pose, nativeTarget?.BossHealthView); }
                if (promptEndpoint != null) { promptCapture ??= new NativePromptCapture(promptEndpoint, sessionId, Log); promptCapture.Capture(pose); }
                if (paletteEndpoint != null) { attackPalette ??= new NativeAttackPalette(); attackPalette.Publish(paletteEndpoint, sessionId, pose.GeometryFrame); }
                if (menuEndpoint != null) { menuCapture ??= new NativeMenuCapture(menuEndpoint, sessionId, Log,()=>detailedCapture?.ActiveId??Guid.Empty,WriteNativeDetail); using (new NativeWorkCost.Scope(NativeWorkCost.MenuCapture, "menu_capture", NativeCostLog)) menuCapture.Capture(pose); }
            }
            catch (Exception error) { FailSession("NS_VISUAL_FAILED", error); return; }
        }
#endif
    }

#if HOLLOW_KNIGHT
    private float nextPauseDimTrace;
    private static readonly System.Reflection.FieldInfo modalDimmerField = HarmonyLib.AccessTools.Field(typeof(UIManager), "modalDimmer");
#endif
    private void OnGUI()
    {
#if HOLLOW_KNIGHT
        var previousHudColor = GUI.color;
        var dimmer = UIManager.instance == null ? null : modalDimmerField.GetValue(UIManager.instance) as CanvasGroup;
        float brightness = dimmer != null && dimmer.gameObject.activeInHierarchy ? 1 - dimmer.alpha : 1;
        if (GameManager.instance?.IsGamePaused() == true && Time.realtimeSinceStartup >= nextPauseDimTrace)
        {
            nextPauseDimTrace = Time.realtimeSinceStartup + 0.25f;
            Log("HK_HUD_PAUSE_DIM brightness=" + brightness + " nativeCanvasAlpha=" + (dimmer == null ? 0 : dimmer.alpha));
        }
        GUI.color = new Color(previousHudColor.r * brightness, previousHudColor.g * brightness, previousHudColor.b * brightness, previousHudColor.a);
        yiHud?.Draw(endpoint?.FreshPose?.Vitals);
        yiEffectsHud?.Draw(endpoint?.FreshPose?.Vitals);
        bossHudPresenter?.Draw();
        GUI.color = previousHudColor;
        visualPresenter?.DrawRecovery();
        menuPresenter?.Draw();
        if (playerDebugMenu != null) {
            playerDebugMenu.ConfigureDiagnostic();
            long guiCostStart = System.Diagnostics.Stopwatch.GetTimestamp();
            playerDebugMenu.Draw(hitCompleted ? null : contactSource?.Latest, endpoint?.FreshPose);
            if (EncounterProfile.Selected.Family == EncounterProfile.NightmareGrimm) HostWorkCost.Gui.Record(guiCostStart, playerDebugMenu.LastEdges,
                "gui_" + Event.current.type + "_collision_" + playerDebugMenu.Collision, Log);
        }
        if (!visualProbe && poseProbe && endpoint?.FreshPose is Bridge.Pose pose && Camera.main != null && UnitySceneManager.GetActiveScene().name == "GG_Atrium")
        {
            var lower = Camera.main.WorldToScreenPoint(new Vector3(pose.X - pose.Width * 0.5f, pose.Y, 0));
            var upper = Camera.main.WorldToScreenPoint(new Vector3(pose.X + pose.Width * 0.5f, pose.Y + pose.Height, 0));
            if (lower.z > 0 && upper.z > 0)
            {
                GUI.Box(new Rect(lower.x, Screen.height - upper.y, upper.x - lower.x, upper.y - lower.y), "");
                GUI.Label(new Rect(lower.x, Screen.height - upper.y - 22, 150, 22), "Native Yi pose only");
            }
        }
#endif
        if (visualProbe) return;
        if (HollowSols.Foundation.NativeSaveContext.Enabled) return;
        GUI.Box(new Rect(16, 16, 580, 110), inputProbe
            ? "Yi in Godhome - routed input diagnostic; save writes disabled\n"
#if HOLLOW_KNIGHT
                + (controlSource?.Mode ?? "Waiting for native Yi...")
#else
                + "Use Hollow Knight's window. Native Yi owns movement."
#endif
                + "\nA: jump, RT: dash, LB: parry, X: attack. Left stick: move.\nD-pad Up: heal, Y: bow, RB: talisman."
                + (smokeSeconds == 0 ? "\nNo time limit. Close both games when finished." : returnGrace ? "\nFinishing encounter return (up to 15 seconds)." : "\nAutomatic diagnostic exit in " + Math.Max(0, smokeSeconds - (int)clock.Elapsed.TotalSeconds) + " seconds.")
            : memoryPreview
            ? "Yi in Godhome - native Yi initialization test\nBattle Memories in memory at slot index 3. Save I/O disabled.\n" + (geometryPreview
                ? "Automated terrain landing test; no input needed." : "Hollow Knight connection is not enabled.")
                + "\nAutomatic diagnostic exit in " + Math.Max(0, smokeSeconds - (int)clock.Elapsed.TotalSeconds) + " seconds."
            : previewing
            ? "Yi in Godhome - native Godhome inspection\nONLY Hollow Knight slot 4 loaded. Saving disabled.\n" + (humanTest ? "Controller test: stand up and visit the Hall of Gods." : "Yi gameplay is not enabled; input is disabled for this test.") + "\nAutomatic diagnostic exit in " + Math.Max(0, smokeSeconds - (int)clock.Elapsed.TotalSeconds) + " seconds."
            : inspecting
            ? "Yi in Godhome - read-only save inspection\nONLY Hollow Knight slot 4 can be read.\nSaving, deletion and gameplay entry are disabled."
            : "Yi in Godhome - protected development test\nSaves are hidden; loading and saving are disabled.\nMenu backgrounds may differ because save metadata is hidden.");
    }

    private void OnApplicationQuit()
    {
#if HOLLOW_KNIGHT
        configurationMenu?.Dispose(); configurationMenu=null;
        hostFrameProbe?.Dispose(); hostFrameProbe = null;
#endif
#if HOLLOW_KNIGHT
        encounterReadyGate?.Dispose(); encounterReadyGate = null;
        controlSource?.Release();
        heroProxy?.Dispose();
        hubPresence?.Dispose();
        godhomeProbe?.Dispose();
        visualPresenter?.Dispose(); arrowPresenter?.Dispose(); arrowPresenter = null;
        hostHits?.Dispose();
        yiHud?.Dispose(); yiEffectsHud?.Dispose();yiEffectsHud=null;
        if (poseProbe) Log("HK_POSE_RANGE samples=" + poseSamples + " minY=" + poseMinY.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
            + " maxY=" + poseMaxY.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
#else
        inputBridge?.Dispose();
        nativeTarget?.Dispose(); nativeTarget = null;
        hostContact?.Dispose(); hostContact = null;
        restPoint?.Dispose(); restPoint = null;
        contentAudit?.Dispose(); contentAudit = null;
        memoryProbe?.Dispose();
        visualCapture?.Dispose(); visualCapture = null; arrowCapture?.Dispose(); arrowCapture = null; hudCapture?.Dispose(); hudCapture = null; hudEffectsCapture?.Dispose(); hudEffectsCapture=null;
#endif
        endpoint?.Dispose();
        contactEndpoint?.Dispose();
        hitEndpoint?.Dispose();
        visualEndpoint?.Dispose(); arrowEndpoint?.Dispose(); hudEndpoint?.Dispose(); hudEffectsEndpoint?.Dispose(); menuEndpoint?.Dispose(); promptEndpoint?.Dispose(); bossHudEndpoint?.Dispose(); restEndpoint?.Dispose(); wallEndpoint?.Dispose(); paletteEndpoint?.Dispose(); summonEndpoint?.Dispose();
#if HOLLOW_KNIGHT
        rageMask?.Dispose(); rageMask = null; menuPresenter?.Dispose(); promptPresenter?.Dispose(); bossHudPresenter?.Dispose(); restPresenter?.Dispose(); summonPresenter?.Dispose(); attackPresentation?.Dispose(); attackPresentation = null;
        playerDebugMenu?.Dispose(); playerDebugMenu = null;
#else
        menuCapture?.Dispose(); menuCapture = null; promptCapture?.Dispose(); promptCapture = null; bossHudCapture?.Dispose(); bossHudCapture = null; restCapture?.Dispose(); restCapture = null; wallCapture?.Dispose(); wallCapture = null; summonCapture?.Dispose(); summonCapture = null;
#endif
    }

    private void OnDisable() => Log("PLUGIN_DISABLED game=" + Game + " frame=" + Time.frameCount);

    private void OnDestroy()
    {
        framePerformance.Dispose();
        Log("PLUGIN_DESTROYED game=" + Game + " frame=" + Time.frameCount + " elapsedMs=" + clock.ElapsedMilliseconds);
        UnitySceneManager.sceneLoaded -= SceneLoaded;
#if !HOLLOW_KNIGHT
        ambience?.Dispose(); ambience = null;
        roomIsolation?.Dispose(); roomIsolation = null;
        inputBridge?.Dispose();
        nativeTarget?.Dispose(); nativeTarget = null;
        hostContact?.Dispose(); hostContact = null;
        restPoint?.Dispose(); restPoint = null;
        memoryProbe?.Dispose();
        visualCapture?.Dispose(); visualCapture = null; arrowCapture?.Dispose(); arrowCapture = null; hudCapture?.Dispose(); hudCapture = null; hudEffectsCapture?.Dispose(); hudEffectsCapture=null;
#else
        configurationMenu?.Dispose(); configurationMenu=null;
        encounterReadyGate?.Dispose(); encounterReadyGate = null;
        heroProxy?.Dispose();
        hubPresence?.Dispose();
        godhomeProbe?.Dispose();
        visualPresenter?.Dispose(); arrowPresenter?.Dispose(); arrowPresenter = null;
        hostHits?.Dispose();
        yiHud?.Dispose(); yiEffectsHud?.Dispose();yiEffectsHud=null;
#endif
        endpoint?.Dispose();
        contactEndpoint?.Dispose();
        hitEndpoint?.Dispose();
        visualEndpoint?.Dispose(); arrowEndpoint?.Dispose(); hudEndpoint?.Dispose(); hudEffectsEndpoint?.Dispose(); menuEndpoint?.Dispose(); promptEndpoint?.Dispose(); bossHudEndpoint?.Dispose(); restEndpoint?.Dispose(); wallEndpoint?.Dispose(); paletteEndpoint?.Dispose(); summonEndpoint?.Dispose();
#if HOLLOW_KNIGHT
        rageMask?.Dispose(); rageMask = null; menuPresenter?.Dispose(); promptPresenter?.Dispose(); bossHudPresenter?.Dispose(); restPresenter?.Dispose(); summonPresenter?.Dispose(); attackPresentation?.Dispose(); attackPresentation = null;
        playerDebugMenu?.Dispose(); playerDebugMenu = null;
#else
        menuCapture?.Dispose(); menuCapture = null; promptCapture?.Dispose(); promptCapture = null; bossHudCapture?.Dispose(); bossHudCapture = null; restCapture?.Dispose(); restCapture = null; wallCapture?.Dispose(); wallCapture = null; summonCapture?.Dispose(); summonCapture = null;
#endif
        lock (logLock) { detailedCapture?.Dispose(); detailedCapture = null; eventWriter?.Dispose(); eventWriter = null; }
    }
}
