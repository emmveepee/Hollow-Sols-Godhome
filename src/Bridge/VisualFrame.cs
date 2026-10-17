using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace YiGodhome.Bridge;

public sealed class VisualFrame
{
    public const int RageMaskSide = 1024;
    public Guid Session;
    public long Sequence, NativeFrame, CaptureTick, SentTick, GeometryFrame;
    public long ContentSequence;
    public long ContentFrame;
    public long Frequency = Stopwatch.Frequency;
    public double PreviousRoundTripMs;
    public float X, Y, WorldWidth, WorldHeight;
    public int Width, Height;
    public byte[] Rgba = Array.Empty<byte>();
    public uint Hash;
    internal bool ComputeHashBeforeSend;
    internal bool StraightAlphaBeforeSend;
    internal VisualFrame? WhiteMatte;
    internal int MatteBackground = 255;
    internal double ResolveMilliseconds;
    public bool Recovering;
    public bool ControlsMenu;
    public bool ControlsBinding;
    public bool ControlsMouseCapture;
    public bool RootMenu;
    public bool InventoryMenu;
    public float ScreenFade;
    public float AnchorX, AnchorY;
    public Pose? Motion;
    public VisualRegion[] Regions = Array.Empty<VisualRegion>();
    internal PixelBuffers? BufferOwner;
    internal void ReleasePixels()
    {
        System.Threading.Interlocked.Exchange(ref WhiteMatte,null)?.ReleasePixels();
        var owner = System.Threading.Interlocked.Exchange(ref BufferOwner, null);
        if (owner != null) owner.Return(Rgba);
    }
}

// Separate bounded framing prevents large visual payloads from delaying control packets.
public static class VisualCodec
{
    public const ushort Version = 15;
    public const int HeaderSize = 158;
    public const int MaxSide = 2560;
    public const int MaxHudSide = 2048;
    public const int MaxHudPixels = 2048 * 1024;
    private const uint Magic = 0x56484759;
    public static uint Checksum(byte[] bytes)
    {
        uint hash = 2166136261;
        foreach (byte b in bytes) hash = unchecked((hash ^ b) * 16777619);
        return hash;
    }
    public static void Write(Stream stream, VisualFrame frame, bool hud = false)
    {
        Validate(frame, frame.Rgba.Length, hud);
        if(frame.WhiteMatte!=null) {
            long resolveStart=Stopwatch.GetTimestamp();
            ActorPixels.FromDualMatte(frame.Rgba,frame.WhiteMatte.Rgba,true,frame.MatteBackground);
            frame.ResolveMilliseconds=(Stopwatch.GetTimestamp()-resolveStart)*1000d/Stopwatch.Frequency;
            frame.WhiteMatte.ReleasePixels(); frame.WhiteMatte=null;
            frame.StraightAlphaBeforeSend=false;
        } else if(frame.StraightAlphaBeforeSend) {
            ActorPixels.StraightAlpha(frame.Rgba); frame.StraightAlphaBeforeSend=false;
        }
        uint calculatedHash = Checksum(frame.Rgba);
        // Owned capture buffers are hashed once here on the channel worker.
        // Prehashed callers still require validation; receivers always validate.
        if (frame.ComputeHashBeforeSend) { frame.Hash = calculatedHash; frame.ComputeHashBeforeSend = false; }
        else if (calculatedHash != frame.Hash) throw new InvalidDataException("Visual checksum mismatch.");
        using var w = new WireWriter(stream);
        w.Write(Magic); w.Write(Version); w.Write(frame.Session.ToByteArray()); w.Write(frame.Sequence); w.Write(frame.NativeFrame);
        w.Write(frame.CaptureTick); w.Write(frame.SentTick); w.Write(frame.Frequency); w.Write(frame.PreviousRoundTripMs); w.Write(frame.GeometryFrame);
        w.Write(frame.X); w.Write(frame.Y); w.Write(frame.WorldWidth); w.Write(frame.WorldHeight);
        w.Write(frame.Width); w.Write(frame.Height); w.Write(frame.Rgba.Length); w.Write(frame.Hash);
        w.Write((byte)((frame.Recovering ? 1 : 0) | (frame.ControlsMenu ? 2 : 0) | (frame.ControlsBinding ? 4 : 0) | (frame.RootMenu ? 8 : 0) | (frame.InventoryMenu ? 16 : 0) | (frame.ControlsMouseCapture ? 32 : 0))); w.Write(frame.ScreenFade); w.Write(frame.AnchorX); w.Write(frame.AnchorY);
        var motion = frame.Motion;
        w.Write((byte)(motion == null ? 0 : motion.Grounded ? 3 : 1));
        w.Write(motion?.VelocityX ?? 0); w.Write(motion?.VelocityY ?? 0); w.Write(motion?.Width ?? 0); w.Write(motion?.Height ?? 0);
        w.Write(frame.ContentSequence); w.Write(frame.ContentFrame); w.Write((ushort)frame.Regions.Length);
        foreach(var region in frame.Regions) {
            w.Write(region.X);w.Write(region.Y);w.Write(region.Width);w.Write(region.Height);
            w.Write(region.WorldX);w.Write(region.WorldY);w.Write(region.WorldWidth);w.Write(region.WorldHeight);
        }
        w.Write(frame.Rgba); w.Flush();
    }
    public static VisualFrame Read(Stream stream, bool hud = false, PixelBuffers? buffers = null)
    {
        using var r = new BinaryReader(stream, Encoding.UTF8, true);
        if (r.ReadUInt32() != Magic || r.ReadUInt16() != Version) throw new InvalidDataException("Visual magic/version mismatch.");
        byte[] id = r.ReadBytes(16); if (id.Length != 16) throw new EndOfStreamException();
        var f = new VisualFrame { Session = new Guid(id), Sequence = r.ReadInt64(), NativeFrame = r.ReadInt64(), CaptureTick = r.ReadInt64(),
            SentTick = r.ReadInt64(), Frequency = r.ReadInt64(), PreviousRoundTripMs = r.ReadDouble(), GeometryFrame = r.ReadInt64(),
            X = r.ReadSingle(), Y = r.ReadSingle(), WorldWidth = r.ReadSingle(), WorldHeight = r.ReadSingle(), Width = r.ReadInt32(), Height = r.ReadInt32() };
        int bytes = r.ReadInt32(); f.Hash = r.ReadUInt32();
        byte recovery = r.ReadByte(); if (recovery > 63) throw new InvalidDataException("Invalid visual presentation flag.");
        f.Recovering = (recovery & 1)!=0; f.ControlsMenu=(recovery & 2)!=0; f.ControlsBinding=(recovery & 4)!=0; f.RootMenu=(recovery & 8)!=0; f.InventoryMenu=(recovery & 16)!=0; f.ControlsMouseCapture=(recovery & 32)!=0; f.ScreenFade = r.ReadSingle(); f.AnchorX = r.ReadSingle(); f.AnchorY = r.ReadSingle();
        byte flags = r.ReadByte(); float vx = r.ReadSingle(), vy = r.ReadSingle(), width = r.ReadSingle(), height = r.ReadSingle();
        if (flags == 1 || flags == 3) f.Motion = new Pose(f.Session, f.GeometryFrame, f.AnchorX, f.AnchorY, vx, vy, width, height, flags == 3);
        else if (flags != 0 || vx != 0 || vy != 0 || width != 0 || height != 0) throw new InvalidDataException("Invalid image motion flags.");
        f.ContentSequence = r.ReadInt64();
        f.ContentFrame = r.ReadInt64();
        int regions=r.ReadUInt16();
        if(regions>MinionHudAtlas.MaximumRegions)throw new InvalidDataException("Visual region capacity exceeded.");
        // Validate dimensions/byte count before allocating even bounded metadata.
        Validate(f, bytes, hud);
        if(regions>0) {
            f.Regions=new VisualRegion[regions];
            for(int i=0;i<regions;i++)f.Regions[i]=new VisualRegion(r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),
                r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle());
            Validate(f,bytes,hud);
        }
        f.Rgba = buffers?.Rent(bytes) ?? new byte[bytes]; f.BufferOwner = buffers;
        try
        {
            int offset = 0;
            while (offset < bytes) { int n = r.Read(f.Rgba, offset, bytes - offset); if (n == 0) throw new EndOfStreamException(); offset += n; }
            if (Checksum(f.Rgba) != f.Hash) throw new InvalidDataException("Visual checksum mismatch.");
            return f;
        }
        catch { f.ReleasePixels(); throw; }
    }
    private static void Validate(VisualFrame f, int bytes, bool hud)
    {
        if(f.Regions==null || f.Regions.Length>MinionHudAtlas.MaximumRegions
            || f.Regions.Length>0 && (hud || f.Motion!=null || f.Recovering))throw new InvalidDataException("Invalid visual regions.");
        foreach(var region in f.Regions)
            if(region.X<0 || region.Y<0 || region.Width<=0 || region.Height<=0
                || (long)region.X+region.Width>f.Width || (long)region.Y+region.Height>f.Height
                || !Finite(region.WorldX,100000) || !Finite(region.WorldY,100000)
                || !Finite(region.WorldWidth,100) || !Finite(region.WorldHeight,100)
                || region.WorldWidth<=0 || region.WorldHeight<=0)throw new InvalidDataException("Invalid visual region bounds.");
        var m = f.Motion;
        if (m != null && (m.GeometrySession != f.Session || m.GeometryFrame != f.GeometryFrame || m.X != f.AnchorX || m.Y != f.AnchorY
            || !Finite(m.VelocityX, 10000) || !Finite(m.VelocityY, 10000) || !Finite(m.Width, 100) || !Finite(m.Height, 100)
            || m.Width <= 0 || m.Height <= 0)) throw new InvalidDataException("Invalid captured motion.");
        if (f.Session == Guid.Empty || f.Sequence <= 0 || f.NativeFrame < 0 || f.GeometryFrame < 0 || f.ContentSequence < 0 || f.ContentFrame < 0 || f.CaptureTick <= 0
            || f.SentTick < f.CaptureTick || f.Frequency <= 0 || f.Frequency > 1000000000000
            || double.IsNaN(f.PreviousRoundTripMs) || f.PreviousRoundTripMs < 0 || f.PreviousRoundTripMs > 60000
            || !Finite(f.X, 100000) || !Finite(f.Y, 100000) || !Finite(f.WorldWidth, 100) || !Finite(f.WorldHeight, 100)
            || !Finite(f.AnchorX, 100000) || !Finite(f.AnchorY, 100000)
            || f.WorldWidth <= 0 || f.WorldHeight <= 0 || f.Width < 1 || f.Height < 1 || f.Width > (hud ? MaxHudSide : MaxSide) || f.Height > (hud ? MaxHudSide : MaxSide)
            || (long)f.Width * f.Height > (hud ? MaxHudPixels : MaxSide * MaxSide)
            || !Finite(f.ScreenFade, 1) || f.ScreenFade < 0 || !f.Recovering && f.ScreenFade != 0 || hud && f.Recovering
            || f.ControlsMenu && (!hud || f.Recovering || f.Width<=1)
            || f.ControlsBinding && !f.ControlsMenu
            || f.ControlsMouseCapture && !f.ControlsBinding
            || f.RootMenu && (!hud || f.Width <= 1 || f.Recovering || f.ControlsMenu || f.ControlsBinding)
            || f.InventoryMenu && (!hud || f.Width <= 1 || f.Recovering || f.ControlsMenu || f.ControlsBinding || f.RootMenu)
            || bytes != (long)f.Width * f.Height * 4) throw new InvalidDataException("Invalid visual frame."
                + " sequence=" + f.Sequence + " nativeFrame=" + f.NativeFrame + " geometryFrame=" + f.GeometryFrame
                + " contentSequence=" + f.ContentSequence + " contentFrame=" + f.ContentFrame
                + " captureTick=" + f.CaptureTick + " sentTick=" + f.SentTick + " frequency=" + f.Frequency
                + " rtt=" + f.PreviousRoundTripMs + " x=" + f.X + " y=" + f.Y
                + " worldWidth=" + f.WorldWidth + " worldHeight=" + f.WorldHeight
                + " width=" + f.Width + " height=" + f.Height + " bytes=" + bytes
                + " fade=" + f.ScreenFade + " recovering=" + f.Recovering + " hud=" + hud);
    }
    private static bool Finite(float x, float max) => !float.IsNaN(x) && !float.IsInfinity(x) && Math.Abs(x) <= max;
}
