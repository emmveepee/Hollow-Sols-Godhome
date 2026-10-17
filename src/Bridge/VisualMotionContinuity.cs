using System;

namespace YiGodhome.Bridge;

// Native position continuity is independent of image age. An old image is
// not a safe fallback once newer native movement has already been displayed.
public sealed class VisualMotionContinuity
{
    private Pose? previous;
    private long previousFrame = -1, waitingImageFrame = -1;
    private void Reset() { previous = null; previousFrame = waitingImageFrame = -1; }
    public Pose? Sample(VisualFrame? image, Pose? control, long controlFrame)
    {
        if (image?.Motion == null || control == null || image.Session != control.GeometrySession
            || image.GeometryFrame != control.GeometryFrame) { Reset(); return null; }
        if (image.Recovering) { Reset(); return VisualPlacement.Sample(image,control,controlFrame); }
        if (previous != null && (previous.GeometrySession != image.Session || previous.GeometryFrame != image.GeometryFrame)) Reset();
        bool useControl = controlFrame >= image.NativeFrame;
        var candidate = useControl ? control : image.Motion;
        long frame = useControl ? controlFrame : image.NativeFrame;
        if (previous != null && frame <= previousFrame) {
            if (waitingImageFrame >= 0 && image.NativeFrame >= waitingImageFrame) waitingImageFrame = -1;
            return waitingImageFrame < 0 ? previous : null;
        }
        if (previous == null && useControl && !VisualPlacement.CanTrack(image,control)) waitingImageFrame = frame;
        if (previous != null) {
            double dx = candidate.X-previous.X, dy = candidate.Y-previous.Y;
            double oldSpeed = Math.Sqrt(previous.VelocityX*previous.VelocityX+previous.VelocityY*previous.VelocityY);
            double speed = Math.Sqrt(candidate.VelocityX*candidate.VelocityX+candidate.VelocityY*candidate.VelocityY);
            double limit = Math.Max(Math.Max(candidate.Width,candidate.Height),Math.Max(oldSpeed,speed)*.25);
            if (dx*dx+dy*dy > limit*limit) waitingImageFrame = frame;
        }
        previous = useControl ? control : new Pose(image.Session,image.GeometryFrame,candidate.X,candidate.Y,
            candidate.VelocityX,candidate.VelocityY,candidate.Width,candidate.Height,candidate.Grounded,control.Vitals,control.Melee);
        previousFrame = frame;
        if (waitingImageFrame >= 0) {
            if (image.NativeFrame < waitingImageFrame) return null;
            waitingImageFrame = -1;
        }
        return previous;
    }
}
