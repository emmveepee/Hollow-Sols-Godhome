using System;

namespace YiGodhome.Bridge;

public static class ResolvedDamage
{
    public static float Loss(float before, float injuryBefore, float after, float injuryAfter)
    {
        // Native posture-to-injury transfers conserve their sum but store each
        // component as a float. Accumulate in double and reject representational
        // noise bounded by one float epsilon per observed component.
        double loss = (double)before + injuryBefore - after - injuryAfter;
        double error = (Math.Abs((double)before) + Math.Abs((double)injuryBefore)
            + Math.Abs((double)after) + Math.Abs((double)injuryAfter)) * 1.1920928955078125e-7;
        return Math.Abs(loss) <= error ? 0 : (float)loss;
    }
}
