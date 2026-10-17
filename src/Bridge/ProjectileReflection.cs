using System;
namespace YiGodhome.Bridge;

// One world-space speed for every encounter. Incoming/native speed is not an
// input; no difficulty, projectile type or late-homing acceleration can scale it.
public static class ProjectileReflection
{
    public const float Speed=40;
    public static (float X,float Y) Velocity(float x,float y) {
        if(!float.IsFinite(x) || !float.IsFinite(y))throw new ArgumentOutOfRangeException();
        double length=Math.Sqrt((double)x*x+(double)y*y);
        if(length<.000001)return (Speed,0); // Already overlapping the receiver.
        return ((float)(x/length*Speed),(float)(y/length*Speed));
    }
}
