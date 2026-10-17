using System;
using System.Linq;
using InControl;
using UnityEngine;
using YiGodhome.Bridge;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

namespace YiGodhome;

// Separate transient action set; mirrors NS-owned native bindings without
// changing either game's persisted settings from the host process.
internal sealed class ControlSource : PlayerActionSet
{
    public bool HubReentryFixture;
    private readonly PlayerAction left, right, up, down, jump, dodge, parry, attack;
    private readonly PlayerAction heal, bow, talisman, inventory, cancel, interact, nextItem, previousItem;
    private readonly PlayerAction menuLeft, menuRight, menuUp, menuDown, menuPrevious, menuNext, menuSubmit, menuConfirm, menuSpecial, childPrevious, childNext;
    public bool MenuOpen;
    private readonly InputRearmGate rearm = new InputRearmGate();
    public void ObserveAvailability(bool available)
    {
        if (!scripted) rearm.Observe(GameplayHeld(), available && Application.isFocused);
    }
    private Buttons GameplayHeld() => (jump.IsPressed ? Buttons.Jump : 0) | (dodge.IsPressed ? Buttons.Dodge : 0)
        | (parry.IsPressed ? Buttons.Parry : 0) | (attack.IsPressed ? Buttons.Attack : 0)
        | (heal.IsPressed ? Buttons.Heal : 0) | (bow.IsPressed ? Buttons.Bow : 0) | (talisman.IsPressed ? Buttons.Talisman : 0);
    internal float InteractionValue => interact.Value;
    internal string InteractionControllerBinding=>interact.Bindings.OfType<DeviceBindingSource>().FirstOrDefault()?.Control.ToString()??"unbound";
    private readonly Action<string> log;
    private readonly bool scripted;
    private readonly string targetScene;
    private readonly bool contactFixture;
    private readonly bool targetFixture;
    private readonly bool hitFixture;
    private readonly bool lifecycleFixture;
    private readonly bool crimsonFixture;
    private readonly bool cheatFixture;
    private readonly bool restFixture;
    private readonly bool kickFixture;
    private float counterHeldAt = -1, counterReleaseUntil;
    private PlayMakerFSM? counterBossFsm;
    private BoxCollider2D? armorPlatform;
    private float armorPlatformAt=-1;
    private bool armorStandingFixtureComplete;
    private float counterHealth = -1;
    public Func<HitKind, bool>? HasResolvedHit;
    public Func<HitKind, bool>? HasSummonHit;
    public Func<bool>? HasSummons;
    public Func<ContactFrame?>? DiagnosticTargets;
    public Func<bool>? DiagnosticCounterPrecharge;
    public Func<bool>? DiagnosticSkipCounter;
    private float slyChargeStarted = -1;
    public Func<SummonedTarget, bool>? DiagnosticEligible;
    public Func<long>? DiagnosticProgress;
    public Func<int>? PauseProbeStage;
    private long diagnosticProgress;
    private float diagnosticAttemptAt = -1;
    private Buttons diagnosticAttempt;
    private bool diagnosticFailed;
    public bool AllowLifecycleAttack { get; set; }
    private Collider2D? contactCollider;
    private float contactParryUntil, nextContactParry;
    private bool pitDropped;
    private float healUntil, nextHeal;
    private float started = -1;
    private bool complete;
    private float poolJumpAt = -1;
    private bool poolExited;
    private float voidHealth = -1, voidHitAt = -1, voidRecoveredAt = -1;
    private int voidRecoveries;
    private bool voidMovementVerified;
    private float voidReturnX;
    private bool hubStatueRequested, hubStatueDone;
    private float restTestAt = -1;
    private bool restTestDone;
    internal static bool DiagnosticRestComplete { get; private set; }
    private float hubStatueAt;
    private float nextTrace;
    private bool performanceReady;
    private readonly bool previousBackground;
    private readonly bool hub;
    private byte[] appliedBindings = Array.Empty<byte>();
    public string Mode => scripted ? "Automated control sequence; no input needed." : "Focus Hollow Knight to control Yi.";
    public ControlSource(Action<string> log, bool scripted, bool arena = false, bool contactFixture = false, bool hub = false)
    {
        this.log = log; this.scripted = scripted; this.hub = hub;
        restFixture = scripted && YiGodhome.Bridge.SessionConfiguration.Current.Has("rest-probe");
        DiagnosticRestComplete = false;
        kickFixture = scripted && YiGodhome.Bridge.SessionConfiguration.Current.Has("kick-probe");
        cheatFixture = scripted && YiGodhome.Bridge.SessionConfiguration.Current.Has("cheat-probe");
        crimsonFixture = scripted && YiGodhome.Bridge.SessionConfiguration.Current.Has("crimson-probe");
        targetScene = arena ? YiGodhome.Bridge.EncounterProfile.Selected.Scene : "GG_Atrium";
        this.contactFixture = contactFixture;
        targetFixture = contactFixture && YiGodhome.Bridge.SessionConfiguration.Current.Has("target-probe");
        hitFixture = targetFixture && YiGodhome.Bridge.SessionConfiguration.Current.Has("hit-probe");
        lifecycleFixture = hitFixture && YiGodhome.Bridge.SessionConfiguration.Current.Has("lifecycle-probe");
        PlayerAction Bind(string name, Key key, InputControlType controller)
        { var action = CreatePlayerAction(name); action.AddDefaultBinding(key); action.AddDefaultBinding(controller); return action; }
        left = Bind("MoveLeft", Key.A, InputControlType.LeftStickLeft); right = Bind("MoveRight", Key.D, InputControlType.LeftStickRight);
        up = Bind("MoveUp", Key.W, InputControlType.LeftStickUp); down = Bind("MoveDown", Key.S, InputControlType.LeftStickDown);
        menuLeft = Bind("YiMenuLeft", Key.LeftArrow, InputControlType.DPadLeft); menuRight = Bind("YiMenuRight", Key.RightArrow, InputControlType.DPadRight);
        menuUp = Bind("YiMenuUp", Key.UpArrow, InputControlType.DPadUp); menuDown = Bind("YiMenuDown", Key.DownArrow, InputControlType.DPadDown);
        menuPrevious = Bind("YiMenuPrevious", Key.Q, InputControlType.LeftBumper); menuNext = Bind("YiMenuNext", Key.E, InputControlType.RightBumper);
        menuSubmit = Bind("YiMenuSubmit", Key.Z, InputControlType.Action1); menuSubmit.AddDefaultBinding(Key.Return); menuSubmit.AddDefaultBinding(Key.Space);
        menuConfirm = Bind("YiMenuConfirm", Key.E, InputControlType.Action1); menuConfirm.AddDefaultBinding(Key.Return); menuConfirm.AddDefaultBinding(Key.Space);
        menuSpecial=Bind("YiMenuSpecial",Key.C,InputControlType.Action4);
        childPrevious = CreatePlayerAction("YiMenuChildPrevious"); childPrevious.AddDefaultBinding(InputControlType.LeftTrigger);
        childNext = CreatePlayerAction("YiMenuChildNext"); childNext.AddDefaultBinding(InputControlType.RightTrigger);
        jump = Bind("Jump", Key.Space, InputControlType.Action1); attack = Bind("Attack", Key.J, InputControlType.Action3);
        parry = Bind("Parry", Key.K, InputControlType.LeftBumper); dodge = Bind("Dodge", Key.LeftShift, InputControlType.RightTrigger);
        heal = Bind("Heal", Key.R, InputControlType.DPadUp); bow = Bind("WeaponAttack", Key.C, InputControlType.Action4);
        talisman = Bind("FooAttack", Key.F, InputControlType.RightBumper);
        inventory = Bind("Map", Key.Tab, InputControlType.Select);
        nextItem = Bind("NextItem", Key.O, InputControlType.DPadRight);
        previousItem = Bind("PreviousItem", Key.U, InputControlType.DPadLeft);
        inventory.AddDefaultBinding(InputControlType.Back); inventory.AddDefaultBinding(InputControlType.View);
        cancel = Bind("YiCancel", Key.X, InputControlType.Action2);
        interact = Bind("Hack", Key.E, InputControlType.Action2);
        previousBackground = Application.runInBackground; Application.runInBackground = true;
        log("HK_INPUT_SOURCE mode=" + (scripted ? "scripted" : "physical") + " bindings=temporary");
    }

    private float sequenceJumpUntil;
    public Controls Sample(Endpoint endpoint)
    {
        var bindings = endpoint.NativeBindings;
        if (bindings.Length > 0 && !ReferenceEquals(bindings, appliedBindings)) {
            LoadData(bindings); ClearInputState(); appliedBindings = bindings;
            log("HK_NATIVE_YI_BINDINGS_APPLIED bytes=" + bindings.Length + " nativeSettingsOwner=NineSols persistentHostWrites=False");
            return default; // A held old binding cannot become a new attack edge.
        }
        var gm = GameManager.instance; var pose = endpoint.FreshPose;
        if (!scripted && Time.realtimeSinceStartup >= nextTrace)
        {
            nextTrace = Time.realtimeSinceStartup + 1;
            log("HK_INPUT_CAPTURE focused=" + Application.isFocused + " managerEnabled=" + InputManager.Enabled + " actionsEnabled=" + Enabled
                + " tick=" + UpdateTick + " freshPose=" + (pose != null) + " gameState=" + (gm == null ? "none" : gm.gameState.ToString())
                + " left=" + left.Value + " right=" + right.Value + " jump=" + jump.Value
                + " interact=" + interact.Value + " inventory=" + inventory.Value + " device=" + InputManager.ActiveDevice.Name);
        }
        if (gm == null || gm.profileID != 4 || gm.gameState.ToString() != "PLAYING" && !(gm.IsGamePaused() && HollowSolsNativePause.Active) || (hub ? !HubPresence.Allowed(UnitySceneManager.GetActiveScene().name) : UnitySceneManager.GetActiveScene().name != targetScene) || pose == null)
            return default;
        if(HollowSolsNativePause.Requested)return new Controls(0,0,Buttons.NativePauseMenu | Buttons.HostPaused);
        if (SessionConfiguration.Current.Has("void-probe") && UnitySceneManager.GetActiveScene().name != "GG_Workshop" && UnitySceneManager.GetActiveScene().name != "GG_Atrium") return default;
        if (SessionConfiguration.Current.Performance) {
            if (!performanceReady) { performanceReady = true; log("PERFORMANCE_READY workload=" + (SessionConfiguration.Current.HubCombat ? "atrium-gameplay-idle" : "atrium-idle") + " scene=" + UnitySceneManager.GetActiveScene().name + " x=" + pose.X + " y=" + pose.Y + " geometry=" + pose.GeometryFrame); }
            return default;
        }
        if (!scripted)
        {
            if (!Application.isFocused) return default;
            if (MenuOpen)
            {
                Buttons menu = (menuSubmit.IsPressed ? Buttons.Jump : 0) | (menuConfirm.IsPressed ? Buttons.Confirm : 0)
                    | (menuPrevious.IsPressed ? Buttons.Parry : 0) | (menuNext.IsPressed ? Buttons.Talisman : 0)
                    | (childPrevious.IsPressed ? Buttons.ChildPrevious : 0) | (childNext.IsPressed ? Buttons.ChildNext : 0)
                    | (menuSpecial.IsPressed ? Buttons.Bow : 0) | (cancel.IsPressed ? Buttons.Cancel : 0) | (inventory.IsPressed ? Buttons.Inventory : 0)
                    | (interact.IsPressed ? Buttons.Interact : 0);
                return NativeMenuPresenter.MenuInput(new Controls(Mathf.Clamp(right.Value - left.Value + menuRight.Value - menuLeft.Value, -1, 1),
                    Mathf.Clamp(up.Value - down.Value + menuUp.Value - menuDown.Value, -1, 1), menu));
            }
            Buttons buttons = (jump.IsPressed ? Buttons.Jump : 0) | (dodge.IsPressed ? Buttons.Dodge : 0)
                | (parry.IsPressed ? Buttons.Parry : 0) | (attack.IsPressed ? Buttons.Attack : 0)
                | (heal.IsPressed ? Buttons.Heal : 0) | (bow.IsPressed ? Buttons.Bow : 0) | (talisman.IsPressed ? Buttons.Talisman : 0)
                | (inventory.IsPressed ? Buttons.Inventory : 0) | (cancel.IsPressed ? Buttons.Cancel : 0) | (interact.IsPressed ? Buttons.Interact : 0)
                | (nextItem.IsPressed ? Buttons.NextItem : 0) | (previousItem.IsPressed ? Buttons.PreviousItem : 0);
            return rearm.Filter(new Controls(right.Value - left.Value, up.Value - down.Value, buttons));
        }
        // Exercise native in-flight effect retirement during bounded sequence handoffs.
        if (SessionConfiguration.Current.Has("pantheon-probe") && endpoint.FreshEncounter?.Phase == EncounterPhase.Advancing)
            return new Controls(0, 0, Time.realtimeSinceStartup % 1f < .25f ? Buttons.Bow : Buttons.None);
        // A fixture must settle a native menu before attempting a statue or
        // combat input. Use native Cancel edges; never bypass UI admission.
        if (hub && SessionConfiguration.Current.HubCombat && !restFixture && MenuOpen)
            if(HollowSolsNativePause.Active && Time.realtimeSinceStartup<HollowSolsNativePause.FixtureHoldUntil)return default;
        if (hub && SessionConfiguration.Current.HubCombat && !restFixture && MenuOpen)
            return new Controls(0, 0, Time.realtimeSinceStartup % .6f < .2f ? Buttons.Cancel : Buttons.None);
        // A scripted hub-combat regression ends in the Hall; never choose a retry for the player.
        if (hub && !HubReentryFixture && endpoint.FreshEncounter?.Phase == EncounterPhase.Ready && !SessionConfiguration.Current.Has("pantheon-probe")) return default;
        if (SessionConfiguration.Current.Has("pantheon-probe") && ReturnPreparation.IsHub(UnitySceneManager.GetActiveScene().name) && endpoint.FreshEncounter?.Phase == EncounterPhase.Ready) return default;
        if (SessionConfiguration.Current.Has("pantheon-probe") && BossSequenceController.IsInSequence && BossSceneController.Instance?.customExitPoint != null) {
            var exit = BossSceneController.Instance.customExitPoint;
            float delta = exit.transform.position.x - pose.X;
            // Bounded fixture traverses the authored native exit. It never
            // calls FinishBossScene or fabricates a won encounter.
            if (pose.Grounded && exit.transform.position.y - pose.Y > 2 && Time.realtimeSinceStartup > sequenceJumpUntil + .4f) sequenceJumpUntil = Time.realtimeSinceStartup + .25f;
            if (Math.Abs(delta) > .6f) return new Controls(Math.Sign(delta), 0, Time.realtimeSinceStartup < sequenceJumpUntil ? Buttons.Jump : Buttons.None);
            InteractionBinding.ScriptedPressUntil = Time.realtimeSinceStartup + .15f;
            return new Controls(0, 0, Buttons.Interact);
        }
        if (started < 0)
        {
            if (!pose.Grounded) return default;
            started = Time.realtimeSinceStartup; log("HK_INPUT_SCRIPT_BEGIN frame=" + Time.frameCount + " floor=" + pose.Y);
        }
        float t = Time.realtimeSinceStartup - started;
        if (SessionConfiguration.Current.Has("lever-probe") && UnitySceneManager.GetActiveScene().name == "GG_Workshop")
            return new Controls(0, 0, t >= 2 && t < 2.3f || t >= 5 && t < 5.3f ? Buttons.Attack : Buttons.None);
        if (hub && !contactFixture && t < 5)
            return new Controls(0, 0, t >= 1 && t < 1.2f || t >= 4 && t < 4.2f ? Buttons.Inventory : Buttons.None);
        if (hub && restFixture && !restTestDone && UnitySceneManager.GetActiveScene().name == "GG_Workshop") {
            if (restTestAt < 0) {
                if (!pose.Grounded || Math.Abs(pose.X - 11.18f) > 0.3f || Math.Abs(pose.Y - 35) > 0.3f) return default;
                restTestAt = Time.realtimeSinceStartup; log("HK_REST_FIXTURE interaction=True context=Hall");
            }
            float elapsed = Time.realtimeSinceStartup - restTestAt;
            if (elapsed < 0.2f) return new Controls(0, 0, Buttons.Interact);
            if (elapsed < 1) return new Controls(0, 0, Buttons.Cancel);
            if (elapsed >= 2 && elapsed < 2.2f) return new Controls(0, 0, Buttons.Jump);
            if (elapsed >= 4 && elapsed < 4.2f || elapsed >= 7 && elapsed < 7.2f) return new Controls(0, 0, Buttons.Inventory);
            if (elapsed >= 10 && elapsed < 10.3f) return new Controls(0, 0, Buttons.Cancel);
            if (elapsed < 14) return default;
            // Closing inventory can reveal the native rest panel underneath.
            // Wait for actual menu closure before exercising the next encounter.
            if (MenuOpen) {
                if (elapsed > 20) throw new InvalidOperationException("Rest-to-challenge fixture could not close native menus.");
                return new Controls(0, 0, elapsed % 1 < .2f ? Buttons.Cancel : Buttons.None);
            }
            restTestDone = DiagnosticRestComplete = true; log("HK_REST_FIXTURE finished=True context=Hall next=NativeChallenge"); return default;
        }
        if (hub && !hubStatueDone && UnitySceneManager.GetActiveScene().name == "GG_Workshop")
        {
            if (hubStatueRequested)
            {
                if (Time.realtimeSinceStartup - hubStatueAt < 1 || HeroController.instance.controlReqlinquished) return default;
                hubStatueDone = true; started = Time.realtimeSinceStartup;
                log("HK_HUB_MENU_FIXTURE controlReturned=True");
            }
            else
            {
                var statue = Resources.FindObjectsOfTypeAll<BossStatue>().Single(s => s.gameObject.scene.name == "GG_Workshop" && (s.statueStatePD == EncounterProfile.Selected.Statue || s.dreamStatueStatePD == EncounterProfile.Selected.Statue));
                var inspect = statue.GetComponentsInChildren<PlayMakerFSM>().Single(f => f.name == "Inspect" && f.FsmName == "npc_control");
                float distance = inspect.transform.position.x - pose.X;
                if (Math.Abs(distance) > 0.2f) return new Controls(Math.Sign(distance), 0, Buttons.None);
                if (inspect.ActiveStateName.StartsWith("In Range", StringComparison.Ordinal) && HeroController.instance.CanInteract())
                {
                    hubStatueRequested = true; hubStatueAt = Time.realtimeSinceStartup;
                    log("HK_HUB_MENU_FIXTURE request=True yiX=" + pose.X + " statueX=" + inspect.transform.position.x);
                    // Exercise the same native up-action edge used by physical B.
                    InteractionBinding.ScriptedPressUntil = Time.realtimeSinceStartup + 0.15f;
                }
                return default;
            }
        }
        if (hub && hubStatueDone && UnitySceneManager.GetActiveScene().name == "GG_Workshop")
            return new Controls(-1, 0, Buttons.None);
        if (hub && restFixture && hubStatueDone && !restTestDone && UnitySceneManager.GetActiveScene().name == "GG_Atrium")
        {
            if (restTestAt < 0)
            {
                if (!pose.Grounded || Math.Abs(pose.Y - 60) > 0.2f) return default;
                if (Math.Abs(pose.X - 110.6f) > 0.3f) return new Controls(Math.Sign(110.6f - pose.X), 0, Buttons.None);
                restTestAt = Time.realtimeSinceStartup; log("HK_REST_FIXTURE interaction=True");
            }
            float elapsed = Time.realtimeSinceStartup - restTestAt;
            if (elapsed < 0.2f) return new Controls(0, 0, Buttons.Interact);
            // Simulate holding physical B across the transition from Interact to
            // UI Cancel; the same press must not dismiss the new rest panel.
            if (elapsed < 1) return new Controls(0, 0, Buttons.Cancel);
            if (elapsed >= 2 && elapsed < 2.2f) return new Controls(0, 0, Buttons.Jump);
            if (elapsed >= 4 && elapsed < 4.2f || elapsed >= 7 && elapsed < 7.2f) return new Controls(0, 0, Buttons.Inventory);
            if (elapsed >= 10 && elapsed < 10.3f) return new Controls(0, 0, Buttons.Cancel);
            if (elapsed < 14) return default;
            restTestDone = true; log("HK_REST_FIXTURE finished=True");
        }
        if (restFixture && restTestDone) return default;
        if (contactFixture)
        {
            if (cheatFixture)
            {
                if(EncounterProfile.Selected == EncounterProfile.Grimm && DiagnosticTargets?.Invoke() is ContactFrame balloon && balloon.Crimson) return default;
                if(SessionConfiguration.Current.Has("crimson-probe")
                    && (EncounterProfile.Selected.Family==EncounterProfile.TraitorLord || EncounterProfile.Selected.Family==EncounterProfile.BrokenVessel)
                    && DiagnosticTargets?.Invoke() is ContactFrame incomingCrimson && incomingCrimson.Crimson) {
                    diagnosticAttemptAt=-1;
                    if(Time.realtimeSinceStartup<counterReleaseUntil)return default;
                    var approach=new Bounds(new Vector3(pose.X,pose.Y+pose.Height*.5f,0),new Vector3(pose.Width+.6f,pose.Height+.6f,1));
                    bool imminent=false;
                    foreach(var volume in incomingCrimson.Volumes) {
                        if(!volume.Active || (volume.Traits&AttackTraits.Crimson)==0)continue;
                        float minX=float.PositiveInfinity,minY=minX,maxX=float.NegativeInfinity,maxY=maxX;
                        for(int i=0;i<volume.Points.Length;i+=2){minX=Math.Min(minX,volume.Points[i]);maxX=Math.Max(maxX,volume.Points[i]);minY=Math.Min(minY,volume.Points[i+1]);maxY=Math.Max(maxY,volume.Points[i+1]);}
                        if(maxX>=approach.min.x && minX<=approach.max.x && maxY>=approach.min.y && minY<=approach.max.y){imminent=true;break;}
                    }
                    if(imminent && pose.Vitals?.CounterReady==true){counterReleaseUntil=Time.realtimeSinceStartup+.2f;return default;}
                    return new Controls(0,0,Buttons.Parry);
                }
                if (SessionConfiguration.Current.Has("homing-probe")) return new Controls(1, 0, t % 3f < .2f ? Buttons.Bow : Buttons.None);
                if (SessionConfiguration.Current.Has("crimson-probe") && (EncounterProfile.Selected == EncounterProfile.NightmareGrimm || EncounterProfile.Selected == EncounterProfile.DungDefender || EncounterProfile.Selected == EncounterProfile.WhiteDefender || EncounterProfile.Selected == EncounterProfile.Sly || EncounterProfile.Selected == EncounterProfile.Galien || EncounterProfile.Selected == EncounterProfile.Marmu || EncounterProfile.Selected == EncounterProfile.GreyPrinceZote || EncounterProfile.Selected.Family==EncounterProfile.CrystalGuardian || EncounterProfile.Selected.Family==EncounterProfile.EnragedGuardian || EncounterProfile.Selected.Family==EncounterProfile.OroMato || EncounterProfile.Selected.Family==EncounterProfile.Sheo || EncounterProfile.Selected.ArmorAndHead) && DiagnosticTargets?.Invoke() is ContactFrame grimm && (grimm.Crimson || DiagnosticCounterPrecharge?.Invoke()==true)) {
                    diagnosticAttemptAt=-1;
                    if (DiagnosticSkipCounter?.Invoke()==true) return default;
                    if (EncounterProfile.Selected == EncounterProfile.Sly) {
                        float now = Time.realtimeSinceStartup;
                        if (now < counterReleaseUntil) { slyChargeStarted = -1; return default; }
                        if (slyChargeStarted < 0) slyChargeStarted = now;
                        // A native exhausted/ignored hold needs a new input edge.
                        // Observe the native charge flag; never fabricate readiness.
                        if (now - slyChargeStarted > .5f && pose.Vitals?.CounterCharging != true) {
                            counterReleaseUntil = now + .15f; slyChargeStarted = -1;
                            log("HK_SLY_COUNTER_REARM nativeCharging=False nativeReady=" + (pose.Vitals?.CounterReady??false) + " frame=" + Time.frameCount);
                            return default;
                        }
                    }
                    if (Time.realtimeSinceStartup < counterReleaseUntil) return default;
                    float counterMove=0;
                    bool counterNear=true;
                    if(EncounterProfile.Selected==EncounterProfile.GreyPrinceZote && !grimm.Windup) {
                        float dx=grimm.X-pose.X;
                        counterMove=Math.Abs(dx)>Math.Max(.5f,grimm.Width*.4f)?Math.Sign(dx):0;
                        counterNear=grimm.Y-grimm.Height*.5f<=pose.Y+pose.Height+1.5f;
                    }
                    if (grimm.Crimson && !grimm.Windup && counterNear && pose.Vitals?.CounterReady == true) { counterReleaseUntil = Time.realtimeSinceStartup + .2f; return new Controls(counterMove,0,Buttons.None); }
                    return new Controls(counterMove, 0, Buttons.Parry);
                }
                if (SessionConfiguration.Current.Has("markoth-projectile-probe")) return new Controls(1, 0, t % 3f < (t % 6f < 3f ? .2f : 1.5f) ? Buttons.Bow : Buttons.None);
                if ((EncounterProfile.Selected.Family == EncounterProfile.SoulMaster || EncounterProfile.Selected.Family == EncounterProfile.SoulTyrant)
                    && SessionConfiguration.Current.Has("trace-probe") && !SessionConfiguration.Current.Has("crimson-probe") && t<100) return default;
                if ((EncounterProfile.Selected == EncounterProfile.SoulMaster || EncounterProfile.Selected == EncounterProfile.SoulTyrant) && DiagnosticTargets?.Invoke() is ContactFrame soul && soul.Crimson) {
                    diagnosticAttemptAt=-1;
                    if (Time.realtimeSinceStartup < counterReleaseUntil) return default;
                    if (soul.Crimson && !soul.Windup && pose.Vitals?.CounterReady == true) { counterReleaseUntil = Time.realtimeSinceStartup + .2f; return default; }
                    return new Controls(0, 0, Buttons.Parry);
                }
                if (SessionConfiguration.Current.Has("markoth-shield-probe")) {
                    if (t < 7) return new Controls(1, 0, t % 3f < (t % 6f < 3f ? .2f : 1.5f) ? Buttons.Bow : Buttons.None);
                    var shieldFrame = DiagnosticTargets?.Invoke();
                    if (Time.realtimeSinceStartup < counterReleaseUntil) return default;
                    // A continuous held input times out natively and cannot
                    // start another charge without a fresh press. Arm on the
                    // authored warning, rather than holding between cycles.
                    if (shieldFrame?.Crimson != true) {
                        var markothTarget = shieldFrame?.Summons.FirstOrDefault(s => s.BossBar && s.Active);
                        if (markothTarget == null) return default;
                        float dx = markothTarget.X - pose.X;
                        float cycle = t % 3;
                        float move = cycle < .15f ? Math.Sign(dx) : 0;
                        return new Controls(move, 0, cycle >= .15f && cycle < .35f ? Buttons.Bow : Buttons.None);
                    }
                    if (shieldFrame?.Crimson == true && !shieldFrame.Windup && pose.Vitals?.CounterReady == true) { counterReleaseUntil = Time.realtimeSinceStartup + .2f; return default; }
                    return new Controls(0, 0, Buttons.Parry);
                }
                if (SessionConfiguration.Current.Has("pause-probe")) {
                    int stage = PauseProbeStage?.Invoke() ?? 0;
                    if (stage >= 3) return default;
                    Buttons action = stage == 0 ? Buttons.Attack : stage == 1 ? Buttons.Bow : Buttons.Talisman;
                    return new Controls(1, 0, t % 1.5f < 0.7f ? action : Buttons.None);
                }
                if (EncounterProfile.Selected.ArmorAndHead) {
                    if (counterBossFsm == null) counterBossFsm = Resources.FindObjectsOfTypeAll<PlayMakerFSM>().Single(f => f.gameObject.scene.name == targetScene && f.name == EncounterProfile.Selected.Boss && f.FsmName == "FalseyControl");
                    string phase = counterBossFsm.ActiveStateName;
                    if(SessionConfiguration.Current.Has("stagger-probe") && !armorStandingFixtureComplete && (phase=="Opened" || phase=="Hit")) {
                        armorPlatform??=counterBossFsm.FsmStates.Single(s=>s.Name=="Roll End").Actions.OfType<HutongGames.PlayMaker.Actions.ActivateGameObject>()
                            .Select(a=>FSMUtility.GetSafe(a.gameObject,a)).Single(o=>o!=null && o.name=="FK Terrain Block").GetComponent<BoxCollider2D>();
                        if(armorPlatform==null) throw new InvalidOperationException("Native armor standing fixture lacks authored platform.");
                        if(armorPlatform.enabled && armorPlatform.gameObject.activeInHierarchy) {
                            var b=armorPlatform.bounds;
                            if(armorPlatformAt<0) {armorPlatformAt=Time.realtimeSinceStartup;log("HK_ARMOR_STANDING_FIXTURE_BEGIN nativeInput=True collider="+armorPlatform.GetInstanceID());}
                            if(pose.Grounded && pose.X>=b.min.x && pose.X<=b.max.x && Math.Abs(pose.Y-b.max.y)<.1f) {
                                armorStandingFixtureComplete=true;log("HK_ARMOR_STANDING_FIXTURE_PASS nativeInput=True grounded=True yiY="+pose.Y+" nativeTop="+b.max.y);return default;
                            }
                            if(Time.realtimeSinceStartup-armorPlatformAt>4) throw new InvalidOperationException("Native armor standing fixture did not reach platform within four seconds.");
                            float dx=b.center.x-pose.X;
                            // Preserve a native variable-height jump hold; a
                            // grounded-only one-frame press immediately cuts it.
                            return new Controls(Math.Abs(dx)>.15f?Math.Sign(dx):0,0,(Time.realtimeSinceStartup-armorPlatformAt)%1f<.35f?Buttons.Jump:Buttons.None);
                        }
                    }
                    bool rage = phase == "Rage Jump Antic" || phase == "R Attack Antic" || phase == "Rage Begin" || phase == "Rage" || phase == "Particle Pause";
                    if (rage) {
                        float now = Time.realtimeSinceStartup;
                        if (now < counterReleaseUntil) return default;
                        if (phase == "Rage" && pose.Vitals?.CounterReady == true) {
                            counterReleaseUntil = now + 0.12f;
                            log("HK_MACE_RELEASE_FIXTURE nativeInput=True frame=" + Time.frameCount); return default;
                        }
                        return new Controls(0, 0, Buttons.Parry);
                    }
                }
                if (YiGodhome.Bridge.SessionConfiguration.Current.Has("pv-tuning-probe") && PureVesselCombat.Current is PureVesselCombat pv && pv.ProbeStage < 6) {
                    if (pv.ProbeStage == 2) return new Controls(1, 0, Time.realtimeSinceStartup % 0.4f < 0.15f ? Buttons.Attack : Buttons.None);
                    if (pv.ProbeStage == 4) return new Controls(0, 0, pv.TendrilActive ? Buttons.None : Buttons.Parry);
                    return default;
                }
                bool soloNailmaster=EncounterProfile.Selected.Family==EncounterProfile.OroMato && DiagnosticTargets?.Invoke()?.BossTitle==BossTitleVariant.NailmasterOro;
                if(SessionConfiguration.Current.Has("crimson-probe") && t<110
                    && (EncounterProfile.Selected.Family==EncounterProfile.BrokenVessel || EncounterProfile.Selected.Family==EncounterProfile.TraitorLord)) return default;
                if(EncounterProfile.Selected.Family==EncounterProfile.Sheo && SessionConfiguration.Current.Has("trace-probe")
                    && !SessionConfiguration.Current.Has("crimson-probe") && t<65)return default;
                bool pairedCounterProbe=EncounterProfile.Selected.Family==EncounterProfile.OroMato && SessionConfiguration.Current.Has("crimson-probe");
                bool nailmasterPlacementProbe=EncounterProfile.Selected.Family==EncounterProfile.OroMato
                    && (pairedCounterProbe || SessionConfiguration.Current.Has("invulnerable-probe"));
                if(pairedCounterProbe && !soloNailmaster && t<160) return default;
                // The native Mage Knight cannot choose a valid teleport while
                // the idle counter fixture remains outside its authored arena.
                // Walk in with ordinary input before waiting for a real dash.
                if(kickFixture && EncounterProfile.Selected.Family==EncounterProfile.SoulWarrior && pose.X<41)
                    return new Controls(1,0,Buttons.None);
                if (kickFixture && !soloNailmaster && ((EncounterProfile.Selected != EncounterProfile.NightmareGrimm && EncounterProfile.Selected != EncounterProfile.Sly && EncounterProfile.Selected != EncounterProfile.GreyPrinceZote && EncounterProfile.Selected != EncounterProfile.WhiteDefender && EncounterProfile.Selected.Family!=EncounterProfile.OroMato) || t < 65) || DiagnosticDifficulty.Tier == 2 && !SessionConfiguration.Current.Has("invulnerable-probe")) return default;
                bool reflectionFinish = SessionConfiguration.Current.Has("reflection-finish-probe");
                if (reflectionFinish && PureVesselCombat.Current?.Health <= 100) return default;
                if (pose.Vitals?.TalismanActive == true) return new Controls(0, 0, Buttons.Talisman);
                bool testingSummons = EncounterProfile.Selected == EncounterProfile.Vengefly && HasSummonHit?.Invoke(HitKind.Talisman) != true;
                if (testingSummons && HasSummons?.Invoke() != true) return default;
                var resolved = testingSummons || EncounterProfile.Selected.Roster ? HasSummonHit : HasResolvedHit;
                bool talismanFinish = !reflectionFinish && EncounterProfile.Selected == EncounterProfile.PureVessel && PureVesselCombat.Current?.Health <= 200;
                Buttons test = talismanFinish ? Buttons.Talisman : resolved?.Invoke(HitKind.Melee) != true ? Buttons.Attack
                    : resolved?.Invoke(HitKind.Bow) != true ? Buttons.Bow
                    : resolved?.Invoke(HitKind.Talisman) != true ? Buttons.Talisman : Buttons.Attack;
                if (SessionConfiguration.Current.Has("talisman-traversal-probe")) test = Buttons.Talisman;
                if(pairedCounterProbe || EncounterProfile.Selected.Family==EncounterProfile.OroMato && SessionConfiguration.Current.Has("invulnerable-probe")) test=Buttons.Attack;
                if (EncounterProfile.Selected.Roster) {
                    if (diagnosticFailed) return default;
                    var diagnosticFrame=DiagnosticTargets?.Invoke();
                    bool guideCore=EncounterProfile.Selected.Key==EncounterProfile.Uumuu.Key && SessionConfiguration.Current.Has("trace-probe")
                        && diagnosticFrame?.Summons.Any(s=>s.BossBar && s.Active && s.Health>0 && s.PassiveRepel)==true
                        && diagnosticFrame.Summons.Any(s=>!s.BossBar && s.Active && s.Health>0 && s.HideHealthBar);
                    if(guideCore) test=Buttons.None;
                    else if(EncounterProfile.Selected.Key==EncounterProfile.Uumuu.Key && diagnosticFrame?.Summons.Any(s=>s.BossBar && s.Active && s.Health>0 && s.PassiveRepel)==true) test=Buttons.Attack;
                    var target = diagnosticFrame?.Summons
                        .Where(s => s.Active && s.Health > 0 && DiagnosticEligible?.Invoke(s) != false)
                        .OrderBy(s => EncounterProfile.Selected.Key == EncounterProfile.Uumuu.Key && s.BossBar ? 1 : 0)
                        // The native placement fixture chooses the first live
                        // snapshot target. Do not aim at a different brother's
                        // height while Yi is being positioned beside that one.
                        .ThenBy(s => nailmasterPlacementProbe ? 0 : Math.Abs(s.X - pose.X)).FirstOrDefault();
                    if (target == null) { diagnosticAttemptAt = -1; return default; }
                    if(SessionConfiguration.Current.Has("stagger-probe") && EncounterProfile.Selected == EncounterProfile.NightmareGrimm && target.Health <= 50) test=Buttons.Talisman;
                    long progress = DiagnosticProgress?.Invoke() ?? 0;
                    if (diagnosticAttemptAt < 0 || diagnosticAttempt != test || progress != diagnosticProgress) {
                        diagnosticProgress = progress;
                        diagnosticAttempt = test; diagnosticAttemptAt = Time.realtimeSinceStartup;
                    }
                    if (Time.realtimeSinceStartup - diagnosticAttemptAt > 20) {
                        diagnosticFailed = true;
                        log("HK_TARGET_FIXTURE_FAILED reason=no-progress action=" + test + " replay=False");
                        return default;
                    }
                    if(guideCore) return default;
                    float dx = target.X - pose.X;
                    float cycle = t % (test == Buttons.Attack ? 0.65f : 1.5f);
                    // Native movement turns Yi before the press; no direct facing,
                    // projectile direction or damage overrides are involved.
                    float move = Math.Abs(dx) > (test == Buttons.Bow ? 3 : 1.3f) || cycle < 0.2f ? Math.Sign(dx) : 0;
                    bool aligned = Math.Abs(target.Y - (pose.Y + pose.Height * 0.5f)) < target.Height * 0.5f + 0.5f;
                    if(SessionConfiguration.Current.Has("stagger-probe") && EncounterProfile.Selected == EncounterProfile.NightmareGrimm && target.Health <= 50 && target.Y > pose.Y + pose.Height) {
                        var jump=cycle < .15f || cycle > .35f && cycle < .5f ? Buttons.Jump : Buttons.None;
                        return new Controls(move,0,jump | (aligned && cycle >= .2f ? Buttons.Talisman : Buttons.None));
                    }
                    if (EncounterProfile.Selected.Key == EncounterProfile.Uumuu.Key && !aligned && target.Y > pose.Y + pose.Height && pose.Grounded)
                        return new Controls(move, 0, Buttons.Jump);
                    if((EncounterProfile.Selected.Family==EncounterProfile.SoulMaster || EncounterProfile.Selected.Family==EncounterProfile.SoulTyrant)
                        && !aligned && target.Y>pose.Y+pose.Height) {
                        var jump=cycle<.15f || cycle>.35f && cycle<.5f ? Buttons.Jump : Buttons.None;
                        return new Controls(move,0,jump);
                    }
                    return new Controls(move, 0, aligned && cycle >= 0.2f && cycle < (test == Buttons.Talisman ? 1.45f : 0.5f) ? test : Buttons.None);
                }
                float pulse = t % (test == Buttons.Attack ? 0.65f : 1.5f);
                return new Controls(EncounterProfile.Selected.Roster ? 0 : 1, 0, pulse >= 0.2f && pulse < (test == Buttons.Talisman ? 1.45f : 0.5f) ? test : Buttons.None);
            }
            if (hitFixture && pose.Vitals?.TalismanActive == true) return new Controls(0, 0, Buttons.Talisman);
            if (contactCollider == null)
                foreach (var d in UnityEngine.Object.FindObjectsByType<DamageHero>(FindObjectsSortMode.None))
                    if (d.name == (EncounterProfile.Selected.Family == EncounterProfile.Gruz ? "Hero Damager" : EncounterProfile.Selected.Boss) && d.gameObject.scene.name == targetScene) contactCollider = d.GetComponent<Collider2D>();
            // Approach the moving boss so the death fixture does not idle at an arena edge.
            // Damage still comes solely from native attacks; no defenses or health are changed.
            if (lifecycleFixture && !AllowLifecycleAttack || hub && t >= 20)
                return new Controls(contactCollider == null || Math.Abs(contactCollider.bounds.center.x - pose.X) < 0.5f ? 0 : Math.Sign(contactCollider.bounds.center.x - pose.X), 0, Buttons.None);
            if (EncounterProfile.Selected == EncounterProfile.Xero && t >= 20 && t < 28)
            {
                pitDropped |= pose.Y < 9;
                if (!pitDropped) return new Controls(-1, 0, Buttons.None);
                if (pose.Y < 9.8f || !pose.Grounded) return default;
            }
            if (EncounterProfile.Selected == EncounterProfile.Xero && t >= 20)
            {
                if (pose.Vitals?.Health < 150 && pose.Vitals.Potions > 0 && pose.Grounded && Time.realtimeSinceStartup >= nextHeal)
                { healUntil = Time.realtimeSinceStartup + 1; nextHeal = Time.realtimeSinceStartup + 3; }
                if (Time.realtimeSinceStartup < healUntil) return new Controls(0, 0, Buttons.Heal);
            }
            var yi = new Bounds(new Vector3(pose.X, pose.Y + pose.Height * 0.5f, 0), new Vector3(pose.Width, pose.Height, 1));
            if (crimsonFixture && contactCollider != null)
            {
                float now = Time.realtimeSinceStartup;
                float health = pose.Vitals?.Health ?? 0;
                if (counterHealth >= 0 && health < counterHealth) { counterHeldAt = -1; counterReleaseUntil = now + 0.7f; }
                counterHealth = health;
                if (counterBossFsm == null)
                    foreach (var fsm in contactCollider.GetComponentsInParent<PlayMakerFSM>())
                        if (fsm.FsmName == "Big Fly Control") counterBossFsm = fsm;
                if (now < counterReleaseUntil) return default;
                if (counterHeldAt < 0) counterHeldAt = now;
                if (now - counterHeldAt > 0.8f && pose.Vitals?.CounterCharging != true)
                { counterHeldAt = -1; counterReleaseUntil = now + 0.1f; return default; }
                var approach = yi; approach.Expand(new Vector3(2.5f, 2.5f, 0));
                if (pose.Vitals?.CounterReady == true && counterBossFsm?.ActiveStateName == "Flying" && approach.Intersects(contactCollider.bounds))
                {
                    counterHeldAt = -1; counterReleaseUntil = now + 0.4f;
                    log("HK_CRIMSON_RELEASE_FIXTURE frame=" + Time.frameCount + " nativeInput=True");
                    return default;
                }
                return new Controls(0, 0, Buttons.Parry);
            }
            Collider2D? incoming = contactCollider;
            if (EncounterProfile.Selected == EncounterProfile.Xero)
            {
                incoming = null;
                var approach = yi; approach.Expand(new Vector3(1.5f, 1.5f, 0));
                foreach (var fsm in UnityEngine.Object.FindObjectsByType<PlayMakerFSM>(FindObjectsSortMode.None))
                    if (fsm.FsmName == "xero_nail" && fsm.gameObject.scene.name == targetScene
                        && (fsm.ActiveStateName == "Shoot" || fsm.ActiveStateName == "Returning"))
                    { var shape = fsm.GetComponent<Collider2D>(); if (shape != null && shape.enabled) { var bounds = shape.bounds; var center = bounds.center; center.z = 0; bounds.center = center; if (approach.Intersects(bounds)) { incoming = shape; break; } } }
            }
            bool goopApproaching=false;
            if(cheatFixture && SessionConfiguration.Current.Has("trace-probe")
                && (EncounterProfile.Selected.Family==EncounterProfile.Nosk || EncounterProfile.Selected.Family==EncounterProfile.WingedNosk || EncounterProfile.Selected.Family==EncounterProfile.GodTamer)
                && DiagnosticTargets?.Invoke() is ContactFrame goopFrame) {
                var approach=yi;approach.Expand(new Vector3(.6f,.6f,0));
                foreach(var shot in goopFrame.Volumes) {
                    if(!shot.Active || shot.Category!=AttackCategory.Projectile || !shot.NotifyParry)continue;
                    float minX=float.PositiveInfinity,minY=minX,maxX=float.NegativeInfinity,maxY=maxX;
                    for(int i=0;i<shot.Points.Length;i+=2){minX=Math.Min(minX,shot.Points[i]);maxX=Math.Max(maxX,shot.Points[i]);minY=Math.Min(minY,shot.Points[i+1]);maxY=Math.Max(maxY,shot.Points[i+1]);}
                    if(maxX>=approach.min.x && minX<=approach.max.x && maxY>=approach.min.y && minY<=approach.max.y){goopApproaching=true;break;}
                }
            }
            if (t >= 5 && Time.realtimeSinceStartup >= nextContactParry && (goopApproaching || incoming != null
                && (EncounterProfile.Selected == EncounterProfile.Xero || incoming.bounds.Intersects(yi))))
            {
                contactParryUntil = Time.realtimeSinceStartup + 0.09f; nextContactParry = Time.realtimeSinceStartup + 1.2f;
                log("HK_CONTACT_PARRY_FIXTURE frame=" + Time.frameCount + " nativeInput=True");
            }
            Buttons defense = Time.realtimeSinceStartup < contactParryUntil ? Buttons.Parry : Buttons.None;
            if (defense != Buttons.None) return new Controls(0, 0, defense);
            if (EncounterProfile.Selected == EncounterProfile.Xero && t < 20) return default;
            if (EncounterProfile.Selected == EncounterProfile.Xero && pitDropped && !targetFixture) return default;
            if (t % 12 >= 8 && t % 12 < 11) return new Controls(0, 0, t % 12 >= 9 && t % 12 < 9.12f ? Buttons.Heal : Buttons.None);
            if (hitFixture && HasResolvedHit != null && HasResolvedHit(HitKind.Melee) && HasResolvedHit(HitKind.Bow)
                && (!HasResolvedHit(HitKind.Talisman) || EncounterProfile.Selected == EncounterProfile.Xero && t % 6 >= 3) && pose.Vitals?.Chi >= 1 && contactCollider != null)
            {
                float targetX = contactCollider.bounds.center.x - pose.X;
                float targetY = contactCollider.bounds.center.y - (pose.Y + pose.Height * 0.5f);
                if (EncounterProfile.Selected == EncounterProfile.Xero)
                    targetY = contactCollider.bounds.ClosestPoint(yi.center).y - yi.center.y;
                // Retry a press that native parry/recovery refused. Once native FooAttack
                // starts, the action-state branch above holds continuously to explosion.
                Buttons attempt = Math.Abs(targetY) < 1.2f && Math.Abs(targetX) < 3 && t % 1.5f < 1.3f ? Buttons.Talisman : Buttons.None;
                if (EncounterProfile.Selected == EncounterProfile.Xero && Math.Abs(targetY) >= 1.2f && (t % 2.2f < 0.12f || t % 2.2f >= 0.35f && t % 2.2f < 0.47f)) attempt = Buttons.Jump;
                if (EncounterProfile.Selected != EncounterProfile.Xero && targetY >= 1.2f && t % 1.8f < 0.1f) attempt |= Buttons.Jump;
                return new Controls(Math.Abs(targetX) > 1.3f ? Math.Sign(targetX) : 0, 0, attempt);
            }
            if (targetFixture && contactCollider != null)
            {
                float dx = contactCollider.bounds.center.x - pose.X;
                float move = Math.Abs(dx) > 1.3f ? Math.Sign(dx) : 0;
                Buttons action = t < 20 ? (t % 0.7f < 0.1f ? Buttons.Attack : Buttons.None)
                    : t < 25 ? (t % 1.2f < 0.1f ? Buttons.Bow : Buttons.None)
                    : t < 35 ? Buttons.Talisman : (t % 0.7f < 0.1f ? Buttons.Attack : Buttons.None);
                if (hitFixture && HasResolvedHit != null)
                {
                    if (!HasResolvedHit(HitKind.Melee)) action = t % 0.7f < 0.1f ? Buttons.Attack : Buttons.None;
                    else if (!HasResolvedHit(HitKind.Bow))
                    {
                        move = Math.Abs(dx) > 3 ? Math.Sign(dx) : 0;
                        action = Math.Abs(contactCollider.bounds.center.y - (pose.Y + 0.8f)) < 1.5f && t % 1.2f < 0.12f ? Buttons.Bow : Buttons.None;
                    }
                    else if (!HasResolvedHit(HitKind.Talisman)) action = Buttons.None; // Earn native Qi before attempting the hold.
                    else action = t % 3 < 2.4f ? Buttons.Attack : Buttons.None; // Exercise native charge and release after the three hit paths pass.
                }
                if (contactCollider.bounds.min.y > pose.Y + 2 && (t % 1.8f < 0.1f
                    || EncounterProfile.Selected == EncounterProfile.Xero && t % 1.8f > 0.35f && t % 1.8f < 0.45f)) action |= Buttons.Jump;
                if (EncounterProfile.Selected == EncounterProfile.Xero)
                {
                    float cycle = t % 2.2f;
                    action = cycle < 0.12f || cycle >= 0.35f && cycle < 0.47f ? Buttons.Jump
                        : cycle >= 0.55f && cycle < 0.7f ? (HasResolvedHit?.Invoke(HitKind.Melee) == true && HasResolvedHit?.Invoke(HitKind.Bow) != true ? Buttons.Bow : Buttons.Attack) : Buttons.None;
                }
                return new Controls(move, 0, defense != Buttons.None ? defense : action);
            }
            return new Controls(t % 10 < 5 ? 1 : -1, 0, defense);
        }
        if (hub && t >= 17 && UnitySceneManager.GetActiveScene().name == "GG_Atrium")
        {
            // Explicit scripted regression only: walk into the observed pool,
            // then jump toward its left bank and use native upward ledge climbing.
            if (poolExited)
            {
                if (pose.Vitals == null) return default;
                if (voidHealth < 0) { voidHealth = pose.Vitals.Health; log("HK_HUB_VOID_FIXTURE_BEGIN geometry=" + pose.GeometryFrame); }
                if (pose.Vitals.Health != voidHealth)
                {
                    log("HK_HUB_VOID_FAILED reason=harmless-void-changed-health expected=" + voidHealth + " actual=" + pose.Vitals.Health); return default;
                }
                if (voidHitAt < 0 && pose.Y < 9 && voidRecoveries < 2)
                {
                    voidHitAt = Time.realtimeSinceStartup;
                    log("HK_HUB_VOID_ENTER health=" + voidHealth + " x=" + pose.X + " y=" + pose.Y);
                }
                if (voidHitAt >= 0)
                {
                    if (Time.realtimeSinceStartup - voidHitAt < 2 || !pose.Grounded) return default;
                    voidRecoveries++; voidHitAt = -1; voidRecoveredAt = Time.realtimeSinceStartup; voidReturnX = pose.X;
                    log("HK_HUB_VOID_RECOVERED count=" + voidRecoveries + " x=" + pose.X + " y=" + pose.Y + " geometry=" + pose.GeometryFrame + " health=" + pose.Vitals.Health);
                }
                if (voidRecoveredAt >= 0 && Time.realtimeSinceStartup - voidRecoveredAt < 1)
                    return new Controls(1, 0, Buttons.None);
                if (voidRecoveries >= 2)
                {
                    if (!voidMovementVerified && pose.Grounded && pose.X - voidReturnX > 2)
                    { voidMovementVerified = true; log("HK_HUB_VOID_MOVEMENT_VERIFIED x=" + pose.X + " y=" + pose.Y); }
                    return default;
                }
                return new Controls(-1, 0, Buttons.None);
            }
            if (poolJumpAt < 0)
            {
                if (pose.X < 146) return new Controls(1, 0, Buttons.None);
                if (!pose.Grounded) return default;
                if (pose.Y < 10.4f || pose.Y > 12.8f) { poolExited = true; log("HK_HUB_POOL_FAILED reason=shallow-floor-not-reached y=" + pose.Y); return default; }
                poolJumpAt = Time.realtimeSinceStartup;
                log("HK_HUB_POOL_ENTER x=" + pose.X + " y=" + pose.Y + " nativeInput=True");
            }
            float jumpTime = Time.realtimeSinceStartup - poolJumpAt;
            if (jumpTime > 0.5f && pose.X < 141 && pose.Y >= 12.9f && pose.Grounded)
            {
                poolExited = true;
                log("HK_HUB_POOL_EXIT x=" + pose.X + " y=" + pose.Y + " nativeInput=True");
                return default;
            }
            if (jumpTime > 8) { poolExited = true; log("HK_HUB_POOL_FAILED reason=exit-timeout"); return default; }
            return new Controls(-1, 1, jumpTime < 0.12f || jumpTime >= 0.3f && jumpTime < 0.42f ? Buttons.Jump : Buttons.None);
        }
        if (t >= 1 && t < 1.4f) return new Controls(-1, 0, 0);
        if (t >= 2 && t < 2.4f) return new Controls(1, 0, 0);
        if (t >= 3 && t < 3.15f || t >= 3.4f && t < 3.55f) return new Controls(0, 0, Buttons.Jump);
        if (t >= 5 && t < 5.15f) return new Controls(-1, 0, Buttons.Dodge);
        if (t >= 7 && t < 7.15f) return new Controls(0, 0, Buttons.Parry);
        if (t >= 8 && t < 8.15f) return new Controls(0, 0, Buttons.Attack);
        if (t >= 10 && t < 10.15f) return new Controls(0, 0, Buttons.Heal);
        if (t >= 12 && t < 12.15f) return new Controls(0, 0, Buttons.Bow);
        if (t >= 14 && t < 14.15f) return new Controls(0, 0, Buttons.Talisman);
        if (t >= 16 && !complete) { complete = true; log("HK_INPUT_SCRIPT_COMPLETE frame=" + Time.frameCount + " extendedActions=True"); }
        return default;
    }

    public void Release() { Destroy(); Application.runInBackground = previousBackground; }
}
