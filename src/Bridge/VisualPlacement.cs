using System;
namespace YiGodhome.Bridge;

public static class VisualPlacement
{
    internal static bool CanTrack(VisualFrame frame, Pose control) {
        if (frame.Recovering || frame.Motion == null || frame.Session != control.GeometrySession || frame.GeometryFrame != control.GeometryFrame) return false;
        float dx = control.X-frame.AnchorX, dy = control.Y-frame.AnchorY;
        double speed = Math.Sqrt(control.VelocityX*control.VelocityX+control.VelocityY*control.VelocityY);
        double limit = Math.Max(Math.Max(control.Width,control.Height),speed*.125);
        return dx*dx+dy*dy <= limit*limit;
    }
    // Native position delivery is independent of large image transfer. Prefer
    // the newest actual native sample, never extrapolate or move backwards to
    // a control sample older than the image. Recovery retains its own pixels.
    public static Pose? Sample(VisualFrame? frame, Pose? control, long controlFrame = long.MaxValue)
    {
        if (frame?.Motion == null || control == null || frame.Session != control.GeometrySession
            || frame.GeometryFrame != control.GeometryFrame) return null;
        if (controlFrame >= frame.NativeFrame && CanTrack(frame,control)) return control;
        var m = frame.Motion;
        return new Pose(frame.Session, frame.GeometryFrame, m.X, m.Y, m.VelocityX, m.VelocityY,
            m.Width, m.Height, m.Grounded, control.Vitals, control.Melee);
    }
    public static void Resolve(VisualFrame frame, Pose pose, out float x, out float y)
    {
        x = frame.X; y = frame.Y;
        // The normal actor caller validates consecutive native motion before
        // resolving. Never switch back to an older image because its anchor
        // trails continuous movement; that produces a visible backwards jump.
        if (frame.Recovering || frame.Motion == null || frame.Session != pose.GeometrySession || frame.GeometryFrame != pose.GeometryFrame) return;
        x += pose.X-frame.AnchorX; y += pose.Y-frame.AnchorY;
    }
}
