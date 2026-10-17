using System;
namespace YiGodhome.Bridge;

// Native physics rounds to eighth units. A door walk must not start against
// the old destination transform before that placement reaches presentation.
public static class DoorWalkAdmission
{
    public static bool AtEntrance(float actual,float entrance)
        => !float.IsNaN(actual) && !float.IsInfinity(actual)
            && !float.IsNaN(entrance) && !float.IsInfinity(entrance)
            && Math.Abs(actual-entrance)<=.25f;
}
