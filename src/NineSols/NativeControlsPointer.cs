using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using YiGodhome.Bridge;

namespace YiGodhome;

// Hit-test authored button rectangles in the exact camera/target space used
// for the forwarded pixels. Native graphics need not be raycast targets.
internal sealed class NativeControlsPointer
{
    private static readonly System.Reflection.FieldInfo GroupField=HarmonyLib.AccessTools.Field(typeof(UIControlButton),"belongGroup");
    private sealed class Target
    {
        internal readonly UIControlButton Button;
        internal readonly Selectable Selectable;
        private UIControlGroup? group;
        private UIControlGroup? Group=>group!=null?group:group=GroupField.GetValue(Button) as UIControlGroup;
        internal readonly RectTransform Rect;
        internal readonly Component[] Masks;
        internal readonly float[] Quad=new float[8];
        internal readonly float[][] Clips;
        internal readonly bool[] ClipEnabled;
        internal bool Visible;
        internal int Depth;
        internal Target(UIControlButton button)
        {
            Button=button;Selectable=button.GetComponent<Selectable>();
            Rect=(RectTransform)Selectable.transform;
            Masks=button.GetComponentsInParent<RectMask2D>(true).Cast<Component>()
                .Concat(button.GetComponentsInParent<Mask>(true)).Take(9).ToArray();
            Clips=Masks.Select(_=>new float[8]).ToArray();
            ClipEnabled=new bool[Masks.Length];
        }
        internal bool Active=>Button!=null && Button.isActiveAndEnabled && Selectable!=null && Selectable.IsInteractable()
            && Group!=null && Group.interactable && Masks.Length<=8;
    }
    private readonly Target[] targets;
    private readonly Vector3[] corners=new Vector3[4];
    private readonly Action<string> log;
    private float capturedAt=-100;
    private bool layoutLogged;
    private int hoverLogs;
    internal NativeControlsPointer(PlayerControlBindingPanel panel,Action<string> log)
    {
        this.log=log;
        var buttons=panel.GetComponentsInChildren<UIControlButton>(true);
        targets=buttons.Length>128?Array.Empty<Target>():buttons.Where(b=>b.GetComponent<Selectable>()!=null && b.transform is RectTransform).Select(b=>new Target(b)).ToArray();
        log("NS_CONTROLS_POINTER_BOUND nativeButtons="+buttons.Length+" boundedTargets="+targets.Length+" raycastRequired=False");
    }
    private bool Project(RectTransform rect,Camera camera,float[] quad)
    {
        if(rect.rect.width<=0 || rect.rect.height<=0)return false;
        rect.GetWorldCorners(corners);
        var viewport=camera.rect;
        for(int i=0;i<4;i++) {
            var p=camera.WorldToViewportPoint(corners[i]);
            if(p.z<=0)return false;
            quad[i*2]=viewport.x+p.x*viewport.width;quad[i*2+1]=viewport.y+p.y*viewport.height;
        }
        return true;
    }
    internal void Capture(Camera camera)
    {
        int visible=0;
        foreach(var target in targets) {
            target.Visible=target.Active && Project(target.Rect,camera,target.Quad);
            if(!target.Visible)continue;
            for(int i=0;i<target.Masks.Length;i++) {
                var mask=target.Masks[i] as Behaviour;
                target.ClipEnabled[i]=mask!=null && mask.isActiveAndEnabled;
                if(mask==null || !target.ClipEnabled[i])continue;
                if(!Project((RectTransform)mask.transform,camera,target.Clips[i])){target.Visible=false;break;}
            }
            target.Depth=target.Selectable.targetGraphic?.depth??0;
            if(target.Visible)visible++;
        }
        capturedAt=Time.realtimeSinceStartup;
        if(!layoutLogged && visible>0) {
            layoutLogged=true;log("NS_CONTROLS_POINTER_LAYOUT active="+visible+" camera="+camera.name+" viewport="+camera.rect+" target="+camera.targetTexture.width+"x"+camera.targetTexture.height);
            int index=0;
            foreach(var target in targets.Where(t=>t.Visible).Take(8))log("NS_CONTROLS_POINTER_RECT index="+(index++)+" button="+target.Button.name
                +" u0="+target.Quad[0]+" v0="+target.Quad[1]+" u2="+target.Quad[4]+" v2="+target.Quad[5]);
        }
    }
    internal void Point(float u,float v,bool click)
    {
        if(Time.realtimeSinceStartup-capturedAt>.25f)return;
        Target? chosen=null;
        foreach(var target in targets) {
            if(!target.Visible || !target.Active || !MenuPointer.Contains(target.Quad,u,v))continue;
            bool clipped=false;
            for(int i=0;i<target.Clips.Length;i++) {
                if(target.ClipEnabled[i] && !MenuPointer.Contains(target.Clips[i],u,v)){clipped=true;break;}
            }
            if(!clipped && (chosen==null || target.Depth>=chosen.Depth))chosen=target;
        }
        var system=EventSystem.current;
        if(chosen==null || system==null){if(click)log("NS_CONTROLS_POINTER_MISS nativeRects=True u="+u+" v="+v);return;}
        if(system.currentSelectedGameObject!=chosen.Button.gameObject) {
            system.SetSelectedGameObject(chosen.Button.gameObject);
            if(hoverLogs++<16)log("NS_CONTROLS_POINTER_HOVER nativeSelection=True button="+chosen.Button.name+" u="+u+" v="+v);
        }
        if(!click)return;
        chosen.Button.OnPointerClick(new PointerEventData(system){button=PointerEventData.InputButton.Left,clickCount=1});
        log("NS_CONTROLS_POINTER_CLICK nativeRects=True nativeCallback=True button="+chosen.Button.name+" u="+u+" v="+v);
    }
}
