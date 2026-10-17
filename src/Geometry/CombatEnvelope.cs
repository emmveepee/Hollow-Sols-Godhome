using System;
using System.Linq;

namespace YiGodhome.Geometry;

// Flat standable terrain plus an off-edge margin defines computational reach.
public readonly struct CombatEnvelope
{
    public readonly float Left, Right, Floor;
    private CombatEnvelope(float left, float right, float floor) { Left=left; Right=right; Floor=floor; }
    public static bool TryFromPlatform(Snapshot room, out CombatEnvelope envelope)
    {
        var spans=new System.Collections.Generic.List<(float Left,float Right)>();
        foreach(var shape in room.Shapes) {
            if(shape.HazardId!=0 || shape.Radius!=0) continue;
            int segments=shape.Closed?shape.Points.Length:shape.Points.Length-1;
            for(int i=0;i<segments;i++){
                var a=shape.Points[i];var b=shape.Points[(i+1)%shape.Points.Length];
                if(Math.Abs(a.Y-b.Y)>.001f || Math.Abs(a.Y-room.Floor.Y)>.01f || a.X==b.X) continue;
                spans.Add((Math.Min(a.X,b.X),Math.Max(a.X,b.X)));
            }
        }
        var center=spans.FirstOrDefault(s=>s.Left<=room.Floor.X && s.Right>=room.Floor.X);
        if(center.Left==center.Right){envelope=default;return false;}
        float left=center.Left,right=center.Right;
        for(int pass=0;pass<spans.Count;pass++) foreach(var span in spans)
            if(span.Left<=right+.001f && span.Right>=left-.001f){left=Math.Min(left,span.Left);right=Math.Max(right,span.Right);}
        float margin=Math.Max(3f,room.PlayerWidth*4);
        envelope=new CombatEnvelope(left-margin,right+margin,room.Floor.Y-margin);return true;
    }
    public bool Outside(float[] points)
    {
        if(points.Length<6 || points.Length%2!=0) return false;
        float minX=float.PositiveInfinity, maxX=float.NegativeInfinity, maxY=float.NegativeInfinity;
        for(int i=0;i<points.Length;i+=2){minX=Math.Min(minX,points[i]);maxX=Math.Max(maxX,points[i]);maxY=Math.Max(maxY,points[i+1]);}
        return maxX<Left || minX>Right || maxY<Floor;
    }
}
