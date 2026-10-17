using System;
using System.Collections.Generic;
using System.Linq;

namespace YiGodhome.Geometry;

// Player-requested Attuned collision repair. Only these two authored terrain
// signatures are replaced; native boss/world physics and other rooms are intact.
public static class NoskPlatform
{
    // Installed native Actor contacts are quantized to eighth units. Positive-X
    // rounding must not place the ceiling probe inside a fractional left wall.
    public static float AlignNativeLeftFace(float x)=>(float)(Math.Ceiling(x*8)/8);
    private static readonly Point[] Left={new(64,1),new(64,0),new(96,0),new(96,6),new(88,6),new(88,3),new(71,3),new(71,26),new(64,26),new(64,1)};
    private static readonly Point[] Right={new(96,1),new(96,0),new(128,0),new(128,26),new(118,26),new(118,4),new(104,4),new(104,6),new(96,6),new(96,1)};
    private static bool Matches(Shape shape,Point[] expected)=>!shape.Closed && shape.HazardId==0 && shape.Radius==0 && shape.Points.Length==expected.Length
        && shape.Points.Where((p,i)=>Math.Abs(p.X-expected[i].X)>.001f || Math.Abs(p.Y-expected[i].Y)>.001f).Count()==0;
    private static Shape Copy(Shape shape,Point[] points)=>new Shape {Points=points,Radius=shape.Radius,Closed=shape.Closed,
        AdjacentStart=shape.AdjacentStart,AdjacentEnd=shape.AdjacentEnd,Start=shape.Start,End=shape.End};
    public static Shape[] Normalize(string scene,Shape[] shapes)
    {
        if(scene!="GG_Nosk")return shapes;
        var left=shapes.Where(s=>Matches(s,Left)).ToArray();var right=shapes.Where(s=>Matches(s,Right)).ToArray();
        if(left.Length==0 && right.Length==0)return shapes;
        if(left.Length!=1 || right.Length!=1)throw new InvalidOperationException("Nosk platform collision signatures changed; refusing a partial repair.");
        var result=new List<Shape>(shapes.Length+1);
        foreach(var shape in shapes) {
            if(ReferenceEquals(shape,left[0]))result.Add(Copy(shape,new[]{Left[0],Left[1],Left[2],new Point(96,3),Left[6],Left[7],Left[8],Left[9]}));
            else if(ReferenceEquals(shape,right[0]))result.Add(Copy(shape,new[]{Right[0],Right[1],Right[2],Right[3],Right[4],Right[5],Right[6],new Point(96,4),Right[9]}));
            else result.Add(shape);
        }
        // Native Yi's ceiling overlap query can see the interior of a filled
        // polygon when horizontal pixel rounding presses into its left face.
        // Keep the arena's native edge topology and an exact rectangular outline.
        result.Add(new Shape {Points=new[]{new Point(88,3),new Point(104,3),new Point(104,6),new Point(88,6),new Point(88,3)}});
        return result.ToArray();
    }
}
