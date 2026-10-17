using System;
namespace YiGodhome.Bridge;

// Presentation only. The authored reserve and FINAL HIT gate still own combat.
public static class RadiancePresentation
{
    public static (int Health, int Maximum) Health(int nativeHealth, int nativeMaximum, int finalThreshold, bool finalHit)
    {
        if(finalThreshold<=0 || nativeMaximum<=finalThreshold || nativeHealth<0 || nativeHealth>nativeMaximum)
            throw new ArgumentOutOfRangeException(nameof(finalThreshold));
        int maximum=nativeMaximum-finalThreshold+2;
        return (nativeHealth==0?0:finalHit?1:Math.Min(maximum,Math.Max(2,nativeHealth-finalThreshold+2)),maximum);
    }
    public static bool RainCounterNear(bool rain, bool nativeCounterWindow, float separation, float scale)
        => rain && nativeCounterWindow && float.IsFinite(separation) && float.IsFinite(scale)
            && scale>0 && separation>0 && separation<=.25f*scale;
}
