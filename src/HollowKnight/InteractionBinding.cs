using System;
using HarmonyLib;
using InControl;
using UnityEngine;

namespace YiGodhome;

// Translate the interaction input, leaving authored door/statue range and eligibility checks intact.
internal sealed class InteractionBinding : PlayerActionSet, IDisposable
{
    private static InteractionBinding? current;
    private readonly Harmony harmony = new Harmony("local.yigodhome.interaction-binding");
    private readonly PlayerAction interact;
    private readonly Func<bool> ownsInput;
    private readonly Func<bool> blockInput;
    private readonly Func<float>? nativeValue;
    private readonly Action<string> log;
    private float nextTrace;
    internal static float ScriptedPressUntil;
    public bool MenuOpen;
    public InteractionBinding(Func<bool> ownsInput, Action<string> log, Func<bool>? blockInput = null, Func<float>? nativeValue=null)
    {
        if (current != null) throw new InvalidOperationException("Duplicate interaction binding.");
        this.ownsInput = ownsInput; this.log = log;
        this.nativeValue=nativeValue;
        this.blockInput = blockInput ?? (() => false);
        interact = CreatePlayerAction("YiInteract");
        // Installed Nine Sols SavePoint prompt -> Hack -> E / Action2.
        interact.AddDefaultBinding(Key.E); interact.AddDefaultBinding(InputControlType.Action2);
        current = this;
        harmony.Patch(AccessTools.Method(typeof(PlayerAction), "UpdateBindings", new[] { typeof(ulong), typeof(float) }),
            prefix: new HarmonyMethod(typeof(InteractionBinding), nameof(BeforeBindings)));
        log("HK_INTERACT_BINDING defaultKeyboard=E defaultController=Action2 nativeNsMirror="+(nativeValue!=null)+" savedBindingsUnchanged=True");
    }
    private static bool BeforeBindings(PlayerAction __instance, ulong __0, float __1)
    {
        var owner = current;
        if (owner == null || !(__instance.Owner is HeroActions actions)) return true;
        if (owner.blockInput() && __instance != actions.pause) { __instance.CommitWithValue(0, __0, __1); return false; }
        if (!owner.ownsInput()) return true;
        if (owner.MenuOpen && __instance != actions.pause)
        { __instance.CommitWithValue(0, __0, __1); return false; }
        if (__instance == actions.openInventory || __instance == actions.quickMap)
        { __instance.CommitWithValue(0, __0, __1); return false; }
        if (__instance != actions.up) return true;
        float value=owner.nativeValue?.Invoke()??owner.interact.Value;
        __instance.CommitWithValue(Time.realtimeSinceStartup < ScriptedPressUntil ? 1 : Application.isFocused ? value : 0, __0, __1);
        if (__instance.WasPressed) owner.log("HK_INTERACT_EDGE nativeUpAction=True source=NineSolsBinding");
        if (Time.realtimeSinceStartup >= owner.nextTrace)
        {
            owner.nextTrace = Time.realtimeSinceStartup + 2;
            owner.log("HK_INTERACT_REGISTRATION sourceTick=" + owner.interact.UpdateTick + " nativeTick=" + __0 + " value=" + owner.interact.Value);
        }
        return false;
    }
    public void Dispose() { harmony.UnpatchSelf(); if (current == this) current = null; Destroy(); }
}
