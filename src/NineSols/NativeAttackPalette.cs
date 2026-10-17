using System;
using UnityEngine;
using YiGodhome.Bridge;

namespace YiGodhome;

// Native accessibility colors are small visual data, never copied game assets.
internal sealed class NativeAttackPalette
{
    private float next;
    public void Publish(VisualEndpoint endpoint, Guid session, long geometry)
    {
        if (Time.realtimeSinceStartup < next) return;
        next = Time.realtimeSinceStartup + 0.1f;
        var preset = GameConfig.Instance.CurrentColorSet;
        if (preset == null) return;
        var colors = new Color32[] { preset.TaiChiKickOutlineHintColor, preset.TaiChiKickAfterimageHintColor,
            preset.particleColorsDangerHint_P.Evaluate(0.5f) };
        var frame = endpoint.RentFrame(12);
        for (int i = 0; i < 3; i++)
        { frame.Rgba[i * 4] = colors[i].r; frame.Rgba[i * 4 + 1] = colors[i].g; frame.Rgba[i * 4 + 2] = colors[i].b; frame.Rgba[i * 4 + 3] = colors[i].a; }
        frame.Session = session; frame.GeometryFrame = geometry; frame.NativeFrame = Time.frameCount;
        frame.CaptureTick = System.Diagnostics.Stopwatch.GetTimestamp(); frame.Width = 3; frame.Height = 1;
        frame.WorldWidth = frame.WorldHeight = 1;
        endpoint.PublishPixels(frame);
    }
}
