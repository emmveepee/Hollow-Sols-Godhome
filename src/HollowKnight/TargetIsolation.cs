using System;
using HarmonyLib;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

namespace YiGodhome;

// The retained Knight is a camera/AI target. Yi is the sole player-health authority.
internal sealed class TargetIsolation : IDisposable
{
    private static TargetIsolation? active;
    private readonly Harmony harmony = new Harmony("local.yigodhome.target.isolation");
    private readonly Action<string> log;
    private bool reported;
    public TargetIsolation(Action<string> log)
    {
        if (active != null) throw new InvalidOperationException("Target isolation already installed.");
        this.log = log; active = this;
        try { harmony.Patch(AccessTools.Method(typeof(HeroController), "CanTakeDamage"), postfix: new HarmonyMethod(typeof(TargetIsolation), nameof(After))); }
        catch { Dispose(); throw; }
    }
    private static void After(HeroController __instance, ref bool __result)
    {
        var current = active;
        if (current == null || __instance != HeroController.instance || GameManager.instance.profileID != 4
            || UnitySceneManager.GetActiveScene().name != YiGodhome.Bridge.EncounterProfile.Selected.Scene) return;
        if (__result && !current.reported)
        {
            current.reported = true;
            current.log("HK_TARGET_NATIVE_DAMAGE_REFUSED mode=" + __instance.damageMode + " invincible=" + GameManager.instance.playerData.isInvincible);
        }
        __result = false;
    }
    public void Dispose() { if (active == this) { active = null; harmony.UnpatchSelf(); } }
}

