using System;

namespace YiGodhome.Bridge;

// Debug contours only. Curved native colliders are inscribed polylines; these
// points never become gameplay colliders or affect native parry decisions.
public static class ParryContactContours
{
    public static bool Active(bool parrying, bool dealerEnabled, bool colliderEnabled, bool hierarchyActive, bool conditionsValid)
        => parrying && dealerEnabled && colliderEnabled && hierarchyActive && conditionsValid;
    public static float[] Box(float x, float y, float width, float height) => new[] {
        x-width*.5f,y-height*.5f, x+width*.5f,y-height*.5f,
        x+width*.5f,y+height*.5f, x-width*.5f,y+height*.5f };
    public static float[] Circle(float x, float y, float radius)
    {
        var result = new float[64];
        for (int i=0;i<32;i++) {
            double angle=i*Math.PI/16;
            result[i*2]=x+(float)Math.Cos(angle)*radius; result[i*2+1]=y+(float)Math.Sin(angle)*radius;
        }
        return result;
    }
    public static float[] Capsule(float x, float y, float width, float height, bool vertical)
    {
        float radius=(vertical?width:height)*.5f;
        float segment=Math.Max(0,(vertical?height:width)*.5f-radius);
        var result=new float[68];
        for(int half=0;half<2;half++)for(int i=0;i<=16;i++) {
            double angle=(vertical?0:-Math.PI*.5)+half*Math.PI+i*Math.PI/16;
            float px=(float)Math.Cos(angle)*radius,py=(float)Math.Sin(angle)*radius;
            if(vertical)py+=(half==0?segment:-segment); else px+=(half==0?segment:-segment);
            int index=(half*17+i)*2;result[index]=x+px;result[index+1]=y+py;
        }
        return result;
    }
}
