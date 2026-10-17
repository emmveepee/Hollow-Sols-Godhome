using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using YiGodhome.Bridge;
using Pose = YiGodhome.Bridge.Pose;

namespace YiGodhome;

// Native NS minion HUD pixels retain world positions, but share HK's menu
// canvas so terrain cannot occlude them and native menus remain above them.
internal sealed class NativeWorldHudPresenter : IDisposable
{
    private readonly Action<string> log;
    private GameObject? container;
    private readonly List<RawImage> images=new();
    private Canvas? canvas;
    private Texture2D? texture;
    private readonly Dictionary<int,(Texture2D Texture,long Used)> textures=new();
    private long textureUse,retainedBytes;
    private long sequence;
    internal NativeWorldHudPresenter(Action<string> log) { this.log=log; }
    internal void Update(VisualFrame? frame,Pose? pose)
    {
        foreach(var image in images)image.enabled=false;
        if(frame==null || pose==null || frame.Session!=pose.GeometrySession
            || frame.GeometryFrame!=pose.GeometryFrame || Camera.main==null) return;
        // A transparent clear only hides regions. It must not become a sixth
        // texture tier and evict a warmed full-size atlas on every despawn.
        if(frame.Width==1 && frame.Height==1 && frame.Regions.Length==0)return;
        if(container==null) {
            var ui=UIManager.instance;
            var pause=ui==null?null:AccessTools.Field(typeof(UIManager),"pauseMenuScreen").GetValue(ui) as MenuScreen;
            canvas=pause?.GetComponentInParent<Canvas>()?.rootCanvas;
            if(canvas==null)return;
            var obj=new GameObject("Yi native minion HUD",typeof(RectTransform),typeof(LayoutElement));
            obj.layer=canvas.gameObject.layer;
            obj.transform.SetParent(canvas.transform,false);obj.transform.SetAsFirstSibling();
            obj.GetComponent<LayoutElement>().ignoreLayout=true;
            var rect=(RectTransform)obj.transform;
            rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.sizeDelta=Vector2.zero;
            container=obj;
            log("HK_MINION_HUD_ORDER nativeCanvas=True terrainAbove=True nativeMenusAbove=True");
        }
        if(texture==null || texture.width!=frame.Width || texture.height!=frame.Height) {
            int key=frame.Width*4096+frame.Height;
            if(!textures.TryGetValue(key,out var cached)) {
                long bytes=(long)frame.Width*frame.Height*4;
                // All five atlas tiers fit30MiB. Counts and attached effects
                // can change without destroying/recreating warm textures.
                while(textures.Count>=5 || retainedBytes+bytes>32L*1024*1024) {
                    int oldest=-1;long used=long.MaxValue;
                    foreach(var entry in textures)if(entry.Value.Used<used){oldest=entry.Key;used=entry.Value.Used;}
                    var retired=textures[oldest].Texture;textures.Remove(oldest);
                    retainedBytes-=(long)retired.width*retired.height*4;UnityEngine.Object.Destroy(retired);
                }
                cached=(new Texture2D(frame.Width,frame.Height,TextureFormat.RGBA32,false) {
                    wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear
                },0);
                retainedBytes+=bytes;
                log("HK_MINION_HUD_TEXTURE_CACHE width="+frame.Width+" height="+frame.Height+" retainedBytes="+retainedBytes+" maximumBytes=33554432");
            }
            texture=cached.Texture;textures[key]=(texture,++textureUse);
            sequence=0;
        }
        if(sequence!=frame.Sequence) {
            texture.LoadRawTextureData(frame.Rgba);texture.Apply(false,false);sequence=frame.Sequence;
        }
        float z=HeroController.instance==null?0:HeroController.instance.transform.position.z;
        var camera=Camera.main;
        var uiCamera=canvas!.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
        var root=(RectTransform)canvas.transform;
        int count=Math.Max(1,frame.Regions.Length);
        for(int i=0;i<count;i++) {
            while(images.Count<=i) {
                var obj=new GameObject("Native HUD region",typeof(RectTransform),typeof(RawImage),typeof(LayoutElement));
                obj.layer=canvas.gameObject.layer;obj.transform.SetParent(container!.transform,false);
                obj.GetComponent<LayoutElement>().ignoreLayout=true;
                var added=obj.GetComponent<RawImage>();added.raycastTarget=false;images.Add(added);
                added.rectTransform.anchorMin=added.rectTransform.anchorMax=new Vector2(.5f,.5f);added.rectTransform.pivot=Vector2.zero;
            }
            var image=images[i];
            var region=frame.Regions.Length>0?frame.Regions[i]:new VisualRegion(0,0,frame.Width,frame.Height,frame.X,frame.Y,frame.WorldWidth,frame.WorldHeight);
            var lower=camera.WorldToScreenPoint(new Vector3(region.WorldX,region.WorldY,z));
            var upper=camera.WorldToScreenPoint(new Vector3(region.WorldX+region.WorldWidth,region.WorldY+region.WorldHeight,z));
            if(lower.z<=0 || upper.z<=0
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(root,lower,uiCamera,out var a)
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(root,upper,uiCamera,out var b))continue;
            image.texture=texture;image.uvRect=new Rect((float)region.X/frame.Width,(float)region.Y/frame.Height,(float)region.Width/frame.Width,(float)region.Height/frame.Height);
            image.rectTransform.anchoredPosition=a;image.rectTransform.sizeDelta=b-a;image.enabled=true;
        }
    }
    public void Dispose()
    {
        if(container!=null)UnityEngine.Object.Destroy(container);
        foreach(var cached in textures.Values)UnityEngine.Object.Destroy(cached.Texture);
        textures.Clear();retainedBytes=textureUse=0;
        images.Clear();container=null;canvas=null;texture=null;sequence=0;
    }
}
