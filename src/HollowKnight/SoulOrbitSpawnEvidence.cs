using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace YiGodhome;

// Finite observation of roots produced by the pinned native spinner emission.
internal sealed class SoulOrbitSpawnEvidence : MonoBehaviour
{
    private PlayMakerFSM control=null!;
    private MeshRenderer art=null!;
    private tk2dBaseSprite sprite=null!;
    private Action<string>? log;
    private long group;
    private int samples;
    private float spawnedAt,next;
    private static readonly System.Reflection.FieldInfo CallbackAction=AccessTools.Field(typeof(iTweenFSMEvents),"itweenFSMAction");
    internal static void Bind(GameObject root,long group,Action<string>? log)
    {
        var control=Array.Find(root.GetComponents<PlayMakerFSM>(),f=>f.FsmName=="Orb Control");
        if(control==null || root.GetComponent<MeshRenderer>()==null || root.GetComponent<tk2dBaseSprite>()==null)
            throw new InvalidOperationException("Native orbit spawn artwork/control contract changed.");
        var probe=root.GetComponent<SoulOrbitSpawnEvidence>()??root.AddComponent<SoulOrbitSpawnEvidence>();
        probe.control=control;probe.art=root.GetComponent<MeshRenderer>();probe.sprite=root.GetComponent<tk2dBaseSprite>();
        probe.log=log;probe.group=group;probe.samples=0;probe.spawnedAt=Time.time;probe.next=Time.time;
        probe.Sample();
    }
    private void Update(){if(control!=null && samples<6 && Time.time>=next)Sample();}
    private void Sample()
    {
        if(control==null || art==null || sprite==null)return;
        samples++;next=Time.time+(samples<3?.15f:samples==3?.3f:samples==4?.6f:1.2f);
        log?.Invoke("HK_SOUL_ORBIT_SPAWN_SAMPLE group="+group+" root="+gameObject.GetInstanceID()+" sample="+samples+" state="+control.ActiveStateName.Replace(' ','_')
            +" active="+gameObject.activeInHierarchy+" fsmEnabled="+control.enabled+" renderer="+art.enabled+" alpha="+sprite.color.a
            +" scale="+transform.lossyScale+" position="+transform.position+" parent="+(transform.parent?.name.Replace(' ','_')??"none")
            +" gameplayTime="+Time.time+" elapsed="+(Time.time-spawnedAt)+" frame="+Time.frameCount);
        var callbacks=GetComponents<iTweenFSMEvents>();
        if(callbacks.Length>12)throw new InvalidOperationException("Native orb tween callback budget changed.");
        foreach(var callback in callbacks) {
            if(callback==null)continue;
            if(CallbackAction.GetValue(callback) is not HutongGames.PlayMaker.Actions.iTweenMoveBy action || action.Fsm.GameObject==null || action.Fsm.GameObject.name!="Orb Spinner" || action.State.Name!="Spawn")continue;
            var moves=action.State.Actions.OfType<HutongGames.PlayMaker.Actions.iTweenMoveBy>().ToArray();
            log?.Invoke("HK_SOUL_ORBIT_TWEEN_WAIT group="+group+" root="+gameObject.GetInstanceID()+" spinner="+action.Fsm.GameObject.GetInstanceID()
                +" nativeState="+action.Fsm.ActiveStateName.Replace(' ','_')+" actionFinished="+action.Finished+" pending="+moves.Count(m=>!m.Finished)
                +" time="+action.time.Value+" delay="+action.delay.Value+" exactTarget="+(FSMUtility.GetSafe(action.gameObject,action)==gameObject)
                +" gameplayTime="+Time.time+" elapsed="+(Time.time-spawnedAt)+" frame="+Time.frameCount);
        }
    }
    private void OnDisable(){if(control!=null && samples<6)Sample();}
}
