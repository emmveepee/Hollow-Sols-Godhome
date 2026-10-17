using System;
using System.IO;
namespace YiGodhome.Bridge;

// Scripted statue selection only. Live damage always reads the native encounter tier.
public static class DiagnosticDifficulty
{
    public static int Tier { get; } = Load();
    private static int Load()
    {
        string? log = Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG");
        if (string.IsNullOrEmpty(log)) return 0;
        string path = Path.Combine(Path.GetDirectoryName(log)!, "difficulty-probe.request");
        if (!File.Exists(path)) return 0;
        if (!int.TryParse(File.ReadAllText(path).Trim(), out int tier) || tier < 0 || tier > 2)
            throw new InvalidDataException("Invalid diagnostic difficulty.");
        return tier;
    }
}
