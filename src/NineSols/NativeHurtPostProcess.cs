using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace YiGodhome;

// Exact initialized camera-effect owners. Private disabled copies execute only
// their native image-render methods, never native camera/gameplay callbacks.
internal sealed class NativeHurtPostProcess : IDisposable
{
    private readonly CameraFilterPack_Lut_Mask? sourceLut;
    private readonly CameraFilterPack_Vision_Blood_Fast? sourceBlood;
    private readonly CameraFilterPack_Lut_Mask? lut;
    private readonly CameraFilterPack_Vision_Blood_Fast? blood;
    private readonly Action<RenderTexture,RenderTexture>? renderLut,renderBlood;
    private readonly GameObject owner;
    private readonly RenderTexture scratch;
    private readonly Action<string> log;
    private Texture2D? converted;
    private bool reported,reportedHurtGrade;
    private static readonly FieldInfo lutTime=AccessTools.Field(typeof(CameraFilterPack_Lut_Mask),"TimeX");
    private static readonly FieldInfo bloodTime=AccessTools.Field(typeof(CameraFilterPack_Vision_Blood_Fast),"TimeX");
    internal NativeHurtPostProcess(Animator? native,int width,int height,Action<string> logger)
    {
        log=logger;
        owner=new GameObject("YiGodhome Native Hurt Image Methods");
        owner.AddComponent<Camera>().enabled=false;
        scratch=new RenderTexture(width,height,0,RenderTextureFormat.ARGB32); scratch.Create();
        if(native!=null) {
            sourceLut=native.GetComponent<CameraFilterPack_Lut_Mask>();
            sourceBlood=native.GetComponent<CameraFilterPack_Vision_Blood_Fast>();
        }
        if(sourceLut!=null) {
            lut=owner.AddComponent<CameraFilterPack_Lut_Mask>(); lut.enabled=false;
            AccessTools.Field(typeof(CameraFilterPack_Lut_Mask),"SCShader").SetValue(lut,Shader.Find("CameraFilterPack/Lut_Mask"));
            renderLut=(Action<RenderTexture,RenderTexture>)Delegate.CreateDelegate(typeof(Action<RenderTexture,RenderTexture>),lut,AccessTools.Method(typeof(CameraFilterPack_Lut_Mask),"OnRenderImage"));
        }
        if(sourceBlood!=null) {
            blood=owner.AddComponent<CameraFilterPack_Vision_Blood_Fast>(); blood.enabled=false;
            AccessTools.Field(typeof(CameraFilterPack_Vision_Blood_Fast),"SCShader").SetValue(blood,Shader.Find("CameraFilterPack/Vision_Blood_Fast"));
            renderBlood=(Action<RenderTexture,RenderTexture>)Delegate.CreateDelegate(typeof(Action<RenderTexture,RenderTexture>),blood,AccessTools.Method(typeof(CameraFilterPack_Vision_Blood_Fast),"OnRenderImage"));
        }
        log("NS_NATIVE_HURT_RENDER_BOUND lut="+(sourceLut!=null)+" blood="+(sourceBlood!=null)+" nativeMethods=True privateCopies=True");
    }
    private bool HurtGrade => sourceLut!=null && sourceLut.enabled && sourceLut.Blend>0;
    internal bool Active => HurtGrade || sourceBlood!=null && sourceBlood.enabled;
    internal void Render(RenderTexture input,RenderTexture output)
    {
        // Both HurtEffect and Sustain Layer.LowHealth animate this native
        // LUT/mask. NoEffect disables it and zeros Blend. Follow those native
        // properties instead of clipping low-health/transition presentation
        // with a second, bridge-owned animation-state filter.
        bool useLut=HurtGrade;
        bool useBlood=sourceBlood!=null && sourceBlood.enabled;
        var current=input;
        if(useLut && lut!=null) {
            lut.Blend=sourceLut!.Blend; lut.Mask=sourceLut.Mask; lut.Inverse=sourceLut.Inverse;
            if(converted!=sourceLut.LutTexture) { lut.Convert(sourceLut.LutTexture); converted=sourceLut.LutTexture; }
            lutTime.SetValue(lut,lutTime.GetValue(sourceLut));
            renderLut!(current,useBlood?scratch:output); current=useBlood?scratch:output;
        }
        if(useBlood && blood!=null) {
            blood.HoleSize=sourceBlood!.HoleSize; blood.HoleSmooth=sourceBlood.HoleSmooth;
            blood.Color1=sourceBlood.Color1; blood.Color2=sourceBlood.Color2;
            bloodTime.SetValue(blood,bloodTime.GetValue(sourceBlood)); renderBlood!(current,output);
        }
        if(!useLut && !useBlood) Graphics.Blit(input,output);
        if(!reported && useBlood || !reportedHurtGrade && useLut) {
            reported=true; reportedHurtGrade|=useLut;
            log("NS_NATIVE_HURT_RENDER nativeMethods=True lut="+useLut+" blood="+useBlood+" sourceBlend="+(sourceLut?.Blend??0)+" captureBlend="+(lut?.Blend??0)+" nativeEffectLifetime=True nativeStrength=True dualMatte=True referenceLevel=64 frame="+Time.frameCount);
        }
    }
    public void Dispose()
    {
        if(lut!=null) {
            var texture=AccessTools.Field(typeof(CameraFilterPack_Lut_Mask),"converted3DLut").GetValue(lut) as Texture3D;
            if(texture!=null) UnityEngine.Object.Destroy(texture);
        }
        UnityEngine.Object.Destroy(owner); scratch.Release(); UnityEngine.Object.Destroy(scratch);
    }
}
