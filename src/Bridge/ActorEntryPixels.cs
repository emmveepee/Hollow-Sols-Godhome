using System;
namespace YiGodhome.Bridge;

// First-image readiness is room scoped. After real artwork arrives, native
// damage blinking remains presentation and must never reacquire input gates.
public sealed class ActorEntryPixels
{
    private Guid session;
    private long geometry=-1;
    private int room=-1;
    private bool ready;
    public bool Observe(VisualFrame frame,int sceneHandle)
    {
        if(frame.Session!=session || frame.GeometryFrame!=geometry || sceneHandle!=room) {
            session=frame.Session;geometry=frame.GeometryFrame;room=sceneHandle;ready=false;
        }
        if(!ready)ready=ActorPixels.Visible(frame.Rgba);
        return ready;
    }
}
