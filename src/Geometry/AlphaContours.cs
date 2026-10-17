using System;
using System.Collections.Generic;
using System.IO;

namespace YiGodhome.Geometry;

// Original pixel-boundary extraction. Includes holes and disconnected islands.
public static class AlphaContours
{
    public static Point[][] Extract(byte[] rgba, int side, int maximum = 4096) => ExtractCore(rgba, side, maximum, false);

    // Decorative ribbons must fit their mesh budget even for noisy native alpha.
    // Keep the largest closed islands/holes and distribute a fixed point budget;
    // collision geometry continues to use strict validation elsewhere.
    public static Point[][] ExtractBounded(byte[] rgba, int side, int maximum = 4096) => ExtractCore(rgba, side, maximum, true);

    private static Point[][] ExtractCore(byte[] rgba, int side, int maximum, bool bounded)
    {
        if (side < 2 || side > 1024 || rgba.Length != side * side * 4 || maximum < 4)
            throw new ArgumentException("Invalid bounded silhouette.");
        var edges = new Dictionary<int, List<int>>();
        int stride = side + 1;
        bool Filled(int x, int y) => x >= 0 && y >= 0 && x < side && y < side && rgba[(y * side + x) * 4 + 3] >= 32;
        void Edge(int x, int y, int a, int b) {
            int start = y * stride + x, end = b * stride + a;
            if (!edges.TryGetValue(start, out var list)) edges.Add(start, list = new List<int>(1));
            list.Add(end);
        }
        for (int y = 0; y < side; y++) for (int x = 0; x < side; x++) if (Filled(x,y)) {
            if (!Filled(x,y-1)) Edge(x,y,x+1,y);
            if (!Filled(x+1,y)) Edge(x+1,y,x+1,y+1);
            if (!Filled(x,y+1)) Edge(x+1,y+1,x,y+1);
            if (!Filled(x-1,y)) Edge(x,y+1,x,y);
        }
        var contours = new List<Point[]>(); int total = 0;
        while (edges.Count > 0) {
            int start = 0; foreach (int key in edges.Keys) { start = key; break; }
            int current = start; var loop = new List<Point>();
            do {
                loop.Add(new Point(current % stride, current / stride));
                if (!edges.TryGetValue(current, out var next)) throw new InvalidDataException("Unclosed alpha boundary.");
                int end = next[next.Count-1]; next.RemoveAt(next.Count-1); if (next.Count == 0) edges.Remove(current);
                current = end;
                if (loop.Count > side * side * 4) throw new InvalidDataException("Alpha boundary exceeded traversal budget.");
            } while (current != start);
            // Eliminate tiny raster specks; smooth subpixel stairs without moving
            // the boundary far enough to create a detached edge.
            if (loop.Count < 12) continue;
            int step = Math.Max(1, loop.Count / 1200); var reduced = new List<Point>();
            for (int i = 0; i < loop.Count; i += step) {
                var a = loop[(i+loop.Count-1)%loop.Count]; var b = loop[i]; var c = loop[(i+1)%loop.Count];
                reduced.Add(new Point((a.X + 2*b.X + c.X)*.25f, (a.Y + 2*b.Y + c.Y)*.25f));
            }
            total += reduced.Count;
            if (!bounded && (total > maximum || contours.Count >= 32)) throw new InvalidDataException("Silhouette contour budget exceeded.");
            contours.Add(reduced.ToArray());
        }
        if (!bounded || total <= maximum && contours.Count <= 32) return contours.ToArray();
        float Area(Point[] loop) {
            float sum=0; for(int i=0;i<loop.Length;i++) { var a=loop[i]; var b=loop[(i+1)%loop.Length]; sum+=a.X*b.Y-b.X*a.Y; }
            return Math.Abs(sum);
        }
        contours.Sort((a,b)=>Area(b).CompareTo(Area(a)));
        int count=Math.Min(contours.Count, Math.Min(32,maximum/4));
        if(count==0) return Array.Empty<Point[]>();
        int selected=0; for(int i=0;i<count;i++) selected+=contours[i].Length;
        int remaining=maximum;
        var result=new Point[count][];
        for(int i=0;i<count;i++) {
            var loop=contours[i];
            int quota=Math.Min(loop.Length, Math.Max(4,(int)((long)remaining*loop.Length/selected)));
            quota=Math.Min(quota,remaining-4*(count-i-1));
            var reduced=new Point[quota];
            for(int j=0;j<quota;j++) reduced[j]=loop[(int)((long)j*loop.Length/quota)];
            result[i]=reduced;remaining-=quota;selected-=loop.Length;
        }
        return result;
    }
}
