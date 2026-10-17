using System;
using HarmonyLib;

namespace YiGodhome;

internal sealed class NativeAmbienceIsolation : IDisposable
{
    private static NativeAmbienceIsolation? current;
    private readonly Harmony harmony = new Harmony("local.yigodhome.native-ambience-isolation");
    private readonly Action<string> log;
    private bool stoppedExisting;
    public NativeAmbienceIsolation(Action<string> log)
    {
        this.log = log;
        if (current != null) throw new InvalidOperationException("Duplicate ambience isolation.");
        current = this;
        harmony.Patch(AccessTools.PropertySetter(typeof(AmbienceTrack), "CurrentAmbStr"),
            prefix: new HarmonyMethod(typeof(NativeAmbienceIsolation), nameof(BeforeSet)));
    }
    private static void BeforeSet(ref string __0)
    {
        if (current == null || string.IsNullOrEmpty(__0)) return;
        current.log("NS_AMBIENCE_SUPPRESSED event=" + __0 + " effectsUnchanged=True");
        __0 = ""; // Native setter stops the old track and does not post an empty event.
    }
    public void Update()
    {
        if (stoppedExisting || ApplicationCore.Instance?.soundManager?.ambienceManager == null) return;
        var manager = ApplicationCore.Instance.soundManager.ambienceManager;
        foreach (AmbienceManager.AmbienceType type in Enum.GetValues(typeof(AmbienceManager.AmbienceType))) manager.StopAmbience(type, 0.05f);
        stoppedExisting = true;
        log("NS_AMBIENCE_ISOLATED nativeTracksStopped=True effectsUnchanged=True");
    }
    public void Dispose() { harmony.UnpatchSelf(); if (current == this) current = null; }
}
