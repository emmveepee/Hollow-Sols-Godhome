using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using YiGodhome.Geometry;
namespace YiGodhome;

// One native rest context is projected onto the nearest configured host placement.
// Authored room sensors remain quarantined; only the explicit host interaction enters it.
internal sealed class NativeRestPoint : IDisposable
{
    private static NativeRestPoint? current;
    private readonly Harmony harmony = new Harmony("local.yigodhome.rest-point");
    private readonly Action<string> log;
    private readonly SavePoint point;
    private readonly Transform spawn;
    private readonly Vector3 originalPosition;
    private readonly System.Collections.Generic.Dictionary<GameObject, bool> activeStates = new System.Collections.Generic.Dictionary<GameObject, bool>();
    private readonly System.Collections.Generic.Dictionary<Collider2D, bool> colliderStates = new System.Collections.Generic.Dictionary<Collider2D, bool>();
    private readonly bool fixture;
    private NativePresentationCulling? presentationCulling;
    private AnimatorCullingMode originalCulling;
    private bool fixturePlaced;
    private bool optionsAudited;
    private bool fixtureRestored;
    private readonly System.Collections.Generic.HashSet<UIControlButton> blockedOptions = new System.Collections.Generic.HashSet<UIControlButton>();
    private readonly System.Collections.Generic.Dictionary<UnityEngine.UI.Selectable, bool> selectableStates = new System.Collections.Generic.Dictionary<UnityEngine.UI.Selectable, bool>();
    private GeometryLandingProbe? geometry;
    private bool entering, sitting;
    private float enteredAt;
    private float nextTrace;
    public static bool Active => current?.sitting == true;
    public Transform? VisualRoot => geometry?.RestPoints.Length > 0 ? point.myAnimator.transform : null;
    public GeometryLandingProbe? Geometry => geometry;
    public NativeRestPoint(Action<string> log)
    {
        if (current != null) throw new InvalidOperationException("Native rest context already exists.");
        this.log = log;
        string run = System.IO.Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!;
        fixture = YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures && YiGodhome.Bridge.SessionConfiguration.Current.Has("rest-probe");
        point = NativeCanvas.Current?.Rest ?? Resources.FindObjectsOfTypeAll<SavePoint>().Single(p => p.gameObject.scene.name == "A2_S5_BossHorseman_Final");
        originalPosition = point.transform.position;
        var spawnField = AccessTools.Field(typeof(SavePoint), "spawnPoint") ?? throw new InvalidOperationException("Native rest spawnPoint field changed.");
        spawn = (Transform)spawnField.GetValue(point);
        if (point.myAnimator == null || spawn == null || point.BindingTeleportPoint == null)
            throw new InvalidOperationException("Native rest dependencies unavailable.");
        originalCulling = point.myAnimator.cullingMode;
        current = this;
        try
        {
            harmony.Patch(AccessTools.Method(typeof(SavePoint), "InitAnimator"), postfix: new HarmonyMethod(typeof(NativeRestPoint), nameof(OpenedIdle)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(SavePoint), "shouldEnterBerserk"), prefix: new HarmonyMethod(typeof(NativeRestPoint), nameof(NoTutorial)));
            harmony.Patch(AccessTools.Method(typeof(GameCore), "SetReviveSavePoint"), prefix: new HarmonyMethod(typeof(NativeRestPoint), nameof(PreserveNativeRevive)));
            harmony.Patch(AccessTools.Method(typeof(SavePanel), "UpdateView"), postfix: new HarmonyMethod(typeof(NativeRestPoint), nameof(FilterOptions)));
            harmony.Patch(AccessTools.Method(typeof(UIViewToWorldPositionFollower), "SetPosition"), prefix: new HarmonyMethod(typeof(NativeRestPoint), nameof(PlaceOptions)));
            harmony.Patch(AccessTools.Method(typeof(UIControlButton), "Submit"), prefix: new HarmonyMethod(typeof(NativeRestPoint), nameof(AllowOption)));
            foreach (string method in new[] { "ShowTeleportPanel", "ShowMemoryChallengePanel" })
                harmony.Patch(AccessTools.Method(typeof(SavePanel), method), prefix: new HarmonyMethod(typeof(NativeRestPoint), nameof(NoTravel)));
            log("NS_REST_GRAPH rootActive=" + point.gameObject.activeSelf + " parentActive=" + (point.transform.parent == null || point.transform.parent.gameObject.activeInHierarchy)
                + " artActive=" + point.myAnimator.gameObject.activeSelf + " spawn=" + NativeRoomIsolation.PathOf(spawn));
            // The memory arena hides its rest object. Exclude the audited travel/story
            // acting graph before enabling the native rest animation hierarchy.
            foreach (Transform child in point.transform)
                if (child.name.StartsWith("[CutScene]", StringComparison.Ordinal)) SetActive(child.gameObject, false);
            foreach (var collider in point.GetComponentsInChildren<Collider2D>(true))
            { colliderStates.Add(collider, collider.enabled); collider.enabled = false; }
            SetActive(point.gameObject, true); SetActive(point.myAnimator.gameObject, true);
            presentationCulling = new NativePresentationCulling(point.transform);
            // The borrowed arena initializes an unrevealed SP_idle root. Use the
            // native opened idle presentation without unlocking its travel flag.
            point.myAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            point.myAnimator.Play("SP_Activated", 0, 0);
            log("NS_REST_READY native=True fastTravel=False authoredSensorsRemainDisabled=True diskWrites=False");
        }
        catch { Dispose(); throw; }
    }
    private void SetActive(GameObject obj, bool enabled)
    { if (!activeStates.ContainsKey(obj)) activeStates.Add(obj, obj.activeSelf); obj.SetActive(enabled); }
    private static void OpenedIdle(SavePoint __instance)
    {
        if (current == null || __instance != current.point || current.sitting) return;
        __instance.myAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        __instance.myAnimator.Play("SP_Activated", 0, 0);
    }
    private static bool NoTutorial(SavePoint __instance, ref bool __result)
    { if (current == null || __instance != current.point) return true; __result = false; return false; }
    private static bool PreserveNativeRevive() => current?.entering != true;
    private static bool NoTravel(SavePanel __instance) => current == null || __instance.CurrentSavePointGameObjectOnScene != current.point;
    private static bool AllowOption(UIControlButton __instance) {
        if(current==null)return true;
        if(current.blockedOptions.Contains(__instance))return false;
        var panel=GameCore.Instance.savePanelUiController;
        if(Active && __instance.transform.IsChildOf(panel.transform))
            current.log("NS_ROOT_SUBMIT button="+__instance.name+" selected="+UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject?.name
                +" group="+ApplicationUIGroupManager.Instance.currentControlGroup?.name+" rootState="+panel.state);
        return true;
    }
    public static void RefreshOptions()
    {
        if (!Active) return;
        foreach (var button in current!.blockedOptions) if (button != null) button.gameObject.SetActive(false);
    }
    private static bool PlaceOptions(UIViewToWorldPositionFollower __instance)
    {
        if (!Active || !ReferenceEquals(__instance, AccessTools.Field(typeof(SavePanel), "follower").GetValue(GameCore.Instance.savePanelUiController))) return true;
        var camera = (Camera)AccessTools.Field(typeof(UIViewToWorldPositionFollower), "_uiCamera").GetValue(__instance);
        var original = __instance.transform.position;
        var position = camera.ViewportToWorldPoint(new Vector3(0.67f, 0.5f, camera.WorldToViewportPoint(original).z));
        position.z = original.z; __instance.transform.position = position;
        return false;
    }
    private static void FilterOptions(SavePanel __instance)
    {
        if (NoTravel(__instance)) return;
        current!.SetActive(__instance.TeleportButton.gameObject, false); current.SetActive(__instance.MemoryButton.gameObject, false);
        current.blockedOptions.Add(__instance.TeleportButton); current.blockedOptions.Add(__instance.MemoryButton);
        // The memory template's authored default is Challenge. A disabled default
        // leaves EventSystem dispatch pointing at an unavailable button.
        var group = (UIControlGroup)AccessTools.Field(typeof(UIRootPanel), "defaultControlGroup").GetValue(__instance);
        var restore = __instance.RestoreButton.GetComponent<UnityEngine.UI.Selectable>();
        if (group != null)
        {
            foreach (var button in group.GetComponentsInChildren<UIControlButton>(true))
                if (button.GetComponentInParent<UIControlGroup>() == group && button != __instance.RestoreButton && button != __instance.jadeButton
                    && button != __instance.SkillTreeButton && button != __instance.StatusButton && button != group.exitButton)
                { current.blockedOptions.Add(button); current.SetActive(button.gameObject, false); }
            var valid=group.GetComponentsInChildren<UnityEngine.UI.Selectable>(true)
                .FirstOrDefault(s=>s.gameObject.activeSelf && s.interactable && s.GetComponentInParent<UIControlGroup>()==group && !current.blockedOptions.Contains(s.GetComponent<UIControlButton>()));
            // UpdateView also runs while the whole parent is hidden. Parent
            // visibility must not erase the authored default/current selection.
            group.defaultSelectable = restore.gameObject.activeSelf?restore:valid;
            if (group.currentSelectable == null || !group.currentSelectable.gameObject.activeSelf) group.currentSelectable = group.defaultSelectable;
        }
        foreach (var button in current.blockedOptions)
        {
            var selectable = button.GetComponent<UnityEngine.UI.Selectable>();
            if (selectable == null) continue;
            if (!current.selectableStates.ContainsKey(selectable)) current.selectableStates.Add(selectable, selectable.interactable);
            selectable.interactable = false;
        }
        var manager=ApplicationUIGroupManager.Instance;
        var stack=(System.Collections.Generic.List<UIControlGroup>)AccessTools.Field(typeof(ApplicationUIGroupManager),"controlGroupStack").GetValue(manager);
        current!.log("NS_REST_NAV_OWNER current="+manager.currentControlGroup?.name+" interactive="+manager.currentControlGroup?.interactable
            +" stack="+string.Join(";",stack.Take(8).Select(g=>g==null?"none":g.name+":"+g.gameObject.activeInHierarchy+":"+g.bindPanel?.state)));
        current!.log("NS_REST_OPTIONS group=" + group?.name + " restoreActive=" + restore.gameObject.activeInHierarchy
            +" restoreSelf="+restore.gameObject.activeSelf+" restoreParent="+restore.transform.parent.gameObject.activeInHierarchy
            + " default=" + group?.defaultSelectable?.name + " travelActive=" + __instance.TeleportButton.gameObject.activeInHierarchy
            + " memoryActive=" + __instance.MemoryButton.gameObject.activeInHierarchy);
        if (current.fixture && !current.optionsAudited)
        {
            current.optionsAudited = true;
            foreach (var component in __instance.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                if (component is UIControlGroup controls) current.log("NS_REST_GROUP path=" + NativeRoomIsolation.PathOf(controls.transform)
                    + " panel=" + controls.bindPanel?.name + " default=" + controls.defaultSelectable?.name + " exit=" + controls.exitButton?.name + " override=" + controls.IsOverrideCancel);
                foreach (var field in component.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                    if (typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(field.FieldType) && field.GetValue(component) is UnityEngine.Events.UnityEventBase e)
                        for (int i = 0; i < e.GetPersistentEventCount(); i++) current.log("NS_REST_UI_CALLBACK component=" + component.name + " field=" + field.Name + " target=" + e.GetPersistentTarget(i) + " method=" + e.GetPersistentMethodName(i));
            }
        }
    }
    public void Update(GeometryLandingProbe? next)
    {
        if (geometry != next)
        {
            if (sitting) throw new InvalidOperationException("Host room changed during native rest.");
            geometry = next;
        }
        var player = Player.i;
        if (fixture && sitting && !fixtureRestored && player.health.currentValue == player.health.maxHealth.Value)
        { fixtureRestored = true; log("NS_REST_RESOURCE_RESTORED native=True health=" + player.health.currentValue); }
        if (fixture && sitting && Time.realtimeSinceStartup >= nextTrace)
        {
            nextTrace = Time.realtimeSinceStartup + 1;
            var animator = point.myAnimator;
            var state = animator.GetCurrentAnimatorStateInfo(0);
            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            var module = eventSystem?.currentInputModule as InControl.InControlInputModule;
            log("NS_REST_TRANSITION active=" + animator.gameObject.activeInHierarchy + " enabled=" + animator.enabled
                + " speed=" + animator.speed + " culling=" + animator.cullingMode + " state=" + state.fullPathHash
                + " time=" + state.normalizedTime + " clips=" + string.Join(",", animator.GetCurrentAnimatorClipInfo(0).Select(c => c.clip.name))
                + " player=" + player.CurrentStateType + " menu=" + NativeMenu.Open
                + " selected=" + UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject?.name
                + " selectedActive=" + UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject?.activeInHierarchy
                + " panel=" + GameCore.Instance.savePanelUiController.state
                + " module=" + eventSystem?.currentInputModule?.GetType().Name + " cancelBound=" + ReferenceEquals(module?.CancelAction, player.playerInput.uiActions.Cancel)
                + " navigation=" + eventSystem?.sendNavigationEvents);
        }
        foreach (var collider in colliderStates.Keys) if (collider != null) collider.enabled = false;
        if (sitting && player.actions.Enabled && player.CurrentStateType == PlayerStateType.Normal && Time.realtimeSinceStartup > enteredAt + 0.5f)
        { sitting = false; log("NS_REST_EXIT nativeControlRestored=True"); }
        if (geometry == null || geometry.RestPoints.Length == 0 || sitting) return;
        var foot = geometry.Map.ToSource(new Point(player.transform.position.x, player.transform.position.y));
        Point nearest = geometry.RestPoints.OrderBy(p => Math.Abs(p.X - foot.X) + Math.Abs(p.Y - foot.Y)).First();
        Point location = geometry.Map.ToTarget(nearest);
        var offset = spawn.position - point.transform.position;
        point.transform.position = new Vector3(location.X - offset.x, location.Y - offset.y, originalPosition.z);
        if (fixture && !fixturePlaced && !NativeRoomEntry.Active)
        {
            fixturePlaced = true; player.Velocity = Vector2.zero;
            player.SetPosition(new Vector3(location.X, location.Y + 0.1f, player.transform.position.z), true);
            player.SafeGroundRecorder.ForceSave(new Vector2(location.X, location.Y)); player.ResetNeedToCheckGroundFlag();
            log("NS_REST_FIXTURE_PLACED explicitDiagnostic=True hostX=" + nearest.X + " hostY=" + nearest.Y);
        }
    }
    public static void Interact()
    {
        var rest = current; var player = Player.i;
        if (rest?.geometry == null || rest.geometry.RestPoints.Length == 0 || rest.sitting || NativeMenu.Open || !player.actions.Enabled || !player.IsOnGround) return;
        var anchor = rest.spawn.position;
        if (Math.Abs(player.transform.position.x - anchor.x) > rest.geometry.Map.Scale * 1.8f
            || Math.Abs(player.transform.position.y - anchor.y) > rest.geometry.Map.Scale * 1.5f) return;
        rest.sitting = true; rest.enteredAt = Time.realtimeSinceStartup; rest.entering = true;
        if (rest.fixture)
        {
            player.health.CurrentHealthValue = Math.Max(1, player.health.maxHealth.Value - 60);
            rest.log("NS_REST_RESOURCE_FIXTURE explicitDiagnostic=True health=" + player.health.currentValue);
        }
        try { rest.point.SaveSpawnPosition(); }
        finally { rest.entering = false; }
        rest.log("NS_REST_ENTER native=True state=" + player.CurrentStateType + " anchor=" + anchor);
    }
    public static Bounds PresentationBounds(Collider2D collider)
    {
        if (collider.enabled) return collider.bounds;
        if (!(collider is BoxCollider2D box)) throw new InvalidOperationException("Unaudited resting player collider.");
        var scale = box.transform.lossyScale;
        return new Bounds(box.transform.TransformPoint(box.offset), new Vector3(Math.Abs(box.size.x * scale.x), Math.Abs(box.size.y * scale.y), 0));
    }
    public void Dispose()
    {
        if (current != this) return;
        presentationCulling?.Dispose(); presentationCulling = null;
        if (sitting && point != null) { point.PlayerStandUp(); point.ReturnPlayerControl(); }
        if (point != null) point.transform.position = originalPosition;
        if (point != null && point.myAnimator != null) point.myAnimator.cullingMode = originalCulling;
        foreach (var entry in activeStates) if (entry.Key != null) entry.Key.SetActive(entry.Value);
        foreach (var entry in colliderStates) if (entry.Key != null) entry.Key.enabled = entry.Value;
        foreach (var entry in selectableStates) if (entry.Key != null) entry.Key.interactable = entry.Value;
        current = null; harmony.UnpatchSelf();
    }
}
