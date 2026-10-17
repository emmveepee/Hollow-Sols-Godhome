using System;
using System.Linq;
using HarmonyLib;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace YiGodhome;

internal static class NativeOrbTweenCompletion
{
    private static readonly System.Reflection.FieldInfo CallbackAction=AccessTools.Field(typeof(iTweenFSMEvents),"itweenFSMAction");
    internal static void CompleteStopped(GameObject orb,Transform spinner,int collider,long activation,Action<string>? log)
    {
        var summon=spinner.GetComponents<PlayMakerFSM>().Single(f=>f.FsmName=="Summon Orbs");
        if(summon.ActiveStateName!="Spawn")return;
        var callbacks=orb.GetComponents<iTweenFSMEvents>();
        if(callbacks.Length>12)throw new InvalidOperationException("Native orbit tween callback ownership budget changed.");
        var moves=callbacks.Where(c=>c!=null).Select(c=>CallbackAction.GetValue(c)).OfType<iTweenMoveBy>()
            .Where(a=>a.Fsm==summon.Fsm && a.State.Name=="Spawn" && !a.Finished && FSMUtility.GetSafe(a.gameObject,a)==orb).Distinct().ToArray();
        if(moves.Length>1 || moves.Any(a=>(int)a.loopType!=0))throw new InvalidOperationException("Native orbit expansion action ownership changed.");
        foreach(var move in moves) {
            // Only transient native completion is acknowledged. Other roots keep
            // their authored tweens, and the native FSM chooses its next state.
            move.Finish();
            log?.Invoke("HK_SOUL_ORBIT_TWEEN_SETTLED root="+orb.GetInstanceID()+" spinner="+spinner.gameObject.GetInstanceID()
                +" collider="+collider+" activation="+activation+" actionFinished="+move.Finished+" nativeState="+summon.ActiveStateName.Replace(' ','_')
                +" otherPending="+move.State.Actions.OfType<iTweenMoveBy>().Count(a=>!a.Finished)+" nativeCompletionOnly=True frame="+Time.frameCount);
        }
    }
}
