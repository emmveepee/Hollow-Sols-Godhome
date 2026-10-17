using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace YiGodhome;

// Observation only: no result replacement, state mutation, or skipped native calls.
internal sealed class NativeCombatObserver : IDisposable
{
    private static NativeCombatObserver? active;
    private readonly Harmony harmony = new Harmony("local.yigodhome.combat.observer");
    private readonly Action<string> log;
    private int count;
    public Action<EffectHitData>? IncomingContact;
    public Action<MonsterCore, EffectHitData>? TargetHitCompleted;
    public NativeCombatObserver(Action<string> log)
    {
        if (active != null) throw new InvalidOperationException("Combat observer already active.");
        this.log = log; active = this;
        try
        {
            Hook(typeof(DamageReceiver), "ApplyDelayDamage");
            Hook(typeof(PlayerHealth), "ReceiveDamage");
            Hook(typeof(PlayerParryState), "Parried");
            Hook(typeof(ParriableAttackEffect), "PlayerParriedCheck");
            Hook(typeof(MonsterCore), "DecreasePosture");
            Hook(typeof(PostureSystem), "TakeDamage");
            Hook(typeof(FooAttachable), "OnDeposit");
            Hook(typeof(FooAttachable), "OnExplode");
        }
        catch { Dispose(); throw; }
    }
    private void Hook(Type type, string name)
    {
        var method = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly).Single(m => m.Name == name);
        if (method.ReturnType != typeof(bool) && method.ReturnType != typeof(void)) throw new InvalidOperationException("Unexpected observed return type.");
        harmony.Patch(method, prefix: new HarmonyMethod(typeof(NativeCombatObserver), nameof(Before)),
            postfix: new HarmonyMethod(typeof(NativeCombatObserver), method.ReturnType == typeof(bool) ? nameof(AfterBool) : nameof(AfterVoid)));
        log("NS_COMBAT_OBSERVER method=" + type.Name + "." + name);
    }
    public void ObserveLifecycle()
    {
        Hook(typeof(PlayerDeadState), "Revive"); Hook(typeof(GameCore), "ResetLevel");
        Hook(typeof(Player), "RestoreEverything"); Hook(typeof(Player), "RespawnAtSavePoint");
    }
    private static void Before(object __instance, MethodBase __originalMethod, object[] __args)
        => active?.Record("before", __instance, __originalMethod, __args, "pending");
    private static void AfterBool(object __instance, MethodBase __originalMethod, object[] __args, bool __result)
        => active?.Record("after", __instance, __originalMethod, __args, __result.ToString());
    private static void AfterVoid(object __instance, MethodBase __originalMethod, object[] __args)
        => active?.Record("after", __instance, __originalMethod, __args, "void");
    private void Record(string phase, object instance, MethodBase method, object[] args, string result)
    {
        if (++count > 2000) return;
        var player = Player.i;
        var hit = args.OfType<EffectHitData>().FirstOrDefault();
        if (phase == "before" && method.Name == "PlayerParriedCheck" && hit != null) IncomingContact?.Invoke(hit);
        var posture = instance as PostureSystem ?? (instance as MonsterCore)?.postureSystem;
        var parry = instance as PlayerParryState;
        log("NS_COMBAT_EVENT n=" + count + " frame=" + Time.frameCount + " phase=" + phase + " method=" + method.DeclaringType!.Name + "." + method.Name
            + " instance=" + (instance is UnityEngine.Object o ? o.GetInstanceID().ToString() : "none")
            + " result=" + result + " health=" + (player == null ? -1 : player.health.currentValue)
            + " state=" + (player == null ? "none" : player.CurrentStateType.ToString())
            + " chi=" + (player == null ? -1 : player.chiContainer.Value)
            + " posture=" + (posture == null ? -1 : posture.PostureValue)
            + " injury=" + (posture == null ? -1 : posture.InternalInjury)
            + " accurate=" + (parry != null && parry.IsAccurateParried)
            + " playerInjury=" + (player == null ? -1 : ((PlayerHealth)player.health).CurrentInternalInjury)
            + " dealer=" + (hit?.dealer == null ? "none" : hit.dealer.name + ":" + hit.dealer.GetInstanceID())
            + " receiver=" + (hit?.receiver == null ? "none" : hit.receiver.name + ":" + hit.receiver.GetInstanceID()));
        if (phase == "after" && instance is MonsterCore core && hit != null) TargetHitCompleted?.Invoke(core, hit);
    }
    public void Dispose() { if (active == this) { active = null; harmony.UnpatchSelf(); } }
}
