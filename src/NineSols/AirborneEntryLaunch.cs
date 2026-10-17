// SPDX-License-Identifier: GPL-3.0-or-later
using System;
namespace YiGodhome;

// One launch impulse, then native Normal-state gravity, collision and animation.
internal static class AirborneEntryLaunch
{
    internal static (float X,float Y) CalculateComponents(float x,float y,float targetX,float targetY,float gravity,float nativeJumpSpeed) {
        if(!(gravity>0) || !(nativeJumpSpeed>0))throw new InvalidOperationException("Native entry gravity/jump stats unavailable.");
        // Keep a normal native jump's clearance above the landing ledge even
        // when HK's authored entry begins well below that ledge.
        double rise=Math.Max(0,targetY-y);
        double speed=Math.Sqrt(nativeJumpSpeed*nativeJumpSpeed+2*gravity*rise);
        double time=(speed+Math.Sqrt(speed*speed-2*gravity*(targetY-y)))/gravity;
        double vx=(targetX-x)/time;
        if(double.IsNaN(vx) || double.IsInfinity(vx) || double.IsInfinity(speed))throw new InvalidOperationException("Invalid native entry impulse.");
        return ((float)vx,(float)speed);
    }
}
