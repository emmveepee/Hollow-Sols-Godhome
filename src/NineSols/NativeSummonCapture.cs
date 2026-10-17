using System;
using System.Collections.Generic;
using UnityEngine;
using YiGodhome.Bridge;
using YiGodhome.Geometry;
using Pose = YiGodhome.Bridge.Pose;

namespace YiGodhome;

// Native HP widgets use independent fixed-density tiles. Distant summons and
// attached talisman effects cannot expand a widget's camera footprint.
internal sealed class NativeSummonCapture : IDisposable
{
    private sealed class Widget
    {
        public UIMonsterHP View=null!;
        public Bounds Bounds;
        public Vector3 Position,Scale;
    }
    private readonly VisualEndpoint endpoint;
    private readonly Camera camera;
    private RenderTexture resolved=null!;
    private Texture2D pixels=null!;
    private readonly Dictionary<int,(RenderTexture Resolved,Texture2D Pixels)> targets=new();
    private readonly Dictionary<int,(RenderTexture Target,RenderTexture Resolved)> capturesByHeight=new();
    private readonly NativeHudReadback readback;
    private readonly List<Renderer> renderers=new();
    private readonly List<int> layers=new();
    private readonly List<bool> isWidget=new();
    private readonly HashSet<Renderer> seen=new();
    private readonly Dictionary<UIMonsterHP,int> owners=new();
    private readonly List<Widget> widgets=new();
    private int count,height;
    private long publishedFrame=-1;
    private float next;
    private int captures;
    private bool hudCaptured,headHudCaptured;
    private bool Admit(long frame){if(frame<=publishedFrame)return false;publishedFrame=frame;return true;}
    public NativeSummonCapture(VisualEndpoint endpoint)
    {
        this.endpoint=endpoint;
        if((SystemInfo.copyTextureSupport & UnityEngine.Rendering.CopyTextureSupport.Basic)==0)
            throw new NotSupportedException("Native minion HUD requires basic GPU texture-copy support.");
        camera=new GameObject("YiGodhome Summon UI Camera").AddComponent<Camera>();camera.enabled=false;
        camera.orthographic=true;camera.nearClipPlane=.1f;camera.farClipPlane=500;
        camera.cullingMask=1<<30;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;
        camera.allowHDR=false;camera.allowMSAA=false;camera.useOcclusionCulling=false;
        readback=new NativeHudReadback(endpoint,message=>Debug.Log(message),"summon_hud",Admit);
    }
    private void SelectSize(int h)
    {
        if(!targets.TryGetValue(h,out var cached)) {
            if(targets.Count>=5)throw new InvalidOperationException("Summon HUD resolution cache exceeded.");
            var output=new RenderTexture(MinionHudAtlas.Width,h,0,RenderTextureFormat.ARGB32);output.Create();
            cached=(output,new Texture2D(MinionHudAtlas.Width,h,TextureFormat.RGBA32,false));targets.Add(h,cached);
            Debug.Log("NS_MINION_HUD_ATLAS height="+h+" tile="+MinionHudAtlas.Tile+" fixedWidgetDensity=True maximumWidgets="+MinionHudAtlas.MaximumWidgets);
        }
        height=h;resolved=cached.Resolved;pixels=cached.Pixels;
    }
    private static Bounds ActualBounds(Renderer r)=>r.TryGetComponent<Shapes.ShapeRenderer>(out var shape)?shape.GetWorldBounds():r.bounds;
    private void Region(int y,int w,int h,float x,float centerY,float extent)
    {
        if(!capturesByHeight.TryGetValue(h,out var pair)) {
            if(capturesByHeight.Count>=3)throw new InvalidOperationException("Native minion HUD render-target budget exceeded.");
            var capture=new RenderTexture(w*2,h*2,24,RenderTextureFormat.ARGB32){filterMode=FilterMode.Bilinear};capture.Create();
            var output=new RenderTexture(w,h,0,RenderTextureFormat.ARGB32);output.Create();
            pair=(capture,output);capturesByHeight.Add(h,pair);
        }
        // Camera.targetTexture ignores pixelRect. Render each group into its
        // own full target, then copy exact resolved texels into the atlas.
        camera.targetTexture=pair.Target;camera.aspect=(float)w/h;
        camera.orthographicSize=extent;camera.transform.position=new Vector3(x,centerY,-100);camera.Render();
        Graphics.Blit(pair.Target,pair.Resolved);
        Graphics.CopyTexture(pair.Resolved,0,0,0,0,w,h,resolved,0,0,0,y);
    }
    public void Capture(Pose pose,CoordinateMap map,IEnumerable<Renderer> source)
    {
        readback.Observe(pose.GeometryFrame);
        if(readback.Busy || !endpoint.CanQueuePixels || Time.realtimeSinceStartup<next)return;
        next=Time.realtimeSinceStartup+1f/60;
        renderers.Clear();layers.Clear();isWidget.Clear();seen.Clear();owners.Clear();count=0;
        bool effects=false;Bounds effectBounds=default;
        foreach(var r in source) {
            if(r==null || !r.enabled || !r.gameObject.activeInHierarchy || !seen.Add(r))continue;
            renderers.Add(r);layers.Add(r.gameObject.layer);
            var view=r.GetComponentInParent<UIMonsterHP>();isWidget.Add(view!=null);
            var bounds=ActualBounds(r);
            if(view==null) {
                if(effects)effectBounds.Encapsulate(bounds);else effectBounds=bounds;
                effects=true;continue;
            }
            if(!owners.TryGetValue(view,out int index)) {
                if(count>=MinionHudAtlas.MaximumWidgets)throw new InvalidOperationException("Native minion HUD ownership budget exceeded.");
                index=count++;owners.Add(view,index);
                if(index==widgets.Count)widgets.Add(new Widget());
                var item=widgets[index];item.View=view;item.Bounds=bounds;
                item.Position=view.transform.position;item.Scale=view.transform.localScale;
            } else widgets[index].Bounds.Encapsulate(bounds);
        }
        bool visible=renderers.Count>0;
        var frame=endpoint.RentFrame(visible?MinionHudAtlas.Width*(MinionHudAtlas.BarHeight(count)+(effects?MinionHudAtlas.EffectsSide:0))*4:4);
        bool asynchronous=visible && readback.Supported && !SessionConfiguration.Current.RunFixtures;
        var old=RenderTexture.active;
        try {
            if(visible) {
                int baseY=effects?MinionHudAtlas.EffectsSide:0,barHeight=MinionHudAtlas.BarHeight(count);
                SelectSize(baseY+barHeight);
                frame.Regions=new VisualRegion[count+(effects?1:0)];
                RenderTexture.active=resolved;GL.Clear(false,true,Color.clear);
                // Layer31 is excluded from this camera. Keep the two native
                // groups separate even when their source world bounds overlap.
                for(int i=0;i<renderers.Count;i++)renderers[i].gameObject.layer=isWidget[i]?31:30;
                if(effects) {
                    float extent=Math.Max(8,Math.Max(effectBounds.extents.x,effectBounds.extents.y)+4);
                    Region(0,MinionHudAtlas.EffectsSide,MinionHudAtlas.EffectsSide,effectBounds.center.x,effectBounds.center.y,extent);
                    var corner=map.ToSource(new Point(effectBounds.center.x-extent,effectBounds.center.y-extent));
                    frame.Regions[0]=new VisualRegion(0,0,MinionHudAtlas.EffectsSide,MinionHudAtlas.EffectsSide,corner.X,corner.Y,extent*2/map.Scale,extent*2/map.Scale);
                }
                for(int i=0;i<count;i++) {
                    var item=widgets[i];float side=Math.Max(item.Bounds.size.x,item.Bounds.size.y);
                    if(side<=0 || float.IsNaN(side) || float.IsInfinity(side))throw new InvalidOperationException("Invalid native minion widget bounds.");
                    var center=map.ToSource(new Point(item.Bounds.center.x,item.Bounds.center.y));
                    frame.Regions[i+(effects?1:0)]=MinionHudAtlas.Widget(i,count,effects,center.X,center.Y,item.Bounds.size.x/map.Scale,item.Bounds.size.y/map.Scale);
                    float scale=MinionHudAtlas.Content/side;
                    var slot=new Vector3(i%MinionHudAtlas.Columns*MinionHudAtlas.Tile+MinionHudAtlas.Tile/2f,
                        i/MinionHudAtlas.Columns*MinionHudAtlas.Tile+MinionHudAtlas.Tile/2f,item.Position.z);
                    item.View.transform.localScale=item.Scale*scale;
                    var offset=item.Bounds.center-item.Position;offset.z=0;
                    item.View.transform.position=slot-offset*scale;
                }
                for(int i=0;i<renderers.Count;i++)renderers[i].gameObject.layer=isWidget[i]?30:31;
                if(count>0)Region(baseY,MinionHudAtlas.Width,barHeight,MinionHudAtlas.Width/2f,barHeight/2f,barHeight/2f);
                RenderTexture.active=resolved;
                if(!asynchronous) {
                    pixels.ReadPixels(new Rect(0,0,MinionHudAtlas.Width,height),0,0,false);pixels.Apply(false,false);pixels.GetRawTextureData<byte>().CopyTo(frame.Rgba);
                }
                if(SessionConfiguration.Current.RunFixtures && ActorPixels.Visible(frame.Rgba)) {
                    string directory=System.IO.Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!;
                    if(++captures>=30 && !hudCaptured){hudCaptured=true;System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory,"native-summon-hud.png"),pixels.EncodeToPNG());}
                    if(!headHudCaptured && EncounterProfile.Selected.ArmorAndHead && count>0){headHudCaptured=true;System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory,"native-head-hud.png"),pixels.EncodeToPNG());}
                }
            } else Array.Clear(frame.Rgba,0,frame.Rgba.Length);
        }
        catch {frame.ReleasePixels();throw;}
        finally {
            RenderTexture.active=old;
            for(int i=0;i<count;i++)if(widgets[i].View!=null){widgets[i].View.transform.position=widgets[i].Position;widgets[i].View.transform.localScale=widgets[i].Scale;}
            for(int i=0;i<renderers.Count;i++)if(renderers[i]!=null)renderers[i].gameObject.layer=layers[i];
            for(int i=0;i<count;i++)widgets[i].View=null!;
            renderers.Clear();seen.Clear();owners.Clear();
        }
        frame.Session=pose.GeometrySession;frame.GeometryFrame=pose.GeometryFrame;frame.NativeFrame=Time.frameCount;
        frame.CaptureTick=System.Diagnostics.Stopwatch.GetTimestamp();frame.Width=visible?MinionHudAtlas.Width:1;frame.Height=visible?height:1;
        frame.WorldWidth=frame.WorldHeight=1;
        if(asynchronous)readback.Request(resolved,frame,0,height,false,null);
        else if(Admit(frame.NativeFrame))endpoint.PublishPixels(frame);else frame.ReleasePixels();
    }
    public void Dispose()
    {
        readback.Dispose();UnityEngine.Object.Destroy(camera.gameObject);
        foreach(var set in targets.Values){set.Resolved.Release();UnityEngine.Object.Destroy(set.Resolved);UnityEngine.Object.Destroy(set.Pixels);}
        foreach(var set in capturesByHeight.Values){set.Target.Release();set.Resolved.Release();UnityEngine.Object.Destroy(set.Target);UnityEngine.Object.Destroy(set.Resolved);}
        capturesByHeight.Clear();
        targets.Clear();
    }
}
