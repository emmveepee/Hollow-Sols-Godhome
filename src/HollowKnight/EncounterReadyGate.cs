using System;
using HarmonyLib;
using UnityEngine;

namespace YiGodhome;

// Defer the native wake action, rather than freezing the game or replacing its FSM.
internal sealed class EncounterReadyGate : IDisposable
{
    private static EncounterReadyGate? current;
    private readonly Harmony harmony = new Harmony("local.yigodhome.encounter-ready");
    private readonly Action<string> log;
    private HutongGames.PlayMaker.FsmStateAction? pending;
    private float heldAt;
    private bool resuming;
    private Renderer[] hidden = Array.Empty<Renderer>();
    private bool[] rendererStates = Array.Empty<bool>();
    public bool Waiting => pending != null;

    public EncounterReadyGate(Action<string> log)
    {
        this.log = log;
        if (current != null) throw new InvalidOperationException("Duplicate encounter readiness gate.");
        current = this;
        harmony.Patch(AccessTools.Method(typeof(HutongGames.PlayMaker.Actions.Tk2dPlayAnimation), "OnEnter"),
            prefix: new HarmonyMethod(typeof(EncounterReadyGate), nameof(BeforeEnter)));
        harmony.Patch(AccessTools.Method(typeof(WaitForFinishedEnteringScene), "OnEnter"),
            prefix: new HarmonyMethod(typeof(EncounterReadyGate), nameof(BeforeEnter)));
        harmony.Patch(AccessTools.Method(typeof(HutongGames.PlayMaker.Actions.Wait), "OnEnter"),
            prefix: new HarmonyMethod(typeof(EncounterReadyGate), nameof(BeforeEnter)));
        harmony.Patch(AccessTools.Method(typeof(HutongGames.PlayMaker.Actions.Wait), "OnUpdate"),
            prefix: new HarmonyMethod(typeof(EncounterReadyGate), nameof(BeforeUpdate)));
    }

    private static bool BeforeUpdate(HutongGames.PlayMaker.FsmStateAction __instance) => current?.pending != __instance;

    private static bool BeforeEnter(HutongGames.PlayMaker.FsmStateAction __instance)
    {
        var gate = current;
        if (gate == null || gate.resuming) return true;
        var owner = __instance.Fsm.GameObject;
        var profile = owner == null ? null : Bridge.EncounterProfile.ForScene(owner.scene.name);
        if (profile == null) return true;
        if (owner == null || owner.name != profile.Boss || owner.scene.name != profile.Scene) return true;
        bool matchingAction = profile.EntryStartsAnimation ? __instance is HutongGames.PlayMaker.Actions.Tk2dPlayAnimation
            : profile.EntryWaitsForPlayer ? __instance is WaitForFinishedEnteringScene : __instance is HutongGames.PlayMaker.Actions.Wait;
        if (!matchingAction || __instance.Fsm.Name != profile.EntryFsm || __instance.State.Name != profile.EntryState) return true;
        if (GameManager.instance.profileID != 4) throw new InvalidOperationException("Encounter gate outside profile 4.");
        if (gate.pending != null) throw new InvalidOperationException("Repeated native wake while readiness is pending.");
        gate.pending = __instance;
        // Hold attack admission, not native boss artwork through the scene fade.
        gate.heldAt = Time.realtimeSinceStartup;
        gate.log("HK_ENCOUNTER_HELD state=" + __instance.State.Name.Replace(' ', '_'));
        if (Bridge.SessionConfiguration.Current.RunFixtures) {
            gate.log("HK_ENTRY_ART scene=" + profile.Scene + " state=" + __instance.State.Name.Replace(' ', '_')
                + " clip=" + (owner.GetComponent<tk2dSpriteAnimator>()?.CurrentClip?.name ?? "none") + " frame=" + Time.frameCount);
            if (profile == Bridge.EncounterProfile.Gruz)
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!, "native-gruz-entry.png"));
        }
        return false;
    }

    public void Update(bool ready)
    {
        var action = pending;
        if (action == null) return;
        if (action.Fsm.GameObject == null || action.Fsm.ActiveStateName != action.State.Name)
            throw new InvalidOperationException("Native wake left held state before readiness.");
        if (Time.realtimeSinceStartup - heldAt > 60) throw new TimeoutException("Yi did not become ready for the held encounter.");
        if (!ready) return;
        pending = null;
        RestorePresentation();
        log("HK_ENCOUNTER_RELEASED waitedSeconds=" + (Time.realtimeSinceStartup - heldAt));
        resuming = true;
        try { action.OnEnter(); }
        finally { resuming = false; }
    }

    internal void CancelForNativeReturn(string scene)
    {
        var action = pending;
        if (action == null) return;
        var owner = action.Fsm.GameObject;
        var profile = Bridge.EncounterProfile.ForScene(scene);
        if (owner == null || profile == null || owner.scene.name != scene || owner.name != profile.Boss
            || action.Fsm.Name != profile.EntryFsm || action.State.Name != profile.EntryState)
            throw new InvalidOperationException("Cancelled encounter admission owner mismatch.");
        pending = null;
        RestorePresentation();
        log("HK_ENCOUNTER_ADMISSION_CANCELLED scene=" + scene + " state=" + action.State.Name.Replace(' ', '_') + " nativeReturn=True");
    }

    private void RestorePresentation()
    {
        for (int i = 0; i < hidden.Length; i++) if (hidden[i] != null) hidden[i].enabled = rendererStates[i];
        hidden = Array.Empty<Renderer>(); rendererStates = Array.Empty<bool>();
    }
    public void Dispose() { RestorePresentation(); harmony.UnpatchSelf(); pending = null; if (current == this) current = null; }
}
