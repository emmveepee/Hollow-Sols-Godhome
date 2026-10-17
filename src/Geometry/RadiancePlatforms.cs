using System;
using System.Linq;
namespace YiGodhome.Geometry;

// The native ascent boxes are already flat. Use the proven Nosk outline/contact
// representation to keep Yi's quantized ceiling probe outside their side faces.
public static class RadiancePlatforms
{
    public static Shape Outline(Point[] box) {
        if(box.Length!=4)throw new ArgumentException("Expected an ascent box.");
        return new Shape {Points=new[]{box[0],box[1],box[2],box[3],box[0]}};
    }
    public static bool Matches(string scene,Shape shape) {
        if(scene!="GG_Radiance" || shape.Closed || shape.OneWay || shape.HazardId!=0 || shape.Radius!=0 || shape.Points.Length!=5)return false;
        var p=shape.Points;
        if(p[0].X!=p[3].X || p[1].X!=p[2].X || p[0].Y!=p[1].Y || p[2].Y!=p[3].Y || p[0].X!=p[4].X || p[0].Y!=p[4].Y)return false;
        float width=Math.Abs(p[1].X-p[0].X),height=Math.Abs(p[2].Y-p[1].Y);
        return Math.Abs(width-2.5836055f)<.001f && (Math.Abs(height-2.5330005f)<.001f || Math.Abs(height-.93868494f)<.001f)
            || Math.Abs(width-4.272402f)<.001f && Math.Abs(height-.9833678f)<.001f;
    }
    public static Point[] MapOutline(Shape shape,CoordinateMap map) {
        var points=shape.Points.Select(map.ToTarget).ToArray();
        float left=points.Min(p=>p.X),right=points.Max(p=>p.X);
        // Mirror Nosk's positive-X correction on the opposite face. Each face
        // moves inward by less than one native eighth-unit; top/bottom stay put.
        float alignedLeft=(float)(Math.Ceiling(left*8)/8),alignedRight=(float)(Math.Floor(right*8)/8);
        if(alignedLeft>=alignedRight)throw new InvalidOperationException("Ascent platform collapsed on the native contact grid.");
        for(int i=0;i<points.Length;i++)points[i]=new Point(points[i].X==left?alignedLeft:alignedRight,points[i].Y);
        return points;
    }
}
