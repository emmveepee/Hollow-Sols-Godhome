using UnityEngine;
using YiGodhome.Bridge;
namespace YiGodhome;
internal static class NativeBodyStrike
{
    private static readonly float[] bodyScratch=new float[8]; // Main-thread scratch never escapes Combine.
    internal static void Combine(AttackVolume volume,HealthManager owner) {
        var box=owner.GetComponent<BoxCollider2D>()??throw new System.InvalidOperationException("Combined native body strike requires its authored box.");
        var h=box.size*.5f;
        for(int i=0;i<4;i++){var p=box.transform.TransformPoint(new Vector2(i==0||i==3?-h.x:h.x,i<2?-h.y:h.y)+box.offset);bodyScratch[i*2]=p.x;bodyScratch[i*2+1]=p.y;}
        volume.Points=YiGodhome.Geometry.StrikeHull.Combine(volume.Points,bodyScratch);
        volume.CircleX=volume.CircleY=volume.CircleRadius=0;
    }
    // One body-sized translated strike; no native collider mutation.
    internal static void Replace(AttackVolume volume,HealthManager owner) {
        var box=owner.GetComponent<BoxCollider2D>();Bounds bounds;
        if(box!=null) {
            var a=box.transform.TransformPoint(box.offset-box.size*.5f);
            var b=box.transform.TransformPoint(box.offset+box.size*.5f);
            bounds=new Bounds();bounds.SetMinMax(Vector3.Min(a,b),Vector3.Max(a,b));
        } else {
            var shape=owner.GetComponent<Collider2D>();
            bounds=shape!=null&&shape.enabled?shape.bounds:owner.GetComponent<Renderer>()?.bounds??default;
        }
        if(bounds.size.x<=0 || bounds.size.y<=0){volume.Active=false;return;}
        if(volume.Points.Length!=8)volume.Points=new float[8];
        var p=volume.Points;p[0]=bounds.min.x;p[1]=bounds.min.y;p[2]=bounds.max.x;p[3]=bounds.min.y;
        p[4]=bounds.max.x;p[5]=bounds.max.y;p[6]=bounds.min.x;p[7]=bounds.max.y;
        volume.CircleX=volume.CircleY=volume.CircleRadius=0;
    }
}
