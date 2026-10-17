using System;
using System.Linq;
using UnityEngine;

namespace YiGodhome;

// Bounded trace-only observation of one admitted native orb and its own art.
// It does not drive animation, damage, pooling, parentage or FSM transitions.
internal sealed class SoulOrbImpactEvidence : MonoBehaviour
{
    private PlayMakerFSM control=null!;
    private Action<string>? log;
    private tk2dSpriteAnimator[] animators=Array.Empty<tk2dSpriteAnimator>();
    private PlayMakerFSM[] impactControls=Array.Empty<PlayMakerFSM>();
    private Transform? impact;
    private string state="";
    private int samples;
    private float nextSample,impactAt;
    private static readonly bool[] pixelClaimed=new bool[2];
    private int pixelKind=-1,pixelStage;
    internal static void Bind(PlayMakerFSM orb,Action<string>? logger) {
        if(!YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures || !YiGodhome.Bridge.SessionConfiguration.Current.Has("trace-probe")
            || orb.GetComponent<SoulOrbImpactEvidence>()!=null)return;
        var probe=orb.gameObject.AddComponent<SoulOrbImpactEvidence>();
        probe.control=orb;probe.log=logger;
        probe.animators=orb.GetComponentsInChildren<tk2dSpriteAnimator>(true);
        if(probe.animators.Length>8)throw new InvalidOperationException("Native orb animation evidence budget changed.");
        probe.impact=orb.transform.Find("Impact");
        if(probe.impact==null)throw new InvalidOperationException("Native orb impact artwork reference changed.");
        probe.impactControls=probe.impact.GetComponentsInChildren<PlayMakerFSM>(true);
        if(probe.impactControls.Length>4)throw new InvalidOperationException("Native orb impact FSM evidence budget changed.");
        logger?.Invoke("HK_SOUL_IMPACT_ART_BIND root="+orb.gameObject.GetInstanceID()+" impact="+probe.impact.gameObject.GetInstanceID()
            +" components="+string.Join(",",probe.impact.GetComponents<Component>().Select(c=>c.GetType().FullName+"@"+c.GetType().Assembly.GetName().Name))
            +" impactParent="+(probe.impact.parent?.gameObject.GetInstanceID()??0)+" typedAnimators="+probe.animators.Length+" nativeChild=True");
        var direct=probe.impact.GetComponent<tk2dSpriteAnimator>();
        logger?.Invoke("HK_SOUL_IMPACT_ANIMATOR_REFERENCE root="+orb.gameObject.GetInstanceID()+" direct="+(direct!=null)
            +" cached="+(direct!=null && probe.animators.Contains(direct))+" impactSelf="+probe.impact.gameObject.activeSelf);
        foreach(var wait in orb.FsmStates.Single(s=>s.Name=="Impact").Actions.OfType<HutongGames.PlayMaker.Actions.Wait>())
            logger?.Invoke("HK_SOUL_IMPACT_NATIVE_WAIT root="+orb.gameObject.GetInstanceID()+" seconds="+wait.time.Value);
    }
    private void Update() {
        if(control==null || samples>=16)return;
        bool changed=state!=control.ActiveStateName;
        if(changed) {state=control.ActiveStateName;if(state=="Impact")impactAt=Time.time;}
        if(changed && state=="Impact" && impact!=null && impact.gameObject.activeInHierarchy
            && control.GetComponent<NativeSoulOrbImpact>()!=null) {
            int kind=transform.parent==null?1:0;
            if(!pixelClaimed[kind]) {pixelClaimed[kind]=true;pixelKind=kind;pixelStage=1;CapturePixels("active");}
        }
        if(pixelStage==1 && state=="Impact" && Time.time-impactAt>=.6f && impact!=null && !impact.gameObject.activeInHierarchy) {
            CapturePixels("cleared");pixelStage=2;
        }
        if(!changed && (state!="Impact" || Time.time<nextSample))return;
        nextSample=Time.time+(Time.time-impactAt<.5f?.1f:1);samples++;
        log?.Invoke("HK_SOUL_ORB_LIFETIME root="+gameObject.GetInstanceID()+" state="+state.Replace(' ','_')+" parent="+(transform.parent?.name.Replace(' ','_')??"none")
            +" impactActive="+(impact!=null && impact.gameObject.activeInHierarchy)+" gameplayTime="+Time.time+" impactElapsed="+(Time.time-impactAt)
            +" fsmEnabled="+control.enabled+" clockScale="+Time.timeScale+" frame="+Time.frameCount);
        var direct=impact?.GetComponent<tk2dSpriteAnimator>();
        log?.Invoke("HK_SOUL_IMPACT_DIRECT root="+gameObject.GetInstanceID()+" animator="+(direct?.GetInstanceID()??0)
            +" cachedAlive="+animators.Count(a=>a!=null)+" enabled="+(direct?.enabled??false)+" paused="+(direct?.Paused??false)
            +" clip="+(direct?.CurrentClip?.name.Replace(' ','_')??"none")+" animationFrame="+(direct?.CurrentClip==null?-1:direct.CurrentFrame)+" playing="+(direct?.Playing??false)+" frame="+Time.frameCount);
        foreach(var animator in animators)if(animator!=null)
            log?.Invoke("HK_SOUL_ORB_ANIMATION root="+gameObject.GetInstanceID()+" owner="+animator.name.Replace(' ','_')+" active="+animator.gameObject.activeInHierarchy
                +" enabled="+animator.enabled+" paused="+animator.Paused+" clip="+(animator.CurrentClip?.name.Replace(' ','_')??"none")
                +" animationFrame="+(animator.CurrentClip==null?-1:animator.CurrentFrame)+" sprite="+(animator.GetComponent<tk2dBaseSprite>()?.spriteId??-1)+" frame="+Time.frameCount);
        foreach(var fsm in impactControls)if(fsm!=null)
            log?.Invoke("HK_SOUL_IMPACT_ART_STATE root="+gameObject.GetInstanceID()+" owner="+fsm.name.Replace(' ','_')+" fsm="+fsm.FsmName.Replace(' ','_')
                +" state="+fsm.ActiveStateName.Replace(' ','_')+" enabled="+fsm.enabled+" active="+fsm.gameObject.activeInHierarchy+" frame="+Time.frameCount);
    }
    private void CapturePixels(string stage) {
        string file="native-soul-impact-"+(pixelKind==1?"reflected":"attached")+"-"+stage+".png";
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!,file));
        log?.Invoke("HK_SOUL_IMPACT_PIXELS root="+gameObject.GetInstanceID()+" file="+file+" stage="+stage
            +" position="+transform.position+" parent="+(transform.parent?.name.Replace(' ','_')??"none")
            +" gameplayTime="+Time.time+" elapsed="+(Time.time-impactAt)+" frame="+Time.frameCount+" diagnosticOnly=True");
    }
    private void OnDisable() {
        if(control!=null && samples<16)log?.Invoke("HK_SOUL_ORB_DISABLED root="+gameObject.GetInstanceID()+" state="+control.ActiveStateName.Replace(' ','_')
            +" impactActive="+(impact!=null && impact.gameObject.activeSelf)+" gameplayTime="+Time.time+" frame="+Time.frameCount);
    }
    private void OnEnable() {state="";nextSample=0;}
}
