#if HOLLOWSOLS_PUBLIC_RELEASE
using System;
using YiGodhome.Bridge;
namespace YiGodhome;
// No input binding, menu, collision renderer, travel or detailed-capture code.
internal sealed class DisabledPlayerTools : IDisposable
{
    public bool Open => false;
    public bool Collision => false;
    internal int LastEdges => 0;
    public DisabledPlayerTools(Action<string> travel, Func<bool> canTravel, Action<bool> detail, Func<bool> active, Func<string> status, Action<string> log) { }
    public void Update() { }
    public void ConfigureDiagnostic() { }
    public void Draw(ContactFrame? frame, Pose? pose) { }
    public void Dispose() { }
}
#else
using System;
using UnityEngine;
using YiGodhome.Bridge;
using Pose = YiGodhome.Bridge.Pose;

namespace YiGodhome;

// Explicit user-operated Godhome tools. No grants, save operations or campaign destinations.
internal sealed class PlayerDebugMenu : InControl.PlayerActionSet, IDisposable
{
    public bool Open { get; private set; }
    public bool Collision { get; private set; }
    internal int LastEdges { get; private set; }
    private string? collisionPerfMode;
    internal void ConfigureDiagnostic()
    {
        if (!SessionConfiguration.Current.RunFixtures || !SessionConfiguration.Current.Has("trace-probe") || EncounterProfile.Selected.Family != EncounterProfile.NightmareGrimm) return;
        if (collisionPerfMode == null) {
            string directory = System.IO.Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!;
            string path = System.IO.Path.Combine(directory,"collision-perf.request");
            collisionPerfMode = System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path).Trim() : "alternate";
            if (collisionPerfMode != "alternate" && collisionPerfMode != "on" && collisionPerfMode != "off")
                throw new InvalidOperationException("Invalid bounded collision performance mode.");
            log("HK_COLLISION_PERF_MODE mode=" + collisionPerfMode + " traceOnly=True startupOnly=True");
        }
        Collision = collisionPerfMode == "on" || collisionPerfMode == "alternate" && ((int)(Time.realtimeSinceStartup / 2) & 1) == 0;
    }
    private GameObject? contourCanvas;
    private CollisionContourGraphic? contours;
    private readonly Action<string> log;
    private bool priorCursor;
    private CursorLockMode priorLock;
    private readonly Action<string> travel;
    private readonly Func<bool> canTravel;
    private readonly Action<bool> detail;
    private readonly Func<bool> detailActive;
    private readonly Func<string> detailStatus;
    private readonly InControl.PlayerAction toggle;
    public PlayerDebugMenu(Action<string> travel, Func<bool> canTravel, Action<bool> detail, Func<bool> detailActive, Func<string> detailStatus, Action<string> log) { this.log = log; this.travel = travel; this.canTravel = canTravel;
        this.detail = detail; this.detailActive = detailActive; this.detailStatus = detailStatus;
        toggle = CreatePlayerAction("YiDebugMenu"); toggle.AddDefaultBinding(InControl.Key.F9); }
    public void Update()
    { if (Application.isFocused && toggle.WasPressed) SetOpen(!Open); }
    private void SetOpen(bool value)
    {
        if (Open == value) return;
        if (value) { priorCursor = Cursor.visible; priorLock = Cursor.lockState; }
        Open = value;
        if (!value) { Cursor.visible = priorCursor; Cursor.lockState = priorLock; }
    }
    public void Dispose() { SetOpen(false); if (contourCanvas != null) UnityEngine.Object.Destroy(contourCanvas); contours = null; contourCanvas = null; Destroy(); }
    public void Draw(ContactFrame? frame, Pose? pose)
    {
        LastEdges = 0;
        int originalDepth = GUI.depth;
        var originalMatrix = GUI.matrix;
        try {
        GUI.depth = -1100;
        GUI.matrix = Matrix4x4.identity;
        if (Event.current.type == EventType.Repaint) {
            var camera = Camera.main;
            if (Collision && camera != null) {
                EnsureContours();
                contours!.enabled = true;
                contours.Show(frame, pose, camera, Collision, Collision);
                LastEdges = contours.Edges;
            } else if (contours != null) contours.enabled = false;
        }
        if (!Open) return;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        var oldColor = GUI.color; int oldDepth = GUI.depth; bool oldEnabled = GUI.enabled;
        try {
            GUI.depth = -1200; GUI.color = Color.white;
            GUILayout.BeginArea(new Rect(30, 50, 410, 400), "Godhome testing", GUI.skin.window);
            if (GUILayout.Button(detailActive() ? "Stop detailed logging" : "Start detailed logging")) detail(!detailActive());
            GUILayout.Label(detailStatus());
            Collision = GUILayout.Toggle(Collision, "Show hurtboxes / hitboxes");
            GUILayout.Label("Green: targets   Red: attacks   Cyan: Yi melee");
            GUILayout.Label("Magenta: parry contact / active air-deflect");
            GUILayout.Label("Spatial contacts; native timing and facing still decide.");
            var pv = PureVesselCombat.Current;
            bool counterEnabled = GUI.enabled;
            GUI.enabled = counterEnabled && pv != null && pv.Health > 0;
            bool counterOnly = GUILayout.Toggle(pv?.CounterOnly == true, "Pure Vessel: counter only (parry spam)");
            if (pv != null && counterOnly != pv.CounterOnly) pv.SetCounterOnly(counterOnly);
            GUI.enabled = counterEnabled;
            bool previous = GUI.enabled; GUI.enabled = previous && canTravel();
            if (GUILayout.Button("Teleport to Godhome hub")) { travel("GG_Atrium"); SetOpen(false); }
            if (GUILayout.Button("Teleport to Hall of Gods")) { travel("GG_Workshop"); SetOpen(false); }
            GUI.enabled = previous;
            if (GUILayout.Button("Close")) SetOpen(false);
            GUILayout.EndArea();
        } finally { GUI.color = oldColor; GUI.depth = oldDepth; GUI.enabled = oldEnabled; }
        } finally { GUI.depth = originalDepth; GUI.matrix = originalMatrix; }
    }
    private void EnsureContours()
    {
        if (contourCanvas != null) return;
        contourCanvas = new GameObject("YiGodhomeCollisionBounds", typeof(RectTransform));
        var canvas = contourCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32760;
        var body = new GameObject("ExactContours", typeof(RectTransform));
        body.transform.SetParent(contourCanvas.transform, false);
        var rect = (RectTransform)body.transform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero; rect.anchoredPosition = Vector2.zero;
        contours = body.AddComponent<CollisionContourGraphic>();
        contours.raycastTarget = false; contours.Log = log;
    }
}

#endif
