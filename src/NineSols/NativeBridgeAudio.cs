using System;
using HollowSols.Foundation;

namespace YiGodhome;

// A runtime Wwise lease, independent of native preference flags and save files.
// HK owns music/UI; the existing gameplay mirror owns Yi's effects while Active.
internal sealed class NativeBridgeAudio
{
    private readonly Action<string> log;
    private bool leased, muted;
    private float master, effects, music;
    internal NativeBridgeAudio(Action<string> log) { this.log = log; }

    internal void Update(BridgeState state)
    {
        if (state == BridgeState.Vanilla) { Restore(); return; }
        if (!leased)
        {
            if (state != BridgeState.Connected && state != BridgeState.Loading && state != BridgeState.Active) return;
            master = Read("Global_Game_Volume"); effects = Read("SoundEffect_Volume"); music = Read("BGM_Volume");
            leased = true;
            log("NS_BRIDGE_AUDIO_CAPTURE master=" + master + " effects=" + effects + " music=" + music + " preferencesUnchanged=True");
        }
        if (state == BridgeState.Active) { muted = false; return; }
        // Apply synchronously on connection, then after native Update callbacks
        // each frame: menu/scene fades must never reopen the waiting screen's mix.
        Set("Global_Game_Volume", 0); Set("SoundEffect_Volume", 0); Set("BGM_Volume", 0);
        if (!muted) log("NS_BRIDGE_AUDIO_MUTED state=" + state + " menuSfx=True music=True runtimeOnly=True");
        muted = true;
    }

    internal void Restore()
    {
        if (!leased) return;
        // Installed native code never changes Wwise output gain; its default is
        // unity. Undo the gameplay mirror's linear gain before restoring RTPCs.
        Require(AkSoundEngine.SetOutputVolume(0UL, 1f));
        Set("Global_Game_Volume", master); Set("SoundEffect_Volume", effects); Set("BGM_Volume", music);
        leased = muted = false;
        log("NS_BRIDGE_AUDIO_RESTORED master=" + master + " effects=" + effects + " music=" + music + " preferencesUnchanged=True");
    }

    internal void AssertMuted()
    {
        if (!leased || Read("Global_Game_Volume") != 0 || Read("SoundEffect_Volume") != 0 || Read("BGM_Volume") != 0)
            throw new InvalidOperationException("Connected Nine Sols menu audio was not muted.");
        log("NS_BRIDGE_AUDIO_FIXTURE muted=True nativeRtpcReadback=True");
    }
    internal void AssertRestored()
    {
        if (leased || Math.Abs(Read("Global_Game_Volume") - master) > .0001f
            || Math.Abs(Read("SoundEffect_Volume") - effects) > .0001f || Math.Abs(Read("BGM_Volume") - music) > .0001f)
            throw new InvalidOperationException("Vanilla Nine Sols audio was not restored.");
        log("NS_BRIDGE_AUDIO_FIXTURE restored=True nativeRtpcReadback=True");
    }
    internal static float Read(string name)
    {
        float value; int scope = 1; // Wwise RTPCValue_Global, independent of emitters.
        Require(AkSoundEngine.GetRTPCValue(name, ulong.MaxValue, 0, out value, ref scope));
        if (scope != 1 || float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidOperationException("Invalid native audio parameter " + name);
        return value;
    }
    private static void Set(string name, float value) => Require(AkSoundEngine.SetRTPCValue(name, value));
    private static void Require(AKRESULT result)
    { if (result != AKRESULT.AK_Success) throw new InvalidOperationException("Native bridge audio operation failed: " + result); }
}
