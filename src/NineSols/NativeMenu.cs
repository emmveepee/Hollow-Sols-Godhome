using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using YiGodhome.Bridge;
using Pose = YiGodhome.Bridge.Pose;

namespace YiGodhome;

// Native panels retain equipment and ability authority. No UI data is synthesized.
internal static class NativeMenu
{
    private static PlayerControlBindingPanel? controls;
    private static float controlsFocusAt;
    private static bool controlsOpening;
    private static bool controlsInputReleased;
    internal static bool ControlsPixelsVisible;
    internal static bool ControlsVisible=>ControlsInteractive && ControlsPixelsVisible && controls!.GetComponentsInParent<CanvasGroup>(true).All(c=>c.alpha>.95f);
    internal static bool NavigationInteractive {
        get {
            var group=ApplicationUIGroupManager.Instance.currentControlGroup;
            if(group==null || !group.gameObject.activeInHierarchy || !group.interactable || group.bindPanel?.state!=UIPanelState.Show)return false;
            if(ControlsOpen)return ControlsInteractive && controlsInputReleased && group.transform.IsChildOf(controls!.transform);
            return true;
        }
    }
    private static float selectionMissingAt=-1;
    private static InControl.PlayerAction? listeningAction;
    private static InControl.BindingSource? capturedBinding;
    private static float bindingNeutralAt=-1;
    private static bool bindingReleased;
    internal static void ObserveBindingRelease(Controls input) {
        const Buttons metadata=Buttons.HostPaused|Buttons.Keyboard|Buttons.NativePauseMenu|Buttons.DoorEnter|Buttons.StatueEnter|Buttons.EntryRevealed|Buttons.ParryDebug|Buttons.BindingMouse;
        if(ControlsInteractive && !controlsInputReleased && (input.Buttons&~metadata)==0 && Math.Abs(input.X)<.2f && Math.Abs(input.Y)<.2f)
            controlsInputReleased=true;
        if(listeningAction==null || listeningAction.IsListeningForBinding)return;
        bool neutral=(input.Buttons&~metadata)==0 && Math.Abs(input.X)<.2f && Math.Abs(input.Y)<.2f
            && !(capturedBinding!=null && NativeBindingInput.Held(capturedBinding));
        if(!neutral){bindingNeutralAt=-1;bindingReleased=false;return;}
        if(bindingNeutralAt<0)bindingNeutralAt=Time.realtimeSinceStartup;
        bindingReleased=Time.realtimeSinceStartup-bindingNeutralAt>=.15f;
    }
    private static bool bindingFocused;
    private static bool bindingHostCapture;
    private static ControllerMappingButton? pendingBinding;
    private static float bindingFocusAt;
    private static bool bindingDispatching;
    internal static bool PrepareBinding(ControllerMappingButton button,Action<string> log) {
        if(bindingDispatching || controls==null || !button.transform.IsChildOf(controls.transform))return true;
        if(BindingActive)return false;
        if(controls.ForBindingSourceType==InControl.BindingSourceType.DeviceBindingSource)return true;
        var sourceType=(InControl.BindingSourceType)HarmonyLib.AccessTools.Field(typeof(ControllerMappingButton),"forBindingSourceType").GetValue(button);
        bindingHostCapture=(sourceType==InControl.BindingSourceType.KeyBindingSource || sourceType==InControl.BindingSourceType.MouseBindingSource)
            && NativeBindingInput.Begin(sourceType,log);
        // Keep the existing release/initialization fence before native listening.
        pendingBinding=button;bindingFocusAt=Time.realtimeSinceStartup+.25f;
        ApplicationCore.Instance.playerInput.uiActions.ClearInputState();
        if(!bindingHostCapture){NativePairedGame.TryFocusNineSols(log);bindingFocused=true;}
        log("NS_NATIVE_BINDING_FOCUS_PENDING nativeListenerDeferred=True");return false;
    }
    internal static bool IsBindingAction(InControl.PlayerAction action)=>ReferenceEquals(action,listeningAction) && action.IsListeningForBinding;
    internal static bool OwnsPanel(RCGUIPanel panel) {
        bool Tree(Component? root)=>root!=null && (panel.transform.IsChildOf(root.transform) || root.transform.IsChildOf(panel.transform));
        return Tree(controls) || Tree(NativeGameplayUI.Current?.menuUI) || Tree(NativeGameplayUI.Current?.PausePanelUI)
            || NativeRestPoint.Active && Tree(GameCore.Instance.savePanelUiController);
    }
    internal static void BindingSubmitted(ControllerMappingButton button,Action<string> log) {
        if(controls==null || !button.transform.IsChildOf(controls.transform))return;
        var action=(InControl.PlayerAction)HarmonyLib.AccessTools.PropertyGetter(typeof(ControllerMappingButton),"bindAction").Invoke(button,null);
        if(!action.IsListeningForBinding || ReferenceEquals(action,listeningAction))return;
        listeningAction=action;
        capturedBinding=null;bindingNeutralAt=-1;bindingReleased=false;
        var added=action.ListenOptions.OnBindingAdded;
        action.ListenOptions.OnBindingAdded=(owner,source)=>{capturedBinding=source;added?.Invoke(owner,source);};
        // Native Submit disables UI actions. Their held navigation values must
        // not keep moving EventSystem selection and deselect/cancel this row.
        ApplicationCore.Instance.playerInput.uiActions.ClearInputState();
        log("NS_NATIVE_BINDING_CAPTURE_BEGIN action="+action.Name+" nativeListener=True windowFocus="+bindingFocused);
    }
    private static void FinishBinding(Action<string> log,bool cancel=false) {
        if(cancel)pendingBinding=null;
        if(listeningAction==null && (pendingBinding!=null || !bindingFocused && !bindingHostCapture))return;
        if(cancel && listeningAction?.IsListeningForBinding==true)listeningAction.StopListeningForBinding();
        if(listeningAction?.IsListeningForBinding==true || !cancel && listeningAction!=null && !bindingReleased)return;
        if(bindingFocused)NativePairedGame.TryFocusHollowKnight(log);
        log("NS_NATIVE_BINDING_CAPTURE_END action="+(listeningAction?.Name??"pending")+" cancelled="+cancel);
        listeningAction=null;capturedBinding=null;bindingNeutralAt=-1;bindingReleased=false;bindingFocused=false;bindingHostCapture=false;NativeBindingInput.End();
        ApplicationCore.Instance.playerInput.uiActions.ClearInputState();
    }
    private static UIControlGroup? controlsPreviousGroup;
    private static bool controlsPreviousUsable;
    private static bool otherMenuOpen;
    private static float otherMenuFocusAt;
    private static readonly List<GameObject> activatedParents = new();
    private static readonly List<RCGUIPanel> openedParents = new();
    private static RectTransform? detached;
    private static Transform? detachedParent;
    private static int detachedSibling;
    private static Vector2 oldAnchorsMin,oldAnchorsMax,oldPosition,oldSize,oldPivot;
    private static Vector3 oldScale,oldAngles;
    private static void RestoreControlsHierarchy() {
        if(detached!=null && detachedParent!=null) {
            detached.SetParent(detachedParent,false);detached.SetSiblingIndex(detachedSibling);
            detached.anchorMin=oldAnchorsMin;detached.anchorMax=oldAnchorsMax;detached.pivot=oldPivot;
            detached.sizeDelta=oldSize;detached.anchoredPosition=oldPosition;detached.localScale=oldScale;detached.localEulerAngles=oldAngles;
        }
        detached=null;detachedParent=null;
    }
    private static void RestoreControlsParents() {
        controlsPointer=null;
        var manager=ApplicationUIGroupManager.Instance;
        // Unwind only borrowed groups, using native stack restoration. Hiding
        // panels alone leaves them current and makes a later PushShow self-push.
        for(int count=0;count<8;count++) {
            var group=manager.currentControlGroup;
            if(group==null || group==controlsPreviousGroup && controlsPreviousUsable)break;
            bool owned=group==controlsPreviousGroup || controls!=null && group.transform.IsChildOf(controls.transform)
                || openedParents.Any(p=>p!=null && group.transform.IsChildOf(p.transform));
            if(!owned)break;
            if(group.bindPanel.state==UIPanelState.Hide)manager.PopUIPanel();
            else group.PopHide();
            if(manager.currentControlGroup==group)break;
        }
        controlsPreviousGroup=null;
        RestoreControlsHierarchy();
        foreach(var panel in openedParents.AsEnumerable().Reverse())if(panel!=null)panel.HideUIEasy();
        openedParents.Clear();
        foreach(var parent in activatedParents)if(parent!=null)parent.SetActive(false);
        activatedParents.Clear();
    }
    public static bool ControlsOpen => controls != null && (controlsOpening || controls.gameObject.activeInHierarchy && controls.state != UIPanelState.Hide);
    internal static bool ControlsInteractive=>ControlsOpen && controls!.state==UIPanelState.Show && controls.GetComponent<UIControlGroup>().interactable;
    internal static GameObject? ControlsSelection {
        get {
            var selection=UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
            return ControlsOpen && selection!=null && selection.transform.IsChildOf(controls!.transform)?selection:null;
        }
    }
    internal static void CancelBinding(Action<string> log){if(BindingActive)log("NS_BINDING_EXPLICIT_HOST_CLOSE");FinishBinding(log,true);}
    internal static bool BindingActive=>pendingBinding!=null || listeningAction!=null;
    internal static bool ListenerActive=>listeningAction?.IsListeningForBinding==true;
    private sealed class FixtureBindingListener : InControl.BindingSourceListener {
        private readonly InControl.BindingSource source;
        public FixtureBindingListener(InControl.BindingSource source)=>this.source=source;
        public void Reset() { }
        public InControl.BindingSource Listen(InControl.BindingListenOptions options,InControl.InputDevice device)=>source;
    }
    internal static void FixtureAcceptBack(Action<string> log) {
        if(HollowSolsMenuPlugin.Instance?.FixtureCommandsAllowed!=true || !ListenerActive)throw new InvalidOperationException("Binding assignment fixture not authorized/ready.");
        var action=listeningAction!;var owner=action.Owner;var original=owner.SaveData();
        var field=HarmonyLib.AccessTools.Field(typeof(InControl.PlayerAction),"bindingSourceListeners");
        var prior=field.GetValue(action);
        InControl.BindingSource source=controls!.ForBindingSourceType==InControl.BindingSourceType.DeviceBindingSource
            ? new InControl.DeviceBindingSource(InControl.InputControlType.Action2):new InControl.KeyBindingSource(InControl.Key.F12);
        try {
            // Only the physical discovery is substituted. Native DetectBindings
            // owns validation, replacement, completion and the row callbacks.
            field.SetValue(action,new InControl.BindingSourceListener[]{new FixtureBindingListener(source)});
            HarmonyLib.AccessTools.Method(typeof(InControl.PlayerAction),"DetectBindings").Invoke(action,null);
            if(action.IsListeningForBinding || !action.HasBinding(source) || !BindingActive)
                throw new InvalidOperationException("Native Back assignment failed or lost the release fence.");
            Toggle(log);NativeGameplayUI.Current.ShowMenu(PlayerInfoPanelType.Status);
            if(InventoryOpen || !ControlsOpen)throw new InvalidOperationException("Inventory displaced Controls during assignment.");
            log("NS_BINDING_ACCEPTED_BACK source="+source.Name+" nativeReplacement=True releaseFence=True inventoryBlocked=True");
        } finally {field.SetValue(action,prior);owner.LoadData(original);}
    }
    internal static string ControlsTrace {
        get {
            var group=ApplicationUIGroupManager.Instance.currentControlGroup;
            var system=UnityEngine.EventSystems.EventSystem.current;
            var selected=system==null?null:system.currentSelectedGameObject;
            var module=system==null?null:system.currentInputModule;
            var bound=(InControl.InControlInputModule)HarmonyLib.AccessTools.Field(typeof(PlayerInputBinder),"inputModule").GetValue(ApplicationCore.Instance.playerInput);
            string Name(UnityEngine.Object? obj)=>obj==null?"none":obj.name;
            return "state="+controls?.state+" interactive="+ControlsInteractive+" group="+Name(group)+" selected="+Name(selected)
                +" eventSystem="+Name(system)+" module="+Name(module)+" boundModule="+Name(bound)+" boundActive="+(bound!=null && bound.isActiveAndEnabled)
                +" groupInteractive="+(group!=null && group.interactable);
        }
    }
    internal static Canvas? ControlsCanvas => ControlsOpen ? controls!.GetComponentInParent<Canvas>(true) : null;
    private static NativeControlsPointer? controlsPointer;
    internal static void CapturePointer(Camera camera)
    {
        if(ControlsInteractive && !BindingActive)controlsPointer?.Capture(camera);
    }
    internal static void PointPointer(float u,float v,bool click)
    {
        if(!ControlsInteractive || !NavigationInteractive || BindingActive)return;
        controlsPointer?.Point(u,v,click);
    }
    internal static void CloseInventory(Action<string> log)
    {
        if(!InventoryOpen || ControlsOpen || BindingActive)return;
        NativeGameplayUI.Current.menuUI.HideMenu();
        log("NS_INVENTORY_ESCAPE_CLOSE nativeHideMenu=True");
    }
    public static bool InventoryOpen => NativeGameplayUI.Current != null && NativeGameplayUI.Current.menuUI != null
        && NativeGameplayUI.Current.menuUI.state != UIPanelState.Hide;
    public static bool PauseOpen => NativeGameplayUI.Current?.PausePanelUI!=null && NativeGameplayUI.Current.PausePanelUI.isActiveAndEnabled
        && NativeGameplayUI.Current.PausePanelUI.state!=UIPanelState.Hide;
    public static bool Open => ControlsOpen || PauseOpen || InventoryOpen || NativeRestPoint.Active && GameCore.Instance.savePanelUiController.state != UIPanelState.Hide;
    internal static void OpenControls(Action<string> log) {
        if (ControlsOpen) return;
        var type = NativeInputBridge.KeyboardInput ? InControl.BindingSourceType.KeyBindingSource : InControl.BindingSourceType.DeviceBindingSource;
        var panels = Resources.FindObjectsOfTypeAll<PlayerControlBindingPanel>()
            .Where(p => p.gameObject.scene.IsValid() && p.ForBindingSourceType == type).ToArray();
        if (panels.Length != 1) throw new InvalidOperationException("Native controls panel ownership is ambiguous.");
        controlsPreviousGroup=ApplicationUIGroupManager.Instance.currentControlGroup;
        controlsPreviousUsable=controlsPreviousGroup!=null && controlsPreviousGroup.gameObject.activeInHierarchy
            && controlsPreviousGroup.bindPanel!=null && controlsPreviousGroup.bindPanel.state!=UIPanelState.Hide;
        controls = panels[0];
        var canvas = controls.GetComponentInParent<Canvas>(true) ?? throw new InvalidOperationException("Native controls canvas missing.");
        var hierarchy=new List<Transform>();
        for(var parent=controls.transform.parent;parent!=null && parent!=canvas.transform.parent;parent=parent.parent)hierarchy.Add(parent);
        foreach(var parent in hierarchy.AsEnumerable().Reverse()) {
            if(!parent.gameObject.activeSelf){activatedParents.Add(parent.gameObject);parent.gameObject.SetActive(true);}
            var panel=parent.GetComponent<RCGUIPanel>();
            // Native Start deactivates panels still in Hide. Activate through
            // their native lifecycle so first-frame initialization cannot hide
            // the controls child again, and UI input votes remain native.
            if(panel!=null && panel.state==UIPanelState.Hide){openedParents.Add(panel);panel.ShowUIEasy();}
        }
        // The normal settings navigator hides its preceding group. Borrowing
        // its child under that group's CanvasGroup makes subsequent Controls
        // opens interactive but invisible. Keep the native panel on its native
        // canvas as a sibling during the lease, then restore its exact layout.
        detached=(RectTransform)controls.transform;detachedParent=detached.parent;detachedSibling=detached.GetSiblingIndex();
        oldAnchorsMin=detached.anchorMin;oldAnchorsMax=detached.anchorMax;oldPivot=detached.pivot;
        oldPosition=detached.anchoredPosition;oldSize=detached.sizeDelta;oldScale=detached.localScale;oldAngles=detached.localEulerAngles;
        var worldPosition=detached.position;var rectSize=detached.rect.size;
        detached.SetParent(canvas.transform,true);detached.SetAsLastSibling();
        detached.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,rectSize.x);detached.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,rectSize.y);detached.position=worldPosition;
        controlsPointer=new NativeControlsPointer(controls,log);
        log("NS_CONTROLS_CANVAS_LEASE originalParent="+detachedParent.name+" nativeCanvas="+canvas.name+" size="+rectSize);
        // PushShow must own the first reveal and its unlock callback. Calling
        // ShowUIEasy first makes ShowUI return early once already shown, leaving
        // the group locked forever after PushShow's LockControl.
        controlsOpening=true;controlsInputReleased=false;ControlsPixelsVisible=false;
        // Root panels enqueue their default group after 0.2 seconds. Let that
        // native setup finish, then use the controls group's authored navigation
        // transition to hide the preceding menu and select a binding row.
        controlsFocusAt = Time.unscaledTime + 0.3f;
        log("NS_NATIVE_CONTROLS_OPEN nativePanel=True nativeBindings=True hostPausePreserved=True inputType=" + type
            + " state=" + controls.state + " active=" + controls.gameObject.activeInHierarchy + " componentEnabled=" + controls.enabled);
    }
    internal static void PollControls(Action<string> log) {
        bool other=Open && !ControlsOpen;
        if(other && !otherMenuOpen)otherMenuFocusAt=Time.realtimeSinceStartup+.35f;
        otherMenuOpen=other;
        if(!other)otherMenuFocusAt=0;
        if(otherMenuFocusAt>0 && Time.realtimeSinceStartup>=otherMenuFocusAt && !BindingActive) {
            otherMenuFocusAt=0;ApplicationCore.Instance.playerInput.uiActions.ClearInputState();
            NativePairedGame.TryFocusHollowKnight(log);
            log("NS_BORROWED_MENU_FOCUS hostNavigation=True nativeCallbacksSettled=True");
        }
        if(pendingBinding!=null && Time.realtimeSinceStartup>=bindingFocusAt) {
            if(controls==null || !ControlsOpen || Time.realtimeSinceStartup-bindingFocusAt>3){log("NS_BINDING_PENDING_ABORT "+ControlsTrace);FinishBinding(log,true);}
            else if(bindingHostCapture?NativeBindingInput.HostReady:Application.isFocused) {
                var row=pendingBinding;pendingBinding=null;
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(row.gameObject);
                bindingDispatching=true;
                try{row.Submit();}finally{bindingDispatching=false;}
            }
        }
        FinishBinding(log);
        if (ControlsOpen && controlsFocusAt > 0 && Time.unscaledTime >= controlsFocusAt) {
            controlsFocusAt = 0;
            var group = controls!.GetComponent<UIControlGroup>();
            if (group == null) throw new InvalidOperationException("Native controls navigation group missing.");
            controls.gameObject.SetActive(true);
            group.PushShow();
            controlsOpening=false;
            // Native parent activation can restore its application window.
            // Navigation belongs to HK; real key capture takes focus only when
            // the player submits an actual binding row.
            NativePairedGame.TryFocusHollowKnight(log);
            log("NS_NATIVE_CONTROLS_FOCUS nativeGroup=True parentInitializationDrained=True "+ControlsTrace);
        }
        RestoreControlsSelection(log);
        if (controls == null && activatedParents.Count == 0 || ControlsOpen) return;
        if(BindingActive)log("NS_BINDING_PANEL_CLOSED "+ControlsTrace);
        RestoreControlsParents(); controls = null; controlsFocusAt = 0;controlsOpening=false;
        FinishBinding(log,true);
        log("NS_NATIVE_CONTROLS_CLOSED nativeSettingsRetained=True");
    }
    private static void RestoreControlsSelection(Action<string> log) {
        var system=UnityEngine.EventSystems.EventSystem.current;
        var group=ApplicationUIGroupManager.Instance.currentControlGroup;
        // Window focus and parent restoration can clear EventSystem selection
        // after the native reveal callback. A blank selection cannot navigate.
        // Never steal it from a modal child or unlock an unfinished transition.
        if(!ControlsInteractive || BindingActive || system==null || group==null
            || !group.transform.IsChildOf(controls!.transform) || !group.interactable) {selectionMissingAt=-1;return;}
        var selected=system.currentSelectedGameObject;
        if(selected!=null && selected.activeInHierarchy && selected.transform.IsChildOf(group.transform)) {selectionMissingAt=-1;return;}
        if(selectionMissingAt<0){selectionMissingAt=Time.realtimeSinceStartup;return;}
        if(Time.realtimeSinceStartup-selectionMissingAt<.1f)return;
        bool Valid(UnityEngine.UI.Selectable? item)=>item!=null && item.isActiveAndEnabled && item.IsInteractable()
            && item.GetComponentInParent<UIControlGroup>()==group;
        var next=Valid(group.currentSelectable)?group.currentSelectable:Valid(group.defaultSelectable)?group.defaultSelectable
            :group.GetComponentsInChildren<UnityEngine.UI.Selectable>().FirstOrDefault(Valid);
        if(next==null)return;
        next.Select();selectionMissingAt=-1;
        log("NS_NATIVE_CONTROLS_SELECTION_RESTORED nativeRevealUnlocked=True "+ControlsTrace);
    }
    internal static void FixtureRootBackGlyph(Action<string> log) {
        if(HollowSolsMenuPlugin.Instance?.FixtureCommandsAllowed!=true || !NativeRestPoint.Active)throw new InvalidOperationException("Unauthorized Root prompt fixture.");
        var labels=GameCore.Instance.savePanelUiController.ParentCanvas.GetComponentsInChildren<InputPromptUILabel>(true)
            .Where(l=>l.gameObject.activeInHierarchy && l.data?.FirstActionData?.actionSetType==ActionSetType.UI && l.data.FirstActionData.Name=="Cancel").Take(16).ToArray();
        if(labels.Length==0)throw new InvalidOperationException("Native Root Back prompt not found.");
        var actions=ApplicationCore.Instance.playerInput.gameplayActions;
        var bytes=actions.SaveData();var old=actions.LastInputType;
        try {
            actions.Hack.ClearBindings();actions.Hack.AddBinding(new InControl.DeviceBindingSource(InControl.InputControlType.Action4));
            actions.LastInputType=InControl.BindingSourceType.DeviceBindingSource;
            foreach(var label in labels) {
                if(!ReferenceEquals(label.data.FirstActionData.Action,actions.Hack))throw new InvalidOperationException("Root Back did not resolve the native rebound action.");
                HarmonyLib.AccessTools.Method(typeof(InputPromptUILabel),"UpdateView").Invoke(label,null);
                var expected=PlayerInputBinder.Instance.InputBindingIconSetting.FindIcon(actions.Hack,InputBindingGameSetting.ScenarioSet.DefaultAction);
                if(expected==null || label.iconImage==null || label.iconImage.sprite!=expected)throw new InvalidOperationException("Native Root Back glyph did not follow Y.");
                log("NS_ROOT_BACK_GLYPH_PASS nativeLabel=True binding=Y sprite="+expected.name);
            }
        } finally {actions.LoadData(bytes);actions.LastInputType=old;foreach(var label in labels)HarmonyLib.AccessTools.Method(typeof(InputPromptUILabel),"UpdateView").Invoke(label,null);}
    }
    internal static void CloseControls(Action<string> log) {
        FinishBinding(log,true);
        if (controls != null) controls.HideUIEasy();
        RestoreControlsParents(); controls = null; controlsFocusAt = 0;controlsOpening=false;
    }
    internal static void OpenPause(Action<string> log) {
        if(NativeGameplayUI.Current?.PausePanelUI==null || PauseOpen)return;
        NativeGameplayUI.Current.PausePanelUI.ShowUIEasy();log("NS_NATIVE_PAUSE_OPEN nativePanel=True hostPausePreserved=True");
    }
    public static void Toggle(Action<string> log)
    {
        if(ControlsOpen || BindingActive)return;
        var ui = NativeGameplayUI.Current;
        if (ui == null || ui.menuUI == null) return;
        if (InventoryOpen) ui.menuUI.HideMenu();
        else if (NativeRestPoint.Active) HarmonyLib.AccessTools.Method(typeof(SavePanel), "ShowStatus").Invoke(GameCore.Instance.savePanelUiController, null);
        else ui.ShowMenu(PlayerInfoPanelType.Status);
        log("NS_MENU_TOGGLE native=True state=" + ui.menuUI.state + " available=" + ui.IsTabMenuAvailable.CurrentValue);
    }
}

internal sealed class NativeMenuCapture : IDisposable
{
    private readonly NativeMenuBoundary boundary;
    private readonly VisualEndpoint endpoint;
    private readonly Guid session;
    private readonly Camera source;
    private readonly RenderTexture target;
    private readonly Texture2D pixels;
    private float next;
    private bool lastOpen;
    private readonly Action<string> log;
    private int openFrames;
    private int restFrames;
    private float measuredAt;
    private int measuredFrames;
    private double captureMs;
    private bool boundaryChecked;
    private CanvasGroup? controlsHudMask;
    private bool ownsHudMask;
    public NativeMenuCapture(VisualEndpoint endpoint, Guid session, Action<string> log, Func<Guid>? detailedId=null, Action<string>? detail=null)
    {
        this.endpoint = endpoint; this.session = session; this.log = log;
        boundary = new NativeMenuBoundary(log,detailedId,detail);
        source = NativeGameplayUI.Current.menuUI.ParentCanvas.worldCamera;
        if (source == null) source = NativeGameplayUI.Current.menuUI.GetComponentInParent<Camera>();
        if (source == null) throw new InvalidOperationException("Native UI camera missing.");
        log("NS_MENU_CAMERA path=" + NativeRoomIsolation.PathOf(source.transform) + " mode=" + NativeGameplayUI.Current.menuUI.ParentCanvas.renderMode);
        int height = Math.Min(1080, (int)Math.Sqrt(VisualCodec.MaxHudPixels / source.aspect));
        int width = Math.Min(VisualCodec.MaxHudSide, (int)(height * source.aspect));
        target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32); target.Create();
        pixels = new Texture2D(width, height, TextureFormat.RGBA32, false);
    }
    public void Capture(Pose pose)
    {
        NativeMenu.PollControls(log);
        NativeRestPoint.RefreshOptions();
        bool open = NativeMenu.Open;
        if (Time.realtimeSinceStartup < next && open == lastOpen) return;
        float interval = open ? 1f / 60 : 0.1f;
        next = Math.Max(next + interval, Time.realtimeSinceStartup);
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        var frame = endpoint.RentFrame(open ? pixels.width * pixels.height * 4 : 4);
        if (open)
        {
            var pauseCanvas=NativeMenu.ControlsCanvas ?? (NativeMenu.PauseOpen?NativeGameplayUI.Current.PausePanelUI.ParentCanvas:null);
            var captureCamera=pauseCanvas?.worldCamera??source;
            var canvasMode=pauseCanvas?.renderMode??RenderMode.ScreenSpaceCamera;
            var canvasCamera=pauseCanvas?.worldCamera;
            var old = RenderTexture.active;
            var oldTarget = captureCamera.targetTexture;
            var oldClear = captureCamera.clearFlags; var oldColor = captureCamera.backgroundColor;
            float hudAlpha=1;
            if(NativeMenu.ControlsOpen) {
                var playerUI=NativeGameplayUI.Current.PlayerUI;
                if(controlsHudMask==null && playerUI!=null) {
                    controlsHudMask=playerUI.GetComponent<CanvasGroup>();ownsHudMask=controlsHudMask==null;
                    if(controlsHudMask==null)controlsHudMask=playerUI.AddComponent<CanvasGroup>();
                }
                if(controlsHudMask!=null){hudAlpha=controlsHudMask.alpha;controlsHudMask.alpha=0;}
            }
            try
            {
                // Screen-space canvases are associated with the native camera;
                // copying camera settings does not associate their geometry.
                if(pauseCanvas!=null && canvasMode==RenderMode.ScreenSpaceOverlay){pauseCanvas.renderMode=RenderMode.ScreenSpaceCamera;pauseCanvas.worldCamera=captureCamera;}
                captureCamera.targetTexture = target;
                captureCamera.clearFlags = CameraClearFlags.SolidColor; captureCamera.backgroundColor = Color.clear;
                captureCamera.Render(); RenderTexture.active = target;
                NativeMenu.CapturePointer(captureCamera);
                pixels.ReadPixels(new Rect(0, 0, pixels.width, pixels.height), 0, 0, false); pixels.Apply(false, false);
                pixels.GetRawTextureData<byte>().CopyTo(frame.Rgba);
                if (NativeRestPoint.Active && ++restFrames == 30 && YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures)
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!, "native-rest-menu.png"), pixels.EncodeToPNG());
                if (++openFrames == 90 && YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures)
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!, "native-menu.png"), pixels.EncodeToPNG());
            }
            catch { frame.ReleasePixels(); throw; }
            finally { captureCamera.targetTexture = oldTarget; captureCamera.clearFlags = oldClear; captureCamera.backgroundColor = oldColor;
                if(NativeMenu.ControlsOpen && controlsHudMask!=null)controlsHudMask.alpha=hudAlpha;
                if(pauseCanvas!=null){pauseCanvas.renderMode=canvasMode;pauseCanvas.worldCamera=canvasCamera;} RenderTexture.active = old; }
        }
        else Array.Clear(frame.Rgba, 0, frame.Rgba.Length);
        frame.Session = session; frame.GeometryFrame = pose.GeometryFrame; frame.NativeFrame = Time.frameCount;
        frame.CaptureTick = System.Diagnostics.Stopwatch.GetTimestamp(); frame.Width = open ? pixels.width : 1; frame.Height = open ? pixels.height : 1;
        frame.WorldWidth = frame.WorldHeight = 1;
        frame.ControlsMenu=NativeMenu.ControlsOpen;
        frame.ControlsBinding=frame.ControlsMenu && NativeMenu.BindingActive;
        frame.ControlsMouseCapture=frame.ControlsBinding && NativeBindingInput.MouseRequested;
        frame.InventoryMenu=open && NativeMenu.InventoryOpen && !frame.ControlsMenu && !NativeMenu.PauseOpen;
        frame.RootMenu=open && NativeRestPoint.Active && !NativeMenu.ControlsOpen && !NativeMenu.InventoryOpen && !NativeMenu.PauseOpen;
        if(frame.ControlsMenu && NativeMenu.ControlsInteractive && HollowSolsMenuPlugin.Instance?.FixtureCommandsAllowed==true) {
            int bright=0;
            for(int y=frame.Height/4;y<frame.Height*3/4;y++)for(int x=frame.Width/4;x<frame.Width*3/4;x++) {
                int i=(y*frame.Width+x)*4;
                if(frame.Rgba[i+3]>128 && frame.Rgba[i]+frame.Rgba[i+1]+frame.Rgba[i+2]>200)bright++;
            }
            NativeMenu.ControlsPixelsVisible=bright>128;
        } else NativeMenu.ControlsPixelsVisible=false;
        endpoint.PublishPixels(frame);
        if (open)
        {
            captureMs += (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            measuredFrames++;
            if (Time.realtimeSinceStartup - measuredAt >= 2)
            {
                log("NS_MENU_CADENCE frames=" + measuredFrames + " seconds=" + (Time.realtimeSinceStartup - measuredAt)
                    + " captureMs=" + captureMs.ToString("F2") + " width=" + pixels.width + " height=" + pixels.height);
                measuredFrames = 0; captureMs = 0; measuredAt = Time.realtimeSinceStartup;
            }
            if (!boundaryChecked && NativeMenu.InventoryOpen && YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures)
            {
                boundaryChecked = true;
                foreach (var tab in NativeGameplayUI.Current.menuUI.GetComponentsInChildren<UITabsItem>(true))
                    if (tab.OverridePanelType == null && (tab.PanelType == PlayerInfoPanelType.MemoryChallenge || tab.PanelType == PlayerInfoPanelType.TeleportPanel) && tab.IsAllValid)
                        throw new InvalidOperationException("Travel tab remained valid.");
                NativeGameplayUI.Current.menuUI.GoToTab(PlayerInfoPanelType.MemoryChallenge);
                var replayButtons = Resources.FindObjectsOfTypeAll<ChallengeUIButton>();
                foreach (var button in replayButtons) button.Submit();
                // Hidden Battle tabs need not instantiate their pooled buttons.
                // A disabled, unbound diagnostic button must return before even
                // reading bindData, let alone autosaving or changing scenes.
                var fixture = new GameObject("YiGodhome Blocked Replay Fixture"); fixture.SetActive(false);
                try { fixture.AddComponent<ChallengeUIButton>().Submit(); }
                finally { UnityEngine.Object.Destroy(fixture); }
                log("NS_MENU_BOUNDARY_PROBE invalidTabsRejected=True replaySubmitAttempts=" + (replayButtons.Length + 1));
            }
        }
        else { measuredAt = Time.realtimeSinceStartup; measuredFrames = 0; captureMs = 0; }
        if (open != lastOpen) log("NS_MENU_PRESENTATION open=" + open + " geometry=" + pose.GeometryFrame);
        lastOpen = open;
    }
    public void Dispose() { NativeMenu.CloseControls(log); if(ownsHudMask && controlsHudMask!=null)UnityEngine.Object.Destroy(controlsHudMask); boundary.Dispose(); target.Release(); UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(pixels); }
}
