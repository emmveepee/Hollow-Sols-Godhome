using System;

namespace YiGodhome.Bridge;

// A published contour can outlive its capture frame. Replace it on change;
// never refill the previous array while a transport reader may still own it.
internal sealed class ImmutableContourSnapshot<TKey> where TKey : struct, IEquatable<TKey>
{
    private TKey key;
    private float[]? points;
    internal bool TryGet(TKey current, out float[] result)
    {
        result = points!;
        return points != null && key.Equals(current);
    }
    internal void Publish(TKey current, float[] replacement)
    {
        key = current;
        points = replacement;
    }
}
