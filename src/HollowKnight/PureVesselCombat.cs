using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using YiGodhome.Bridge;

namespace YiGodhome;

// PV's authored state/body mapping; native FSMs still own motion and recovery.
internal sealed class PureVesselCombat : IDisposable
{
    private readonly HealthManager boss;
    private readonly Guid encounter;
    private string previousState = "";
    private readonly PlayMakerFSM control;
    private readonly Action<string>? log;
    private int family;
    private long action, retired;
    private long focusAction, retiredFocus;
    private readonly System.Collections.Generic.Dictionary<int, (bool Enabled, long Action)> explosions = new System.Collections.Generic.Dictionary<int, (bool, long)>();
    private float dashRecovery;
    private readonly bool probe;
    private float probeAt = -1;
    private int probeCommand = -1;
    private bool tendrilIssued;
    private readonly System.Collections.Generic.List<(HutongGames.PlayMaker.FsmFloat Time, float Original)> probeWaits = new System.Collections.Generic.List<(HutongGames.PlayMaker.FsmFloat, float)>();
    public static PureVesselCombat? Current { get; private set; }
    public bool CounterOnly { get; private set; }
    private int counterCycles;
    private readonly Harmony counterHarmony = new Harmony("local.yigodhome.pv-counter-only");
    private static void SelectCounter(HutongGames.PlayMaker.Fsm __instance, ref HutongGames.PlayMaker.FsmState __0)
    {
        var adapter = Current;
        if (adapter?.CounterOnly != true || __instance != adapter.control.Fsm || __0 == null || __0.Name != "Idle Stance"
            || adapter.boss == null || adapter.boss.isDead || adapter.boss.hp <= 0) return;
        // Idle Stance chooses the next attack synchronously; Update cannot see it.
        // Redirect only this selection boundary, never the current native action.
        __0 = adapter.control.FsmStates.Single(s => s.Name == "Counter Antic");
        if (++adapter.counterCycles <= 60) adapter.log?.Invoke("HK_PV_MANUAL_COUNTER_CYCLE cycle=" + adapter.counterCycles + " nativeAntic=True frame=" + Time.frameCount);
    }
    public void SetCounterOnly(bool value)
    {
        if (!SessionConfiguration.Current.ManualTools && !probe)
            throw new InvalidOperationException("Counter-only mode requires manual F9 tools or the bounded PV fixture.");
        CounterOnly = value && boss != null && !boss.isDead && boss.hp > 0;
        log?.Invoke("HK_PV_MANUAL_COUNTER_ONLY enabled=" + CounterOnly + " encounter=" + encounter + " nativeCounter=True");
    }
    public int ProbeStage { get; private set; } = -1;
    public int Health => boss == null ? 0 : Math.Max(0, boss.hp);
    public bool TendrilActive => boss.GetComponentsInChildren<DamageHero>(true).Any(d => d.name == "T Hit" && d.isActiveAndEnabled);
    public long ActionId => family == 0 ? 0 : action;
    public bool Kickable => PureVesselAttackWindows.Dash(control.ActiveStateName) && dashRecovery == 0 && action != retired;
    public bool Crimson => (family == 2 || family == 3) && action != retired;
    public bool Windup => Crimson && (control.ActiveStateName == "Tendril Antic" || control.ActiveStateName == "Tendril Burst" || PureVesselAttackWindows.FocusWindup(control.ActiveStateName));
    public bool KickWindup => family == 1 && action != retired && PureVesselAttackWindows.DashWindup(control.ActiveStateName);
    public bool Interrupted => dashRecovery > 0;
    public bool Descending => PureVesselAttackWindows.Descending(control.ActiveStateName);
    public PureVesselCombat(HealthManager boss, PlayMakerFSM control, Action<string>? log, Guid encounter)
    {
        this.encounter = encounter; this.boss = boss; this.control = control; this.log = log;
        probe = SessionConfiguration.Current.RunFixtures && SessionConfiguration.Current.Has("pv-tuning-probe"); Current = this;
        counterHarmony.Patch(AccessTools.Method(typeof(HutongGames.PlayMaker.Fsm), "SwitchState", new[] { typeof(HutongGames.PlayMaker.FsmState) }),
            prefix: new HarmonyMethod(typeof(PureVesselCombat), nameof(SelectCounter)));
        foreach (string state in new[] { "Dash", "Dash Recover", "Dash End", "Stun Start", "Tendril Antic", "Tendril Hit", "Idle Stance", "Focus Charge", "Focus Burst", "Ball Up" })
            if (!control.FsmStates.Any(s => s.Name == state)) throw new InvalidOperationException("PV native state changed: " + state);
        var antic = boss.GetComponent<tk2dSpriteAnimator>().GetClipByName("Dash Antic");
        float duration = antic.frames.Length / antic.fps;
        if (duration < 0.15f) throw new InvalidOperationException("PV dash anticipation shorter than warning budget.");
        log?.Invoke("HK_PV_DASH_CUE_BUDGET nativeAnticSeconds=" + duration + " cueSeconds=0.10");
        if (probe) foreach (var wait in control.FsmStates.Single(s => s.Name == "Counter Stance").Actions.OfType<HutongGames.PlayMaker.Actions.Wait>()) {
            probeWaits.Add((wait.time, wait.time.Value)); wait.time.Value = 5;
        }
    }
    public void Update()
    {
        if (boss == null) return;
        if (probe) {
            if (probeAt < 0) probeAt = Time.time;
            float t = Time.time - probeAt;
            int command = t < 3 ? -1 : t < 8 ? 0 : t < 13 ? 1 : t < 22 ? 2 : t < 31 ? 3 : t < 43 ? 4 : t < 55 ? 5 : 6;
            ProbeStage = command;
            if (command != probeCommand && command >= 0 && command < 6 && dashRecovery == 0) {
                probeCommand = command;
                SetCounterOnly(command == 2);
                string probeState = command < 2 ? "Dash Antic" : command == 2 ? "Idle Stance" : command == 3 ? "SmallShot Antic" : command == 4 ? "Idle Stance" : "Slash1 Antic";
                control.SetState(probeState); log?.Invoke("HK_PV_PROBE stage=" + command + " state=" + probeState + " frame=" + Time.frameCount);
            }
            if (command == 4 && t >= 33 && !tendrilIssued) { tendrilIssued = true; control.SetState("Tendril Antic"); log?.Invoke("HK_PV_PROBE stage=4 state=Tendril Antic frame=" + Time.frameCount); }
        }
        if (dashRecovery > 0 && Time.time >= dashRecovery) {
            dashRecovery = 0; control.enabled = true;
            if (!boss.isDead && boss.hp > 0) control.SetState("Dash End");
            log?.Invoke("HK_PV_DASH_RECOVERED action=" + action + " frame=" + Time.frameCount);
        }
        string state = control.ActiveStateName;
        int next = state.StartsWith("Dash", StringComparison.Ordinal) ? 1 : state.StartsWith("Tendril", StringComparison.Ordinal) || state == "T2" || state == "T3" ? 2 : PureVesselAttackWindows.Focus(state) ? 3 : 0;
        if (next != 0 && next != family) { action++; if (next == 3) focusAction = action; log?.Invoke("HK_PV_ACTION action=" + action + " family=" + next + " state=" + state + " frame=" + Time.frameCount); }
        family = next;
        if (state == "Dash" && previousState != state) log?.Invoke("HK_PV_DASH_START action=" + action + " encounter=" + encounter + " frame=" + Time.frameCount);
        previousState = state;
    }
    public void Decorate(AttackVolume volume, DamageHero source)
    {
        string sourceName = source.name;
        if (sourceName == "Dash Stab") {
            volume.Active &= Kickable;
            if (family == 1) { volume.ActionId = action; volume.Traits |= AttackTraits.Kickable; }
        }
        if (sourceName == "T Hit") {
            // Stagger bypasses the native Tendril Hit End cleanup. Never let a
            // still-enabled child become an unrelated ordinary melee attack.
            string state = control.ActiveStateName;
            volume.Active &= Crimson && PureVesselAttackWindows.Tendril(state);
            if (family == 2) { volume.ActionId = action; volume.Traits |= AttackTraits.Crimson; volume.CounterInjuryEntity=boss.GetInstanceID(); }
        }
        if (sourceName == "Focus Hit" || sourceName == "hero_damager") {
            int id = source.GetInstanceID(); explosions.TryGetValue(id, out var old);
            long binding = volume.Active && !old.Enabled ? focusAction : old.Action;
            explosions[id] = (volume.Active, binding);
            volume.Active &= binding > 0 && binding != retiredFocus;
            volume.ActionId = binding;
            if (binding > 0) volume.Traits |= AttackTraits.Crimson;
        }
        if (sourceName == "Slash1" || sourceName == "Slash2" || sourceName == "Slash3") {
            volume.Traits |= AttackTraits.LargeParryPush | AttackTraits.RetireOnParry;
            volume.Traits |= boss.transform.lossyScale.x > 0 ? AttackTraits.PushRight : AttackTraits.PushLeft;
        }
        if (sourceName == "Slash1") volume.Traits |= AttackTraits.InitialParryPush;
        if (volume.Category == AttackCategory.Projectile && volume.ConsumeOnContact) volume.Traits |= AttackTraits.Reflectable;
        if (volume.ActionId > 0 && volume.ActionId == retired) volume.Active = false;
    }
    public bool Interrupt(long id, HitKind kind)
    {
        if (id <= 0 || id != action || id == retired || boss.isDead || boss.hp <= 0) return false;
        if (kind == HitKind.TaiChiKick && !Kickable || kind == HitKind.UnboundedCounter && !Crimson) return false;
        retired = id;
        if (family == 3) retiredFocus = focusAction;
        boss.GetComponent<Rigidbody2D>().linearVelocity = Vector2.zero;
        if (kind == HitKind.TaiChiKick) {
            control.SetState("Dash Recover");
            // Dash Recover already disables the native polygon. Keep its object
            // alive so the next authored Dash can enable that collider again.
            control.enabled = false; boss.GetComponent<Rigidbody2D>().linearVelocity = Vector2.zero;
            dashRecovery = Time.time + 0.5f;
            log?.Invoke("HK_PV_DASH_INTERRUPTED action=" + id + " seconds=0.5 damaging=False velocity=0 frame=" + Time.frameCount);
        } else {
            control.SetState("Stun Start");
            log?.Invoke((family == 2 ? "HK_PV_TENDRIL_INTERRUPTED" : "HK_PV_FOCUS_INTERRUPTED") + " action=" + id + " state=" + control.ActiveStateName + " nativeStagger=True frame=" + Time.frameCount);
        }
        return true;
    }
    public void Dispose() { counterHarmony.UnpatchSelf(); if (Current == this) Current = null; foreach (var wait in probeWaits) wait.Time.Value = wait.Original; if (dashRecovery > 0 && control != null) { control.enabled = true; dashRecovery = 0; } }
}
