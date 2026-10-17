using System;
using System.IO;
using UnityEngine;
using YiGodhome.Bridge;

namespace YiGodhome;

// Diagnostic collision only: native health, jade usage and death callbacks decide
// the result. No health refill is allowed between the two contacts.
internal sealed class RadiantRevivalProbe : IDisposable
{
    private static RadiantRevivalProbe? current;
    private static int radiantRuns;
    private bool withJade;
    private readonly bool enabled;
    public bool Enabled => enabled && (DiagnosticDifficulty.Tier == 2 || phase != 5);
    public static bool BlocksCheat => current != null && current.phase != 5;
    private readonly MonsterBase owner;
    private readonly Action<string> log;
    private NativeContactFixture? attack;
    private int phase;
    private float deadline;
    public RadiantRevivalProbe(MonsterBase owner, Action<string> log)
    {
        this.owner = owner; this.log = log;
        string root = Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!;
        enabled = !SessionConfiguration.Current.Has("invulnerable-probe") && EncounterProfile.Selected == EncounterProfile.PureVessel && YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures && YiGodhome.Bridge.SessionConfiguration.Current.Has("cheat-probe");
    }
    public void Update(ContactFrame? frame)
    {
        if (!enabled || phase == 5) return;
        var player = Player.i;
        if (player == null) return;
        var jade = player.mainAbilities.ReviveJade.AbilityData;
        if (phase == 0) {
            if (frame == null || !frame.HurtActive || !player.actions.Enabled) return;
            player.RestoreEverything();
            if (DiagnosticDifficulty.Tier == 2) {
                withJade = ++radiantRuns == 1;
                if (withJade) { jade.SetToActivated(); jade.RestoreUsage(player); }
                else jade.UnEquipCheck();
                if (jade.IsActivated != withJade) throw new InvalidOperationException("Native revival jade equipment did not match diagnostic.");
            }
            attack = new NativeContactFixture(owner, log, 27, independent: true); attack.SetDifficulty(2, frame.Difficulty);
            current = this;
            deadline = Time.realtimeSinceStartup + 2; phase = 1;
            log(DiagnosticDifficulty.Tier == 2 ? "NS_RADIANT_PROBE_BEGIN nativeJade=" + withJade + " charge=" + jade.CanUseEffect : "NS_DIFFICULTY_PROBE_BEGIN tier=" + frame.Difficulty + " health=" + player.health.currentValue);
        }
        if ((phase == 1 || phase == 3) && Time.realtimeSinceStartup >= deadline) {
            player.ChangeState(PlayerStateType.Normal, false);
            attack!.SetWorldBounds(player.transform.position + new Vector3(0, 15, 0), new Vector2(40, 50), true);
            phase++; deadline = Time.realtimeSinceStartup + 3;
        }
        if (phase == 2 || phase == 4)
            attack!.SetWorldBounds(player.transform.position + new Vector3(0, 15, 0), new Vector2(40, 50), true);
        if (DiagnosticDifficulty.Tier == 2 && withJade && phase == 2 && !jade.CanUseEffect) {
            attack!.SetWorldBounds(Vector3.zero, Vector2.one, false);
            if (player.health.IsDead) throw new InvalidOperationException("Radiant hit bypassed native revival.");
            log("NS_RADIANT_REVIVED health=" + player.health.currentValue + " charge=False nativeCollision=True");
            phase = 3; deadline = Time.realtimeSinceStartup + 3;
        }
        ObserveNativeDamage();
        if ((phase == 2 || phase == 4) && Time.realtimeSinceStartup > deadline)
            throw new InvalidOperationException("Radiant diagnostic collision missed verdict: phase=" + phase + " health=" + player.health.currentValue + " state=" + player.CurrentStateType + " charge=" + jade.CanUseEffect);
    }
    public static void ObserveNativeDamage()
    {
        var probe = current; var player = Player.i;
        if (probe != null && probe.phase == 2 && DiagnosticDifficulty.Tier == 2 && !probe.withJade && player != null && player.health.IsDead) {
            probe.phase = 5;
            probe.log("NS_RADIANT_NO_JADE_DEAD before=240 after=0 activated=" + player.mainAbilities.ReviveJade.AbilityData.IsActivated + " state=" + player.CurrentStateType + " nativeCollision=True");
        }
        if (probe != null && probe.phase == 2 && DiagnosticDifficulty.Tier < 2 && player != null && player.health.currentValue < 240) {
            float expected = DiagnosticDifficulty.Tier == 0 ? 186 : 132;
            probe.log("NS_DIFFICULTY_DAMAGE tier=" + DiagnosticDifficulty.Tier + " before=240 after=" + player.health.currentValue + " expected=" + expected + " nativeCollision=True");
            if (Math.Abs(player.health.currentValue - expected) > 0.01f) throw new InvalidOperationException("Native tier damage differed from its calibrated amount.");
            probe.phase = 5; probe.attack!.SetWorldBounds(Vector3.zero, Vector2.one, false);
        }
        if (probe == null || probe.phase != 4 || player == null || !player.health.IsDead) return;
        probe.phase = 5;
        probe.log("NS_RADIANT_SECOND_HIT_DEAD charge=" + player.mainAbilities.ReviveJade.AbilityData.CanUseEffect + " state=" + player.CurrentStateType + " nativeCollision=True");
    }
    public void Dispose() { ObserveNativeDamage(); if (current == this) current = null; attack?.Dispose(); }
}
