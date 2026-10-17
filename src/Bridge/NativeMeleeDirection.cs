using System;
namespace YiGodhome.Bridge;
public static class NativeMeleeDirection
{
    public static float Angle(float x,float y,float fallback)
    {
        if(float.IsNaN(x)||float.IsNaN(y)||float.IsInfinity(x)||float.IsInfinity(y)||x*x+y*y<.000001f)return fallback;
        double angle=Math.Atan2(y,x)*180/Math.PI;
        return (float)(angle<0?angle+360:angle);
    }
}
