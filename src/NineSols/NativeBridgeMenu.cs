using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using HollowSols.Foundation;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using TMPro;

namespace YiGodhome;

internal sealed class NativeBridgeMenu : IDisposable
{
    private readonly HollowSolsMenuPlugin owner;
    private readonly Action<string> log;
    private StartMenuLogic? main;
    private UIControlButton? bridge;
    private GameObject? bridgeRow, panel;
    private UIControlButton? template;
    private UIControlGroup? activePanelGroup;
    private readonly List<(CanvasGroup Group, float Alpha, bool Input, bool Rays)> hidden = new List<(CanvasGroup, float, bool, bool)>();
    private readonly List<GameObject> owned = new List<GameObject>();
    private readonly Harmony harmony = new Harmony("local.hollowsols.ns.menu.rows");
    private static Action<string>? fixtureLog;
    internal void FixtureDiagnostics()
    {
        fixtureLog = log;
        foreach (string method in new[] { "LoadAllSaveSlotMetaForMainMenu", "ReadMetaInSave", "ReadSaveFileAndBackup" })
            harmony.Patch(AccessTools.Method(typeof(SaveManager), method), prefix: new HarmonyMethod(typeof(NativeBridgeMenu), nameof(FixtureSaveEnter)));
    }
    internal void FixtureBack()
    {
        if (activePanelGroup == null) throw new InvalidOperationException("Native bridge dialog missing.");
        activePanelGroup.OnCancel.Invoke();
    }
    private static void FixtureSaveEnter(System.Reflection.MethodBase __originalMethod) => fixtureLog?.Invoke("NS_MENU_METADATA_ENTER method=" + __originalMethod.Name);
    private static readonly Dictionary<UIControlButton, (Action Act, Action Back)> Actions = new Dictionary<UIControlButton, (Action, Action)>();
    internal NativeBridgeMenu(HollowSolsMenuPlugin owner, Action<string> log)
    {
        this.owner = owner; this.log = log;
        harmony.Patch(AccessTools.Method(typeof(UIControlButton), "SubmitImplementation"), prefix: new HarmonyMethod(typeof(NativeBridgeMenu), nameof(Submit)));
        harmony.Patch(AccessTools.Method(typeof(UIInteractOptionButton), "SubmitImplementation"), prefix: new HarmonyMethod(typeof(NativeBridgeMenu), nameof(Submit)));
        harmony.Patch(AccessTools.Method(typeof(UIControlButton), "OnCancel"), prefix: new HarmonyMethod(typeof(NativeBridgeMenu), nameof(Cancel)));
    }
    private static bool Submit(UIControlButton __instance)
    { if (!Actions.TryGetValue(__instance, out var action)) return true; action.Act(); return false; }
    private static bool Cancel(UIControlButton __instance)
    { if (!Actions.TryGetValue(__instance, out var action)) return true; __instance.cancelSound?.Play(); action.Back(); return false; }
    internal void SceneLoaded(Scene scene) { if (scene.name == "TitleScreenMenu") Bind(); }
    internal void Update()
    {
        foreach (var entry in modalText) if (entry.Key != null && entry.Key.text != entry.Value) entry.Key.text = entry.Value;
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "TitleScreenMenu") return;
        if (main == null) Bind();
    }
    internal bool FixtureReady => bridge != null && bridge.isActiveAndEnabled && bridge.SelectableComp.IsInteractable() && main != null && (bool)AccessTools.Field(typeof(StartMenuLogic), "CanInteract").GetValue(main);
    internal bool NativeReturnReady => main != null && main.startControlGroup != null
        && SceneManager.GetActiveScene().name == "TitleScreenMenu"
        && (bool)AccessTools.Field(typeof(StartMenuLogic), "CanInteract").GetValue(main);
    internal void FixtureEvidence()
    {
        if (main == null) { log("NS_MENU_SCOPED_EVIDENCE main=False"); return; }
        var save = SaveManager.Instance;
        log("NS_MENU_SCOPED_START canInteract=" + AccessTools.Field(typeof(StartMenuLogic), "CanInteract").GetValue(main) + " saveLock=" + AccessTools.Field(typeof(SaveManager), "_saveLock").GetValue(save) + " timeScale=" + Time.timeScale + " metadata=" + string.Join(";", save.saveMetaDataList.Select(m => m == null ? "null" : m.exist.ToString())));
        foreach (var row in main.startControlGroup.GetComponentsInChildren<UIControlButton>(true))
            log("NS_MENU_SCOPED_ROW name=" + row.name + " parent=" + row.transform.parent?.name + " type=" + row.GetType().Name + " activeSelf=" + row.gameObject.activeSelf + " active=" + row.gameObject.activeInHierarchy
                + " enabled=" + row.enabled + " selectable=" + (row.SelectableComp != null) + " interactable=" + (row.SelectableComp?.IsInteractable() == true)
                + " y=" + row.transform.localPosition.y + " groups=" + string.Join(";", row.GetComponentsInParent<CanvasGroup>(true).Select(g => g.name + ":" + g.alpha + ":" + g.interactable + ":" + g.gameObject.activeInHierarchy)));
    }
    internal void FixtureSubmitBridge() { if (bridge == null) throw new InvalidOperationException("Native bridge row missing."); owner.StartCoroutine(FixtureHighlightThenSubmit()); }
    private System.Collections.IEnumerator FixtureHighlightThenSubmit()
    {
        string folder = System.IO.Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!;
        template!.SelectableComp.Select(); yield return new WaitForSecondsRealtime(.5f);
        FixtureSelectionState(template);
        Color nativeSelected = template.GetComponentsInChildren<TMP_Text>(true).Single(t => t.gameObject.activeInHierarchy && !string.IsNullOrEmpty(t.text)).color;
        FixturePixels(System.IO.Path.Combine(folder, "native-exit-selected.png")); yield return new WaitForSecondsRealtime(.5f);
        bridge!.SelectableComp.Select(); yield return new WaitForSecondsRealtime(.5f);
        FixtureSelectionState(bridge);
        AssertNativeHighlight(nativeSelected);
        FixturePixels(System.IO.Path.Combine(folder, "bridge-selected.png")); yield return new WaitForSecondsRealtime(.5f);
        FixtureNavigation();
        var priorCursor = Cursor.visible; var priorLock = Cursor.lockState;
        EventSystem.current.SetSelectedGameObject(null); InputExtension.RCGInput.SetCursorVisible(true);
        ExecuteEvents.Execute(bridge.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
        yield return new WaitForSecondsRealtime(.5f);
        if (EventSystem.current.currentSelectedGameObject != bridge.gameObject) throw new InvalidOperationException("Native bridge hover did not select its row.");
        AssertNativeHighlight(nativeSelected); FixturePixels(System.IO.Path.Combine(folder, "bridge-hovered.png"));
        yield return new WaitForSecondsRealtime(.5f); Cursor.visible = priorCursor; Cursor.lockState = priorLock;
        log("NS_NATIVE_BRIDGE_HIGHLIGHT_PASS nativeAnimation=True selectionAndHover=True");
        bridge.Submit();
    }
    private void AssertNativeHighlight(Color expected)
    {
        var color = bridge!.GetComponentsInChildren<TMP_Text>(true).Single(t => t.gameObject.activeInHierarchy && !string.IsNullOrEmpty(t.text)).color;
        if (Mathf.Abs(color.r - expected.r) + Mathf.Abs(color.g - expected.g) + Mathf.Abs(color.b - expected.b) + Mathf.Abs(color.a - expected.a) > .01f) throw new InvalidOperationException("Bridge highlight differs from the native selected Exit row.");
    }
    private void FixtureNavigation()
    {
        var rows = main!.startControlGroup.GetComponentsInChildren<UIControlButton>(true).Where(b => b.isActiveAndEnabled && b.SelectableComp.IsInteractable()).ToArray();
        foreach (var direction in new[] { MoveDirection.Up, MoveDirection.Down }) {
            bridge!.SelectableComp.Select(); var seen = new HashSet<GameObject>();
            for (int i = 0; i < rows.Length; i++) {
                var selected = EventSystem.current.currentSelectedGameObject;
                if (selected == null || !seen.Add(selected)) throw new InvalidOperationException("Native main-menu navigation skipped or duplicated a row.");
                ExecuteEvents.Execute(selected, new AxisEventData(EventSystem.current) { moveDir = direction, moveVector = direction == MoveDirection.Up ? Vector2.up : Vector2.down }, ExecuteEvents.moveHandler);
            }
            if (EventSystem.current.currentSelectedGameObject != bridge.gameObject) throw new InvalidOperationException("Native navigation did not wrap through the bridge row.");
        }
        log("NS_NATIVE_BRIDGE_NAVIGATION_PASS rows=" + rows.Length + " directions=Up,Down");
    }
    private void FixtureSelectionState(UIControlButton row)
    {
        var selectable = row.SelectableComp;
        log("NS_ROW_SELECTION_STATE row=" + row.name + " type=" + selectable.GetType().Name + " transition=" + selectable.transition + " graphic=" + selectable.targetGraphic?.name + " color=" + selectable.targetGraphic?.color + " selectedColor=" + selectable.colors.selectedColor + " state=" + AccessTools.Property(typeof(Selectable), "currentSelectionState").GetValue(selectable));
        foreach (var text in row.GetComponentsInChildren<TMP_Text>(true)) log("NS_ROW_LABEL row=" + row.name + " label=" + text.name + " active=" + text.gameObject.activeInHierarchy + " color=" + text.color);
    }
    internal void FixturePixels(string path) => UnityEngine.ScreenCapture.CaptureScreenshot(path);
    internal void FixtureSelectSlot(SlotSummary slot) => throw new InvalidOperationException("NS cannot select a slot.");
    internal void FixtureVanillaSelection() { Close(); owner.StartCoroutine(FixtureVanillaAfterClose()); }
    private System.Collections.IEnumerator FixtureVanillaAfterClose()
    {
        var row = main!.startControlGroup.GetComponentsInChildren<UIControlButton>(true).Single(b => b.name == "MainMenuButton_Slot");
        float deadline = Time.realtimeSinceStartup + 5;
        while (!row.isActiveAndEnabled || !row.SelectableComp.IsInteractable()) {
            if (Time.realtimeSinceStartup > deadline) { owner.GameplayFailed("NS_NATIVE_MENU_FOCUS_NOT_RESTORED", null); yield break; }
            yield return null;
        }
        row.SelectableComp.Select(); row.Submit();
    }
    internal void FixtureChoose(string text) => throw new InvalidOperationException("HK owns creation choices.");
    private void Bind()
    {
        var next = UnityEngine.Object.FindFirstObjectByType<StartMenuLogic>(); if (next == null || next == main && bridge != null) return;
        if (next.startControlGroup == null) return;
        ReleaseRows(); main = next;
        var buttons = next.startControlGroup.GetComponentsInChildren<UIControlButton>(true).Where(b => b != null).ToArray();
        if (buttons.Length < 2 || buttons.Length > 16) throw new InvalidOperationException("Native NS main-menu row contract changed.");
        template = buttons.Single(b => b.name == "MainMenuButton_Exit");
        bridge = Clone(template, template.transform.parent!, "Hollow Sols Bridge", owner.ActivateBridge, owner.CancelBridge, out bridgeRow);
        bridgeRow.transform.SetAsLastSibling();
        AccessTools.Field(typeof(UIControlGroup), "_allSelectables").SetValue(next.startControlGroup, next.startControlGroup.GetComponentsInChildren<Selectable>(true));
        var navigation = template.SelectableComp.navigation; navigation.mode = Navigation.Mode.Explicit; navigation.selectOnDown = bridge.SelectableComp; template.SelectableComp.navigation = navigation;
        var first = buttons.Single(b => b.name == "MainMenuButton_Slot").SelectableComp;
        var top = first.navigation; top.selectOnUp = bridge.SelectableComp; first.navigation = top;
        bridge.SelectableComp.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = template.SelectableComp, selectOnDown = first };
        log("NS_BRIDGE_MAIN_ROW_BOUND nativeFont=True nativeArtwork=True nativeSoundNavigation=True");
    }
    private UIControlButton Clone(UIControlButton source, Transform parent, string text, Action action, Action back, out GameObject root)
    {
        var staging = new GameObject("Hollow Sols inactive native row"); staging.SetActive(false);
        root = UnityEngine.Object.Instantiate(source.gameObject, staging.transform, false); root.SetActive(false); root.transform.SetParent(parent, false); UnityEngine.Object.Destroy(staging);
        root.name = "Hollow Sols native row";
        var button = root.GetComponent<UIControlButton>() ?? root.GetComponentInChildren<UIControlButton>(true);
        if (button == null) throw new InvalidOperationException("NS native row behavior missing.");
        var selectable = button.GetComponent<Selectable>() ?? button.GetComponentInChildren<Selectable>(true)
            ?? throw new InvalidOperationException("NS native row selectable missing.");
        if (Guid.TryParse(Environment.GetEnvironmentVariable("HOLLOWSOLS_MENU_FIXTURE"), out _)) {
            log("NS_ROW_NATIVE_COMPONENTS names=" + string.Join(";", root.GetComponentsInChildren<MonoBehaviour>(true).Where(c => c != null).Select(c => c.GetType().Name).Take(24)));
            foreach (var events in new[] { button.OnSelectedEvent, button.OnDeSelectedEvent }) for (int i = 0; i < events.GetPersistentEventCount(); i++) log("NS_ROW_NATIVE_SELECTION_CALLBACK target=" + events.GetPersistentTarget(i)?.name + " type=" + events.GetPersistentTarget(i)?.GetType().Name + " method=" + events.GetPersistentMethodName(i));
            foreach (var binder in root.GetComponentsInChildren<UIAbstractValueBinder>(true)) log("NS_ROW_NATIVE_BINDER type=" + binder.GetType().Name + " field=" + binder.fieldName);
            var animator = (Animator)AccessTools.Field(typeof(UIControlButton), "_animator").GetValue(button);
            log("NS_ROW_NATIVE_ANIMATOR present=" + (animator != null) + " owned=" + (animator != null && animator.transform.IsChildOf(root.transform)));
            log("NS_ROW_NATIVE_SELECTION receiver=" + button.name + " selectable=" + selectable.name + " sameObject=" + (button.gameObject == selectable.gameObject));
            foreach (var trigger in root.GetComponentsInChildren<EventTrigger>(true)) foreach (var entry in trigger.triggers) for (int i = 0; i < entry.callback.GetPersistentEventCount(); i++) log("NS_ROW_NATIVE_EVENT event=" + entry.eventID + " target=" + entry.callback.GetPersistentTarget(i)?.name + " method=" + entry.callback.GetPersistentMethodName(i));
        }
        AccessTools.Field(typeof(UIControlButton), "_button").SetValue(button, selectable);
        // Native Auto fields are runtime caches, not serialized clone state.
        // Rebind both owners before Awake/OnEnable so native selection can wake
        // the animator after its idle optimization disables it.
        var nativeAnimator = root.GetComponent<Animator>() ?? throw new InvalidOperationException("Native row animator missing.");
        AccessTools.Field(typeof(UIControlButton), "_animator").SetValue(button, nativeAnimator);
        var automatic = AccessTools.Field(typeof(UIControlButton), "_autoDisableAnimator");
        var nativeAutomatic = root.GetComponent(automatic.FieldType) ?? throw new InvalidOperationException("Native row animation owner missing.");
        automatic.SetValue(button, nativeAutomatic);
        AccessTools.Field(automatic.FieldType, "_animator").SetValue(nativeAutomatic, nativeAnimator);
        button.clickToShowGroup = null; button.clickToPlayDialogue = null; button.onSubmit = new UnityEngine.Events.UnityEvent();
        foreach (var click in root.GetComponentsInChildren<Button>(true)) click.onClick = new Button.ButtonClickedEvent();
        foreach (var trigger in root.GetComponentsInChildren<EventTrigger>(true)) UnityEngine.Object.DestroyImmediate(trigger);
        button.OnSelectedEvent = new UnityEngine.Events.UnityEvent<UIControlButton>(); button.OnDeSelectedEvent = new UnityEngine.Events.UnityEvent<UIControlButton>();
        AccessTools.Field(typeof(UIControlButton), "_activateConditions").SetValue(button, Array.Empty<AbstractConditionComp>());
        foreach (string name in new[] { "_onSubmitActions", "_submitFailReasonProviders" }) {
            var field = AccessTools.Field(typeof(UIControlButton), name); field.SetValue(button, Array.CreateInstance(field.FieldType.GetElementType()!, 0));
        }
        AccessTools.Field(typeof(UIControlButton), "_submitFailReasonProvider").SetValue(button, null);
        foreach (var addOn in root.GetComponentsInChildren<MonoBehaviour>(true).Where(c => c != null && c.GetType().GetInterfaces().Any(i => i.Name == "ISubmitAddOn"))) UnityEngine.Object.DestroyImmediate(addOn);
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true)) {
            if (component == null) continue;
            if (component.GetType().Name.Contains("Localiz", StringComparison.OrdinalIgnoreCase)) UnityEngine.Object.DestroyImmediate(component);
            else if (component is UIAbstractValueBinder) component.enabled = false;
        }
        var label = root.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault(t => !string.IsNullOrEmpty(t.text)) ?? throw new InvalidOperationException("NS native typography missing.");
        foreach (var nativeLabel in root.GetComponentsInChildren<TextMeshProUGUI>(true)) if (!string.IsNullOrEmpty(nativeLabel.text)) nativeLabel.text = text;
        Actions[button] = (action, back); root.SetActive(true); return button;
    }
    internal void Waiting(string text) => Dialog(text, new[] { ("Go Back", (Action)owner.CancelBridge) });
    internal void Notice(string text, Action done) => Dialog(text, new[] { ("Continue", done) });
    internal void FailureNotice(string text, Action report, Action done) => Dialog(text, new[] { ("Prepare Bug Report", report), ("Continue", done) });
    internal void FailureShutdownNotice(string text, Action report, Action done) => Dialog(text, new[] { ("Prepare Bug Report", report), ("Close Game", done) });
    internal void ShutdownNotice(string text, Action close) => Dialog(text, new[] { ("Close Game", close) });
    internal void Confirm(string text, string accept, string cancel, Action yes, Action no) => Dialog(text, new[] { (accept, yes), (cancel, no) });
    internal void Modes(Action<NewGameChoice> chosen) => throw new InvalidOperationException("HK owns save creation.");
    internal void Slots(SlotSummary[] slots) => Waiting("Bridge connected. Waiting for Hollow Knight to select a save...");
    private ConfirmPanel? nativeDialog;
    private readonly List<(UIControlButton Button, UnityEngine.Events.UnityEvent Submit, bool Active)> modalBindings = new List<(UIControlButton, UnityEngine.Events.UnityEvent, bool)>();
    private readonly List<(TMP_Text Text, string Original)> modalLabels = new List<(TMP_Text, string)>();
    private readonly Dictionary<TMP_Text, string> modalText = new Dictionary<TMP_Text, string>();
    private UnityEngine.Events.UnityEvent? modalCancel;
    private bool modalOverride;
    private void Dialog(string text, (string Label, Action Action)[] choices)
    {
        Bind(); ClearPanel();
        if (main == null || choices.Length < 1 || choices.Length > 2) throw new InvalidOperationException("NS native confirmation flow unavailable.");
        var provider = (ConfirmPanelProvider)AccessTools.Field(typeof(StartMenuLogic), "confirmPanel").GetValue(main);
        nativeDialog = (ConfirmPanel)AccessTools.Field(typeof(ConfirmPanelProvider), "_confirmPanel").GetValue(provider)
            ?? throw new InvalidOperationException("NS native confirmation owner is not initialized.");
        provider.ShowConfirm(text, () => { }, "Hollow Sols Bridge");
        activePanelGroup = nativeDialog.controlGroup;
        modalOverride = activePanelGroup.IsOverrideCancel; modalCancel = activePanelGroup.OnCancel;
        activePanelGroup.IsOverrideCancel = true; activePanelGroup.OnCancel = new UnityEngine.Events.UnityEvent();
        activePanelGroup.OnCancel.AddListener(() => choices[choices.Length - 1].Action());
        var buttons = nativeDialog.buttons.Where(b => b != null && b != nativeDialog.triangleButton).ToArray();
        var confirm = nativeDialog.confirmButton;
        var ordered = new[] { confirm }.Concat(buttons.Where(b => b != confirm)).ToArray();
        if (ordered.Length < choices.Length) throw new InvalidOperationException("NS native confirmation buttons changed.");
        for (int i = 0; i < ordered.Length; i++) {
            var button = ordered[i]; modalBindings.Add((button, button.onSubmit, button.gameObject.activeSelf));
            button.onSubmit = new UnityEngine.Events.UnityEvent(); button.gameObject.SetActive(i < choices.Length);
            if (i >= choices.Length) continue;
            Actions[button] = (choices[i].Action, choices[choices.Length - 1].Action);
            foreach (var label in button.GetComponentsInChildren<TMP_Text>(true).Where(t => !string.IsNullOrEmpty(t.text))) SetModalText(label, choices[i].Label);
        }
        SetModalText(nativeDialog.confirmMessageText, text); SetModalText(nativeDialog.titleMessageText, "Hollow Sols Bridge");
        activePanelGroup.defaultSelectable = confirm.SelectableComp; confirm.SelectableComp.Select();
        log("NS_BRIDGE_DIALOG nativeConfirmationProvider=True nativeRoot=True nativeTransitions=True text=" + text);
    }
    private void SetModalText(TMP_Text label, string text)
    { modalLabels.Add((label, label.text)); modalText[label] = text; label.text = text; }
    private void ClearPanel()
    {
        if (activePanelGroup != null) activePanelGroup.PopHide(); activePanelGroup = null;
        if (nativeDialog != null) { nativeDialog.controlGroup.IsOverrideCancel = modalOverride; nativeDialog.controlGroup.OnCancel = modalCancel!; }
        foreach (var entry in modalBindings) if (entry.Button != null) { entry.Button.onSubmit = entry.Submit; entry.Button.gameObject.SetActive(entry.Active); }
        foreach (var entry in modalLabels) if (entry.Text != null) entry.Text.text = entry.Original;
        modalBindings.Clear(); modalLabels.Clear(); modalText.Clear(); nativeDialog = null; modalCancel = null;
        foreach (var key in Actions.Keys.Where(b => b == null || b != bridge).ToArray()) Actions.Remove(key);
        foreach (var obj in owned) if (obj != null) UnityEngine.Object.Destroy(obj); owned.Clear();
        if (panel != null) UnityEngine.Object.Destroy(panel); panel = null;
    }
    internal void Close()
    {
        ClearPanel(); foreach (var item in hidden) if (item.Group != null) { item.Group.alpha = item.Alpha; item.Group.interactable = item.Input; item.Group.blocksRaycasts = item.Rays; } hidden.Clear();
        if (bridge != null) bridge.SelectableComp.Select();
    }
    private void ReleaseRows() { Close(); if (bridge != null) Actions.Remove(bridge); if (bridgeRow != null) UnityEngine.Object.Destroy(bridgeRow); bridge = null; bridgeRow = null; main = null; }
    public void Dispose() { ReleaseRows(); harmony.UnpatchSelf(); }
}
