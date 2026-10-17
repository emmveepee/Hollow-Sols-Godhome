using UnityEngine;

namespace YiGodhome;

internal static class HostAudioSource
{
    // Call HK's actual native slider-to-dB curve, then convert dB to linear
    // output gain. Sending slider/10 to a different engine's RTPC is incorrect.
    public static float Gain(float slider)
    {
        if (slider <= 0) return 0;
        return Mathf.Clamp01(Mathf.Pow(10f, Helper.LinearToDecibel(Mathf.Clamp01(slider / 10f)) / 20f));
    }
}
