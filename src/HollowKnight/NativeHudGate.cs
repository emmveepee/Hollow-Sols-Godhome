using UnityEngine;
using YiGodhome.Bridge;
namespace YiGodhome;

internal sealed class NativeHudGate
{
    internal GameObject? Owner {get;private set;}
    private PlayMakerFSM? slide;
    internal string State=>slide?.ActiveStateName??"";
    internal bool Read(out Vector3 scale)
    {
        scale=Vector3.one;var owner=GameCameras.instance?.hudCanvas;
        if(owner==null)return false;
        if(Owner!=owner || slide==null) {
            Owner=owner;slide=null;
            foreach(var fsm in owner.GetComponents<PlayMakerFSM>())if(fsm.FsmName=="Slide Out")slide=fsm;
        }
        if(slide==null)return false;
        scale=owner.transform.localScale;
        return NativeHudVisibility.Shown(owner.activeInHierarchy,State,scale.x,scale.y);
    }
}
