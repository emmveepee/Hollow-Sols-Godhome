using System;
using UnityEngine;
namespace YiGodhome;

// Native HUD animation only; no health, buff, or incoming-hit authority.
internal static class NativeFireHud
{
    private static Animator? animator;
    private static Transform? fireRoot;
    private static SpriteRenderer? glow;
    private static RCGMaker.Core.InstanceReferenceData? reference;
    internal static void BindReference(RCGMaker.Core.InstanceReferenceData native) { reference=native; }
    internal static bool Ready => reference?.RunTimeInstance?.GetComponent<Animator>() != null;
    private static bool unavailable;
    private static bool observedVisible;
    private static int layer = -1;
    private static readonly int Fire = Animator.StringToHash("hud DOT Fire"), Fade = Animator.StringToHash("hud DOT FireFade");
    private static Animator? Bind(Action<string> log)
    {
        var native = reference?.RunTimeInstance?.GetComponent<Animator>();
        if (native == null) {
            if(!unavailable) log("NS_FIRE_HUD_UNAVAILABLE authoredReference="+(reference==null?"none":reference.name)+" combatUnchanged=True");
            unavailable=true; return null;
        }
        if (animator != native) {
            layer=-1;
            for(int i=0;i<native.layerCount;i++) if(native.HasState(i,Fire) && native.HasState(i,Fade)) { layer=i; break; }
            animator=native;
            fireRoot=null;glow=null;
            fireRoot=native.transform.Find("Damage_DOT Fire");
            glow=fireRoot?.Find("Red Glow")?.GetComponent<SpriteRenderer>();
            if(YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures && YiGodhome.Bridge.SessionConfiguration.Current.Has("hud-reference-probe") && fireRoot!=null) {
                var sprites=fireRoot.GetComponentsInChildren<SpriteRenderer>(true);
                if(sprites.Length>8)throw new InvalidOperationException("Native fire HUD renderer evidence budget changed.");
                foreach(var sprite in sprites) {
                    var material=sprite.sharedMaterial;
                    log("NS_FIRE_HUD_RENDER_CONTRACT renderer="+sprite.name.Replace(' ','_')+" shader="+(material?.shader?.name??"none")
                        +" spriteAlpha="+sprite.color.a+" materialColor="+(material!=null && material.HasProperty("_Color")?material.GetColor("_Color").ToString():"none")
                        +" tintColor="+(material!=null && material.HasProperty("_TintColor")?material.GetColor("_TintColor").ToString():"none")
                        +" srcBlend="+(material!=null && material.HasProperty("_SrcBlend")?material.GetFloat("_SrcBlend"):-1)
                        +" dstBlend="+(material!=null && material.HasProperty("_DstBlend")?material.GetFloat("_DstBlend"):-1)
                        +" nativeMaterialUnchanged=True");
                }
            }
            if(layer<0) {
                if(!unavailable) log("NS_FIRE_HUD_UNAVAILABLE animator="+native.GetInstanceID()+" layers="+native.layerCount+" combatUnchanged=True");
                unavailable=true; return null;
            }
            unavailable=false;
            log("NS_FIRE_HUD_BOUND nativeAnimator="+native.GetInstanceID()+" layer="+layer+" name="+native.GetLayerName(layer));
        }
        return layer<0 ? null : native;
    }
    internal static void Observe(Action<string> log) {
        if(Ready) Bind(log);
        bool visible=EffectsVisible;
        if(visible!=observedVisible) {
            observedVisible=visible;
            log("NS_FIRE_HUD_NATIVE_STATE visible="+visible+" passive=True root=Damage_DOT_Fire frame="+Time.frameCount);
        }
    }
    internal static bool Visible
    {
        get {
            if (animator == null || layer < 0) return false;
            var state = animator.GetCurrentAnimatorStateInfo(layer);
            return state.shortNameHash == Fire || state.shortNameHash == Fade && state.normalizedTime < 1;
        }
    }
    internal static Transform? Root => fireRoot;
    internal static bool EffectsVisible
    {
        get {
            return Visible && fireRoot!=null && fireRoot.gameObject.activeInHierarchy;
        }
    }
    internal static void Reset() { glow=null;fireRoot=null;animator=null;layer=-1;unavailable=false;observedVisible=false; }
}
