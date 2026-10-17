using System;
using HarmonyLib;
using UnityEngine;

namespace YiGodhome;

internal static class NativeStartupScreens
{
    private static Action<string>? log;
    internal static void Install(Action<string> logger)
    {
        log=logger;
        new Harmony("local.yigodhome.startup-screens").Patch(AccessTools.Method(typeof(LogoLogic),"PaddingForApplicationStartUp"),
            prefix:new HarmonyMethod(typeof(NativeStartupScreens),nameof(SkipPresentation)));
    }
    private static bool SkipPresentation(LogoLogic __instance)
    {
        if(__instance.gameObject.scene.name!="Logo" || AccessTools.Field(typeof(LogoLogic),"NextScene").GetValue(__instance) as string!="TitleScreenMenu") return true;
        // Enter the same native completion method as the last logo. It owns
        // RuntimeInitHandler.LoadCore and the menu load, including Steam setup.
        AccessTools.Method(typeof(LogoLogic),"ChangeToMenu").Invoke(__instance,null);
        log?.Invoke("NS_STARTUP_SCREENS_SKIPPED nativeMenuCompletion=True nativeInitializationPreserved=True frame="+Time.frameCount);
        return false;
    }
}
