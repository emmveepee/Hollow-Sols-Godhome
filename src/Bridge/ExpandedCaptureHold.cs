using System;
namespace YiGodhome.Bridge;

// Keep expanded GPU/CPU sizes stable across a short native arrow burst.
// The crop grows with the retained pixel dimensions, preserving the original
// requested density and containing every selected renderer. No effect expires.
internal sealed class ExpandedCaptureHold
{
    private long geometry=-1;
    private int width,height;
    private double until;
    internal (int Width,int Height,float HalfWidth,float HalfHeight) Choose(long room,double now,bool arrow,int requestedWidth,int requestedHeight,float halfWidth,float halfHeight,int minimumExpandedSide=0) {
        if(room!=geometry || now>until){geometry=room;width=height=0;}
        if(arrow && (requestedWidth>1024 || requestedHeight>1024)) {
            width=Math.Max(width,Math.Max(requestedWidth,minimumExpandedSide));height=Math.Max(height,Math.Max(requestedHeight,minimumExpandedSide));until=now+3;
        }
        if(width==0)return(requestedWidth,requestedHeight,halfWidth,halfHeight);
        width=Math.Max(width,requestedWidth);height=Math.Max(height,requestedHeight);
        float density=Math.Min(requestedWidth/(halfWidth*2),requestedHeight/(halfHeight*2));
        return(width,height,width/(density*2),height/(density*2));
    }
}
