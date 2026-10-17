// SPDX-License-Identifier: GPL-3.0-or-later
using System;

namespace YiGodhome.Bridge;

// A native widget gets its own texels, regardless of distance to another
// widget/effect. Pixel rectangles and world rectangles belong to one image.
public readonly struct VisualRegion
{
    public readonly int X, Y, Width, Height;
    public readonly float WorldX, WorldY, WorldWidth, WorldHeight;
    public VisualRegion(int x,int y,int width,int height,float worldX,float worldY,float worldWidth,float worldHeight)
    { X=x;Y=y;Width=width;Height=height;WorldX=worldX;WorldY=worldY;WorldWidth=worldWidth;WorldHeight=worldHeight; }
}

public static class MinionHudAtlas
{
    public const int Width=1024, Tile=192, Content=168, Columns=5, EffectsSide=1024;
    public const int MaximumWidgets=SummonedTargets.Maximum;
    public const int MaximumRegions=MaximumWidgets+1;
    public const int RegionBytes=32;
    public static int BarHeight(int count)
    {
        if(count<0 || count>MaximumWidgets)throw new ArgumentOutOfRangeException(nameof(count));
        int rows=(count+Columns-1)/Columns;
        return rows==0?0:rows*Tile<=512?512:rows*Tile<=1024?1024:1536;
    }
    public static VisualRegion Widget(int index,int count,bool effects,float centerX,float centerY,float worldWidth,float worldHeight)
    {
        if(index<0 || index>=count)throw new ArgumentOutOfRangeException(nameof(index));
        BarHeight(count);
        float side=Math.Max(worldWidth,worldHeight)*Tile/Content;
        return new VisualRegion(index%Columns*Tile,(effects?EffectsSide:0)+index/Columns*Tile,Tile,Tile,
            centerX-side/2,centerY-side/2,side,side);
    }
}
