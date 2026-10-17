using System;
using UnityEngine;
using YiGodhome.Bridge;

namespace YiGodhome;

// Host supplies actual linear mixer gains, not normalized sliders. No preference/save writes.
// Host owns music. Yi effects follow fresh host master/effects and pause state.
internal sealed class NativeAudioMirror
{
    private readonly Action<string> log;
    private float master = -1, effects = -1, nextApply;
    private bool paused, fresh;
    private bool menuAudio;
    internal bool FixtureReady => fresh;
    public NativeAudioMirror(Action<string> log) { this.log = log; }
    internal void FixtureAssert()
    {
        if (!fresh || Math.Abs(NativeBridgeAudio.Read("Global_Game_Volume") - (paused && !menuAudio ? 0f : 1f)) > .0001f
            || NativeBridgeAudio.Read("SoundEffect_Volume") != 1f || NativeBridgeAudio.Read("BGM_Volume") != 0f)
            throw new InvalidOperationException("Active Yi audio does not match the fresh host mix.");
        log("NS_HOST_AUDIO_FIXTURE nativeRtpcReadback=True hostMaster=" + master + " hostEffects=" + effects + " linearOutput=" + master * effects + " paused=" + paused + " controlsFeedback="+menuAudio+" music=Host");
    }
    public void Update(HostAudioState? state)
    {
        // The menu lease owns waiting/loading/ending silence and vanilla
        // restoration. A deferred adapter teardown must not overwrite either.
        if (HollowSolsMenuPlugin.Instance?.Lifecycle is { } lifecycle && lifecycle.State != HollowSols.Foundation.BridgeState.Active) return;
        bool isFresh = state != null, isPaused = state?.Paused == true;
        bool uiAudio = isFresh && isPaused && NativeMenu.ControlsOpen;
        float m = !isFresh || isPaused && !uiAudio ? 0 : state!.Master, e = state?.Effects ?? 0;
        float outputGain = m * e;
        bool changed = m != master || e != effects || isPaused != paused || isFresh != fresh || uiAudio!=menuAudio;
        if (!changed && Time.realtimeSinceStartup < nextApply) return;
        // Set the linear output first, then neutralize NS slider curves.
        if (isFresh && AkSoundEngine.SetOutputVolume(0UL, outputGain) != AKRESULT.AK_Success
            || AkSoundEngine.SetRTPCValue("Global_Game_Volume", isFresh && (!isPaused || uiAudio) ? 1f : 0f) != AKRESULT.AK_Success
            || AkSoundEngine.SetRTPCValue("SoundEffect_Volume", 1f) != AKRESULT.AK_Success
            || AkSoundEngine.SetRTPCValue("BGM_Volume", 0f) != AKRESULT.AK_Success)
            throw new InvalidOperationException("Native host audio RTPC synchronization failed.");
        if (changed) log("NS_HOST_AUDIO master=" + m + " effects=" + e + " outputGain=" + outputGain + " paused=" + isPaused + " controlsFeedback="+uiAudio+" fresh=" + isFresh + " runtimeOnly=True music=Host");
        master = m; effects = e; paused = isPaused; fresh = isFresh; menuAudio=uiAudio;
        nextApply = Time.realtimeSinceStartup + 0.25f;
    }
}
