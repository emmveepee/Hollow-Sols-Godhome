using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace YiGodhome;

internal static class NativeStartupScreens
{
    private static Action<string>? log;
    internal static void Install(Action<string> logger)
    {
        log=logger;
        new Harmony("local.yigodhome.startup-screens").Patch(AccessTools.Method(typeof(StartManager),"Start"),
            postfix:new HarmonyMethod(typeof(NativeStartupScreens),nameof(Wrap)));
    }
    private static void Wrap(StartManager __instance,ref IEnumerator __result) => __result=SkipPresentation(__instance,__result);
    private static IEnumerator SkipPresentation(StartManager owner,IEnumerator native)
    {
        var state=AccessTools.Field(native.GetType(),"<>1__state");
        var animator=AccessTools.Field(typeof(StartManager),"startManagerAnimator").GetValue(owner) as Animator;
        int loading=Animator.StringToHash("LoadingIcon"); bool skipped=false;
        try {
            while(native.MoveNext()) {
                // Installed coroutine state5 waits only for the logo/quote
                // animator. Native language/platform/save-metadata gates run
                // before/after this state and remain in the original routine.
                if(!skipped && owner.gameObject.scene.name=="Pre_Menu_Intro" && state?.GetValue(native) is int step && step==5
                    && animator!=null && animator.HasState(0,loading)) {
                    skipped=true; animator.Play(loading,0,0); animator.Update(0);
                    log?.Invoke("HK_STARTUP_SCREENS_SKIPPED nativeLoadingState=True nativeInitializationPreserved=True frame="+Time.frameCount);
                }
                yield return native.Current;
            }
        } finally { (native as IDisposable)?.Dispose(); }
    }
}
