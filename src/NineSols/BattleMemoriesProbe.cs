using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

namespace YiGodhome;

internal sealed class BattleMemoriesProbe : IDisposable
{
    private readonly Action<string> log;
    private bool started;
    private bool completed;
    private bool failed;
    private float nextSample;
    private SaveManager? activeSave;
    private Player? activePlayer;
    private Vector3 lowest;
    private Vector3 highest;
    private GeometryLandingProbe? geometryProbe;
    private string? pendingGeometry;
    private float geometryWaitStarted;
    private bool pendingCombatAudit;
    private float combatAuditAfter;
    private bool pendingEnemyProbe;
    private NativeEnemyProbe? enemyProbe;
    private NativeJadeProbe? jadeProbe;
    private bool contactBridge;
    private bool restoring, restoreSceneLoaded;
    private NativeWarmScene? warmScene;
    private NativeCanvas? canvas;
    private bool serviceAudited;
    private bool disposed;
    private float colliderWaitTrace;
    private float restoreStarted;
    private readonly bool hubProbe = YiGodhome.Bridge.SessionConfiguration.Current.Has("hub-probe");
    private float nextHubGeometry;
    private byte[]? lastHubGeometry;
    private float geometryContentionAt = -1;
    private long returnAfterGeometry = -1;
    public bool Restored { get; private set; }
    public MonsterBase? ContactOwner => enemyProbe?.TransferOwner;
    public void PrepareContactLease() => (enemyProbe ?? throw new InvalidOperationException("Native contact owner unavailable.")).PrepareContactLease();
    public GeometryLandingProbe? Geometry => geometryProbe;
    private string? queuedSceneContent;
    private long queuedContentGeometry = -1;
    private long contentReadyGeometry = -1;
    public bool ContentReady => queuedSceneContent == null && (canvas == null || canvas.Content?.Ready == true);
    private void QueueSceneContent(string scene, long geometry)
    {
        Bridge.EncounterProfile.SelectScene(scene); // Sequence rooms select the same adapter on both endpoints.
        queuedSceneContent = canvas != null && (HollowSols.Foundation.NativeSaveContext.Enabled || YiGodhome.Geometry.RoomFeatures.ClimbingWalls(scene).Length > 0 || Bridge.EncounterProfile.ForScene(scene)?.Family == Bridge.EncounterProfile.NightmareGrimm || (Bridge.EncounterProfile.ForScene(scene)?.ArmorAndHead == true)) ? scene : null;
        queuedContentGeometry = geometry;
    }
    public void AfterVisualCapture(long geometry)
    {
        if (queuedContentGeometry != geometry) return;
        if (queuedSceneContent != null)
        {
            string scene = queuedSceneContent; queuedSceneContent = null;
            log("NS_CONTENT_AFTER_IMAGE scene=" + scene + " geometry=" + geometry + " nativeFrame=" + Time.frameCount);
            PrepareSceneContent(scene);
        }
        if (ContentReady && contentReadyGeometry != geometry)
        {
            contentReadyGeometry = geometry;
            log("NS_CONTENT_GEOMETRY_READY geometry=" + geometry + " nativeFrame=" + Time.frameCount);
        }
    }
    private bool PrepareSceneContent(string scene)
    {
        if (canvas == null) return true;
        if(HollowSols.Foundation.NativeSaveContext.Enabled) {
            canvas.Content!.Request(NativeContentKind.ArmInspection);
            canvas.Content.Request(NativeContentKind.StatueMeditation);
            canvas.UpdateContent();
        }
        if (Bridge.EncounterProfile.ForScene(scene)?.Family == Bridge.EncounterProfile.NightmareGrimm)
            return canvas.UpdateContent(NativeContentKind.FlameBurn);
        if (YiGodhome.Geometry.RoomFeatures.ClimbingWalls(scene).Length > 0) return canvas.UpdateContent(NativeContentKind.HallClimbTrim);
        if (Bridge.EncounterProfile.ForScene(scene)?.ArmorAndHead != true) return true;
        canvas.UpdateContent( NativeContentKind.JiequanWarning );
        return canvas.Content!.Ready;
    }
    private static readonly FieldInfo Slot = typeof(SaveManager).GetField("currentSlotIndex", BindingFlags.Instance | BindingFlags.NonPublic)!;

    public BattleMemoriesProbe(Action<string> log) { this.log = log; }
    public Func<Guid>? DetailedCaptureId;
    private Guid poseCaptureId;
    private bool? previousPoseAvailable;
    private int poseGateSamples;
    public Bridge.Pose? SamplePose()
    {
        if(activePlayer==null) return null;
        bool actions=activePlayer.actions.Enabled || NativeMenu.Open || NativeRestPoint.Active || NativeRoomEntry.Active || activePlayer.health.IsDead;
        bool collider=activePlayer.myCollider.enabled || NativeRestPoint.Active || activePlayer.health.IsDead;
        var pose=actions && collider && NativeRestPoint.PresentationBounds(activePlayer.myCollider).size.x>0 ? geometryProbe?.Sample() : null;
        Guid detailId=DetailedCaptureId?.Invoke()??Guid.Empty;
        if(detailId!=poseCaptureId) {poseCaptureId=detailId;previousPoseAvailable=null;poseGateSamples=0;}
        if((Bridge.SessionConfiguration.Current.RunFixtures || detailId!=Guid.Empty) && completed
            && previousPoseAvailable!=(pose!=null) && poseGateSamples<48) {
            previousPoseAvailable=pose!=null;poseGateSamples++;
            log("NS_POSE_VISIBILITY_GATE frame="+Time.frameCount+" available="+(pose!=null)+" state="+activePlayer.CurrentStateType
                +" actionsEnabled="+activePlayer.actions.Enabled+" colliderEnabled="+activePlayer.myCollider.enabled
                +" sourceEnabled="+activePlayer.PlayerSprite.enabled+" sourceActive="+activePlayer.PlayerSprite.gameObject.activeInHierarchy
                +" freeze="+activePlayer.freeze+" dead="+activePlayer.health.IsDead+" restoring="+restoring
                +" roomEntry="+NativeRoomEntry.Active+" roomReady="+NativeRoomEntry.ReadyToShow
                +" geometry="+(pose?.GeometryFrame??-1)+" detailCapture="+detailId);
        }
        return pose;
    }
    public Player? ActivePlayer => completed && !failed ? activePlayer : null;
    public void ResumeHub(long previousGeometry)
    {
        if (!hubProbe || !Restored || enemyProbe?.TransferReady != true) throw new InvalidOperationException("Hub return requires completed warm native restoration.");
        string run = Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!;
        string root = Path.GetDirectoryName(Path.GetDirectoryName(run))!;
        pendingGeometry = Path.Combine(root, "HollowKnight", Guid.Parse(Environment.GetEnvironmentVariable("YIGODHOME_SESSION")).ToString(), "godhome-geometry.bin");
        restoring = restoreSceneLoaded = Restored = false;
        returnAfterGeometry = previousGeometry;
        geometryWaitStarted = Time.realtimeSinceStartup; lastHubGeometry = null;
    }
    public void PrepareNativeRevival(bool afterVictory = false, Action? beforeReset = null)
    {
        if (restoring) return;
        if (activePlayer == null || !afterVictory && !activePlayer.health.IsDead) throw new InvalidOperationException("Revival cleanup requires actual native Yi death or acknowledged host victory.");
        restoring = true; restoreSceneLoaded = false; Restored = false;
        restoreStarted = Time.realtimeSinceStartup;
        pendingGeometry = null;
        if (afterVictory) { geometryProbe?.Dispose(); geometryProbe = null; }
        enemyProbe?.Dispose(); enemyProbe = null;
        log("NS_REVIVAL_CLEANUP nativeDead=" + activePlayer.health.IsDead + " geometryReleased=" + afterVictory + " ownerReleased=True");
        warmScene ??= new NativeWarmScene(activePlayer, log);
        warmScene.BeforeReset = () => { beforeReset?.Invoke(); geometryProbe?.Dispose(); geometryProbe = null; };
        if (afterVictory && !activePlayer.health.IsDead)
        {
            log("NS_VICTORY_RESET nativeResetWithoutLoad=True health=" + activePlayer.health.currentValue + " potions=" + activePlayer.potion.DrinkTimesLeft + " chi=" + activePlayer.chiContainer.Value);
        }
        warmScene.Arm(afterVictory && !activePlayer.health.IsDead);
    }
    public void PrepareRetry(long geometryFrame)
    {
        if (!Restored || activePlayer == null || !activePlayer.actions.Enabled || activePlayer.health.IsDead)
            throw new InvalidOperationException("Native retry requires completed revival.");
        string sessionDirectory = Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!;
        string runsRoot = Path.GetDirectoryName(Path.GetDirectoryName(sessionDirectory))!;
        string path = Path.Combine(runsRoot, "HollowKnight", Guid.Parse(Environment.GetEnvironmentVariable("YIGODHOME_SESSION")).ToString(), "godhome-geometry.bin");
        using (var file = new MemoryStream(YiGodhome.Geometry.Codec.ReadFileBytes(path)))
            if (YiGodhome.Geometry.Codec.Read(file).Frame != geometryFrame) throw new InvalidOperationException("Retry geometry is not the announced generation.");
        restoring = false; restoreSceneLoaded = false; Restored = false;
        if (enemyProbe?.TransferReady != true) throw new InvalidOperationException("Retry requested before warm combat owner was ready.");
        pendingEnemyProbe = false;
        pendingGeometry = path; geometryWaitStarted = Time.realtimeSinceStartup;
        log("NS_RETRY_BEGIN geometryFrame=" + geometryFrame + " nativeHealth=" + activePlayer.health.currentValue);
    }

    public void SceneLoaded(Scene scene)
    {
        if (restoring)
        {
            if (warmScene != null) { Fail(new InvalidOperationException("Warm native revival unexpectedly loaded a scene: " + scene.name)); return; }
            if (scene.name != "A2_S5_BossHorseman_Final") { Fail(new InvalidOperationException("Unexpected native revival scene: " + scene.name)); return; }
            restoreSceneLoaded = true;
            log("NS_REVIVAL_SCENE scene=" + scene.name);
            return;
        }
        if (contactBridge && geometryProbe != null) { Fail(new InvalidOperationException("Native contact fixture scene changed; encounter reset is not yet connected.")); return; }
        if (scene.name == "TitleScreenMenu" && !started) Begin();
    }

    private async void Begin()
    {
        started = true;
        var pairedToken = HollowSols.Foundation.NativeSaveContext.Session;
        try
        {
            var save = UnityEngine.Object.FindFirstObjectByType<SaveManager>();
            activeSave = save;
            var menu = UnityEngine.Object.FindFirstObjectByType<StartMenuLogic>();
            if (save == null || menu == null) throw new InvalidOperationException("Native memory entry is unavailable.");
            save.SetSlot(3);
            if ((int)Slot.GetValue(save) != 3) throw new InvalidOperationException("Native slot assignment failed.");
            await Cysharp.Threading.Tasks.UniTask.Yield();
            if (disposed || pairedToken.HasValue && (!pairedToken.Equals(HollowSols.Foundation.NativeSaveContext.Session)
                || HollowSolsMenuPlugin.Instance?.Lifecycle?.State != HollowSols.Foundation.BridgeState.Loading)) return;
            log("NS_MEMORY_BEGIN index=3 source=" + (HollowSols.Foundation.NativeSaveContext.Enabled && !HollowSols.Foundation.NativeSaveContext.IsNewSave ? "paired-native-checkpoint" : "PlayerAbilityUnlockAll") + " saveIO=" + (HollowSols.Foundation.NativeSaveContext.Enabled ? "isolated-native-provider" : "disabled"));
            if (pairedToken.HasValue) await NativePairedGame.LoadPairedNative(menu);
            else await menu.CreateOrLoadSaveSlotAndPlay(3, false, false, true);
            if (disposed || pairedToken.HasValue && (!pairedToken.Equals(HollowSols.Foundation.NativeSaveContext.Session)
                || HollowSolsMenuPlugin.Instance?.Lifecycle?.State != HollowSols.Foundation.BridgeState.Loading)) return;
            if (HollowSols.Foundation.NativeSaveContext.Enabled) NativePairedGame.PrepareNewNativeLevel(log);
            if (pairedToken.HasValue) {
                await NativePairedGame.WaitNativeEntryReady();
                if (disposed || !pairedToken.Equals(HollowSols.Foundation.NativeSaveContext.Session)
                    || HollowSolsMenuPlugin.Instance?.Lifecycle?.State != HollowSols.Foundation.BridgeState.Loading) return;
            }
            if ((int)Slot.GetValue(save) != 3 || !ApplicationCore.IsInBossMemoryMode) throw new InvalidOperationException("Memory initialization did not retain slot/mode.");
            var player = GameCore.Instance?.player;
            if (player == null) throw new InvalidOperationException("Native Yi did not spawn.");
            activePlayer = player;
            lowest = highest = player.transform.position;
            completed = true;
            log("NS_MEMORY_READY index=3 memory=True player=True scene=" + UnitySceneManager.GetActiveScene().name + " position=" + player.transform.position.ToString("F2"));
            log("NS_MEMORY_RESOURCES health=" + player.health.currentValue + " maxHealth=" + player.health.maxHealth.Value
                + " ammo=" + player.ammo.Value + " maxAmmo=" + player.ammo.MaxValue + " heals=" + player.potion.DrinkTimesLeft
                + " maxHeals=" + player.potion.maxValue + " jadeCapacity=" + player.mainAbilities.PlayerMaxJadePowerStat.Value
                + " maxRunSpeed=" + player.MaxRunStat.Value + " jumpSpeed=" + player.JumpSpeedStat.Value);
            log("NS_COLLISION_MASK solid=" + player.solid_layer.value + " globalSolid=" + Actor.globalSolidLayer.value
                + " oneway=" + player.oneway_layer.value + " slope=" + player.onewaySlope_layer.value
                + " wallJumpDisabled=" + player.wallJumpDisabled_layer.value + " grounded=" + player.IsOnGround);
            CollisionAudit.Dump(log, "NS", player);
            string geometryPath = Path.Combine(Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!, "godhome-geometry.bin");
            string sessionDirectory = Path.GetDirectoryName(geometryPath)!;
            pendingCombatAudit = YiGodhome.Bridge.SessionConfiguration.Current.Has("combat-audit");
            pendingEnemyProbe = YiGodhome.Bridge.SessionConfiguration.Current.Has("enemy-probe");
            contactBridge = YiGodhome.Bridge.SessionConfiguration.Current.Has("contact-bridge");
            pendingEnemyProbe |= contactBridge;
            combatAuditAfter = Time.realtimeSinceStartup + (contactBridge ? 0 : 3);
            bool awaitingGeometry = YiGodhome.Bridge.SessionConfiguration.Current.Has("await-geometry");
            if (awaitingGeometry)
            {
                string session = Guid.Parse(Environment.GetEnvironmentVariable("YIGODHOME_SESSION")).ToString();
                string runsRoot = Path.GetDirectoryName(Path.GetDirectoryName(sessionDirectory))!;
                geometryPath = Path.Combine(runsRoot, "HollowKnight", session, "godhome-geometry.bin");
            }
            if (awaitingGeometry || File.Exists(geometryPath))
            {
                pendingGeometry = geometryPath; geometryWaitStarted = Time.realtimeSinceStartup;
                log("NS_GEOMETRY_WAIT_GAMEPLAY actionsEnabled=" + player.actions.Enabled);
            }
            object actions = player.actions;
            foreach (var field in actions.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var action = field.GetValue(actions);
                var bindings = action?.GetType().GetProperty("Bindings")?.GetValue(action) as IEnumerable;
                if (bindings == null) continue;
                foreach (var binding in bindings) log("NS_MEMORY_INPUT action=" + field.Name + " binding=" + binding.GetType().GetProperty("Name")!.GetValue(binding));
            }
            foreach (var field in typeof(PlayerMainAbilityCollection).GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => typeof(PlayerAbilityData).IsAssignableFrom(f.FieldType)))
            {
                var ability = (PlayerAbilityData)field.GetValue(save.playerAbilityCollection);
                if (ability != null) log("NS_MEMORY_ABILITY field=" + field.Name + " acquired=" + ability.IsAcquired + " equipped=" + ability.IsEquipped);
            }
        }
        catch (Exception error) { Fail(error); }
    }

    public void Update()
    {
        if (!started || failed || disposed) return;
        try
        {
            if (restoring)
            {
                if (!Restored && Time.realtimeSinceStartup - restoreStarted > 20) throw new TimeoutException("Native warm revival did not complete.");
                if (warmScene?.ResetCompleted == true) restoreSceneLoaded = true;
                if (restoreSceneLoaded && !Restored && activePlayer != null && !activePlayer.health.IsDead && activePlayer.actions.Enabled
                    && activePlayer.health.currentValue == activePlayer.health.maxHealth.Value)
                {
                    if (activeSave == null || (int)Slot.GetValue(activeSave) != 3 || !ApplicationCore.IsInBossMemoryMode) throw new InvalidOperationException("Native revival changed slot or memory mode.");
                    enemyProbe ??= new NativeEnemyProbe(log, activePlayer, true, true);
                    enemyProbe.Update();
                    if (!enemyProbe.TransferReady) return;
                    Restored = true;
                    log("NS_REVIVAL_READY health=" + activePlayer.health.currentValue + " ammo=" + activePlayer.ammo.Value + " potions=" + activePlayer.potion.DrinkTimesLeft + " chi=" + activePlayer.chiContainer.Value);
                }
                return;
            }
            if (pendingEnemyProbe && activePlayer != null && activePlayer.actions.Enabled && Time.realtimeSinceStartup >= combatAuditAfter)
            { pendingEnemyProbe = false; enemyProbe = new NativeEnemyProbe(log, activePlayer, contactBridge || YiGodhome.Bridge.SessionConfiguration.Current.Has("canvas-service-audit") || YiGodhome.Bridge.SessionConfiguration.Current.Has("jade-probe")); }
            enemyProbe?.Update();
            if (enemyProbe?.TransferReady == true && YiGodhome.Bridge.SessionConfiguration.Current.Has("jade-probe")) {
                jadeProbe ??= new NativeJadeProbe(activePlayer!, enemyProbe.TransferOwner!, log);
                jadeProbe.Update();
            }
            if (!serviceAudited && enemyProbe?.TransferReady == true && YiGodhome.Bridge.SessionConfiguration.Current.Has("canvas-service-audit")) {
                NativeCanvasServiceAudit.Dump(activePlayer!, log); serviceAudited = true;
            }
            if (contactBridge && !YiGodhome.Bridge.SessionConfiguration.Current.Has("canvas-baseline") && activePlayer != null && enemyProbe?.TransferReady == true) {
                canvas ??= new NativeCanvas(activePlayer, enemyProbe.TransferOwner!, log);
                canvas.UpdateContent();
                if (!canvas.BootstrapRetired) return;
            }
            if (pendingCombatAudit && activePlayer != null && activePlayer.actions.Enabled && Time.realtimeSinceStartup >= combatAuditAfter)
            { pendingCombatAudit = false; NativeCombatAudit.Dump(log, activePlayer); }
            if (pendingGeometry != null && activePlayer != null)
            {
                // Memory initialization returns before the native loading transition finishes.
                // That transition re-enables scene colliders. Import only after native gameplay
                // is enabled, so its late setup cannot restore the original floor over ours.
                bool nativeColliderReady = GameCore.Instance?.player == activePlayer && activePlayer.gameObject.activeInHierarchy
                    && activePlayer.myCollider != null && activePlayer.myCollider.enabled
                    && activePlayer.myCollider.bounds.size.x > 0 && activePlayer.myCollider.bounds.size.y > 0;
                if (!nativeColliderReady && Time.realtimeSinceStartup >= colliderWaitTrace) {
                    colliderWaitTrace = Time.realtimeSinceStartup + 5;
                    log("NS_GEOMETRY_WAIT_COLLIDER player="+activePlayer.GetInstanceID()+" current="+(GameCore.Instance?.player==activePlayer)
                        +" active="+activePlayer.gameObject.activeInHierarchy+" enabled="+activePlayer.myCollider?.enabled+" bounds="+activePlayer.myCollider?.bounds.size+" state="+activePlayer.CurrentStateType);
                }
                if (nativeColliderReady && activePlayer.actions.Enabled && File.Exists(YiGodhome.Geometry.Codec.RuntimePath(pendingGeometry)) && (!contactBridge || enemyProbe?.TransferReady == true))
                {
                    if (returnAfterGeometry >= 0)
                    {
                        using var buffer = new MemoryStream(YiGodhome.Geometry.Codec.ReadFileBytes(pendingGeometry));
                        var candidate = YiGodhome.Geometry.Codec.Read(buffer);
                        if (!YiGodhome.Bridge.ReturnPreparation.FreshHall(Guid.Parse(Environment.GetEnvironmentVariable("YIGODHOME_SESSION")),
                            candidate.SourceSession, candidate.Scene, candidate.Frame, returnAfterGeometry))
                        {
                            if (Time.realtimeSinceStartup - geometryWaitStarted > 30) throw new TimeoutException("Fresh Hall return geometry did not arrive.");
                            return;
                        }
                    }
                    using (var packet = new MemoryStream(YiGodhome.Geometry.Codec.ReadFileBytes(pendingGeometry)))
                    { var next = YiGodhome.Geometry.Codec.Read(packet); QueueSceneContent(next.Scene, next.Frame); }
                    log("NS_GEOMETRY_GAMEPLAY_READY elapsed=" + (Time.realtimeSinceStartup - geometryWaitStarted));
                    geometryProbe = new GeometryLandingProbe(log, activePlayer, pendingGeometry, !hubProbe); pendingGeometry = null;
                    returnAfterGeometry = -1;
                }
                else if (Time.realtimeSinceStartup - geometryWaitStarted >
                    (HollowSols.Foundation.NativeSaveContext.Enabled && HollowSolsMenuPlugin.Instance?.Lifecycle?.State == HollowSols.Foundation.BridgeState.Loading ? HollowSols.Foundation.BridgeLifecycle.LoadingSeconds : 30))
                    throw new InvalidOperationException("Native gameplay or current-session geometry did not become ready before terrain import. actions="+activePlayer.actions.Enabled
                        +" scale="+Time.timeScale+" playerState="+activePlayer.CurrentStateType+" pause="+NativeMenu.PauseOpen+" inventory="+NativeMenu.InventoryOpen
                        +" geometryFile="+File.Exists(YiGodhome.Geometry.Codec.RuntimePath(pendingGeometry))+" transfer="+(enemyProbe?.TransferReady??false));
            }
            if (hubProbe && geometryProbe != null && activePlayer != null && Time.realtimeSinceStartup >= nextHubGeometry)
            {
                nextHubGeometry = Time.realtimeSinceStartup + 1f / 30;
                string run = Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!;
                string root = Path.GetDirectoryName(Path.GetDirectoryName(run))!;
                string path = Path.Combine(root, "HollowKnight", Guid.Parse(Environment.GetEnvironmentVariable("YIGODHOME_SESSION")).ToString(), "godhome-geometry.bin");
                YiGodhome.Geometry.Snapshot next;
                if (!YiGodhome.Geometry.Codec.TryReadFileBytes(path, out byte[] bytes)) {
                    if (geometryContentionAt < 0) { geometryContentionAt = Time.realtimeSinceStartup; log("NS_GEOMETRY_CONTENDED nativeFrame=" + Time.frameCount); }
                    if (Time.realtimeSinceStartup - geometryContentionAt > 1) throw new IOException("Geometry exchange remained contended for1second.");
                    return;
                }
                if (geometryContentionAt >= 0) { log("NS_GEOMETRY_CONTENTION_RECOVERED milliseconds=" + ((Time.realtimeSinceStartup - geometryContentionAt) * 1000) + " nativeFrame=" + Time.frameCount); geometryContentionAt = -1; }
                using (var file = new MemoryStream(bytes)) next = YiGodhome.Geometry.Codec.Read(file);
                bool experimentalScene = HollowSols.Foundation.NativeSaveContext.Enabled && HollowSols.Foundation.NativeSaveContext.Mode != HollowSols.Foundation.GameMode.Godhome
                    && next.Scene != "Menu_Title" && next.Scene != "Quit_To_Menu";
                if (next.SourceSession != Guid.Parse(Environment.GetEnvironmentVariable("YIGODHOME_SESSION")) || !next.Scene.StartsWith("GG_", StringComparison.Ordinal) && !experimentalScene)
                    throw new InvalidOperationException("Hub geometry left its session/room boundary.");
                if (next.Frame < geometryProbe.SourceFrame) throw new InvalidOperationException("Hub geometry generation regressed.");
                if (next.Frame > geometryProbe.SourceFrame)
                {
                    QueueSceneContent(next.Scene, next.Frame);
                    geometryProbe.Dispose(); geometryProbe = null;
                    activePlayer.Velocity = Vector2.zero;
                    geometryProbe = new GeometryLandingProbe(log, activePlayer, path, false);
                    log("NS_HUB_HANDOFF scene=" + next.Scene + " geometry=" + next.Frame + " samePlayer=True");
                }
                else if (lastHubGeometry != null && !lastHubGeometry.SequenceEqual(bytes)) geometryProbe.RefreshShapes(next);
                lastHubGeometry = bytes;
            }
            geometryProbe?.Update();
            var save = activeSave;
            if (save != null && (int)Slot.GetValue(save) != 3) throw new InvalidOperationException("Memory probe attempted another slot.");
            if (completed && activePlayer != null)
            {
                lowest = Vector3.Min(lowest, activePlayer.transform.position);
                highest = Vector3.Max(highest, activePlayer.transform.position);
            }
            if (completed && Time.realtimeSinceStartup >= nextSample)
            {
                nextSample = Time.realtimeSinceStartup + 5;
                var player = activePlayer;
                log("NS_MEMORY_SAMPLE frame=" + Time.frameCount + " scene=" + UnitySceneManager.GetActiveScene().name
                    + " state=" + (player == null ? "none" : player.CurrentStateType.ToString()) + " position=" + (player == null ? "none" : player.transform.position.ToString("F2"))
                    + " travel=" + (highest - lowest).ToString("F2"));
            }
        }
        catch (Exception error) { Fail(error); }
    }

    private void Fail(Exception error)
    {
        failed = true;
        log("NS_MEMORY_FAILED " + error);
        if (HollowSols.Foundation.NativeSaveContext.Enabled && HollowSolsMenuPlugin.Instance != null) HollowSolsMenuPlugin.Instance.GameplayFailed("NS_MEMORY_FAILED", error);
        else Application.Quit();
    }
    public void Dispose() { if(disposed)return; disposed=true; jadeProbe?.Dispose(); geometryProbe?.Dispose(); geometryProbe = null; enemyProbe?.Dispose(); warmScene?.Dispose(); warmScene = null; canvas?.Dispose(); canvas = null; }
}
