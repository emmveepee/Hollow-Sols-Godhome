using System;
namespace YiGodhome.Bridge;

internal static class VisualCrop
{
    // Effects can exceed the bounded camera footprint. They must never move
    // its center far enough to slice the primary native sprite.
    internal static float KeepOwner(float center, float half, float minimum, float maximum)
    {
        if (half <= 0 || minimum > maximum || maximum - minimum > 2 * half)
            throw new ArgumentException("Native owner does not fit capture footprint.");
        return Math.Max(maximum - half, Math.Min(minimum + half, center));
    }
}
