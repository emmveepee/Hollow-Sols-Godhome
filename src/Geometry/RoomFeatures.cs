namespace YiGodhome.Geometry;

public readonly struct WallRunStrip
{
    public readonly float X, Bottom, Top, Facing;
    public WallRunStrip(float x, float bottom, float top, float facing)
    { X = x; Bottom = bottom; Top = top; Facing = facing; }
    // The native ledge probe samples 20 native units into the wall. Keep enough
    // backing depth at the established 20:1 mapping to avoid a false ledge.
    public const float Thickness = 1.5f;
    // Accepted artwork stays fixed; its white edge is on the outward face.
    public float ContactX => X + Facing * 0.32f;
    public float SolidCenter => ContactX - Facing * Thickness * 0.5f;
    public bool Matches(Shape shape) {
        if(!shape.Closed || shape.Points.Length!=4 || shape.HazardId!=0)return false;
        float left=float.PositiveInfinity,right=float.NegativeInfinity,bottom=left,top=right;
        foreach(var p in shape.Points) {left=System.Math.Min(left,p.X);right=System.Math.Max(right,p.X);bottom=System.Math.Min(bottom,p.Y);top=System.Math.Max(top,p.Y);}
        return System.Math.Abs(left-(SolidCenter-Thickness*.5f))<.001f && System.Math.Abs(right-(SolidCenter+Thickness*.5f))<.001f
            && System.Math.Abs(bottom-Bottom)<.001f && System.Math.Abs(top-Top)<.001f;
    }
    public float NativeContactOffset(Shape shape,CoordinateMap map) {
        if(!Matches(shape))throw new System.ArgumentException("Wall alignment requires its exact authored strip.");
        float face=shape.Points[0].X;
        foreach(var p in shape.Points)face=Facing>0?System.Math.Max(face,p.X):System.Math.Min(face,p.X);
        float native=map.ToTarget(new Point(face,0)).X;
        // Native Actor rounds contact to eighth units. Move the wall face away
        // from the player, never round his edge into the solid ceiling probe.
        float aligned=(float)(Facing>0?System.Math.Floor(native*8)/8:System.Math.Ceiling(native*8)/8);
        return aligned-native;
    }
}

public static class RoomFeatures
{
    public static readonly WallRunStrip[] MarkothWalls = {
        new WallRunStrip(9, 1.2f, 26, 1), new WallRunStrip(42, 1.2f, 26, -1)
    };
    public static WallRunStrip[] ClimbingWalls(string scene) => scene == "GG_Atrium" ? GodhomeWalls
        : scene == "GG_Workshop" ? HallWalls
        : scene == "GG_Ghost_Markoth_V" || scene == "GG_Ghost_Markoth" ? MarkothWalls
        : scene == "GG_Uumuu" || scene == "GG_Uumuu_V" ? UumuuWalls
        : scene == "GG_Flukemarm" ? FlukemarmWalls : System.Array.Empty<WallRunStrip>();
    // Player's red markup, October12. A small outboard contact offset lets the
    // admitted runnable solid precede the original native terrain edge.
    private static WallRunStrip MarkedFace(float face, float bottom, float top, float facing)
        => new WallRunStrip(face + facing * (.08f - .32f), bottom, top, facing);
    public static readonly WallRunStrip[] GodhomeWalls = {
        MarkedFace(130, 64, 90, 1),
        MarkedFace(85, 41, 60, -1), MarkedFace(134, 41, 60, 1),
        MarkedFace(134, 18, 34, 1), MarkedFace(139, 22, 34, -1)
    };
    // Restore the exact previously implemented Hall of Gods strips.
    public static readonly WallRunStrip[] HallWalls = {
        new WallRunStrip(16, 19, 35, 1), new WallRunStrip(21, 15, 35, -1),
        new WallRunStrip(207, 16, 35, 1), new WallRunStrip(211, 19, 35, -1)
    };
    // Exact continuous native wall faces stop at the stepped arena roofs.
    // Leave those upper steps unchanged instead of extending a new wall.
    public static readonly WallRunStrip[] FlukemarmWalls = {
        new WallRunStrip(2.68f, 3.0f, 21, 1), new WallRunStrip(38.32f, 3.0f, 17, -1)
    };
    public static readonly WallRunStrip[] UumuuWalls = {
        // The native foreground artwork extends inward beyond its terrain mesh.
        // Pin to its visible edges (35.15508 /70.89781 in both native arenas),
        // so Yi and the accepted sliced trim stay outside the opaque wall art.
        new WallRunStrip(34.84f, 100, 136, 1), new WallRunStrip(71.22f, 100, 136, -1)
    };
}
