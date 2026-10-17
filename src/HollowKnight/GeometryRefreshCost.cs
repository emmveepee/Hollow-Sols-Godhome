using System;
using System.Diagnostics;

namespace YiGodhome;

// Aggregate only the already-scoped refresh; admitted by HostFrameProbe's bounded capabilities.
internal static class GeometryRefreshCost
{
    private static string currentScene = "";
    private static long window;
    private static int samples, changed;
    private static double terrain, dream, encode, exchange;
    internal static void Record(Action<string> log, string scene, long begin, long terrainEnd,
        long dreamEnd, long encodeEnd, int bytes, int colliders, bool published)
    {
        long now = Stopwatch.GetTimestamp();
        if (scene != currentScene) { Reset(); currentScene = scene; }
        if (window == 0) window = now;
        double scale = 1000d / Stopwatch.Frequency;
        terrain += (terrainEnd - begin) * scale; dream += (dreamEnd - terrainEnd) * scale;
        encode += (encodeEnd - dreamEnd) * scale; exchange += (now - encodeEnd) * scale;
        samples++; if (published) changed++;
        if (now - window < Stopwatch.Frequency) return;
        log("HK_GEOMETRY_BREAKDOWN scene=" + scene + " samples=" + samples + " terrainMs=" + terrain
            + " dreamMs=" + dream + " encodeMs=" + encode + " exchangeMs=" + exchange
            + " bytes=" + bytes + " colliders=" + colliders + " changed=" + changed + " nativeFrame=" + UnityEngine.Time.frameCount);
        Reset();
    }
    private static void Reset() { window = 0; samples = changed = 0; terrain = dream = encode = exchange = 0; }
}
