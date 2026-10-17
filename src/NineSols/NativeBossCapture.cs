using System;
using System.IO;
using System.Linq;
using UnityEngine;
using YiGodhome.Bridge;
using Pose = YiGodhome.Bridge.Pose;
namespace YiGodhome;

internal sealed class NativeBossCapture : IDisposable
{
    private readonly VisualEndpoint endpoint;
    private readonly Guid session;
    private readonly Action<string> log;
    private readonly Camera camera;
    private readonly RenderTexture target;
    private readonly Texture2D pixels;
    private readonly int layer;
    private float next;
    private int captures;
    private static int nextCaptureOwner;
    private readonly int captureOwner = ++nextCaptureOwner;
    private int layoutCapture;
    private int lastViewCount = -1, capturedViewCount = -1;
    private float viewCountAt;
    private bool injuryCaptured;
    private readonly NativeHudReadback readback;
    public NativeBossCapture(VisualEndpoint endpoint, Guid session, Action<string> log,Camera hudCamera)
    {
        this.endpoint = endpoint; this.session = session; this.log = log;
        readback = new NativeHudReadback(endpoint, log, "boss_hud");
        // Share the exact initialized player-HUD owner. The retained title
        // UIManager singleton can have no PlayerUI after a native level change.
        camera = hudCamera;
        if (camera == null) throw new InvalidOperationException("Native boss HUD camera unavailable.");
        var occupied = UnityEngine.Object.FindObjectsOfType<Renderer>(true).Select(r => r.gameObject.layer).ToArray();
        layer = Enumerable.Range(8, 24).Reverse().FirstOrDefault(l => l != 30 && !occupied.Contains(l));
        if (layer == 0) throw new InvalidOperationException("No isolated boss HUD layer.");
        int width = Math.Min(1920, VisualCodec.MaxHudSide), height = Mathf.RoundToInt(width / camera.aspect);
        target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32); target.Create();
        pixels = new Texture2D(width, height / 3, TextureFormat.RGBA32, false);
    }
    public void Capture(Pose pose, UIBossHP? view)
    {
        readback.Observe(pose.GeometryFrame);
        var views = NativeBossHealth.Views.Where(v => v.gameObject.activeInHierarchy).ToArray();
        if (lastViewCount != views.Length) {
            lastViewCount = views.Length; capturedViewCount = -1; viewCountAt = Time.realtimeSinceStartup;
            log("NS_BOSS_HUD_VIEWS count=" + views.Length + " frame=" + Time.frameCount + " geometry=" + pose.GeometryFrame);
            if (SessionConfiguration.Current.RunFixtures)
                foreach (var widget in views)
                {
                    log("NS_BOSS_HUD_LAYOUT captureOwner=" + captureOwner + " widget=" + widget.GetInstanceID()
                        + " position=" + widget.transform.localPosition + " scale=" + widget.transform.localScale);
                    var title = HarmonyLib.AccessTools.Field(typeof(UIBossHP), "bossNameText").GetValue(widget) as Component;
                    if (title != null)
                        log("NS_BOSS_HUD_TITLE widget=" + widget.GetInstanceID() + " active=" + title.gameObject.activeInHierarchy
                            + " enabled=" + (title as Behaviour)?.enabled + " position=" + title.transform.localPosition
                            + " scale=" + title.transform.localScale + " color=" + HarmonyLib.AccessTools.Property(title.GetType(), "color")?.GetValue(title));
                }
        }
        view = views.FirstOrDefault();
        if (readback.Busy || Time.realtimeSinceStartup < next) return;
        next = Time.realtimeSinceStartup + 1f / 20;
        long costStart = System.Diagnostics.Stopwatch.GetTimestamp();
        bool visible = view != null && view.gameObject.activeInHierarchy;
        bool asynchronous = visible && readback.Supported;
        bool compare = asynchronous && NativeWorkCost.Enabled && !readback.Verified;
        bool evidence = YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures
            && (captures + 1 == 30 || !injuryCaptured && visible && view!.bindingPosture.InternalInjury > 1
                || capturedViewCount != views.Length && Time.realtimeSinceStartup - viewCountAt >= .75f);
        var frame = endpoint.RentFrame(visible ? pixels.width * pixels.height * 4 : 4);
        int bottom = 0;
        if (visible)
        {
            var objects = views.SelectMany(v => v.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).Distinct().ToArray();
            var layers = objects.Select(o => o.layer).ToArray();
            var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active; int oldMask = camera.cullingMask;
            var oldClear = camera.clearFlags; var oldColor = camera.backgroundColor;
            try
            {
                float y = camera.WorldToViewportPoint(view!.transform.position).y;
                bottom = Mathf.Clamp(Mathf.RoundToInt(y * target.height - pixels.height / 2f), 0, target.height - pixels.height);
                foreach (var obj in objects) obj.layer = layer;
                camera.targetTexture = target; camera.cullingMask = 1 << layer;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
                camera.Render(); RenderTexture.active = target;
                if (!asynchronous || compare || evidence) {
                    pixels.ReadPixels(new Rect(0, bottom, pixels.width, pixels.height), 0, 0, false);
                    NativeReadbackVerification.Verify(pixels, "boss_hud", log);
                }
                if (!asynchronous) pixels.GetRawTextureData<byte>().CopyTo(frame.Rgba);
                if (!injuryCaptured && view.bindingPosture.InternalInjury > 1)
                {
                    string root = Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!;
                    if (YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures)
                    {
                        injuryCaptured = true;
                        File.WriteAllBytes(Path.Combine(root, "native-boss-injury.png"), pixels.EncodeToPNG());
                        log("NS_BOSS_INJURY_CAPTURE nativeInjury=" + view.bindingPosture.InternalInjury + " frame=" + Time.frameCount);
                    }
                }
                if (++captures == 30 && evidence)
                {
                    log("NS_BOSS_CAPTURE pixels=" + pixels.width + "x" + pixels.height + " bottom=" + bottom + " source=" + camera.name
                        + " visiblePixels=" + pixels.GetPixels32().Count(p => p.a != 0));
                    string root = Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!;
                    if (YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures) File.WriteAllBytes(Path.Combine(root, "native-boss-hud.png"), pixels.EncodeToPNG());
                }
                if (capturedViewCount != views.Length && Time.realtimeSinceStartup - viewCountAt >= 0.75f) {
                    capturedViewCount = views.Length;
                    if (YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures) {
                        string root = Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!;
                        File.WriteAllBytes(Path.Combine(root, "native-boss-hud-" + views.Length + ".png"), pixels.EncodeToPNG());
                        string filename = "native-boss-hud-owner-" + captureOwner + "-transition-" + (++layoutCapture) + "-count-" + views.Length + ".png";
                        File.WriteAllBytes(Path.Combine(root, filename), pixels.EncodeToPNG());
                        log("NS_BOSS_HUD_LAYOUT_CAPTURE file=" + filename + " frame=" + Time.frameCount);
                        if(layoutCapture>=4 && views.Length==1) {
                            Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;
                            pixels.ReadPixels(new Rect(0,bottom,pixels.width,pixels.height),0,0,false);
                            File.WriteAllBytes(Path.Combine(root,"native-boss-hud-after-canvas-refresh.png"),pixels.EncodeToPNG());
                            log("NS_BOSS_HUD_CANVAS_REFRESH boundedDiagnostic=True frame="+Time.frameCount);
                        }
                        foreach (var widget in views) {
                            var title = HarmonyLib.AccessTools.Field(typeof(UIBossHP), "bossNameText").GetValue(widget) as Component;
                            if (title == null) continue;
                            var renderer = title.GetComponent<CanvasRenderer>();
                            if (renderer != null) {
                                var mesh = renderer.GetMesh();
                                if (mesh != null) {
                                    var colors = mesh.colors32;
                                    log("NS_BOSS_HUD_TITLE_MESH widget=" + widget.GetInstanceID() + " vertices=" + mesh.vertexCount
                                        + " alphaMin=" + (colors.Length == 0 ? -1 : colors.Min(c => c.a)) + " alphaMax=" + (colors.Length == 0 ? -1 : colors.Max(c => c.a)));
                                }
                            }
                            log("NS_BOSS_HUD_TITLE_RENDER widget=" + widget.GetInstanceID() + " text=" + HarmonyLib.AccessTools.Property(title.GetType(), "text")?.GetValue(title)
                                + " color=" + HarmonyLib.AccessTools.Property(title.GetType(), "color")?.GetValue(title)
                                + " enabled=" + (title as Behaviour)?.enabled + " canvasAlpha=" + renderer?.GetAlpha() + " cull=" + renderer?.cull
                                + " world=" + title.transform.position + " viewport=" + camera.WorldToViewportPoint(title.transform.position));
                        }
                    }
                }
            }
            catch { frame.ReleasePixels(); throw; }
            finally
            {
                RenderTexture.active = oldActive; camera.targetTexture = oldTarget; camera.cullingMask = oldMask;
                camera.clearFlags = oldClear; camera.backgroundColor = oldColor;
                for (int i = 0; i < objects.Length; i++) if (objects[i] != null) objects[i].layer = layers[i];
            }
        }
        else Array.Clear(frame.Rgba, 0, frame.Rgba.Length);
        frame.Session = session; frame.GeometryFrame = pose.GeometryFrame; frame.NativeFrame = Time.frameCount;
        frame.CaptureTick = System.Diagnostics.Stopwatch.GetTimestamp(); frame.Width = visible ? pixels.width : 1; frame.Height = visible ? pixels.height : 1;
        frame.WorldWidth = 1; frame.WorldHeight = (float)pixels.height / target.height; frame.Y = (float)bottom / target.height;
        if (asynchronous) readback.Request(target, frame, bottom, pixels.height, false, compare ? pixels.GetRawTextureData<byte>().ToArray() : null);
        else endpoint.PublishPixels(frame);
        NativeWorkCost.BossHud.Record(costStart, "boss_hud", log);
    }
    public void Dispose() { readback.Dispose(); target.Release(); UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(pixels); }
}
