using System;
using System.IO;

namespace YiGodhome.Geometry;

// A single convex strike covering both authored contours. Inputs stay immutable.
public static class StrikeHull
{
    [ThreadStatic] private static Point[]? pointScratch,hullScratch;
    private static readonly System.Collections.Generic.IComparer<Point> Order=System.Collections.Generic.Comparer<Point>.Create((a,b)=>a.X!=b.X?a.X.CompareTo(b.X):a.Y.CompareTo(b.Y));
    private static float Cross(Point a,Point b,Point c)=>(b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
    public static float[] Combine(float[] strike,float[] body)
    {
        if(strike.Length<6 || body.Length<6 || strike.Length%2!=0 || body.Length%2!=0
            || strike.Length+body.Length>264)throw new InvalidDataException("Invalid combined strike contours.");
        var points=pointScratch??=new Point[132];
        int count=0;
        for(int i=0;i<strike.Length;i+=2){var p=new Point(strike[i],strike[i+1]);Codec.CheckPoint(p);points[count++]=p;}
        for(int i=0;i<body.Length;i+=2){var p=new Point(body[i],body[i+1]);Codec.CheckPoint(p);points[count++]=p;}
        Array.Sort(points,0,count,Order);
        var hull=hullScratch??=new Point[264];int n=0;
        for(int i=0;i<count;i++){while(n>=2 && Cross(hull[n-2],hull[n-1],points[i])<=0)n--;hull[n++]=points[i];}
        int lower=n+1;
        for(int i=count-2;i>=0;i--){while(n>=lower && Cross(hull[n-2],hull[n-1],points[i])<=0)n--;hull[n++]=points[i];}
        n--;
        if(n<3 || n>64)throw new InvalidDataException("Combined strike hull is degenerate or exceeds the wire budget.");
        var result=new float[n*2];for(int i=0;i<n;i++){result[i*2]=hull[i].X;result[i*2+1]=hull[i].Y;}
        return result;
    }
}
