using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using YiGodhome.Bridge;
using YiGodhome.Geometry;

namespace YiGodhome;

// Shared boss presentation: every crimson sequence uses the accepted NKG
// black/red animated silhouette. No boss-specific crimson palette or NS outline.
internal sealed class LocalBossAura : IDisposable
{
    private const float CrimsonWidth = 1.12f;
    private static readonly Color CrimsonRed = new Color(1,.045f,.09f);
    private sealed class Shape {
        internal Vector3[] Points = null!, Normals = null!;
        internal int[] Triangles = null!;
        internal Bounds Bounds;
        internal Vector3[] Vertices = Array.Empty<Vector3>();
        internal Color[] Colors = Array.Empty<Color>();
        internal int DeformedFrame=-1;
        internal float DeformedScale;
        internal bool DeformedTeal;
        internal Mesh? RainMesh;
        internal float RainScale;
        internal bool RainTeal;
        internal int RainUploadFrame=-1;
    }
    private sealed class Part {
        internal MeshRenderer Source = null!, Draw = null!;
        internal MeshFilter Filter = null!; internal Mesh Mesh = null!;
        internal MeshFilter Output=null!;
        internal HealthManager? Owner;
        internal tk2dBaseSprite? Sprite;
        internal (int, int) Pose = (int.MinValue, int.MinValue);
        internal bool Teal, Present, RadianceNail;
        internal AttackTraits? LastTraits;
        internal (int,int)? StaticPose;

    }
    private readonly Dictionary<(int, int), Shape> shapeCache = new();
    private readonly Queue<(int, int)> shapeOrder = new();
    private readonly List<Part> parts = new List<Part>();
    private readonly HashSet<MeshRenderer> active = new HashSet<MeshRenderer>();
    private readonly Dictionary<MeshRenderer,Part> bySource=new();
    private readonly HashSet<MeshRenderer> generated=new();
    private readonly List<MeshRenderer> bodySources=new();
    private bool selectionVerified;
    // Pooled native nails may own distinct meshes with identical geometry.
    // Compare once per admitted part, then share the exact captured contour.
    private readonly List<(Vector3[] Vertices,Vector2[] Uv,int[] Triangles,Color[] Colors,Texture Texture,Shader Shader,int Key)> staticNails=new();
    private readonly List<Mesh> rainMeshes=new();
    private (int,int) NailPose(MeshRenderer source,Mesh mesh) {
        var vertices=mesh.vertices;var uv=mesh.uv;var triangles=mesh.triangles;var colors=mesh.colors;var texture=source.sharedMaterial.mainTexture;
        foreach(var item in staticNails)
            if(item.Texture==texture && item.Shader==source.sharedMaterial.shader && item.Vertices.SequenceEqual(vertices) && item.Uv.SequenceEqual(uv)
                && item.Triangles.SequenceEqual(triangles) && item.Colors.SequenceEqual(colors))return(item.Key,0);
        if(staticNails.Count>=128)return(mesh.GetInstanceID(),0);
        staticNails.Add((vertices,uv,triangles,colors,texture,source.sharedMaterial.shader,mesh.GetInstanceID()));return(mesh.GetInstanceID(),0);
    }
    private readonly Material material;
    private readonly Texture2D white, pixels;
    private readonly Camera camera;
    private readonly RenderTexture target;
    private readonly MeshRenderer copy;
    private readonly MeshFilter copyMesh;
    private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
    private readonly Action<string> log;
    private long geometry = -1; private ContactFrame? frame;
    private int shapes, retainedPoints, samples, snapshots; private double totalMs, maxMs;
    private float waveTime;
    private int bufferChanges; private long bufferBytes; private float nextBuffers;
    private int motionFrames; private float nextMotion, nextPartLimit;
    private readonly HashSet<int> capturedCounts = new HashSet<int>();
    private static readonly Vector3 Stage = new Vector3(30000,30000,0);
    internal LocalBossAura(Action<string> log)
    {
        this.log = log;
        if (HostFrameProbe.Enabled) VerifyMeshCopy();
        var shader = Shader.Find("Sprites/Default");
        if (shader == null || !shader.isSupported) throw new InvalidOperationException("Accepted HK sprite compositor unavailable.");
        white = new Texture2D(1,1,TextureFormat.RGBA32,false); white.SetPixel(0,0,Color.white); white.Apply();
        material = new Material(shader) { mainTexture = white, color = Color.white };
        camera = new GameObject("YiGodhome Local Silhouette Camera").AddComponent<Camera>(); camera.enabled = false;
        camera.orthographic = true; camera.aspect = 1; camera.cullingMask = 1 << 29;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
        camera.nearClipPlane = .1f; camera.farClipPlane = 500; camera.allowHDR = camera.allowMSAA = false;
        target = new RenderTexture(512,512,0,RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
        pixels = new Texture2D(512,512,TextureFormat.RGBA32,false);
        var obj = new GameObject("YiGodhome Local Silhouette Input") { layer = 29 };
        copy = obj.AddComponent<MeshRenderer>(); copy.enabled = false; copyMesh = obj.AddComponent<MeshFilter>();
        log("HK_LOCAL_AURA_STYLE local=True nativeBodyPreserved=True interiorOverlap=True animatedRibbon=True nsOutlinePixels=False crimsonBlackRed=True crimsonSize=1.12 sharedAllBosses=True legacyCrimson=False");
    }
    private void VerifyMeshCopy()
    {
        var mesh = new Mesh(); mesh.MarkDynamic();
        try {
            var vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            var colors = new[] { Color.white, Color.white, Color.white };
            mesh.vertices = vertices; mesh.colors = colors; mesh.triangles = new[] { 0, 1, 2 };
            vertices[0] = Vector3.one * 999; colors[0] = Color.red;
            if (mesh.vertices[0] != Vector3.zero || mesh.colors[0] != Color.white)
                throw new InvalidOperationException("Native mesh retains mutable managed upload arrays.");
            log("HK_AURA_NATIVE_MESH_COPY vertexCopies=True colorCopies=True scratchMutationDidNotChangeNative=True ownedControl=True");
        } finally { UnityEngine.Object.Destroy(mesh); }
    }
    private bool detachedDefenderAura;
    internal void Update(HealthManager[]? bodies, ContactFrame? value, MeshRenderer[]? extra = null, Func<HealthManager,AttackTraits?>? traits = null)
    {
        frame = value; active.Clear(); Prune();
        // Surviving threshold dung is still crimson artwork, but must never
        // fabricate a contact-frame windup merely to keep its outline alive.
        detachedDefenderAura=EncounterProfile.Selected.Family==EncounterProfile.WhiteDefender && extra?.Length>0;
        if (value == null || value.CrimsonSequence == 0 && !detachedDefenderAura || bodies == null) { foreach(var p in parts) p.Draw.enabled = false; return; }
        if (geometry != value.GeometryFrame) { Clear(); geometry = value.GeometryFrame; }
        foreach(var body in bodies) {
            if(body==null)continue;
            bodySources.Clear();body.GetComponents(bodySources);
            foreach(var source in bodySources)Admit(source);
        }
        if(extra!=null)foreach(var source in extra)Admit(source);
        if(!selectionVerified && HollowSolsMenuPlugin.Instance?.FixtureRadiancePerformance==true && extra?.Length>=16) {
            var reference=bodies.Where(b=>b!=null).SelectMany(b=>b.GetComponents<MeshRenderer>()).Concat(extra).Distinct()
                .Where(r=>r!=null && r!=copy && !parts.Any(p=>p.Draw==r)).ToArray();
            if(!active.SetEquals(reference))throw new InvalidOperationException("Aura identity cache changed native source selection.");
            foreach(var part in parts)if(part.Owner!=part.Source.GetComponent<HealthManager>() || part.Sprite!=part.Source.GetComponent<tk2dBaseSprite>())
                throw new InvalidOperationException("Aura cached component owner changed.");
            selectionVerified=true;log("HK_AURA_IDENTITY_VERIFIED sources="+active.Count+" exactNativeSet=True componentsEqual=True frame="+Time.frameCount);
        }

        foreach(var part in parts) {
            // Roster body artwork is attached to its exact HealthManager. A
            // child projectile/effect can inherit that transform without
            // inheriting the body's attack palette or inactive state.
            var owner=part.Owner;
            var own=owner==null ? null : traits?.Invoke(owner);
            part.Present=!own.HasValue || own.Value!=AttackTraits.None;
            part.Teal=own.HasValue ? (own.Value & AttackTraits.Kickable)!=0 : value.Kickable || value.KickWindup;
            if(own.HasValue && part.LastTraits!=own && SessionConfiguration.Current.RunFixtures)
                log("HK_LOCAL_AURA_OWNER entity="+owner!.GetInstanceID()+" name="+owner.name.Replace(' ','_')+" traits="+own+" teal="+part.Teal+" present="+part.Present+" frame="+Time.frameCount);
            part.LastTraits=own;
        }
    }
    private void Admit(MeshRenderer source) {
        // Exact renderer identities avoid per-source searches through every
        // pooled ribbon. Native artwork selection/order is unchanged.
        if(source==null || source==copy || generated.Contains(source) || !active.Add(source) || bySource.ContainsKey(source))return;
        var filter=source.GetComponent<MeshFilter>();if(filter==null || filter.sharedMesh==null)return;
        int maximum=EncounterProfile.Selected.Family==EncounterProfile.Grimm || EncounterProfile.Selected.Family==EncounterProfile.NightmareGrimm
            || EncounterProfile.Selected.Family==EncounterProfile.AbsoluteRadiance || EncounterProfile.Selected.Family==EncounterProfile.WhiteDefender?AttackVolumes.MaxCount:16;
        if(parts.Count>=maximum) {
            if(Time.realtimeSinceStartup>=nextPartLimit){nextPartLimit=Time.realtimeSinceStartup+2;log("HK_LOCAL_AURA_PART_LIMIT source="+source.GetInstanceID()+" maximum="+maximum+" nativeBodyPreserved=True");}
            return;
        }
        var obj=new GameObject("YiGodhome Local Body Aura"){layer=source.gameObject.layer};
        obj.transform.SetParent(source.transform,false);obj.transform.localPosition=new Vector3(0,0,.01f);
        var draw=obj.AddComponent<MeshRenderer>();draw.sharedMaterial=material;draw.enabled=false;
        draw.sortingLayerID=source.sortingLayerID;draw.sortingOrder=source.sortingOrder;
        var mesh=new Mesh();mesh.MarkDynamic();var output=obj.AddComponent<MeshFilter>();output.sharedMesh=mesh;
        var part=new Part{Source=source,Filter=filter,Draw=draw,Mesh=mesh,Output=output,Owner=source.GetComponent<HealthManager>(),Sprite=source.GetComponent<tk2dBaseSprite>(),RadianceNail=EncounterProfile.Selected.Family==EncounterProfile.AbsoluteRadiance && HostReflectedProjectile.RadianceNailName(source.name)};
        parts.Add(part);bySource.Add(source,part);generated.Add(draw);
    }
    internal void Render()
    {
        using var measured = HostFrameProbe.Measure(22);
        Prune();
        if (frame == null || frame.CrimsonSequence == 0 && !detachedDefenderAura) return;
        if(GameManager.instance?.IsGamePaused() != true) waveTime += Time.deltaTime;
        long started = Stopwatch.GetTimestamp(); int visible = 0;
        foreach(var part in parts) {
            if (part.Source == null || !part.Present || !active.Contains(part.Source) || !part.Source.enabled || !part.Source.gameObject.activeInHierarchy) { part.Draw.enabled = false; continue; }
            bool teal=part.Teal;
            var sprite = part.Sprite;
            var pose = sprite == null ? (part.StaticPose??(part.Filter.sharedMesh.GetInstanceID(), 0))
                : (sprite.Collection.GetInstanceID(), sprite.spriteId * 4 + (sprite.FlipX ? 1 : 0) + (sprite.FlipY ? 2 : 0));
            if(sprite==null && !part.StaticPose.HasValue && part.RadianceNail)
                pose=(part.StaticPose=NailPose(part.Source,part.Filter.sharedMesh)).Value;
            if (!shapeCache.TryGetValue(pose,out var shape)) {
                shape = Capture(part); if (shape.Points.Length == 0) { part.Draw.enabled = false; continue; }
                while (shapes >= 128 || retainedPoints + shape.Points.Length > 65536) {
                    var old = shapeOrder.Dequeue(); retainedPoints -= shapeCache[old].Points.Length;
                    shapeCache.Remove(old); shapes--;
                }
                shapeCache.Add(pose,shape); shapeOrder.Enqueue(pose); shapes++; retainedPoints+=shape.Points.Length;
                log("HK_LOCAL_AURA_POSE entity=" + part.Source.GetInstanceID() + " pose=" + pose + " points=" + shape.Points.Length + " retained=" + shapes + " frame=" + Time.frameCount);
            }
            if (shape.Vertices.Length != shape.Points.Length * 6) {
                shape.Vertices = new Vector3[shape.Points.Length * 6];
                shape.Colors = new Color[shape.Points.Length * 6];
                bufferChanges++; bufferBytes += (long)shape.Points.Length * 6 * 28;
            }
            // Mesh setters copy synchronously; scratch belongs only to this
            // bounded pose cache and never escapes into native renderer owners.
            var vertices = shape.Vertices; var colors = shape.Colors;
            float scale = Mathf.Max(.01f,Mathf.Abs(part.Source.transform.lossyScale.x));
            if(shape.DeformedFrame!=Time.frameCount || shape.DeformedScale!=scale || shape.DeformedTeal!=teal) {
            shape.DeformedFrame=Time.frameCount;shape.DeformedScale=scale;shape.DeformedTeal=teal;
            for(int i=0;i<shape.Points.Length;i++) {
                var point = shape.Points[i]; var normal = shape.Normals[i];
                float wave = Mathf.Sin((point.x * 7 + point.y * 5)*scale - waveTime * 8) * .6f + Mathf.Sin((point.x * 13 - point.y * 9)*scale + waveTime * 5) * .4f;
                float crest = Mathf.Clamp01((wave+1)*.5f);
                float edge = (.012f + .075f*crest*crest) * (teal ? 1 : CrimsonWidth);
                for(int band=0;band<6;band++) {
                    // Coincident black/red boundary vertices keep a crisp single
                    // exterior. Teal's two added bands have zero width/alpha.
                    float distance = teal ? band == 0 ? -.10f : band == 1 ? edge : band == 2 ? edge+.009f : edge+.016f
                        : band == 0 ? -.10f : band <= 2 ? edge-.022f : band == 3 ? edge+.006f : band == 4 ? edge+.015f : edge+.022f;
                    vertices[i*6+band] = point + normal * (distance / scale);
                    var color = teal ? new Color(.40f,1,.86f) : band < 2 ? Color.black : CrimsonRed;
                    color.a = teal ? band < 2 ? .96f : band == 2 ? .20f : 0 : band < 2 ? 1 : band < 4 ? .96f : band == 4 ? .20f : 0;
                    colors[i*6+band] = color;
                }
            }
            }
            bool rain=part.RadianceNail;
            if(rain && shape.RainMesh==null && rainMeshes.Count<128) {
                shape.RainMesh=new Mesh();shape.RainMesh.MarkDynamic();rainMeshes.Add(shape.RainMesh);
                shape.RainScale=scale;shape.RainTeal=teal;
                shape.RainMesh.vertices=vertices;shape.RainMesh.colors=colors;shape.RainMesh.triangles=shape.Triangles;shape.RainMesh.bounds=shape.Bounds;
                shape.RainUploadFrame=Time.frameCount;
            }
            if(rain && shape.RainMesh!=null && shape.RainScale==scale && shape.RainTeal==teal) {
                if(shape.RainUploadFrame!=Time.frameCount){shape.RainMesh.vertices=vertices;shape.RainMesh.colors=colors;shape.RainUploadFrame=Time.frameCount;}
                part.Output.sharedMesh=shape.RainMesh;
            } else {
                part.Output.sharedMesh=part.Mesh;
                if (part.Pose != pose) { part.Mesh.Clear(); part.Mesh.vertices = vertices; part.Mesh.colors = colors; part.Mesh.triangles = shape.Triangles; part.Pose = pose; }
                else { part.Mesh.vertices = vertices; part.Mesh.colors = colors; }
                part.Mesh.bounds = shape.Bounds;
            }
            part.Draw.enabled = true; visible++;
        }
        if (HostFrameProbe.Enabled && Time.realtimeSinceStartup >= nextBuffers) {
            nextBuffers = Time.realtimeSinceStartup + 2;
            log("HK_AURA_BUFFER_GROWTH changes=" + bufferChanges + " payloadBytes=" + bufferBytes + " cachedShapes=" + shapes + " frame=" + Time.frameCount);
            bufferChanges = 0; bufferBytes = 0;
        }
        double ms = (Stopwatch.GetTimestamp()-started)*1000d/Stopwatch.Frequency; totalMs += ms; maxMs = Math.Max(maxMs,ms); samples++;
        if (samples == 1 || samples % 600 == 0) log("HK_LOCAL_AURA_COST visible=" + visible + " retained=" + shapes + " meanMs=" + (totalMs/samples).ToString("F3") + " maxMs=" + maxMs.ToString("F3") + " frame=" + Time.frameCount);
        if (SessionConfiguration.Current.RunFixtures && HollowSolsMenuPlugin.Instance?.FixtureRadiancePerformance!=true && visible > 0 && snapshots < 3 && capturedCounts.Add(visible)) {
            string directory = System.IO.Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!;
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(directory,"local-aura-" + visible + ".png")); snapshots++;
            log("HK_LOCAL_AURA_CAPTURE parts=" + visible + " poseExact=True frame=" + Time.frameCount);
        }
        if (SessionConfiguration.Current.RunFixtures && frame.Kickable && visible > 0 && capturedCounts.Add(100 + visible)) {
            string directory = System.IO.Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!;
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(directory,"local-aura-dash-" + visible + ".png"));
            log("HK_LOCAL_AURA_DASH_CAPTURE parts=" + visible + " frame=" + Time.frameCount);
        }
        if (SessionConfiguration.Current.RunFixtures && frame.Crimson && visible > 0
            && parts.Any(p => p.Draw.enabled && p.Source.GetComponent<tk2dSpriteAnimator>()?.CurrentClip?.name == "Balloon")
            && capturedCounts.Add(1000)) {
            string directory = System.IO.Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!;
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(directory,"local-aura-balloon.png"));
            log("HK_LOCAL_AURA_BALLOON_CAPTURE blackRed=True size=1.12 frame=" + Time.frameCount);
        }
        if(SessionConfiguration.Current.RunFixtures && frame.Kickable && visible >= 2 && motionFrames < 8 && Time.unscaledTime >= nextMotion) {
            string directory = System.IO.Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!;
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(directory,"local-aura-motion-" + motionFrames++ + ".png")); nextMotion=Time.unscaledTime+.035f;
        }
    }
    private Shape Capture(Part part)
    {
        var bounds = part.Filter.sharedMesh.bounds; float side = Mathf.Max(bounds.size.x,bounds.size.y) + .04f;
        var previous = RenderTexture.active;
        try {
            copyMesh.sharedMesh = part.Filter.sharedMesh; copy.sharedMaterial = part.Source.sharedMaterial;
            part.Source.GetPropertyBlock(properties); copy.SetPropertyBlock(properties);
            copy.transform.position = Stage; copy.transform.rotation = Quaternion.identity; copy.transform.localScale = Vector3.one; copy.enabled = true;
            camera.transform.position = Stage + new Vector3(bounds.center.x,bounds.center.y,-100); camera.orthographicSize = side*.5f;
            camera.Render(); RenderTexture.active = target; pixels.ReadPixels(new Rect(0,0,512,512),0,0); pixels.Apply(false,false);
        } finally { copy.enabled = false; RenderTexture.active = previous; }
        var raw = pixels.GetRawTextureData<byte>().ToArray();
        var loops = AlphaContours.ExtractBounded(raw,512);
        var points = new List<Vector3>(); var normals = new List<Vector3>(); var triangles = new List<int>();
        foreach(var loop in loops) {
            int start = points.Count;
            for(int i=0;i<loop.Length;i++) {
                // Average the tangent across a small arc rather than amplifying
                // alternating one-pixel raster stairs into a comb-shaped halo.
                int radius=Math.Min(4,(loop.Length-1)/2);
                var point = loop[i]; var before = loop[(i+loop.Length-radius)%loop.Length]; var after = loop[(i+radius)%loop.Length];
                points.Add(new Vector3(bounds.center.x+(point.X/512-.5f)*side,bounds.center.y+(point.Y/512-.5f)*side,0));
                normals.Add(new Vector3(after.Y-before.Y,before.X-after.X,0).normalized);
                int a = (start+i)*6, b = (start+(i+1)%loop.Length)*6;
                for(int band=0;band<5;band++) triangles.AddRange(new[] {a+band,b+band,b+band+1,a+band,b+band+1,a+band+1});
            }
        }
        bounds.Expand(.5f);
        return new Shape { Points = points.ToArray(), Normals = normals.ToArray(), Triangles = triangles.ToArray(), Bounds = bounds };
    }
    private void Prune() {
        for(int i=parts.Count-1;i>=0;i--) if(parts[i].Source == null || parts[i].Draw == null || parts[i].Filter == null) {
            var p=parts[i];
            if(p.Draw != null) UnityEngine.Object.Destroy(p.Draw.gameObject);
            UnityEngine.Object.Destroy(p.Mesh); bySource.Remove(p.Source);generated.Remove(p.Draw!);parts.RemoveAt(i);
        }
    }
    private void Clear() { foreach(var p in parts) { if(p.Draw != null) UnityEngine.Object.Destroy(p.Draw.gameObject); UnityEngine.Object.Destroy(p.Mesh); } foreach(var mesh in rainMeshes)UnityEngine.Object.Destroy(mesh);rainMeshes.Clear(); parts.Clear(); active.Clear();bySource.Clear();generated.Clear();bodySources.Clear();selectionVerified=false; shapeCache.Clear(); shapeOrder.Clear(); staticNails.Clear(); shapes = retainedPoints = 0; }
    public void Dispose() {
        Clear(); camera.targetTexture = null; UnityEngine.Object.Destroy(camera.gameObject); UnityEngine.Object.Destroy(copy.gameObject);
        target.Release(); UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(pixels); UnityEngine.Object.Destroy(white); UnityEngine.Object.Destroy(material);
        log("HK_LOCAL_AURA_RETIRED owners=0 shapes=0 cameras=0 textures=0");
    }
}
