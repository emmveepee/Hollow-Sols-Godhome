using System;
using System.Collections.Generic;
using UnityEngine;
using YiGodhome.Bridge;

namespace YiGodhome;

internal static class NativeReadbackVerification
{
    private static readonly HashSet<(string, int, int)> checkedTextures = new();
    internal static void Verify(Texture2D pixels, string channel, Action<string> log)
    {
        if (!NativeWorkCost.Enabled || checkedTextures.Count >= 8 || checkedTextures.Contains((channel, pixels.width, pixels.height))) return;
        var before = pixels.GetRawTextureData<byte>().ToArray();
        if (!ActorPixels.Visible(before)) return;
        // Diagnostic only, once per nonempty resolution/channel. Production
        // transports CPU pixels directly and never renders this scratch texture.
        pixels.Apply(false, false);
        var after = pixels.GetRawTextureData<byte>();
        if (before.Length != after.Length) throw new InvalidOperationException("Readback upload changed CPU pixel length.");
        for (int i = 0; i < before.Length; i++) if (before[i] != after[i]) throw new InvalidOperationException("Readback upload changed CPU pixels.");
        checkedTextures.Add((channel, pixels.width, pixels.height));
        log("NS_CPU_READBACK_VERIFIED channel=" + channel + " width=" + pixels.width + " height=" + pixels.height
            + " bytes=" + before.Length + " exact=True hash=" + VisualCodec.Checksum(before) + " frame=" + Time.frameCount);
    }
}
