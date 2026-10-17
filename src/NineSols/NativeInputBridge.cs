using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using InControl;
using UnityEngine;
using YiGodhome.Bridge;

namespace YiGodhome;

// Only replaces the source values of this Yi's gameplay actions. Native action edges,
// composite axes, gameplay FSMs, movement and combat timing remain game-owned.
internal sealed class NativeInputBridge : IDisposable
{
    private static NativeInputBridge? active;
    internal static bool BindingHostFresh=>active?.endpoint?.IsPeerFresh==true && HostPaused;
    private readonly Harmony harmony = new Harmony("local.yigodhome.input.diagnostic");
    private readonly Player player;
    private readonly PlayerGamePlayActionSet actions;
    private readonly Endpoint? endpoint;
    private readonly Func<Controls>? localFixture;
    private long Sequence => endpoint?.ConsumedInputSequence ?? Time.frameCount;
    private readonly Func<bool> ready;
    private readonly Action<string> log;
    private readonly bool previousEnabled, previousSuspend, previousBackground;
    private Controls held;
    private readonly MenuCommandPress menuCommands=new MenuCommandPress();
    private long pointerSequence=-1;
    private bool menuInputTick;
    private readonly MenuTabPress tabPress = new MenuTabPress();
    private Buttons tabPressed;
    private bool inventoryTabTick;
    private readonly NativeMenuInputVote menuVote;
    private bool menuVoted;
    private float hiveReleaseUntil,hivePressUntil;
    private int hivePressCount;
    internal static bool RequestHiveMeleePress(Player source) {
        var bridge=active;
        if(bridge==null || bridge.player!=source || !bridge.scripted || !SessionConfiguration.Current.Has("trace-probe")
            || !SessionConfiguration.Current.Has("cheat-probe") || EncounterProfile.Selected.Family!=EncounterProfile.HiveKnight
            || !bridge.ready() || bridge.hivePressCount>=8) return false;
        bridge.hivePressCount++;bridge.hiveReleaseUntil=Time.time+.04f;bridge.hivePressUntil=Time.time+.12f;
        bridge.log("NS_HIVE_NATIVE_ATTACK_PRESS_REQUEST bounded=True releaseSeconds=0.04 pressSeconds=0.08 frame="+Time.frameCount);
        return true;
    }
    internal Controls DetailedControls => held;
    public static bool HostPaused { get; private set; }
    internal static bool PairEnding;
    internal static bool DiagnosticBow => Bridge.SessionConfiguration.Current.Has("pause-probe") && active != null && (active.held.Buttons & Buttons.Bow) != 0;
    public static bool KeyboardInput { get; private set; } = true;
    internal static bool ParryDebugRequested => SessionConfiguration.Current.ManualTools && active?.endpoint?.ParryDebugRequested == true;
    private NativeEngineCost? engineCost;
    private static void GameplayScale(TimeScaleModifier __instance, ref float __result)
    { if ((HostPaused || NativeHitPauseObserver.Held) && __instance == TimePauseManager.GamePlayTimeScaleModifier) __result = 0; }
    private static void AnimationScale(Player __instance)
    { if (HostPaused && active?.player == __instance) __instance.animator.speed = 0; }
    // Native Actor.Move uses RCG's movement delta independently of the gameplay
    // modifier. Stop this Yi's native movement step, preserving airborne velocity.
    private static bool MovementStep(Actor __instance, out long __state)
    {
        __state = active?.player == __instance && NativeWorkCost.Enabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        return !HostPaused || active?.player != __instance;
    }
    private static void MovementDone(long __state) { if (active != null) NativeWorkCost.Movement.Record(__state, "player_move", active.log); }
    private static bool PlayerStep(Player __instance, out long __state)
    {
        __state = active?.player == __instance && NativeWorkCost.Enabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        return !HostPaused || active?.player != __instance;
    }
    private static void PlayerStepDone(long __state) { if (active != null) NativeWorkCost.Player.Record(__state, "player_update", active.log); }
    private static void MotionStep(Player __instance, out long __state) => __state = active?.player == __instance && NativeWorkCost.Enabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
    private static void MotionDone(long __state) { if (active != null) NativeWorkCost.PlayerMotion.Record(__state, "player_update_move", active.log); }
    private static void TriggerStep(TriggerDetector __instance, out long __state) => __state = NativeWorkCost.BeginTrigger(__instance);
    private static void TriggerDone(TriggerDetector __instance, long __state) { if (active != null) NativeWorkCost.EndTrigger(__instance, __state, active.log); }
    private static void TriggerEnterDone(long __state) { if (active != null) NativeWorkCost.TriggerEnter.Record(__state, "native_trigger_enter", active.log); }
    private static void TriggerExitDone(long __state) { if (active != null) NativeWorkCost.TriggerExit.Record(__state, "native_trigger_exit", active.log); }
    private bool animationPaused;
    private bool unityPaused;
    private float unityScale;
    private bool? sampledKeyboard;
    private float animationSpeed;
    private ulong lastTick = ulong.MaxValue;
    private string lastState = "";
    private float nextSample;
    private float lastAmmo = -1, lastChi = -1;
    private int lastHeals = -1;
    private bool enabling;
    private bool recoveryRequested;
    private bool recoveryVerified;
    private bool restCancelAwaitRelease;
    private readonly bool scripted;
    private readonly NativeInputBindings? bindingMirror;

    public NativeInputBridge(Player player, Endpoint endpoint, Func<bool> ready, Action<string> log)
        : this(player, endpoint, null, ready, log) { }
    public NativeInputBridge(Player player, Func<Controls> localFixture, Func<bool> ready, Action<string> log)
        : this(player, null, localFixture, ready, log) { }
    private NativeInputBridge(Player player, Endpoint? endpoint, Func<Controls>? localFixture, Func<bool> ready, Action<string> log)
    {
        if (active != null) throw new InvalidOperationException("Input adapter already active.");
        using (var file = File.OpenRead(typeof(PlayerAction).Assembly.Location))
        using (var sha = SHA256.Create())
            if (BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "") != "43670E4ACB7453BA82A6EF1D4ACC5177770B1B27D937F7B6341EB093164FA034")
                throw new InvalidOperationException("Native input library changed; re-audit required.");
        this.player = player; actions = player.actions; this.endpoint = endpoint; this.ready = ready; this.log = log;
        KeyboardInput = endpoint?.KeyboardInput ?? true;
        menuVote=player.gameObject.AddComponent<NativeMenuInputVote>();
        this.localFixture = localFixture;
        scripted = YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures;
        if (endpoint != null) bindingMirror = new NativeInputBindings(endpoint, log);
        var bindingUpdate = typeof(PlayerAction).GetMethod("UpdateBindings", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(ulong), typeof(float) }, null);
        // InControlManager calls UpdateInternal directly, bypassing public Update().
        var managerUpdate = typeof(InputManager).GetMethod("UpdateInternal", BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null);
        if (bindingUpdate == null || managerUpdate == null || bindingUpdate.ReturnType != typeof(void) || managerUpdate.ReturnType != typeof(void))
            throw new InvalidOperationException("Native input hooks do not match audit.");
        previousEnabled = InputManager.Enabled; previousSuspend = InputManager.SuspendInBackground; previousBackground = Application.runInBackground;
        active = this;
        try
        {
            harmony.Patch(AccessTools.Method(typeof(Player),"HorizontalMoveCheck"),prefix:new HarmonyMethod(typeof(NativeRoomEntry),nameof(NativeRoomEntry.AllowHorizontalMove)));
            harmony.Patch(bindingUpdate, prefix: new HarmonyMethod(typeof(NativeInputBridge), nameof(BindingsPrefix)));
            harmony.Patch(managerUpdate, prefix: new HarmonyMethod(typeof(NativeInputBridge), nameof(ManagerPrefix)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(TimeScaleModifier), "finalTimeScale"), postfix: new HarmonyMethod(typeof(NativeInputBridge), nameof(GameplayScale)));
            harmony.Patch(AccessTools.Method(typeof(Player), "AnimatorSpeedSyncSlowDownRatio"), postfix: new HarmonyMethod(typeof(NativeInputBridge), nameof(AnimationScale)));
            harmony.Patch(AccessTools.Method(typeof(Actor), "Move"), prefix: new HarmonyMethod(typeof(NativeInputBridge), nameof(MovementStep)), postfix: new HarmonyMethod(typeof(NativeInputBridge), nameof(MovementDone)));
            harmony.Patch(AccessTools.Method(typeof(Player), "Update"), prefix: new HarmonyMethod(typeof(NativeInputBridge), nameof(PlayerStep)), postfix: new HarmonyMethod(typeof(NativeInputBridge), nameof(PlayerStepDone)));
            if (NativeWorkCost.Enabled) harmony.Patch(AccessTools.Method(typeof(TriggerDetector), "UpdateImplement"), prefix: new HarmonyMethod(typeof(NativeInputBridge), nameof(TriggerStep)), postfix: new HarmonyMethod(typeof(NativeInputBridge), nameof(TriggerDone)));
            if (NativeWorkCost.Enabled) {
                harmony.Patch(AccessTools.Method(typeof(TriggerDetector), "OnTriggerEnter2D"), prefix: new HarmonyMethod(typeof(NativeInputBridge), nameof(TriggerStep)), postfix: new HarmonyMethod(typeof(NativeInputBridge), nameof(TriggerEnterDone)));
                harmony.Patch(AccessTools.Method(typeof(TriggerDetector), "OnTriggerExit2D"), prefix: new HarmonyMethod(typeof(NativeInputBridge), nameof(TriggerStep)), postfix: new HarmonyMethod(typeof(NativeInputBridge), nameof(TriggerExitDone)));
            }
            if (NativeWorkCost.Enabled) harmony.Patch(AccessTools.Method(typeof(Player), "UpdateMove"), prefix: new HarmonyMethod(typeof(NativeInputBridge), nameof(MotionStep)), postfix: new HarmonyMethod(typeof(NativeInputBridge), nameof(MotionDone)));
            ManagerPrefix();
            log("NS_INPUT_READY nativeActions=True bindingsUnchanged=True saveIO=disabled");
        }
        catch { Dispose(); throw; }
    }

    private static void ManagerPrefix()
    {
        var bridge = active;
        if (bridge == null) return;
        InputManager.SuspendInBackground = false; Application.runInBackground = true;
        // The native setter calls UpdateInternal BEFORE assigning enabled=true.
        // Let that nested invocation see disabled and return; the outer call then
        // continues normally with enabled=true, without recursively invoking the setter.
        if (!InputManager.Enabled && !bridge.enabling)
        {
            bridge.enabling = true;
            try { InputManager.Enabled = true; }
            finally { bridge.enabling = false; }
            bridge.log("NS_INPUT_UPDATER_RESUMED frame=" + Time.frameCount + " focused=" + Application.isFocused);
        }
    }

    private static bool BindingsPrefix(PlayerAction __instance, ulong __0, float __1)
    {
        var bridge = active;
        if (bridge == null) return true;
        var ui = bridge.player.playerInput.uiActions;
        bool menuAction = ReferenceEquals(__instance.Owner, ui);
        if (!ReferenceEquals(__instance.Owner, bridge.actions) && !menuAction) return true;
        // Native rebinding retains its listener; keyboard polls are scoped to
        // the foreground HK window without exposing the NS window.
        // Host pause still stops Yi simulation while this native panel is open.
        if (bridge.lastTick != __0)
        {
            bridge.lastTick = __0;
            bridge.menuInputTick=NativeMenu.Open;
            bridge.held = bridge.localFixture != null ? bridge.localFixture() : bridge.endpoint!.ConsumeControls(bridge.held);
            NativeBindingInput.ObserveMouse(bridge.held);
            bridge.tabPressed = bridge.tabPress.Advance(bridge.held.Buttons);
            bridge.inventoryTabTick = NativeMenu.InventoryOpen && !NativeMenu.ControlsOpen && !NativeMenu.PauseOpen;
            NativeMenu.ObserveBindingRelease(bridge.held);
            // Pause is level state published on every host frame, not an input
            // edge. Expiring a delayed attack/input must not unpause the game.
            var host = bridge.endpoint?.Peer;
            bool paused = bridge.endpoint != null && host?.Audio != null
                ? host.Audio.Paused : (bridge.held.Buttons & Buttons.HostPaused) != 0;
            if (HostPaused != paused) bridge.log("NS_HOST_PAUSE paused=" + paused);
            HostPaused = paused || PairEnding;
            bridge.ApplyPauseClock();
            bridge.UpdateMenuVote();
            NativeRoomEntry.Metadata(bridge.held.Buttons);
            bridge.held = NativeWallRunFixture.Apply(bridge.held);
            KeyboardInput = bridge.endpoint?.KeyboardInput ?? (bridge.held.Buttons & Buttons.Keyboard) != 0;
            var type = KeyboardInput ? BindingSourceType.KeyBindingSource : BindingSourceType.DeviceBindingSource;
            bridge.actions.LastInputType = type; ui.LastInputType = type;
            var menuPressed=bridge.menuCommands.Advance(bridge.held.Buttons);
            if((menuPressed&Buttons.CloseInventory)!=0)NativeMenu.CloseInventory(bridge.log);
            if(KeyboardInput && bridge.pointerSequence!=bridge.Sequence && (bridge.held.Buttons&(Buttons.MenuPointer|Buttons.MenuHover))!=0) {
                bridge.pointerSequence=bridge.Sequence;
                NativeMenu.PointPointer(bridge.held.X,bridge.held.Y,(menuPressed&Buttons.MenuPointer)!=0);
            }
            // A bounded native damage fixture owns its short collision window.
            // Consume/ack transport normally, but release competing scripted bow,
            // attack and defense input instead of contaminating its health oracle.
            if (!NativeMenu.Open && (!bridge.ready() || HostPaused || NativeRoomEntry.Active || bridge.scripted && RadiantRevivalProbe.BlocksCheat))
                bridge.held = new Controls(0, 0, bridge.held.Buttons & (Buttons.HostPaused | Buttons.NativePauseMenu | Buttons.Keyboard | Buttons.DoorEnter | Buttons.StatueEnter | Buttons.EntryRevealed));
        }
        // Consume transport/pause metadata even when NS has focus. Every native
        // borrowed panel navigates from HK; binding capture alone needs real gameplay
        // bindings in the controls panel, while simulation remains paused.
        if(NativeMenu.IsBindingAction(__instance))return true;
        bool menuCommand=(bridge.held.Buttons&(Buttons.MenuPointer|Buttons.MenuHover|Buttons.CloseInventory|Buttons.BindingMouse))!=0;
        float value = menuCommand ? 0 : menuAction ? (NativeMenu.Open && ui.Enabled && NativeMenu.NavigationInteractive && !NativeMenu.BindingActive ? bridge.MenuValue(__instance, ui) : 0)
            : bridge.actions.Enabled && !NativeMenu.Open && !bridge.menuInputTick && !HostPaused && !NativeRoomEntry.Active && bridge.ready() ? bridge.Value(__instance) : 0;
        __instance.CommitWithValue(value, __0, __1);
        if(__instance==ui.Cancel && __instance.WasPressed && NativeRestPoint.Active && bridge.rootCancelTraces++<16) {
            var system=UnityEngine.EventSystems.EventSystem.current;
            var module=system?.currentInputModule as InControlInputModule;
            var button=system?.currentSelectedGameObject?.GetComponent<UIControlButton>();
            var group=button==null?null:AccessTools.Field(typeof(UIControlButton),"belongGroup").GetValue(button) as UIControlGroup;
            var feedback=button==null?null:AccessTools.Field(typeof(UIControlButton),"_onSubmitFeedback").GetValue(button);
            bool feedbackAlive=feedback!=null && (bool)AccessTools.PropertyGetter(feedback.GetType(),"isAlive").Invoke(feedback,null);
            bridge.log("NS_ROOT_CANCEL_ADMISSION navigation="+system?.sendNavigationEvents+" cancelBound="+ReferenceEquals(module?.CancelAction,ui.Cancel)
                +" boundCancel="+module?.CancelAction?.Name+" groupId="+group?.GetInstanceID()+" currentGroupId="+ApplicationUIGroupManager.Instance.currentControlGroup?.GetInstanceID()+" group="+group?.name+" interactive="+group?.interactable+" feedbackAlive="+feedbackAlive
                +" customCancel="+(button!=null && AccessTools.Field(typeof(UIControlButton),"onCancelHandler").GetValue(button)!=null));
        }
        if (__instance.WasPressed || __instance.WasReleased)
            bridge.log("NS_INPUT_EDGE id=" + bridge.Sequence + " action=" + __instance.Name
                + " pressed=" + __instance.WasPressed + " released=" + __instance.WasReleased + " nativeTick=" + __0 + " frame=" + Time.frameCount);
        return false;
    }

    private float Value(PlayerAction action)
    {
        if(Time.time<hivePressUntil && ready() && !HostPaused && !NativeRoomEntry.Active) {
            if(action==actions.Attack) return Time.time>=hiveReleaseUntil?1:0;
            if(action==actions.Parry || action==actions.Dodge || action==actions.WeaponAttack || action==actions.FooAttack) return 0;
        }
        if (action == actions.MoveLeft) return Math.Max(0, -held.X);
        if (action == actions.MoveRight) return Math.Max(0, held.X);
        if (action == actions.MoveUp) return Math.Max(0, held.Y);
        if (action == actions.MoveDown) return Math.Max(0, -held.Y);
        Buttons button = action == actions.Jump ? Buttons.Jump : action == actions.Dodge ? Buttons.Dodge
            : action == actions.Parry ? Buttons.Parry : action == actions.Attack ? Buttons.Attack
            : action == actions.Heal ? Buttons.Heal : action == actions.WeaponAttack ? Buttons.Bow
            : action == actions.FooAttack ? Buttons.Talisman : action == actions.NextItem ? Buttons.NextItem
            : action == actions.PreviousItem ? Buttons.PreviousItem : Buttons.None;
        return button != Buttons.None && (held.Buttons & button) != 0 ? 1 : 0;
    }

    private float MenuValue(PlayerAction action, UIControlActionSet ui)
    {
        bool rootBack = NativeRestPoint.Active && !NativeMenu.ControlsOpen;
        if (rootBack && (held.Buttons & Buttons.Interact) != 0)
            return action == ui.Cancel && !restCancelAwaitRelease ? 1 : 0;
        if (action == ui.Cancel && (restCancelAwaitRelease || rootBack)) return 0;
        if (action == ui.MoveLeft) return Math.Max(0, -held.X);
        if (action == ui.MoveRight) return Math.Max(0, held.X);
        if (action == ui.MoveUp) return Math.Max(0, held.Y);
        if (action == ui.MoveDown) return Math.Max(0, -held.Y);
        Buttons button = action == ui.Submit ? Buttons.Jump : action == ui.Confirm ? Buttons.Confirm : action == ui.Cancel ? Buttons.Cancel
            : action == ui.PreviousTab ? Buttons.Parry : action == ui.NextTab ? Buttons.Talisman
            : action == ui.ChildPreviousTab ? Buttons.ChildPrevious : action == ui.ChildNextTab ? Buttons.ChildNext
            : action == ui.SubmitSpecial ? Buttons.Bow : Buttons.None;
        if (inventoryTabTick && (button & MenuTabPress.Tabs) != 0)
            return (tabPressed & button) != 0 ? 1 : 0;
        return button != Buttons.None && (held.Buttons & button) != 0 ? 1 : 0;
    }
    private int rootCancelTraces;
    private bool inventoryHeld;
    private bool interactHeld;

    private void UpdateMenuVote()
    {
        bool open=NativeMenu.Open;
        if(open==menuVoted)return;
        menuVoted=open;
        // Canvas-owned panels do not necessarily traverse the original root's
        // input-state callbacks. Use an independent native vote, retaining all
        // native panel/cutscene votes instead of forcing an action set enabled.
        if(open)player.playerInput.VoteForState(PlayerInputStateType.UI,menuVote);
        else player.playerInput.RevokeAllMyVote(menuVote);
        log("NS_MENU_INPUT_VOTE open="+open+" nativeState="+player.playerInput.currentStateType+" uiEnabled="+player.playerInput.uiActions.Enabled);
    }

    private void ApplyPauseClock()
    {
        if (HostPaused) {
            if (!unityPaused) {
                unityScale = NativeHitPauseObserver.Held ? 1 : Time.timeScale; unityPaused = true;
                animationSpeed = player.animator.speed; animationPaused = true;
                log("NS_HOST_PAUSE_LEASE scale="+unityScale+" animation="+animationSpeed);
            }
            // RCG gameplay time alone does not stop Unity animators, particles,
            // projectile physics or scaled talisman continuations.
            Time.timeScale = 0;
            player.animator.speed = 0;
        } else if (unityPaused) {
            Time.timeScale = NativeHitPauseObserver.Held ? 0 : unityScale; unityPaused = false;
            player.animator.speed = animationSpeed; animationPaused = false;
            log("NS_HOST_PAUSE_RELEASE scale="+Time.timeScale+" animation="+player.animator.speed);
        }
    }

    public void Update()
    {
        UpdateMenuVote();
        bindingMirror?.Update();
        if (NativeWorkCost.Enabled) { engineCost ??= new NativeEngineCost(log); engineCost.Sample(log); }
        ApplyPauseClock();
        if (NativeWallRunFixture.Failure != null) throw new InvalidOperationException(NativeWallRunFixture.Failure);
        if (scripted && sampledKeyboard != KeyboardInput)
        {
            sampledKeyboard = KeyboardInput;
            var icons = PlayerInputBinder.Instance.InputBindingIconSetting;
            log("NS_ACTION_GLYPH keyboard=" + KeyboardInput + " parry=" + icons.FindIcon(actions.Parry, InputBindingGameSetting.ScenarioSet.DefaultAction)?.name
                + " talisman=" + icons.FindIcon(actions.FooAttack, InputBindingGameSetting.ScenarioSet.DefaultAction)?.name);
        }
        bool requestPause=(held.Buttons&Buttons.NativePauseMenu)!=0;
        if(NativeMenu.ControlsOpen && !NativeMenu.BindingActive && (held.Buttons&(Buttons.Cancel|Buttons.NativePauseMenu))==(Buttons.Cancel|Buttons.NativePauseMenu))NativeMenu.CancelBinding(log);
        // The ordered request can arrive before the peer's pause metadata or
        // native readiness. Do not consume its edge before those gates admit it.
        // OpenControls is idempotent; Cancel+request is explicitly a close.
        if(requestPause && (held.Buttons&Buttons.Cancel)==0 && !NativeMenu.ControlsOpen && ready() && HostPaused)NativeMenu.OpenControls(log);
        bool inventory = (held.Buttons & Buttons.Inventory) != 0;
        if (inventory && !inventoryHeld && ready() && !NativeMenu.ControlsOpen && !NativeMenu.BindingActive) NativeMenu.Toggle(log);
        inventoryHeld = inventory;
        bool interact = (held.Buttons & Buttons.Interact) != 0;
        if (interact && !interactHeld && ready() && !NativeMenu.Open)
        {
            NativeRestPoint.Interact();
            if (NativeRestPoint.Active) restCancelAwaitRelease = true;
        }
        // B opens the rest point and is also native UI Cancel. Require release
        // before that same held physical press may close the newly opened panel.
        if (!interact && (held.Buttons & Buttons.Cancel) == 0) restCancelAwaitRelease = false;
        interactHeld = interact;
        if (player.ammo.Value != lastAmmo || player.potion.DrinkTimesLeft != lastHeals || player.chiContainer.Value != lastChi)
        {
            lastAmmo = player.ammo.Value; lastHeals = player.potion.DrinkTimesLeft; lastChi = player.chiContainer.Value;
            log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "NS_INPUT_RESOURCES id={0} frame={1} ammo={2:R} heals={3} chi={4:R} health={5}",
                Sequence, Time.frameCount, lastAmmo, lastHeals, lastChi, player.health.currentValue));
        }
        if (scripted && ready() && !recoveryRequested)
        {
            recoveryRequested = true; InputManager.Enabled = false;
            log("NS_INPUT_RECOVERY_REQUEST frame=" + Time.frameCount);
        }
        else if (recoveryRequested && !recoveryVerified && InputManager.Enabled)
        { recoveryVerified = true; log("NS_INPUT_RECOVERY_VERIFIED frame=" + Time.frameCount + " inputTick=" + lastTick); }
        string state = player.CurrentStateType.ToString();
        if (state != lastState || Time.realtimeSinceStartup >= nextSample)
        {
            lastState = state; nextSample = Time.realtimeSinceStartup + 0.1f;
            if (scripted && HostPaused)
            {
                var animator = player.GetComponentInChildren<Animator>();
                log("NS_PAUSE_SAMPLE scale=" + Time.timeScale + " gameplay=" + TimePauseManager.GamePlayTimeScaleModifier.finalTimeScale
                    + " animation=" + (animator == null ? -1 : animator.GetCurrentAnimatorStateInfo(0).normalizedTime)
                    + " frame=" + Time.frameCount + " x=" + player.transform.position.x + " y=" + player.transform.position.y
                    + " vx=" + player.Velocity.x + " vy=" + player.Velocity.y + " grounded=" + player.IsOnGround);
            }
            var p = player.transform.position; var v = player.Velocity;
            log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "NS_INPUT_NATIVE id={0} frame={1} state={2} x={3:R} y={4:R} vx={5:R} vy={6:R} grounded={7} enabled={8} focused={9} inputTick={10} unityScale={11:R} gameplayScale={12:R} unityDelta={13:R} unscaledDelta={14:R} nativeDelta={15:R}",
                Sequence, Time.frameCount, state, p.x, p.y, v.x, v.y, player.IsOnGround, actions.Enabled, Application.isFocused, lastTick, Time.timeScale, TimePauseManager.GamePlayTimeScaleModifier.finalTimeScale, Time.deltaTime, Time.unscaledDeltaTime, RCGTime.deltaTime));
        }
    }

    public void Dispose()
    {
        PairEnding = false;
        engineCost?.Dispose(); engineCost = null;
        if (active != this) return;
        if (animationPaused && player != null) player.animator.speed = animationSpeed;
        if (unityPaused) { Time.timeScale = unityScale; unityPaused = false; }
        held = default; active = null; HostPaused = false; harmony.UnpatchSelf();
        if(menuVote!=null){if(player!=null)player.playerInput.RevokeAllMyVote(menuVote);UnityEngine.Object.Destroy(menuVote);}
        InputManager.Enabled = previousEnabled; InputManager.SuspendInBackground = previousSuspend; Application.runInBackground = previousBackground;
    }
}

// Identity for only the bridge's temporary UI input-state vote.
internal sealed class NativeMenuInputVote : MonoBehaviour { }
