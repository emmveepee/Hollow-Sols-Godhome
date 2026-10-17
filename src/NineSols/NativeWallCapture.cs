using System;
using System.Linq;
using UnityEngine;
using YiGodhome.Bridge;
using Pose = YiGodhome.Bridge.Pose;

namespace YiGodhome;

// One native visual lease/atlas serves every Hall strip. Collision remains native Yi terrain.
internal sealed class NativeWallCapture : IDisposable
{
    private readonly VisualEndpoint endpoint;
    private readonly Guid session;
    private readonly Action<string> log;
    private NativeContentRepository.Lease? lease;
    private Renderer[] renderers = Array.Empty<Renderer>();
    private GameObject[] objects = Array.Empty<GameObject>();
    private int[] layers = Array.Empty<int>();
    private readonly Camera camera;
    private readonly RenderTexture target;
    private readonly Texture2D pixels;
    private readonly byte[] atlas = new byte[128 * 1024 * 4];
    private float next, costAt;
    private int samples;
    private double total, maximum;
    private long geometry = -1;
    private bool captured;
    internal NativeWallCapture(VisualEndpoint endpoint, Guid session, Action<string> log)
    {
        this.endpoint = endpoint; this.session = session; this.log = log;
        camera = new GameObject("YiGodhome Native Wall Camera").AddComponent<Camera>(); camera.enabled = false;
        camera.orthographic = true; camera.aspect = 128f / 1024; camera.nearClipPlane = 0.1f; camera.farClipPlane = 500;
        camera.cullingMask = 1 << 30; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
        camera.allowHDR = camera.allowMSAA = camera.useOcclusionCulling = false;
        target = new RenderTexture(128, 1024, 24, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
        pixels = new Texture2D(128, 1024, TextureFormat.RGBA32, false);
    }
    internal void Capture(Pose pose, GeometryLandingProbe terrain)
    {
        if (YiGodhome.Geometry.RoomFeatures.ClimbingWalls(terrain.SourceScene).Length == 0) { Retire(); return; }
        var repository = NativeContentRepository.Current;
        if (repository?.Ready != true) return;
        if (lease == null) {
            lease = repository.Borrow(NativeContentKind.HallClimbTrim);
            lease.Root.transform.position = new Vector3(40000, 40000, 0); lease.Root.SetActive(true);
            renderers = lease.Root.GetComponentsInChildren<Renderer>(true);
            objects = renderers.Select(r => r.gameObject).Distinct().ToArray(); layers = new int[objects.Length];
            if (renderers.Length == 0) throw new InvalidOperationException("Native wall artwork has no renderers.");
            log("NS_WALL_ART_CREATED owner=" + lease.Root.GetInstanceID() + " scene=" + lease.Root.scene.name + " nativeArt=True visualOnly=True");
        }
        if (Time.realtimeSinceStartup < next) return;
        next = Time.realtimeSinceStartup + 1f / 10;
        long tick = System.Diagnostics.Stopwatch.GetTimestamp();
        var frame = endpoint.RentFrame(128 * 1024 * 4);
        var previous = RenderTexture.active;
        try {
            if (!captured || geometry != pose.GeometryFrame) {
                var bounds = renderers[0].bounds; foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                camera.orthographicSize = Math.Max(bounds.extents.y, bounds.extents.x / camera.aspect) * 1.04f;
                camera.transform.position = new Vector3(bounds.center.x, bounds.center.y, -100);
                for (int i = 0; i < objects.Length; i++) { layers[i] = objects[i].layer; objects[i].layer = 30; }
                camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 128, 1024), 0, 0, false); pixels.Apply(false, false);
                pixels.GetRawTextureData<byte>().CopyTo(atlas);
                if (!ActorPixels.Visible(atlas)) throw new InvalidOperationException("Native wall atlas rendered empty.");
                int first = -1, last = -1, occupied = 0;
                for (int y = 0; y < 1024; y++) {
                    bool opaque = false;
                    for (int x = 0; x < 128; x++) if (atlas[(y * 128 + x) * 4 + 3] > 32) { opaque = true; break; }
                    if (opaque) { if (first < 0) first = y; last = y; occupied++; }
                }
                int gaps = last - first + 1 - occupied;
                if (gaps > 2) throw new InvalidOperationException("Native wall trim has disconnected segments: " + gaps);
                geometry = pose.GeometryFrame; captured = true;
                log("NS_WALL_ART_CAPTURE geometry=" + geometry + " nativeFrame=" + Time.frameCount + " width=128 height=1024 opaque=True blankInteriorRows=" + gaps + " cachedStatic=True");
                if (SessionConfiguration.Current.RunFixtures) System.IO.File.WriteAllBytes(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!, "native-wall-atlas.png"), pixels.EncodeToPNG());
            }
            // These admitted native tiles have no animator/effect/gameplay components.
            // Refresh the static atlas lease, avoiding repeated identical GPU readback.
            Buffer.BlockCopy(atlas, 0, frame.Rgba, 0, atlas.Length);
            frame.Session = session; frame.GeometryFrame = pose.GeometryFrame; frame.NativeFrame = Time.frameCount;
            frame.CaptureTick = tick; frame.Width = 128; frame.Height = 1024; frame.WorldWidth = frame.WorldHeight = 1;
            endpoint.PublishPixels(frame);
        } catch { frame.ReleasePixels(); throw; }
        finally { RenderTexture.active = previous; for (int i = 0; i < objects.Length; i++) objects[i].layer = layers[i]; }
        double cost = (System.Diagnostics.Stopwatch.GetTimestamp() - tick) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        samples++; total += cost; maximum = Math.Max(maximum, cost);
        if (Time.realtimeSinceStartup - costAt >= 2) {
            log(FormattableString.Invariant($"NS_WALL_ART_COST samples={samples} meanMs={total / samples:F3} maxMs={maximum:F3}"));
            costAt = Time.realtimeSinceStartup; samples = 0; total = maximum = 0;
        }
    }
    private void Retire()
    {
        if (lease == null) return;
        lease.Dispose(); lease = null; renderers = Array.Empty<Renderer>(); objects = Array.Empty<GameObject>();
        captured = false; log("NS_WALL_ART_RETIRED activeLeases=0");
    }
    public void Dispose()
    {
        Retire(); UnityEngine.Object.Destroy(camera.gameObject); target.Release();
        UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(pixels);
    }
}
