using System;
using UnityEngine;
using YiGodhome.Bridge;

namespace YiGodhome;

// Isolated 120-second native collision comparison. Never admits itself to gameplay.
internal sealed class NativeJadeProbe : IDisposable
{
    private readonly Player player;
    private readonly JadeData jade;
    private readonly bool equipped;
    private readonly NativeContactFixture attack;
    private readonly NativeContactVerdict verdict;
    private readonly NativeInputBridge input;
    private readonly Action<string> log;
    private readonly PlayerParryState parry;
    private readonly float preciseWindow, duration;
    private int trial, phase;
    private float at, pressAt;
    private bool disposed;
    internal NativeJadeProbe(Player player, MonsterBase owner, Action<string> log)
    {
        if (!SessionConfiguration.Current.Has("jade-probe")) throw new InvalidOperationException("Missing Jade comparison capability.");
        this.player = player; this.log = log;
        jade = player.mainAbilities.MustReflectProjectileJade; equipped = jade.IsEquipped;
        if (!jade.IsAcquired || jade.IsTestForceEquip) throw new InvalidOperationException("Ricochet comparison requires acquired native equipment without forced equip.");
        parry = (PlayerParryState)player.FindState(PlayerStateType.Parry);
        preciseWindow = ((float[])HarmonyLib.AccessTools.Field(typeof(PlayerParryState), "spamDatas").GetValue(parry))[0];
        duration = parry.parryDuration.Value;
        if (parry.IsAlwaysAccurate || duration <= preciseWindow + .08f) throw new InvalidOperationException("Native imprecise timing window unavailable.");
        attack = new NativeContactFixture(owner, log, independent: true);
        attack.SetCrimson(false);
        verdict = new NativeContactVerdict(attack.Dealer, player, log, false);
        input = new NativeInputBridge(player, () => new Controls(0, 0, phase == 1 || phase == 2 ? Buttons.Parry : Buttons.None), () => true, log);
        at = Time.time + 1;
    }
    internal void Update()
    {
        if (disposed) return;
        input.Update(); verdict.Check();
        if (trial >= 4) return;
        if (phase == 0 && Time.time >= at) {
            bool on = trial >= 2;
            if (on) jade.EquipCheck(false); else jade.UnEquipCheck();
            if (jade.IsActivated != on) throw new InvalidOperationException("Native Ricochet equipment did not activate as requested.");
            player.ChangeState(PlayerStateType.Normal, false);
            verdict.Begin(); pressAt = Time.time; phase = 1;
            log("NS_JADE_TRIAL_BEGIN trial=" + trial + " ricochet=" + on + " intendedPrecise=" + (trial % 2 == 0));
        }
        if (phase == 1 && player.CurrentStateType == PlayerStateType.Parry && Time.time - pressAt >= (trial % 2 == 0 ? .01f : (preciseWindow + duration) * .5f)) {
            // Native grounded parry checks attack travel against Yi's facing.
            // A centered fixture inherits the donor's arbitrary direction, which
            // can be a rear hit even while Yi is correctly in Parry state.
            attack.SetLargeParryPush(false,direction:-(int)player.Facing);
            log("NS_JADE_CONTACT_DIRECTION facing="+player.Facing+" incoming="+attack.Dealer.GlobalAttackDir+" nativeFacingRulePreserved=True");
            attack.SetContact(player, true); phase = 2; at = Time.time + 2;
        }
        if (phase == 2 && verdict.ResolutionKind == "parry") {
            bool precise = parry.IsAccurateParried, expectedPrecise = trial % 2 == 0;
            bool expectedReflect = expectedPrecise || trial >= 2;
            if (precise != expectedPrecise || verdict.ReflectionEligible != expectedReflect)
                throw new InvalidOperationException("Native Jade comparison mismatch trial=" + trial + " precise=" + precise + " reflect=" + verdict.ReflectionEligible);
            log("NS_JADE_TRIAL_PASS trial=" + trial + " precise=" + precise + " ricochet=" + jade.IsActivated + " reflect=" + verdict.ReflectionEligible + " nativeCollision=True");
            attack.SetContact(player, false); verdict.Suspend(); phase = 0; at = Time.time + 1; trial++;
            if (trial == 4) { Restore(); log("NS_JADE_COMPARISON_COMPLETE trials=4 equipmentRestored=True saveIO=False"); }
        }
        if (phase == 2 && Time.time > at || phase == 1 && Time.time - pressAt > 2)
            throw new TimeoutException("Native Jade contact did not parry: trial=" + trial + " state=" + player.CurrentStateType);
    }
    private void Restore() { if (equipped) jade.EquipCheck(false); else jade.UnEquipCheck(); }
    public void Dispose() { if (disposed) return; disposed = true; input.Dispose(); verdict.Dispose(); attack.Dispose(); Restore(); }
}
