using System;
using UnityEngine;

namespace YiGodhome;

// The orbit prefab omits the native one-shot animation owner used by Mage Orb.
// Restore that owner on admitted instances; pooling resets only its old artwork.
internal sealed class NativeSoulOrbImpact : MonoBehaviour
{
    private GameObject impact=null!;
    internal static void Bind(PlayMakerFSM orb,int collider,Action<string>? log)
    {
        if(orb.GetComponent<NativeSoulOrbImpact>()!=null)return;
        var child=orb.transform.Find("Impact");
        if(child==null || child.parent!=orb.transform)throw new InvalidOperationException("Native Soul impact child ownership changed.");
        if(child.GetComponent<DeactivateAfter2dtkAnimation>()!=null)return;
        var animator=child.GetComponent<tk2dSpriteAnimator>();
        if(animator==null || animator.DefaultClip?.name!="Impact" || animator.DefaultClip.frames.Length==0 || animator.DefaultClip.fps<=0
            || animator.DefaultClip.wrapMode!=tk2dSpriteAnimationClip.WrapMode.Once)
            throw new InvalidOperationException("Native Soul impact animation contract changed.");
        var owner=orb.gameObject.AddComponent<NativeSoulOrbImpact>();owner.impact=child.gameObject;
        owner.impact.SetActive(false);
        owner.impact.AddComponent<DeactivateAfter2dtkAnimation>();
        log?.Invoke("HK_SOUL_NATIVE_IMPACT_RESTORED root="+orb.gameObject.GetInstanceID()+" impact="+child.gameObject.GetInstanceID()
            +" collider="+collider+" nativeAnimationOwner=True poolReset=True frame="+Time.frameCount);
    }
    private void OnDisable(){if(impact!=null)impact.SetActive(false);}
}
