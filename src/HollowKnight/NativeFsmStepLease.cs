using System;
using System.Collections.Generic;
using HarmonyLib;
using HutongGames.PlayMaker;

namespace YiGodhome;

// Exact native FSM suspension preserves its state, avoiding OnDisable/Stop.
internal sealed class NativeFsmStepLease : IDisposable
{
    private static readonly HashSet<Fsm> held=new();
    private static readonly HashSet<Fsm> deferredTransitions=new();
    private static Harmony? hooks;
    private Fsm? owner;
    private static bool Step(Fsm __instance)=>!held.Contains(__instance);
    // Animation/child FSM callbacks can request transitions without stepping
    // the held FSM. Preserve their pending destination until this lease ends.
    private static bool Transition(Fsm __instance)=>!deferredTransitions.Contains(__instance);
    internal static void Prepare()
    {
        if(hooks==null) {
            hooks=new Harmony("local.yigodhome.native.fsm.step.lease");
            try {
                foreach(var method in new[]{"Update","FixedUpdate","LateUpdate"}) hooks.Patch(AccessTools.Method(typeof(Fsm),method),prefix:new HarmonyMethod(typeof(NativeFsmStepLease),nameof(Step)));
                hooks.Patch(AccessTools.Method(typeof(Fsm),"UpdateStateChanges"),prefix:new HarmonyMethod(typeof(NativeFsmStepLease),nameof(Transition)));
            }
            catch {hooks.UnpatchSelf();hooks=null;throw;}
        }
    }
    internal NativeFsmStepLease(Fsm owner,bool deferTransitions=false)
    {
        if(held.Count>=64 || held.Contains(owner)) throw new InvalidOperationException("Native FSM hold ownership/capacity changed.");
        Prepare();
        held.Add(owner);if(deferTransitions)deferredTransitions.Add(owner);this.owner=owner;
    }
    public void Dispose()
    {
        if(owner==null) return;
        deferredTransitions.Remove(owner);held.Remove(owner);owner=null;
    }
    internal static void ReleaseHooks()
    {
        if(held.Count!=0 || deferredTransitions.Count!=0) throw new InvalidOperationException("Native FSM hold survived its encounter owner.");
        hooks?.UnpatchSelf();hooks=null;
    }
}
