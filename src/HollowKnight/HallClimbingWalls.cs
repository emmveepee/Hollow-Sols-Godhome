using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine;
using YiGodhome.Geometry;
using YiGodhome.Bridge;
using Pose = YiGodhome.Bridge.Pose;

namespace YiGodhome;

internal sealed class HallClimbingWalls : IDisposable
{
    private readonly GameObject root;
    private readonly SpriteRenderer[] surfaces;
    private readonly SpriteRenderer[] nativeWallArt;
    private readonly List<SortingGroup> wallGroups = new();
    private readonly Action<string> log;
    private Texture2D? texture;
    private Sprite? sprite;
    private long sequence;
    private bool presented, snapshot;
    private float presentedAt;
    private readonly string scene;
    private readonly bool[] sideSnapshots;
    private float nextSideSnapshot;
    private int cameraSamples;
    private float nextCameraSample;
    private static readonly System.Reflection.FieldInfo currentArea = HarmonyLib.AccessTools.Field(typeof(CameraController),"currentLockArea");
    public HallClimbingWalls(Action<string> log, string scene = "GG_Workshop")
    {
        this.log = log; this.scene=scene; root = new GameObject("YiGodhome Hall Climbing Walls");
        // Exact native column family, including nested room pieces. Root-only
        // lookup misses Uumuu's columns under GG_Arena_Prefab/Wall_L/Wall_R.
        nativeWallArt=scene=="GG_Uumuu" || scene=="GG_Uumuu_V" || scene=="GG_Flukemarm"
            ? Resources.FindObjectsOfTypeAll<SpriteRenderer>().Where(r=>r.gameObject.scene.name==scene
                && r.name.StartsWith("GG_scenery_0006_15",StringComparison.Ordinal)).ToArray()
            : scene=="GG_Atrium" ? Resources.FindObjectsOfTypeAll<SpriteRenderer>().Where(r=>r.gameObject.scene.name==scene
                && (r.name=="GG_scenery_0006_15 (47)" || r.name=="GG_scenery_0006_15 (21)")).ToArray()
            : Array.Empty<SpriteRenderer>();
        var strips = RoomFeatures.ClimbingWalls(scene); surfaces = new SpriteRenderer[strips.Length];
        sideSnapshots = new bool[strips.Length];
        for (int i = 0; i < strips.Length; i++) {
            var strip = strips[i];
            var obj = new GameObject("YiGodhome Climbing Wall") { layer = 8 };
            obj.transform.SetParent(root.transform, false);
            obj.transform.position = new Vector3(strip.SolidCenter, (strip.Bottom + strip.Top) * 0.5f, 0);
            obj.AddComponent<BoxCollider2D>().size = new Vector2(WallRunStrip.Thickness, strip.Top - strip.Bottom);
            var visual = new GameObject("Native Teal Wall Trim") { layer = HeroController.instance.gameObject.layer };
            visual.transform.SetParent(root.transform, false);
            // Keep the narrow runnable edge outside the native foreground wall art.
            float trimTop = strip.Top + (scene == "GG_Workshop" ? .15f : 0);
            visual.transform.position = new Vector3(strip.X + strip.Facing * 0.12f, (strip.Bottom + trimTop) * 0.5f, -0.05f);
            // Mirror the complete sliced geometry, including its authored white edge.
            // SpriteRenderer.flipX does not reliably mirror the sliced edge in this build.
            visual.transform.localScale = new Vector3(strip.Facing, 1, 1);
            var renderer = surfaces[i] = visual.AddComponent<SpriteRenderer>();
            renderer.enabled = false; renderer.drawMode = SpriteDrawMode.Sliced;
            renderer.size = new Vector2(0.64f, trimTop - strip.Bottom);
            // Keep the hub in the native world band too. Hero-band promotion
            // overrides foreground occlusion even when world depth is correct.
            log("HK_WALL_RUN_PLACED x=" + strip.X + " bottom=" + strip.Bottom + " top=" + strip.Top + " nativeHostSolid=True");
        }
    }
    private void BindSurface(SpriteRenderer surface,WallRunStrip strip)
    {
        if(scene=="GG_Atrium") {
            // This single marked face spans two authored wall planes: upper
            // -.1142, lower -.0358. Original trim at-.05 cleared only the lower.
            // Stay behind the separate foreground bushes/columns (z<-1).
            if(Math.Abs(strip.X-129.76f)<.001f && strip.Bottom==64 && strip.Top==90 && nativeWallArt.Length==2
                && nativeWallArt.All(r=>r!=null && r.sortingLayerID==surface.sortingLayerID && r.sortingOrder==surface.sortingOrder
                    && r.transform.position.z>-.2f && r.transform.position.z<.1f)) {
                var p=surface.transform.position;p.z=nativeWallArt.Min(r=>r.transform.position.z)-.01f;surface.transform.position=p;
                log("HK_HUB_UPPER_WALL_DEPTH nativePieces=47,21 worldZ="+p.z+" originalSortBand=True foregroundUnchanged=True");
            }
            return; // Other hub strips keep their accepted original placement.
        }

        // Restore the original external band. Do not promote a trim over every
        // overlapping decoration, fog layer, combat effect or native title.
        SpriteRenderer? edge=null;float best=float.PositiveInfinity,bestPlane=float.PositiveInfinity;
        foreach(var art in nativeWallArt) {
            if(art==null || !art.enabled || !art.gameObject.activeInHierarchy)continue;
            var b=art.bounds;
            // Match the gameplay wall face, not foreground/background copies of
            // this same native column asset. Those have their own parallax.
            float plane=Math.Abs(art.transform.position.z-surface.transform.position.z);
            if(plane>.12f
                || b.max.y<strip.Bottom || b.min.y>strip.Top || b.size.y<(strip.Top-strip.Bottom)*.5f)continue;
            float face=strip.Facing>0?b.max.x:b.min.x;
            float distance=Math.Abs(face-strip.ContactX);
            if(distance>.65f)continue;
            // Flukemarm's tilted (3)/(4) pieces have the closest XY edge but
            // sit behind its overlapping upright column. Bind the coplanar
            // upright wall first; otherwise that second wall covers the pair.
            bool preferPlane=scene=="GG_Flukemarm";
            if(preferPlane ? plane>bestPlane+.001f || Math.Abs(plane-bestPlane)<=.001f && distance>=best : distance>=best)continue;
            edge=art;best=distance;bestPlane=plane;
        }
        if(edge==null) {
            log("HK_WALL_SORT_BIND scene="+scene+" x="+strip.X+" matched=False originalBand=True worldZ="+surface.transform.position.z);
            return;
        }
        var group=edge.GetComponent<SortingGroup>();
        if(group==null) {
            // The pair keeps the wall's native external sorting band/depth.
            // Only their internal order changes, so front scenery stays in front.
            if(edge.GetComponentInParent<SortingGroup>()!=null)return;
            group=edge.gameObject.AddComponent<SortingGroup>();
            group.sortingLayerID=edge.sortingLayerID;group.sortingOrder=edge.sortingOrder;
            wallGroups.Add(group);
        } else if(!wallGroups.Contains(group))return;
        surface.transform.SetParent(edge.transform,true);
        surface.sortingLayerID=edge.sortingLayerID;
        surface.sortingOrder=Math.Min(32767,edge.sortingOrder+1);
        log("HK_WALL_SORT_BIND scene="+scene+" x="+strip.X+" wall="+edge.name+" wallId="+edge.GetInstanceID()
            +" nativeZ="+edge.transform.position.z.ToString("R")+" worldZ="+surface.transform.position.z.ToString("R")
            +" layer="+group.sortingLayerID+" externalOrder="+group.sortingOrder+" localTrimOrder="+surface.sortingOrder
            +" nativeQueue="+edge.sharedMaterial?.renderQueue+" localPair=True foregroundUnchanged=True");
    }
    internal void Update(VisualFrame? frame, Pose? pose)
    {
        // Unity can destroy scene renderers before activeSceneChanged is observed.
        // A same-room debug transition still invalidates these native objects.
        if (root == null) return;
        bool valid = frame != null && pose != null && frame.Session == pose.GeometrySession && frame.GeometryFrame == pose.GeometryFrame && frame.Width == 128 && frame.Height == 1024;
        foreach (var surface in surfaces) if (surface != null) surface.enabled = valid;
        if (!valid) return;
        if (texture == null) {
            texture = new Texture2D(128, 1024, TextureFormat.RGBA32, false); texture.wrapMode = TextureWrapMode.Clamp; texture.filterMode = FilterMode.Bilinear;
            // Authored end tiles keep their shape while the middle fits each wall.
            sprite = Sprite.Create(texture, new Rect(0, 0, 128, 1024), new Vector2(0.5f, 0.5f), 128, 0, SpriteMeshType.FullRect, new Vector4(0, 112, 0, 112));
            foreach (var surface in surfaces) { var size = surface.size; surface.sprite = sprite; surface.size = size; }
        }
        if (sequence != frame!.Sequence) {
            texture.LoadRawTextureData(frame.Rgba); texture.Apply(false, false); sequence = frame.Sequence;
            if (!presented) {
                if (!ActorPixels.Visible(frame.Rgba)) throw new InvalidOperationException("Host wall trim pixels are empty.");
                var strips=RoomFeatures.ClimbingWalls(scene);
                for(int i=0;i<surfaces.Length;i++)BindSurface(surfaces[i],strips[i]);
                presented = true; presentedAt = Time.realtimeSinceStartup;
                log("HK_WALL_ART_PRESENTED geometry=" + frame.GeometryFrame + " nativeFrame=" + frame.NativeFrame + " hash=" + frame.Hash + " sequence=" + frame.Sequence + " strips=" + surfaces.Length + " nativePixels=True caps=Sliced width=0.64");
            }
        }
        if (!snapshot && SessionConfiguration.Current.RunFixtures && Time.realtimeSinceStartup - presentedAt > 2 && Camera.main != null) {
            foreach (var surface in surfaces) {
                var point = Camera.main.WorldToViewportPoint(surface.bounds.center);
                if (point.z <= 0 || point.x < 0 || point.x > 1 || point.y < 0 || point.y > 1) continue;
                string path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!, "native-wall-trim.png");
                ScreenCapture.CaptureScreenshot(path); snapshot = true;
                log("HK_WALL_ART_SCREENSHOT geometry=" + frame.GeometryFrame + " nativeFrame=" + frame.NativeFrame + " file=native-wall-trim.png"); break;
            }
        }
        if (SessionConfiguration.Current.RunFixtures && SessionConfiguration.Current.Has("wall-probe")
            && Time.realtimeSinceStartup > nextSideSnapshot && Time.realtimeSinceStartup-presentedAt>2 && Camera.main!=null
            && GameCameras.instance?.cameraFadeFSM?.ActiveStateName=="Normal") {
            if((scene=="GG_Uumuu" || scene=="GG_Uumuu_V") && cameraSamples<8 && Time.realtimeSinceStartup>nextCameraSample) {
                nextCameraSample=Time.realtimeSinceStartup+1;cameraSamples++;
                var controller=GameCameras.instance.cameraController;
                var area=controller==null?null:(CameraLockArea?)currentArea.GetValue(controller);
                log("HK_UUMUU_WALL_CAMERA_SAMPLE scene="+scene+" frame="+Time.frameCount+" playerX="+pose!.X+" playerY="+pose.Y
                    +" camera="+Camera.main.transform.position+" ortho="+Camera.main.orthographicSize+" aspect="+Camera.main.aspect
                    +" area="+(area==null?"none":area.name)+" areaId="+(area==null?0:area.GetInstanceID())
                    +" xMin="+(area==null?0:area.cameraXMin)+" xMax="+(area==null?0:area.cameraXMax)
                    +" yMin="+(area==null?0:area.cameraYMin)+" yMax="+(area==null?0:area.cameraYMax)+" nativeOwner=True");
            }
            for(int i=0;i<surfaces.Length;i++) {
                var surface=surfaces[i]; if(sideSnapshots[i] || surface==null || !surface.enabled) continue;
                var strip=RoomFeatures.ClimbingWalls(scene)[i];
                if(Math.Abs(pose!.X-strip.ContactX)>2) continue;
                var bounds=surface.bounds; var low=Camera.main.WorldToViewportPoint(bounds.min); var high=Camera.main.WorldToViewportPoint(bounds.max);
                if(low.z<=0 || low.x<.05f || high.x>.95f || high.y<.25f || low.y>.75f) continue;
                string file="native-wall-"+scene+"-"+frame.GeometryFrame+"-"+i+".png";
                string path=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!,file);
                ScreenCapture.CaptureScreenshot(path); sideSnapshots[i]=true; nextSideSnapshot=Time.realtimeSinceStartup+.5f;
                log("HK_WALL_SIDE_SCREENSHOT scene="+scene+" index="+i+" file="+file+" position="+surface.transform.position
                    +" bounds="+bounds+" layer="+surface.sortingLayerID+" order="+surface.sortingOrder
                    +" viewportLow="+low+" viewportHigh="+high+" geometry="+frame.GeometryFrame+" nativePixels=True");
                break;
            }
        }
    }
    public void Dispose()
    {
        // Some trim children now belong to their exact native wall's local
        // sorting group. Revoke only our renderers/components, never the wall.
        foreach(var surface in surfaces)if(surface!=null){surface.enabled=false;UnityEngine.Object.Destroy(surface.gameObject);}
        foreach(var group in wallGroups)if(group!=null){group.enabled=false;UnityEngine.Object.Destroy(group);}
        wallGroups.Clear();
        if (root != null) UnityEngine.Object.Destroy(root);
        if (sprite != null) UnityEngine.Object.Destroy(sprite); if (texture != null) UnityEngine.Object.Destroy(texture);
        log("HK_WALL_ART_DISPOSED surfaces=" + surfaces.Length);
    }
}
