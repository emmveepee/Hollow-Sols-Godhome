using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using YiGodhome.Bridge;
using YiGodhome.Geometry;
using Pose = YiGodhome.Bridge.Pose;
namespace YiGodhome;

internal sealed class NativeRestCapture : IDisposable
{
    private readonly VisualEndpoint endpoint;
    private readonly Guid session;
    private readonly Camera camera;
    private readonly RenderTexture target, resolved;
    private readonly Texture2D pixels;
    private readonly NativeHudReadback readback;
    private float next;
    private int captured;
    private float stableExtent;
    private Vector3 centerOffset;
    private long geometryFrame = -1;
    private readonly Action<string> log;
    private readonly string directory;
    private readonly bool diagnostic;
    private int synchronized;
    private bool compared, farVerified;
    private int costSamples;
    private double costTotal, costMax, readbackTotal;
    private float costAt;
    public NativeRestCapture(VisualEndpoint endpoint, Guid session, Action<string> log)
    {
        this.endpoint = endpoint; this.session = session; this.log = log;
        readback = new NativeHudReadback(endpoint, log, "rest");
        directory = Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!;
        diagnostic = YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures;
        camera = new GameObject("YiGodhome Native Rest Camera").AddComponent<Camera>(); camera.enabled = false;
        camera.orthographic = true; camera.aspect = 1; camera.nearClipPlane = 0.1f; camera.farClipPlane = 500;
        camera.cullingMask = 1 << 30; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
        camera.allowHDR = false; camera.allowMSAA = false; camera.useOcclusionCulling = false;
        target = new RenderTexture(2048, 2048, 24, RenderTextureFormat.ARGB32); target.filterMode = FilterMode.Bilinear; target.Create();
        resolved = new RenderTexture(1024, 1024, 0, RenderTextureFormat.ARGB32); resolved.Create();
        pixels = new Texture2D(1024, 1024, TextureFormat.RGBA32, false); camera.targetTexture = target;
    }
    public void Capture(Pose pose, NativeRestPoint rest)
    {
        // In-flight pixels own the resolved target until GPU completion. Observe
        // room changes even while busy, so old-room callbacks cannot publish.
        readback.Observe(pose.GeometryFrame);
        if (readback.Busy) return;
        if (Time.realtimeSinceStartup < next) return;
        long captureStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        next = Math.Max(next + 1f / 60, Time.realtimeSinceStartup);
        var root = rest.VisualRoot;
        var renderers = root == null ? Array.Empty<Renderer>() : root.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
        bool visible = renderers.Length > 0 && rest.Geometry != null;
        bool asynchronous = visible && readback.Supported && !diagnostic;
        var frame = endpoint.RentFrame(visible ? 1024 * 1024 * 4 : 4);
        if (visible)
        {
            var bounds = renderers[0].bounds; foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
            if (geometryFrame != pose.GeometryFrame)
            {
                geometryFrame = pose.GeometryFrame; stableExtent = 0;
                centerOffset = bounds.center - root!.position;
                foreach (var r in renderers) log("NS_REST_RENDERER path=" + NativeRoomIsolation.PathOf(r.transform)
                    + " type=" + r.GetType().Name + " position=" + r.transform.position + " sorting=" + r.sortingLayerID + ":" + r.sortingOrder
                    + " materials=" + string.Join(",", r.sharedMaterials.Where(m => m != null).Select(m => m.name + ":" + m.shader.name + ":" + m.renderQueue)));
            }
            var center = root!.position + centerOffset;
            float required = Math.Max(Math.Max(Math.Abs(bounds.min.x - center.x), Math.Abs(bounds.max.x - center.x)),
                Math.Max(Math.Abs(bounds.min.y - center.y), Math.Abs(bounds.max.y - center.y))) + 2;
            if (required > stableExtent)
            {
                stableExtent = (float)Math.Ceiling(required / 4) * 4;
                log("NS_REST_CAPTURE_GRID geometry=" + geometryFrame + " extent=" + stableExtent + " required=" + required);
            }
            float extent = stableExtent;
            var objects = renderers.Select(r => r.gameObject).Distinct().ToArray(); var layers = objects.Select(o => o.layer).ToArray();
            var old = RenderTexture.active;
            try
            {
                foreach (var obj in objects) obj.layer = 30;
                camera.orthographicSize = extent; camera.transform.position = new Vector3(center.x, center.y, -100);
                // Animator has selected this frame's sprite, but the native FX
                // LateUpdate may still hold the previous atlas rectangle.
                foreach (var r in renderers.OfType<SpriteRenderer>())
                    foreach (var fx in r.GetComponents<MonoBehaviour>().Where(f => f != null
                        && (f.GetType().Name == "_2dxFX_Twist" || f.GetType().Name == "_2dxFX_Pattern")))
                    {
                        // Native OnBecameInvisible disables the effect and its
                        // OnDisable restores the default material. The proxy
                        // camera must refresh it even when the sprite is unchanged.
                        AccessTools.Method(fx.GetType(), "XUpdate").Invoke(fx, null);
                        var field = AccessTools.Field(fx.GetType(), "_lastsprite");
                        if (field == null || r.sprite == null || (Sprite)field.GetValue(fx) == r.sprite) continue;
                        string before = ((Sprite)field.GetValue(fx))?.name ?? "null";
                        // Native FX disables itself when outside the main camera.
                        // Its visible offscreen sprite still needs current atlas UVs.
                        bool compare = diagnostic && !compared && captured > 60 && r.sprite != null && r.sprite.name.StartsWith("SavePointPowerA", StringComparison.Ordinal);
                        if (compare) Snapshot("rest-atlas-before.png");
                        AccessTools.Method(fx.GetType(), "UpdateAnimSprite").Invoke(fx, null);
                        if ((Sprite)field.GetValue(fx) != r.sprite) continue;
                        if (compare) { Snapshot("rest-atlas-after.png"); compared = true; }
                        if (++synchronized <= 8 || synchronized % 120 == 0)
                            log("NS_REST_ATLAS_SYNC frame=" + Time.frameCount + " effect=" + fx.GetType().Name
                                + " before=" + before + " after=" + r.sprite.name + " effectEnabled=" + fx.enabled + " count=" + synchronized);
                    }
                camera.Render(); Graphics.Blit(target, resolved); RenderTexture.active = resolved;
                if (!asynchronous)
                {
                    long readbackStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                    pixels.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0, false); pixels.Apply(false, false);
                    pixels.GetRawTextureData<byte>().CopyTo(frame.Rgba);
                    readbackTotal += (System.Diagnostics.Stopwatch.GetTimestamp() - readbackStarted) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                }
                if (diagnostic && !farVerified && rest.Geometry != null) {
                    var anchor = rest.Geometry.Map.ToSource(new Point(root!.position.x, root.position.y));
                    float distance = Vector2.Distance(new Vector2(anchor.X, anchor.Y), new Vector2(pose.X, pose.Y));
                    if (distance > 6 && ActorPixels.Visible(frame.Rgba)) {
                        farVerified = true; log("NS_REST_FAR_VISIBLE geometry=" + pose.GeometryFrame + " distance=" + distance + " opaque=True nativeActive=" + root.gameObject.activeInHierarchy);
                    }
                }
                if (++captured == 30 && diagnostic) File.WriteAllBytes(Path.Combine(directory, "native-rest.png"), pixels.EncodeToPNG());
            }
            catch { frame.ReleasePixels(); throw; }
            finally { RenderTexture.active = old; for (int i = 0; i < objects.Length; i++) if (objects[i] != null) objects[i].layer = layers[i]; }
            var map = rest.Geometry!.Map;
            var corner = map.ToSource(new Point(center.x - extent, center.y - extent));
            frame.X = corner.X; frame.Y = corner.Y; frame.WorldWidth = frame.WorldHeight = 2 * extent / map.Scale;
            double cost = (System.Diagnostics.Stopwatch.GetTimestamp() - captureStarted) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            costSamples++; costTotal += cost; costMax = Math.Max(costMax, cost);
            if (Time.realtimeSinceStartup - costAt >= 2) {
                log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "NS_REST_COST geometry={0} samples={1} meanMs={2:F3} maxMs={3:F3} readbackMeanMs={4:F3}",
                    pose.GeometryFrame, costSamples, costTotal / costSamples, costMax, readbackTotal / costSamples));
                costAt = Time.realtimeSinceStartup; costSamples = 0; costTotal = costMax = readbackTotal = 0;
            }
        }
        else { Array.Clear(frame.Rgba, 0, frame.Rgba.Length); frame.WorldWidth = frame.WorldHeight = 1; }
        frame.Session = session; frame.GeometryFrame = pose.GeometryFrame; frame.NativeFrame = Time.frameCount;
        frame.CaptureTick = captureStarted; frame.Width = frame.Height = visible ? 1024 : 1;
        if (asynchronous) readback.Request(resolved, frame, 0, 1024, false, null);
        else endpoint.PublishPixels(frame);
    }
    private void Snapshot(string name)
    {
        camera.Render(); Graphics.Blit(target, resolved); RenderTexture.active = resolved;
        pixels.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0, false); pixels.Apply(false, false);
        File.WriteAllBytes(Path.Combine(directory, name), pixels.EncodeToPNG());
    }
    public void Dispose()
    {
        readback.Dispose();
        UnityEngine.Object.Destroy(camera.gameObject); target.Release(); resolved.Release();
        UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(resolved); UnityEngine.Object.Destroy(pixels);
    }
}
