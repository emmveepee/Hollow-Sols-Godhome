using System;
using HarmonyLib;

namespace YiGodhome;

// User-authorized balloon ceiling: held ground jump followed by one air jump.
// Native velocities/gravity define reach; no authored movement value is changed.
internal static class NativeBalloonReach
{
    private static readonly System.Reflection.FieldInfo Stats = AccessTools.Field(typeof(Player), "statData");
    private static readonly System.Reflection.FieldInfo JumpScale = AccessTools.Field(typeof(Player), "cheatJumpScale");
    internal static float Height(Player player, float mapScale)
    {
        var stats = (PlayerStatScriptableObject)Stats.GetValue(player);
        float gravity = stats.Gravity;
        float scale = (float)JumpScale.GetValue(player);
        if (!(gravity > 0) || !(mapScale > 0) || !(scale > 0)) return 0;
        float ground = player.JumpSpeedStat.Value * scale;
        float air = stats.AirJumpSpeed * scale * 1.08f; // Native enhanced-air-jump upper bound.
        // Variable ground jump sustains launch velocity while held. AirJump
        // enters Rising, so its second ascent is ballistic rather than held.
        float reach = (ground * stats.VariableJumpTime + (ground * ground + air * air) / (2 * gravity)) / mapScale;
        return float.IsNaN(reach) || float.IsInfinity(reach) || reach <= 0 || reach > 1000 ? 0 : reach;
    }
}
