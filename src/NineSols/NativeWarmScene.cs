using System;
using HarmonyLib;
using UnityEngine.SceneManagement;

namespace YiGodhome;

// Select the game's existing quick-revival branch only after bridge teardown.
internal sealed class NativeWarmScene : IDisposable
{
    private static NativeWarmScene? active;
    private readonly Harmony harmony = new Harmony("local.yigodhome.warm-scene");
    private readonly Action<string> log;
    private readonly Player player;
    private readonly int sceneHandle;
    private bool armed;
    public Action? BeforeReset;
    private static void BeforeLevelReset() { if (active?.armed == true) active.BeforeReset?.Invoke(); }
    public bool ResetCompleted { get; private set; }

    public NativeWarmScene(Player player, Action<string> log)
    {
        this.player = player; this.log = log;
        ValidateScene(); sceneHandle = SceneManager.GetActiveScene().handle;
        if (active != null) throw new InvalidOperationException("Duplicate warm scene owner.");
        active = this;
        harmony.Patch(AccessTools.Method(typeof(PlayerDeadState), "CanQuickRevive"), postfix: new HarmonyMethod(typeof(NativeWarmScene), nameof(QuickRevive)));
        harmony.Patch(AccessTools.Method(typeof(GameCore), "ResetLevel", new[] { typeof(bool) }), prefix: new HarmonyMethod(typeof(NativeWarmScene), nameof(BeforeLevelReset)), postfix: new HarmonyMethod(typeof(NativeWarmScene), nameof(AfterReset)));
    }

    private static void ValidateScene()
    {
        if (!ApplicationCore.IsInBossMemoryMode || !NativeCanvas.IsServiceContext()
            || (int)AccessTools.Field(typeof(SaveManager), "currentSlotIndex").GetValue(SaveManager.Instance) != 3)
            throw new InvalidOperationException("Warm reset outside audited memory room.");
    }

    public void Arm(bool victory)
    {
        ValidateScene();
        if (SceneManager.GetActiveScene().handle != sceneHandle || GameCore.Instance.player != player)
            throw new InvalidOperationException("Warm scene/player identity changed.");
        if (!victory && !player.health.IsDead) throw new InvalidOperationException("Quick revival requires native death.");
        ResetCompleted = false; armed = true;
        log("NS_WARM_RESET_ARMED victory=" + victory + " sceneHandle=" + sceneHandle + " player=" + player.GetInstanceID());
        if (!victory) return;
        // Same native operations as PlayerDeadState's quick-revival route.
        var data = GameCore.Instance.FetchReviveData();
        GameCore.Instance.ResetLevel(false);
        player.RestoreEverything();
        player.ChangeState(PlayerStateType.Normal, false);
        player.SetPosition(data.playerSpawnPosition(), true);
    }

    private static void QuickRevive(ref bool __result)
    {
        if (active?.armed != true) return;
        ValidateScene();
        if (!active.player.health.IsDead) throw new InvalidOperationException("Quick revival without native death.");
        __result = true;
        active.log("NS_WARM_QUICK_REVIVE nativeBranch=True");
    }

    private static void AfterReset(bool __0)
    {
        var scope = active;
        if (scope?.armed != true) return;
        ValidateScene();
        if (__0 || SceneManager.GetActiveScene().handle != scope.sceneHandle || GameCore.Instance.player != scope.player)
            throw new InvalidOperationException("Warm reset reloaded scene/player.");
        scope.armed = false; scope.ResetCompleted = true;
        scope.log("NS_WARM_RESET_COMPLETED reloaded=False sceneHandle=" + scope.sceneHandle + " player=" + scope.player.GetInstanceID());
    }

    public void Dispose() { harmony.UnpatchSelf(); if (active == this) active = null; }
}
