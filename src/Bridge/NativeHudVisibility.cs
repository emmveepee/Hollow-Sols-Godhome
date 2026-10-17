using System;
namespace YiGodhome.Bridge;

public static class NativeHudVisibility
{
    // Native Slide Out owns timing. Its scale tween remains visible during
    // Go Out/Come In; Out is positioned offscreen and must stay hidden.
    public static bool Shown(bool active,string state,float x,float y)
        => active && !string.IsNullOrEmpty(state) && state!="Init" && state!="Out" && state!="Go Out Instant"
            && !float.IsNaN(x) && !float.IsInfinity(x) && !float.IsNaN(y) && !float.IsInfinity(y)
            && Math.Abs(x)>.0001f && Math.Abs(y)>.0001f;
}
