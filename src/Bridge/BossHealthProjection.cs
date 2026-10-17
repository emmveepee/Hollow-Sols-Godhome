using System;
namespace YiGodhome.Bridge;

// HK HP is the total remaining health, including unrealized native injury.
public readonly struct BossHealthProjection
{
    public const double NativeToHost = 21.0 / 40.0;
    public readonly float Solid, Total, Injury;
    public BossHealthProjection(int health, int maximum, float nativeInjury)
    {
        if (maximum <= 0 || health < 0 || health > maximum || float.IsNaN(nativeInjury) || float.IsInfinity(nativeInjury) || nativeInjury < 0)
            throw new ArgumentOutOfRangeException(nameof(health));
        Injury = (float)Math.Min(health, nativeInjury * NativeToHost);
        Total = (float)health / maximum;
        Solid = (health - Injury) / maximum;
    }
}
