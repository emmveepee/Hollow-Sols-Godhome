using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using HollowSols.Foundation;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace YiGodhome;

// All rows/cards/decorations are native runtime clones. Donor callbacks are
// removed while inactive; no original save-screen component is repurposed.
internal sealed class NativeBridgeMenu : IDisposable
{
    private readonly HollowSolsMenuPlugin owner;
    private readonly Action<string> log;
    private MainMenuOptions? main;
    private GameObject? bridgeRow, panel;
    private HollowSolsChoice? bridgeChoice;
    private readonly List<GameObject> owned = new List<GameObject>();
    private readonly List<(CanvasGroup Group, float Alpha, bool Input, bool Rays)> hidden = new List<(CanvasGroup, float, bool, bool)>();
    private readonly List<(Selectable Item, Navigation Nav)> nativeNavigation = new List<(Selectable, Navigation)>();
    private RectTransform? buttonTemplate;
    private bool open;
    private SpriteRenderer? hiddenTitle;
    private PlayMakerFSM? hiddenSubtitle;
    private bool titleWasVisible;
    private GameObject? hiddenTitleIndicators;
    private bool titleIndicatorsWereActive;
    private Coroutine? showing;
    private static NativeBridgeMenu? current;
    private readonly Harmony fadeScope = new Harmony("local.hollowsols.hk.menu-fades");
    private readonly Dictionary<CanvasGroup, bool> visibility = new Dictionary<CanvasGroup, bool>();
    private bool cards;
    private bool loadingHandoff;
    private readonly List<Selectable> cardNavigation = new List<Selectable>();
    private readonly Dictionary<Selectable, Selectable> cardDeletion = new Dictionary<Selectable, Selectable>();
    internal NativeBridgeMenu(HollowSolsMenuPlugin owner, Action<string> log)
    {
        this.owner = owner; this.log = log; current = this;
        fadeScope.Patch(AccessTools.Method(typeof(UIManager), "FadeInCanvasGroup"), postfix: new HarmonyMethod(typeof(NativeBridgeMenu), nameof(ScopeOwnedFade)));
    }
    private static void ScopeOwnedFade(CanvasGroup __0, ref System.Collections.IEnumerator __result)
    {
        var menu = current; var expected = menu?.panel;
        if (menu == null || expected == null || __0 == null || !__0.transform.IsChildOf(expected.transform)) return;
        __result = menu.OwnedTransition(__result, expected);
    }
    private System.Collections.IEnumerator OwnedTransition(System.Collections.IEnumerator transition, GameObject expected)
    {
        // Native ShowMenu starts child fades on UIManager. Cancelling only our
        // parent coroutine does not retire those children. Scope each native
        // enumerator before MoveNext can touch a replaced/destroyed owned panel.
        while (panel == expected && expected != null && open && transition.MoveNext()) yield return transition.Current;
    }
    internal void SceneLoaded(Scene scene) { if (scene.name == "Menu_Title") { if(loadingHandoff)Close(); Bind(); } }
    internal void Update()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Menu_Title") return;
        if(bridgeChoice!=null) {
            bool wasInteractable=bridgeChoice.interactable;
            bridgeChoice.interactable=owner.BridgeActivationAllowed;
            if(!wasInteractable && bridgeChoice.interactable && !open && EventSystem.current!=null
                && EventSystem.current.currentSelectedGameObject==bridgeChoice.gameObject) {
                EventSystem.current.SetSelectedGameObject(null);bridgeChoice.Select();
            }
        }
        EnforceLoadingHandoff();
        // Native save-card fades still carry the donor file's hidden state. The
        // owned cards use mod metadata; only their parent runs the native fade.
        if (cards && panel != null) foreach (var item in visibility) if (item.Key != null && item.Key.gameObject != panel) {
            item.Key.alpha = item.Value ? 1 : 0; item.Key.interactable = item.Key.blocksRaycasts = item.Value;
        }
        if (main == null) Bind();
        if (main != null && bridgeChoice != null)
        {
            var down = main.quitButton.navigation; down.selectOnDown = bridgeChoice; main.quitButton.navigation = down;
            var up = main.startButton.navigation; up.selectOnUp = bridgeChoice; main.startButton.navigation = up;
            bridgeChoice.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = main.quitButton, selectOnDown = main.startButton };
        }
    }
    internal bool FixtureReady => bridgeChoice != null && bridgeChoice.isActiveAndEnabled && main?.startButton?.IsInteractable() == true;
    internal bool NativeReturnReady => main != null && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Menu_Title"
        && main.startButton != null && main.startButton.IsInteractable();
    internal void FixtureEvidence()
    {
        log("HK_MENU_SCOPED_EVIDENCE main=" + (main != null) + " bridge=" + (bridgeChoice != null));
        if (open && hiddenTitleIndicators != null && hiddenTitleIndicators.activeSelf) throw new InvalidOperationException("Title-only indicators leaked into a bridge submenu.");
        if (panel == null) return;
        foreach (var root in new[] { panel }.Concat(owned).Where(o => o != null)) foreach (var text in root.GetComponentsInChildren<Text>(true).Take(48))
            log("HK_MENU_SCOPED_TEXT name=" + text.name + " value=" + text.text + " active=" + text.gameObject.activeInHierarchy + " enabled=" + text.enabled + " color=" + text.color + " font=" + text.font.name + " lineHeight=" + text.font.lineHeight + " size=" + text.fontSize + " spacing=" + text.lineSpacing + " scale=" + text.transform.lossyScale
                + " groups=" + string.Join(";", text.GetComponentsInParent<CanvasGroup>(true).Select(g => g.name + ":" + g.alpha + ":" + g.gameObject.activeInHierarchy)));
        foreach (var animation in panel.GetComponentsInChildren<Animator>(true).Take(40))
            log("HK_MENU_SCOPED_ANIMATION name=" + animation.name + " active=" + animation.gameObject.activeInHierarchy + " parameters=" + string.Join(";", animation.parameters.Select(p => p.name + ":" + p.type)) + " state=" + animation.GetCurrentAnimatorStateInfo(0).shortNameHash);
    }
    internal void FixtureDiagnostics() { }
    internal void FixtureSubmitBridge() { if (bridgeChoice == null) throw new InvalidOperationException("Native bridge row missing."); bridgeChoice.Select(); ExecuteEvents.Execute(bridgeChoice.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler); }
    internal void FixturePixels(string path) => ScreenCapture.CaptureScreenshot(path);
    internal void FixtureSelectSlot(SlotSummary slot) => owner.SelectSlot(slot);
    internal void FixtureVanillaSelection() { Close(); UIManager.instance.UIGoToProfileMenu(); }
    internal void FixtureCancel() {
        var selected = EventSystem.current.currentSelectedGameObject ?? throw new InvalidOperationException("Native menu focus missing.");
        ExecuteEvents.Execute(selected, new BaseEventData(EventSystem.current), ExecuteEvents.cancelHandler);
    }
    internal void FixtureNavigation() {
        var choices = cards ? cardNavigation.ToArray() : panel!.GetComponentsInChildren<HollowSolsChoice>(true).Where(c => c.isActiveAndEnabled && c.IsInteractable()).Cast<Selectable>().ToArray();
        if (choices.Length < 1 || choices.Length > 12) throw new InvalidOperationException("Owned native navigation budget changed.");
        if (cards && choices.Length != ModSlot.Count + 1) throw new InvalidOperationException("Native save navigation must include four cards and Back.");
        foreach (var choice in choices) log("HK_MENU_NAVIGATION_CHOICE name=" + choice.name + " parent=" + choice.transform.parent.name + " active=" + choice.isActiveAndEnabled + " down=" + choice.navigation.selectOnDown?.name);
        var first = EventSystem.current.currentSelectedGameObject;
        if (first == null || !choices.Any(c => c.gameObject == first)) throw new InvalidOperationException("Native transition did not focus an owned choice.");
        foreach (var direction in new[] { MoveDirection.Down, MoveDirection.Up }) {
            var seen = new HashSet<GameObject>();
            for (int i = 0; i < choices.Length; i++) {
                var current = EventSystem.current.currentSelectedGameObject;
                if (current == null || !seen.Add(current)) throw new InvalidOperationException("Native navigation skipped or duplicated a choice.");
                ExecuteEvents.Execute(current, new AxisEventData(EventSystem.current) { moveDir = direction, moveVector = direction == MoveDirection.Down ? Vector2.down : Vector2.up }, ExecuteEvents.moveHandler);
            }
            if (EventSystem.current.currentSelectedGameObject != first) throw new InvalidOperationException("Native navigation did not wrap back to its first choice.");
        }
        if (cards) foreach (var item in cardDeletion) {
            item.Key.Select(); ExecuteEvents.Execute(item.Key.gameObject, new AxisEventData(EventSystem.current) { moveDir = MoveDirection.Right, moveVector = Vector2.right }, ExecuteEvents.moveHandler);
            if (EventSystem.current.currentSelectedGameObject != item.Value.gameObject) throw new InvalidOperationException("Native save navigation did not reach its paired deletion action.");
            ExecuteEvents.Execute(item.Value.gameObject, new AxisEventData(EventSystem.current) { moveDir = MoveDirection.Left, moveVector = Vector2.left }, ExecuteEvents.moveHandler);
            if (EventSystem.current.currentSelectedGameObject != item.Key.gameObject) throw new InvalidOperationException("Native deletion navigation did not return to its save card.");
        }
        choices[0].Select();
        log("HK_MENU_NATIVE_NAVIGATION_PASS choices=" + choices.Length + " nativeAxisEvents=True directions=Up,Down");
    }
    internal void FixtureChoose(string text)
    {
        var choice = panel!.GetComponentsInChildren<HollowSolsChoice>(true).Single(c => c.Label != null && c.Label.text == text);
        log("HK_FIXTURE_CHOICE text=" + text + " active=" + choice.isActiveAndEnabled + " interactable=" + choice.IsInteractable() + " callback=" + choice.Act?.Method.Name);
        choice.Select(); ExecuteEvents.Execute(choice.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
    }
    private void Bind()
    {
        var ui = UIManager.instance; if (ui == null) return;
        var next = AccessTools.Field(typeof(UIManager), "mainMenuButtons").GetValue(ui) as MainMenuOptions;
        if (next == null || next == main) return;
        // Same account-status flag/write route as native UnlockGGMode. This
        // unlocks new-game selection only; no profile/challenge data is edited.
        var manager=GameManager.instance;
        if(manager!=null && manager.GetStatusRecordInt("RecBossRushMode")==0) {
            manager.SetStatusRecordInt("RecBossRushMode",1);manager.SaveStatusRecords();
            log("HK_GODSEEKER_MODE_UNLOCKED nativeStatusRecord=RecBossRushMode progressionUnchanged=True");
        }
        Dispose(); main = next;
        var quit = next.quitButton;
        buttonTemplate = (RectTransform)(quit.transform.parent.GetComponentsInChildren<MenuSelectable>(true).Length == 1 ? quit.transform.parent : quit.transform);
        var nativeRows = next.GetComponentsInChildren<MenuButton>(true).Where(b => b != null).ToArray();
        foreach (var item in nativeRows) nativeNavigation.Add((item, item.navigation));
        float gap = nativeRows.Select(b => next.transform.InverseTransformPoint(b.transform.position).y).Distinct().OrderByDescending(y => y).Zip(
            nativeRows.Select(b => next.transform.InverseTransformPoint(b.transform.position).y).Distinct().OrderByDescending(y => y).Skip(1), (a,b) => a-b).DefaultIfEmpty(75).First();
        bridgeChoice = HollowSolsConfiguration.NativeChoice(buttonTemplate, buttonTemplate.parent!, "Hollow Sols Bridge", owner.ActivateBridge, () => { }, out bridgeRow);
        var position = buttonTemplate.localPosition; position.y -= gap; bridgeRow.transform.localPosition = position;
        log("HK_BRIDGE_MAIN_ROW_BOUND nativeArtwork=True nativeFont=True nativeKeyboardController=True");
        if (Guid.TryParse(Environment.GetEnvironmentVariable("HOLLOWSOLS_MENU_FIXTURE"), out _)) {
            var modes = (MenuScreen)AccessTools.Field(typeof(UIManager), "playModeMenuScreen").GetValue(ui);
            foreach (var row in modes.GetComponentsInChildren<MenuSelectable>(true)) log("HK_NATIVE_MODE_ROW name=" + row.name + " type=" + row.GetType().Name + " label=" + string.Join(";", row.GetComponentsInChildren<Text>(true).Select(t => t.text)));
        }
    }
    internal void Waiting(string text) => Dialog(text, new[] { ("Go Back", (Action)owner.CancelBridge) });
    internal void Notice(string text, Action done) => Dialog(text, new[] { ("Continue", done) });
    internal void FailureNotice(string text, Action report, Action done) => Dialog(text, new[] { ("Prepare Bug Report", report), ("Continue", done) }, done);
    internal void FailureShutdownNotice(string text, Action report, Action done) => Dialog(text, new[] { ("Prepare Bug Report", report), ("Close Game", done) }, done);
    internal void ShutdownNotice(string text, Action close) => Dialog(text, new[] { ("Close Game", close) });
    internal void Confirm(string text, string accept, string cancel, Action yes, Action no, Action? back = null) => Dialog(text, new[] { (accept, yes), (cancel, no) }, back);
    internal void Modes(Action<NewGameChoice> chosen)
    {
        Bind();
        var template = (MenuScreen)AccessTools.Field(typeof(UIManager), "playModeMenuScreen").GetValue(UIManager.instance);
        PreparePanel(template.gameObject);
        var rows = panel!.GetComponentsInChildren<MenuSelectable>(true).ToArray();
        if (rows.Length != 4) throw new InvalidOperationException("Native mode-selection layout changed.");
        var choices = new List<Selectable>();
        foreach (var row in rows) {
            string label; Action action;
            switch (row.name) {
                case "NormalButton": label = "Classic"; action = () => Campaign(GameMode.Classic, chosen); break;
                case "SteelButton": label = "Steel Soul"; action = () => Campaign(GameMode.SteelSoul, chosen); break;
                case "GGButton": label = "Godhome"; action = () => Confirm("Unlock access to all statues and pantheons?", "Yes", "No", () => chosen(new NewGameChoice(GameMode.Godhome, false, true)), () => chosen(new NewGameChoice(GameMode.Godhome, false, false)), () => Modes(chosen)); break;
                case "ApplyButton": label = "Go Back"; action = owner.ShowSlots; break;
                default: throw new InvalidOperationException("Native mode-selection row changed.");
            }
            var choice = Replace(row, action, owner.ShowSlots); choice.gameObject.SetActive(true);
            choice.Label = choice.GetComponentsInChildren<Text>(true).First(t => !string.IsNullOrEmpty(t.text)); choice.SetLabel(label); choices.Add(choice);
        }
        // Description keys/fonts are native and read-only. Refresh them in the
        // owned mode clone; fixed Hollow Sols labels survive native localization.
        foreach (var localizer in panel.GetComponentsInChildren<AutoLocalizeTextUI>(true)) localizer.enabled = true;
        Finish(choices.ToArray(), true);
        log("HK_BRIDGE_NATIVE_MODE_SCREEN originalLayout=True originalArtwork=True choices=Classic,SteelSoul,Godhome");
    }
    private void Campaign(GameMode mode, Action<NewGameChoice> chosen) => Confirm("Hollow Sols gameplay in " + (mode == GameMode.Classic ? "Classic" : "Steel Soul")
        + " Mode is entirely untested and may not function correctly.", "I Understand", "Go Back", () => chosen(new NewGameChoice(mode, true, false)), () => Modes(chosen));
    private void PreparePanel(GameObject template)
    {
        ClearPanel(); HideMain();
        var staging = new GameObject("Hollow Sols inactive native menu clone"); staging.SetActive(false);
        panel = UnityEngine.Object.Instantiate(template, staging.transform, false); panel.SetActive(false);
        panel.name = "Hollow Sols native menu"; panel.transform.SetParent(template.transform.parent, false); UnityEngine.Object.Destroy(staging);
        if (Guid.TryParse(Environment.GetEnvironmentVariable("HOLLOWSOLS_MENU_FIXTURE"), out _)) {
            foreach (var screen in panel.GetComponentsInChildren<MenuScreen>(true)) {
                foreach (var group in new[] { screen.title, screen.content, screen.controls }) if (group != null) log("HK_FIXTURE_CLONE_GROUP name=" + group.name + " owned=" + group.transform.IsChildOf(panel.transform) + " active=" + group.gameObject.activeInHierarchy + " alpha=" + group.alpha);
            }
        }
        foreach (var behaviour in panel.GetComponentsInChildren<MonoBehaviour>(true))
            if (behaviour != null) {
                if (behaviour is MenuButtonList) UnityEngine.Object.DestroyImmediate(behaviour);
                else if (behaviour is PreselectOption || behaviour is MenuScreen || behaviour.GetType().Name.Contains("Localiz", StringComparison.OrdinalIgnoreCase)) behaviour.enabled = false;
            }
        foreach (var trigger in panel.GetComponentsInChildren<EventTrigger>(true)) UnityEngine.Object.DestroyImmediate(trigger);
        foreach (var receiver in panel.GetComponentsInChildren<MonoBehaviour>(true))
            if (receiver != null && receiver is not MenuSelectable && (receiver is ISubmitHandler || receiver is ICancelHandler || receiver is IPointerClickHandler)) UnityEngine.Object.DestroyImmediate(receiver);
    }
    private void Dialog(string text, (string Label, Action Action)[] choices, Action? back = null)
    {
        Bind();
        var template = AccessTools.Field(typeof(UIManager), "returnMainMenuPrompt").GetValue(UIManager.instance) as MenuScreen
            ?? throw new InvalidOperationException("Native confirmation template missing.");
        PreparePanel(template.gameObject);
        var nativeRows = panel!.GetComponentsInChildren<MenuSelectable>(true).OrderByDescending(r => r.transform.position.y).ToArray();
        if (nativeRows.Length != 2 || choices.Length < 1 || choices.Length > 2) throw new InvalidOperationException("Native confirmation layout changed.");
        var labels = panel.GetComponentsInChildren<Text>(true).Where(t => t.GetComponentInParent<MenuSelectable>(true) == null && !string.IsNullOrEmpty(t.text)).ToArray();
        var heading = labels.FirstOrDefault() ?? throw new InvalidOperationException("Native dialog typography missing.");
        bool longMessage = text.Length > 80;
        var body = longMessage ? labels.Skip(1).FirstOrDefault() ?? throw new InvalidOperationException("Native dialog caption missing.") : heading;
        if (longMessage) { heading.text = "Hollow Sols"; RevealText(heading); }
        foreach (var label in labels) if (label != body && (!longMessage || label != heading)) UnityEngine.Object.DestroyImmediate(label.gameObject);
        var aligner = body.GetComponent<FixVerticalAlign>(); if (aligner != null) UnityEngine.Object.DestroyImmediate(aligner);
        body.text = text; body.supportRichText = false; body.horizontalOverflow = HorizontalWrapMode.Wrap; body.verticalOverflow = VerticalWrapMode.Overflow;
        body.lineSpacing = 1.2f; body.alignment = TextAnchor.UpperCenter;
        var rect = (RectTransform)body.transform;
        float top = longMessage ? rect.sizeDelta.y * .5f : 20;
        rect.pivot = new Vector2(.5f, 1); rect.anchoredPosition += new Vector2(0, top); rect.sizeDelta = new Vector2(rect.sizeDelta.x, longMessage ? 120 : 210);
        if (longMessage) {
            // Keep the authored button positions. The native caption must fit
            // above their actual label bounds, including longer recovery text.
            var firstLabel = nativeRows[0].GetComponentInChildren<Text>(true).rectTransform;
            var corners = new Vector3[4]; firstLabel.GetWorldCorners(corners);
            float available = -rect.InverseTransformPoint(corners[1]).y - 16;
            if (available < 40) throw new InvalidOperationException("Native dialog caption has no safe text area.");
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, available);
            body.resizeTextForBestFit = true; body.resizeTextMinSize = 28; body.resizeTextMaxSize = body.fontSize;
            body.verticalOverflow = VerticalWrapMode.Truncate;
        }
        RevealText(body);
        var rows = new List<HollowSolsChoice>();
        for (int i = 0; i < nativeRows.Length; i++) {
            if (i >= choices.Length) { UnityEngine.Object.DestroyImmediate(nativeRows[i].gameObject); continue; }
            var choice = Replace(nativeRows[i], choices[i].Action, back ?? choices[choices.Length - 1].Action);
            choice.Label = choice.GetComponentsInChildren<Text>(true).First(); choice.SetLabel(choices[i].Label);
            choice.gameObject.SetActive(true); rows.Add(choice);
        }
        Finish(rows.Cast<Selectable>().ToArray(), true);
        log("HK_BRIDGE_DIALOG nativeTemplate=True nativeCaption=" + longMessage + " fontSize=" + body.fontSize + " choices=" + choices.Length + " text=" + text);
    }
    internal void Slots(SlotSummary[] slots)
    {
        if (slots.Length != ModSlot.Count) throw new InvalidOperationException("Expected four paired slots.");
        Bind(); var template = (CanvasGroup)AccessTools.Field(typeof(UIManager), "saveProfileScreen").GetValue(UIManager.instance);
        PreparePanel(template.gameObject);
        var headingFlourishes = new List<Animator>();
        var nativeFleur = (Animator)AccessTools.Field(typeof(UIManager), "saveProfileTopFleur").GetValue(UIManager.instance);
        if (nativeFleur.transform.IsChildOf(template.transform)) {
            var parts = new Stack<string>(); for (var node = nativeFleur.transform; node != template.transform; node = node.parent) parts.Push(node.name);
            headingFlourishes.Add(panel!.transform.Find(string.Join("/", parts)).GetComponent<Animator>());
        } else {
            var staging = new GameObject("Hollow Sols inactive native flourish"); staging.SetActive(false);
            var clone = UnityEngine.Object.Instantiate(nativeFleur.gameObject, staging.transform, false); clone.SetActive(false); clone.transform.SetParent(nativeFleur.transform.parent, false); clone.transform.position = nativeFleur.transform.position; UnityEngine.Object.Destroy(staging);
            owned.Add(clone); clone.SetActive(true); headingFlourishes.Add(clone.GetComponent<Animator>());
        }
        var headingChoices = new List<Selectable>();
        foreach (string name in new[] { "saveProfileTitle", "saveProfileControls" }) {
            var source = (CanvasGroup)AccessTools.Field(typeof(UIManager), name).GetValue(UIManager.instance);
            log("HK_NATIVE_SAVE_HEADING name=" + name + " insideProfile=" + source.transform.IsChildOf(template.transform) + " parent=" + source.transform.parent.name);
            GameObject clone;
            if (source.transform.IsChildOf(template.transform)) {
                var parts = new Stack<string>(); for (var node = source.transform; node != template.transform; node = node.parent) parts.Push(node.name);
                clone = panel!.transform.Find(string.Join("/", parts)).gameObject;
            } else {
                var staging = new GameObject("Hollow Sols inactive native save heading"); staging.SetActive(false);
                clone = UnityEngine.Object.Instantiate(source.gameObject, staging.transform, false); clone.SetActive(false); clone.transform.SetParent(panel!.transform.parent, false); clone.transform.position = source.transform.position; UnityEngine.Object.Destroy(staging); owned.Add(clone);
            }
            foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true)) if (behaviour != null && (behaviour is PreselectOption || behaviour is MenuButtonList || behaviour.GetType().Name.Contains("Localiz", StringComparison.OrdinalIgnoreCase))) behaviour.enabled = false;
            foreach (var trigger in clone.GetComponentsInChildren<EventTrigger>(true)) UnityEngine.Object.DestroyImmediate(trigger);
            foreach (var button in clone.GetComponentsInChildren<MenuSelectable>(true)) { var back = Replace(button, owner.CancelBridge, owner.CancelBridge); back.gameObject.SetActive(true); headingChoices.Add(back); }
            if (name == "saveProfileTitle") foreach (var text in clone.GetComponentsInChildren<Text>(true)) { text.text = "Hollow Sols"; RevealText(text); }
            if (name == "saveProfileTitle") headingFlourishes.AddRange(clone.GetComponentsInChildren<Animator>(true).Where(a => a.parameters.Any(p => p.name == "show" && p.type == AnimatorControllerParameterType.Trigger)));
            foreach (var group in clone.GetComponentsInChildren<CanvasGroup>(true)) Set(group, true); clone.SetActive(true);
        }
        var native = panel!.GetComponentsInChildren<SaveSlotButton>(true).OrderBy(b => (int)b.saveSlot).ToArray();
        if (native.Length != 4) throw new InvalidOperationException("Native four-slot layout changed.");
        var choices = new List<Selectable>();
        var flourishes = new List<Animator>();
        for (int i = 0; i < native.Length; i++)
        {
            var slot = slots[i]; var card = native[i];
            SaveStats? stats = null;
            if ((slot.Condition == SlotCondition.Ready || slot.Condition == SlotCondition.RecoveryAvailable) && slot.Pair != null) {
                try { stats = NativeSavePreview.Read(owner.PreviewSave(slot)); }
                catch (Exception error) { log("HK_SAVE_PREVIEW_UNAVAILABLE slot=" + slot.Slot.Number + " error=" + error.GetType().Name); }
            }
            // Presentation uses the native renderer with checkpoint metadata,
            // never donor-card state or a vanilla profile's cached save stats.
            if (stats != null) {
                AccessTools.Field(typeof(SaveSlotButton), "gm").SetValue(card, GameManager.instance);
                card.healthSlots.gameObject.SetActive(true); card.mpSlots.gameObject.SetActive(true); card.geoIcon.gameObject.SetActive(true);
                AccessTools.Method(typeof(SaveSlotButton), "PresentSaveSlot").Invoke(card, new object[] { stats });
                var area = card.saveSlots.GetBackground(stats.mapZone);
                card.background.sprite = area?.backgroundImage;
            } else { card.background.sprite = null; }
            card.background.enabled = stats != null && card.background.sprite != null;
            var highlight = card.highlight; if (card.topFleur != null) flourishes.Add(card.topFleur);
            var mainChoice = Replace(card, () => owner.SelectSlot(slot), owner.CancelBridge); mainChoice.CardHighlight = highlight; choices.Add(mainChoice); cardNavigation.Add(mainChoice);
            Set(card.newGameText, slot.Condition == SlotCondition.Empty); Set(card.activeSaveSlot, slot.Pair != null);
            if (slot.Condition == SlotCondition.Empty) foreach (var text in card.newGameText.GetComponentsInChildren<Text>(true)) { text.text = "New Game"; RevealText(text); }
            Set(card.saveCorruptedText, slot.Condition == SlotCondition.Unrecoverable); Set(card.loadingText, false); Set(card.clearSavePrompt, false);
            if (card.clearSavePrompt != null) {
                if (!card.clearSavePrompt.transform.IsChildOf(panel.transform)) throw new InvalidOperationException("Native card confirmation is outside the owned clone.");
                UnityEngine.Object.DestroyImmediate(card.clearSavePrompt.gameObject);
            }
            bool defeated = stats?.permadeathMode == 2;
            Set(card.defeatedText, defeated); Set(card.defeatedBackground, defeated); Set(card.brokenSteelOrb, defeated);
            Set(card.normalSoulOrbCg, !defeated && slot.Pair?.Choice.Mode == GameMode.Classic); Set(card.hardcoreSoulOrbCg, !defeated && slot.Pair?.Choice.Mode == GameMode.SteelSoul); Set(card.ggSoulOrbCg, !defeated && slot.Pair?.Choice.Mode == GameMode.Godhome);
            if (defeated) { card.healthSlots.gameObject.SetActive(false); card.mpSlots.gameObject.SetActive(false); card.geoIcon.gameObject.SetActive(false); }
            if (stats == null) { card.locationText.text = ""; card.playTimeText.text = ""; card.geoText.text = ""; card.completionText.text = "";
                card.healthSlots.gameObject.SetActive(false); card.mpSlots.gameObject.SetActive(false); card.geoIcon.gameObject.SetActive(false); }
            else { RevealText(card.locationText); RevealText(card.playTimeText); }
            Set(card.clearSaveButton, slot.Condition != SlotCondition.Empty);
            foreach (var number in card.slotNumberText.GetComponentsInChildren<Text>(true)) number.text = slot.Slot.Number + ".";
            var clear = card.clearSaveButton?.GetComponentInChildren<MenuSelectable>(true);
            if (clear != null)
            {
                var delete = Replace(clear, () => owner.ConfirmDelete(slot), owner.ShowSlots);
                delete.gameObject.SetActive(slot.Condition != SlotCondition.Empty); choices.Add(delete);
                if (slot.Condition != SlotCondition.Empty) cardDeletion.Add(mainChoice, delete);
            }
            Set(card.myCanvasGroup, true); Set(card.backgroundCg, true); Set(card.slotNumberText, true);
            log("HK_SAVE_CARD_PRESENTATION slot=" + slot.Slot.Number + " occupied=" + (stats != null) + " location=" + stats?.mapZone + " art=" + card.background.enabled + " defeated=" + defeated);
        }
        // Replace native Back callbacks too, so bridge connection survives slot changes.
        foreach (var remaining in panel.GetComponentsInChildren<MenuSelectable>(true).Where(b => b is not HollowSolsChoice).ToArray()) {
            if (cardNavigation.Any(c => remaining.transform.IsChildOf(c.transform))) { UnityEngine.Object.DestroyImmediate(remaining.gameObject); continue; }
            var back = Replace(remaining, owner.CancelBridge, owner.CancelBridge); choices.Add(back); cardNavigation.Add(back);
        }
        choices.AddRange(headingChoices); cardNavigation.AddRange(headingChoices);
        cards = true; Finish(choices.Where(s => s.gameObject.activeSelf).ToArray(), false);
        for (int i = 0; i < cardNavigation.Count; i++) {
            var card = cardNavigation[i]; cardDeletion.TryGetValue(card, out var deletion);
            card.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = cardNavigation[(i + cardNavigation.Count - 1) % cardNavigation.Count], selectOnDown = cardNavigation[(i + 1) % cardNavigation.Count], selectOnRight = deletion };
        }
        foreach (var entry in cardDeletion) {
            entry.Value.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = entry.Key, selectOnUp = entry.Key.navigation.selectOnUp, selectOnDown = entry.Key.navigation.selectOnDown };
        }
        foreach (var flourish in flourishes.Concat(headingFlourishes)) { flourish.ResetTrigger("hide"); flourish.SetTrigger("show"); }
        log("HK_BRIDGE_FOUR_NATIVE_SLOTS nativeCards=True isolatedMetadata=True");
    }
    private static HollowSolsChoice Replace(MenuSelectable native, Action action, Action back)
    {
        var obj = native.gameObject; var colors = native.colors; var transition = native.transition; var graphic = native.targetGraphic;
        var fields = new[] { "leftCursor", "rightCursor", "selectHighlight", "descriptionText", "uiAudioPlayer" }.Select(n => AccessTools.Field(typeof(MenuSelectable), n)).ToArray();
        var values = fields.Select(f => f.GetValue(native)).ToArray();
        UnityEngine.Object.DestroyImmediate(native);
        var choice = obj.AddComponent<HollowSolsChoice>(); choice.colors = colors; choice.transition = transition; choice.targetGraphic = graphic;
        for (int i = 0; i < fields.Length; i++) fields[i].SetValue(choice, values[i]);
        if (AccessTools.Field(typeof(MenuSelectable), "uiAudioPlayer").GetValue(choice) == null)
            AccessTools.Field(typeof(MenuSelectable), "uiAudioPlayer").SetValue(choice, UnityEngine.Object.FindFirstObjectByType<MenuAudioController>());
        choice.Act = action; choice.Back = back; return choice;
    }
    private void Set(CanvasGroup? group, bool visible) { if (group == null) return; visibility[group] = visible; ApplyVisibility(group, visible); }
    private static void ApplyVisibility(CanvasGroup group, bool visible) { group.gameObject.SetActive(visible); group.alpha = visible ? 1 : 0; group.interactable = group.blocksRaycasts = visible; }
    private static void RevealText(Text text) { text.gameObject.SetActive(true); text.enabled = true; var color = text.color; color.a = 1; text.color = color; text.canvasRenderer.SetAlpha(1); }
    private void Finish(Selectable[] choices, bool revealChildren)
    {
        foreach (var group in revealChildren ? panel!.GetComponentsInChildren<CanvasGroup>(true) : panel!.GetComponents<CanvasGroup>()) Set(group, true);
        panel.SetActive(true); open = true;
        // Native clone initialization can restore its serialized hidden alpha.
        // Apply owned visibility only after activation has run those callbacks.
        foreach (var item in visibility) if (item.Key != null) ApplyVisibility(item.Key, item.Value);
        for (int i = 0; i < choices.Length; i++) choices[i].navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = choices[(i + choices.Length - 1) % choices.Length], selectOnDown = choices[(i + 1) % choices.Length] };
        var screen = panel.GetComponent<MenuScreen>();
        if (screen != null) {
            screen.defaultHighlight = choices.Length > 0 ? choices[0] : null;
            foreach (var group in new[] { screen.title, screen.content, screen.controls }) if (group != null) {
                if (!group.transform.IsChildOf(panel.transform)) throw new InvalidOperationException("Native menu content is outside its owned clone.");
                group.gameObject.SetActive(true);
            }
            var show = (System.Collections.IEnumerator)AccessTools.Method(typeof(UIManager), "ShowMenu").Invoke(UIManager.instance, new object[] { screen });
            showing = owner.StartCoroutine(ShowWithFocus(show, panel, choices));
        }
        else if (panel.GetComponent<CanvasGroup>() is CanvasGroup group) {
            var fade = (System.Collections.IEnumerator)AccessTools.Method(typeof(UIManager), "FadeInCanvasGroup").Invoke(UIManager.instance, new object[] { group });
            showing = owner.StartCoroutine(ShowWithFocus(fade, panel, choices));
        }
    }
    private System.Collections.IEnumerator ShowWithFocus(System.Collections.IEnumerator nativeTransition, GameObject expected, Selectable[] choices)
    {
        while (panel == expected && expected != null && open && nativeTransition.MoveNext()) yield return nativeTransition.Current;
        if (panel == expected && open && EventSystem.current!=null) {
            var selected=EventSystem.current.currentSelectedGameObject;
            // Native ShowMenu already selects its default. Clearing that same
            // selection queues ValidateDeselect, which hides its cursors at the
            // end of the frame after they have just been shown again.
            // Preserve any valid player choice made during the final fade too.
            if(!choices.Any(c=>c!=null && c.isActiveAndEnabled && c.IsInteractable() && c.gameObject==selected))
                choices.FirstOrDefault(c=>c!=null && c.isActiveAndEnabled && c.IsInteractable())?.Select();
        }
    }
    private void HideMain()
    {
        if (open) return;
        var ui = UIManager.instance;
        var home = (CanvasGroup)AccessTools.Field(typeof(UIManager), "mainMenuScreen").GetValue(ui);
        hiddenTitleIndicators = home.transform.Find("TeamCherryLogo")?.gameObject ?? throw new InvalidOperationException("Native title indicator owner changed.");
        titleIndicatorsWereActive = hiddenTitleIndicators.activeSelf; hiddenTitleIndicators.SetActive(false);
        log("HK_NATIVE_TITLE_INDICATORS hidden=True nativeOwner=MainMenuScreen/TeamCherryLogo");
        hiddenSubtitle = (PlayMakerFSM)AccessTools.Field(typeof(UIManager), "subtitleFSM").GetValue(ui);
        if (hiddenSubtitle != null) hiddenSubtitle.SendEvent("FADE OUT");
        if (AccessTools.Field(typeof(UIManager), "gameTitle")?.GetValue(ui) is SpriteRenderer title && title != null) { hiddenTitle = title; titleWasVisible = title.enabled; title.enabled = false; }
        foreach (string name in new[] { "mainMenuScreen", "saveProfileScreen", "saveProfileTitle", "saveProfileControls" })
            if (AccessTools.Field(typeof(UIManager), name)?.GetValue(ui) is CanvasGroup group) { hidden.Add((group, group.alpha, group.interactable, group.blocksRaycasts)); group.alpha = 0; group.interactable = group.blocksRaycasts = false; }
        open = true;
    }
    private void ClearPanel() {
        if (showing != null) { owner.StopCoroutine(showing); showing = null; var ui = UIManager.instance; if (ui != null) { AccessTools.Field(typeof(UIManager), "isFadingMenu").SetValue(ui, false); InputHandler.Instance?.StartUIInput(); } }
        foreach (var obj in owned) if (obj != null) { obj.SetActive(false); UnityEngine.Object.Destroy(obj); } owned.Clear(); if (panel != null) { panel.SetActive(false); UnityEngine.Object.Destroy(panel); } panel = null;
        visibility.Clear();
        cards = false;
        cardNavigation.Clear(); cardDeletion.Clear();
    }
    internal void Close()
    {
        loadingHandoff=false;
        if (hiddenTitleIndicators != null) hiddenTitleIndicators.SetActive(titleIndicatorsWereActive); hiddenTitleIndicators = null;
        ClearPanel(); foreach (var item in hidden) if (item.Group != null) { item.Group.alpha = item.Alpha; item.Group.interactable = item.Input; item.Group.blocksRaycasts = item.Rays; } hidden.Clear(); open = false;
        if (hiddenTitle != null) hiddenTitle.enabled = titleWasVisible; hiddenTitle = null;
        if (hiddenSubtitle != null) hiddenSubtitle.SendEvent("FADE IN"); hiddenSubtitle = null;
        if (bridgeChoice != null) bridgeChoice.Select();
    }
    internal void BeginLoading()
    {
        HideMain();ClearPanel();loadingHandoff=true;
        foreach(var item in hidden)if(item.Group!=null){item.Group.alpha=0;item.Group.interactable=item.Group.blocksRaycasts=false;}
        if(bridgeChoice!=null)bridgeChoice.interactable=false;
        EventSystem.current?.SetSelectedGameObject(null);
        log("HK_SAVE_LOAD_HANDOFF titleRestored=False titleInput=False");
    }
    internal void EnforceLoadingHandoff()
    {
        if(!loadingHandoff)return;
        foreach(var item in hidden)if(item.Group!=null){item.Group.alpha=0;item.Group.interactable=item.Group.blocksRaycasts=false;}
        if(hiddenTitle!=null)hiddenTitle.enabled=false;
        if(hiddenTitleIndicators!=null)hiddenTitleIndicators.SetActive(false);
    }
    internal void FixtureCheckLoadingHandoff()
    {
        if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name!="Menu_Title")return;
        if(!loadingHandoff || panel!=null || bridgeChoice?.IsInteractable()!=false || hidden.Any(i=>i.Group!=null && (i.Group.alpha!=0 || i.Group.interactable || i.Group.blocksRaycasts)))
            throw new InvalidOperationException("Title menu became visible/interactable during paired save load.");
    }
    public void Dispose()
    {
        fadeScope.UnpatchSelf(); if (current == this) current = null;
        Close(); foreach (var item in nativeNavigation) if (item.Item != null) item.Item.navigation = item.Nav; nativeNavigation.Clear();
        if (bridgeRow != null) UnityEngine.Object.Destroy(bridgeRow); bridgeRow = null; bridgeChoice = null; buttonTemplate = null; main = null;
    }
}
