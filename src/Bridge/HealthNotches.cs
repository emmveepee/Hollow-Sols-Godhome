using System;
using System.IO;
namespace YiGodhome.Bridge;

// Thresholds are remaining native HK HP, descending. Equality keeps a dam
// closed; ordinary applied damage must cross it before injury can fill below.
public static class HealthNotches
{
    public const int Maximum = 16;
    public static void Validate(int[] thresholds, int maximum)
    {
        if (thresholds == null || thresholds.Length > Maximum) throw new InvalidDataException("Invalid health notch capacity.");
        int prior = maximum;
        foreach (int value in thresholds) {
            if (value <= 0 || value >= prior) throw new InvalidDataException("Invalid health notch order/range.");
            prior = value;
        }
    }
    // Display-only reserve removal must move markers with the visible bar.
    // The thresholds themselves remain native HP for internal-injury dams.
    public static float BarFraction(int threshold,int maximum,int displayedMaximum)
        => Math.Max(0,Math.Min(1,(float)(threshold-(maximum-displayedMaximum))/displayedMaximum));
    public static float InjuryLimit(int health, int[] thresholds)
    {
        int floor = 0;
        foreach (int threshold in thresholds) if (health >= threshold) { floor = threshold; break; }
        return (float)(Math.Max(0, health - floor) / BossHealthProjection.NativeToHost);
    }
    public static void Write(BinaryWriter writer, int[] thresholds) { writer.Write((byte)thresholds.Length); foreach (int t in thresholds) writer.Write(t); }
    public static int[] Read(BinaryReader reader)
    {
        int count = reader.ReadByte(); if (count > Maximum) throw new InvalidDataException("Invalid health notch capacity.");
        var result = new int[count]; for (int i = 0; i < count; i++) result[i] = reader.ReadInt32(); return result;
    }
}
