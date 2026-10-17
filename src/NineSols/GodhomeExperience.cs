using HarmonyLib;
using HollowSols.Foundation;

namespace YiGodhome;

// Native max-level HUD reports 100% even when current EXP is zero. Godhome
// needs a zero-progress presentation without removing combat unlocks/levels.
internal static class GodhomeExperience
{
    private static bool Locked=>NativeSaveContext.Enabled && NativeSaveContext.Mode==GameMode.Godhome;
    internal static void Install() {
        var harmony=new Harmony("local.hollowsols.ns.godhome-experience");
        harmony.Patch(AccessTools.PropertyGetter(typeof(PlayerGamePlayData),"CurrentExpPercent"),prefix:new HarmonyMethod(typeof(GodhomeExperience),nameof(Progress)));
        harmony.Patch(AccessTools.PropertySetter(typeof(PlayerGamePlayData),"CurrentExp"),prefix:new HarmonyMethod(typeof(GodhomeExperience),nameof(Write)));
        harmony.Patch(AccessTools.Method(typeof(PlayerGamePlayData),"GainExp"),prefix:new HarmonyMethod(typeof(GodhomeExperience),nameof(Gain)));
    }
    private static bool Progress(ref float __result){if(!Locked)return true;__result=0;return false;}
    private static void Write(ref int __0){if(Locked)__0=0;}
    private static bool Gain()=>!Locked;
    internal static void Normalize() {
        if(!Locked || !GameCore.IsAvailable())return;
        var data=GameCore.Instance.playerGameData;
        if(data!=null && data.CurrentExp!=0)data.CurrentExp=0;
    }
}
