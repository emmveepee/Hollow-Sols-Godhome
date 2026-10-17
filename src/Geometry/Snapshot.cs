using System;
using System.IO;
using System.Text;

namespace YiGodhome.Geometry;

public readonly struct Point
{
    public readonly float X, Y;
    public Point(float x, float y) { X = x; Y = y; }
}

public sealed class Shape
{
    public int HazardId; // Nonzero identifies an authored dream-void box, never solid terrain.
    public bool OneWay; // Native standable platform: ground only, never embedded solid/ceiling.
    public bool Closed;
    public float Radius;
    public bool AdjacentStart, AdjacentEnd;
    public Point Start, End;
    public Point[] Points = Array.Empty<Point>();
}

public sealed class Snapshot
{
    public Guid SourceSession;
    public string Scene = "";
    public long Frame;
    public Point Floor;
    public float PlayerWidth;
    public Shape[] Shapes = Array.Empty<Shape>();
    public Point[] RestPoints = Array.Empty<Point>();
    public byte EntryStyle; // 0 none/dream return, 1 front door, 2 left, 3 right, 4 dream return, 5 top-entry fall, 6 bottom-entry launch.
    public float EntryStartX, EntryStartY;
}

public readonly struct CoordinateMap
{
    public readonly Point Source, Target;
    public readonly float Scale;
    public CoordinateMap(Point source, Point target, float scale)
    {
        Codec.CheckPoint(source); Codec.CheckPoint(target);
        if (!Codec.Finite(scale) || scale < 0.001f || scale > 1000) throw new InvalidDataException("Invalid coordinate scale.");
        Source = source; Target = target; Scale = scale;
    }
    public Point ToTarget(Point p) => Checked(Target.X + (p.X - Source.X) * Scale, Target.Y + (p.Y - Source.Y) * Scale);
    public Point ToSource(Point p) => Checked(Source.X + (p.X - Target.X) / Scale, Source.Y + (p.Y - Target.Y) / Scale);
    public Point VelocityToTarget(Point p) => Checked(p.X * Scale, p.Y * Scale);
    private static Point Checked(float x, float y) { var p = new Point(x, y); Codec.CheckPoint(p); return p; }
}

public static class Codec
{
    // Installed only by the paired adapter with an exact active session/mode.
    // Legacy/standalone callers retain the original Godhome-only boundary.
    public static Func<Guid, string, bool>? ExperimentalSceneAuthorization;
    [ThreadStatic] private static System.Collections.Generic.HashSet<int>? validationHazards;
    public static string RuntimePath(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (Path.GetFileName(path) != "godhome-geometry.bin"
            || !Guid.TryParse(Environment.GetEnvironmentVariable("YIGODHOME_SESSION"), out var session)
            || !Guid.TryParse(Path.GetFileName(directory), out var folder) || session != folder
            || Path.GetFileName(Path.GetDirectoryName(directory)) != "HollowKnight") return path;
        return Path.Combine(Path.GetTempPath(), "YiGodhome", session.ToString(), "godhome-geometry.bin");
    }

    public static byte[] ReadFileBytes(string path)
    {
        path = RuntimePath(path);
        using var gate = new SnapshotFileLock(path);
        return ReadLockedBytes(path);
    }
    public static bool TryReadFileBytes(string path, out byte[] bytes)
    {
        path = RuntimePath(path);
        using var gate = new SnapshotFileLock(path, 0);
        if (!gate.Acquired) { bytes = Array.Empty<byte>(); return false; }
        bytes = ReadLockedBytes(path);
        return true;
    }
    private static byte[] ReadLockedBytes(string path)
    {
        // Permit atomic replacement while this reader holds the prior snapshot.
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (file.Length > MaxBytes) throw new InvalidDataException("Geometry file exceeds the size limit.");
        using var reader = new BinaryReader(file);
        byte[] bytes = reader.ReadBytes(checked((int)file.Length));
        if (bytes.Length != file.Length) throw new EndOfStreamException();
        return bytes;
    }
    public static void ValidateRefresh(Snapshot current, Snapshot next)
    {
        Validate(next);
        if (next.SourceSession != current.SourceSession || next.Scene != current.Scene || next.Frame != current.Frame
            || next.Floor.X != current.Floor.X || next.Floor.Y != current.Floor.Y || next.PlayerWidth != current.PlayerWidth)
            throw new InvalidDataException("Live terrain update changed the room coordinate anchor.");
        if (next.RestPoints.Length != current.RestPoints.Length) throw new InvalidDataException("Live rest placements changed.");
        if (next.EntryStyle != current.EntryStyle || next.EntryStartX != current.EntryStartX || next.EntryStartY != current.EntryStartY) throw new InvalidDataException("Live entrance changed.");
        for (int i = 0; i < current.RestPoints.Length; i++)
            if (next.RestPoints[i].X != current.RestPoints[i].X || next.RestPoints[i].Y != current.RestPoints[i].Y)
                throw new InvalidDataException("Live rest placement moved.");
    }
    public const int MaxBytes = 4 * 1024 * 1024;
    private const int MaxShapes = 4096, MaxPoints = 131072, MaxPathPoints = 8192;
    private const uint Magic = 0x47484759;
    public static bool Finite(float n) => !float.IsNaN(n) && !float.IsInfinity(n);
    public static void CheckPoint(Point p)
    {
        if (!Finite(p.X) || !Finite(p.Y) || Math.Abs(p.X) > 100000 || Math.Abs(p.Y) > 100000)
            throw new InvalidDataException("Invalid geometry coordinate.");
    }
    public static void Validate(Snapshot s)
    {
        if (s.EntryStyle > 6 || !Finite(s.EntryStartX) || !Finite(s.EntryStartY)
            || s.EntryStyle==5 && (s.EntryStartY<=s.Floor.Y || s.EntryStartY-s.Floor.Y>512)
            || s.EntryStyle==6 && Math.Abs(s.EntryStartY-s.Floor.Y)>512 || s.EntryStyle != 0 && Math.Abs(s.EntryStartX - s.Floor.X) > 10)
            throw new InvalidDataException("Invalid room entrance.");
        if (s.SourceSession == Guid.Empty || s.Frame < 0 || s.Scene.Length < 4 || s.Scene.Length > 64
            || !s.Scene.StartsWith("GG_", StringComparison.Ordinal) && ExperimentalSceneAuthorization?.Invoke(s.SourceSession, s.Scene) != true || !Finite(s.PlayerWidth) || s.PlayerWidth <= 0 || s.PlayerWidth > 100
            || s.Shapes.Length == 0 || s.Shapes.Length > MaxShapes) throw new InvalidDataException("Invalid geometry snapshot.");
        foreach (char c in s.Scene) if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_'))
            throw new InvalidDataException("Invalid scene identifier.");
        CheckPoint(s.Floor);
        if (s.RestPoints.Length > 32) throw new InvalidDataException("Too many rest placements.");
        for (int i = 0; i < s.RestPoints.Length; i++) {
            var point = s.RestPoints[i]; CheckPoint(point);
            for (int j = 0; j < i; j++) if (point.X == s.RestPoints[j].X && point.Y == s.RestPoints[j].Y)
                throw new InvalidDataException("Duplicate rest placement.");
        }
        int total = 0;
        var hazardIds = validationHazards; hazardIds?.Clear();
        for (int index = 0; index < s.Shapes.Length; index++)
        {
            var shape = s.Shapes[index];
            if (shape.OneWay && (shape.HazardId != 0 || !shape.Closed || shape.Points.Length != 4 || shape.Radius != 0
                || shape.Points[0].Y != shape.Points[1].Y || shape.Points[1].X != shape.Points[2].X
                || shape.Points[2].Y != shape.Points[3].Y || shape.Points[3].X != shape.Points[0].X
                || shape.Points[0].X >= shape.Points[1].X || shape.Points[0].Y >= shape.Points[2].Y))
                throw new InvalidDataException("Invalid one-way platform.");
            if (shape.HazardId != 0 && (!shape.Closed || shape.Points.Length != 4 || shape.Radius != 0))
                throw new InvalidDataException("Invalid dream-void shape.");
            if (shape.HazardId != 0)
            {
                var p = shape.Points;
                hazardIds ??= validationHazards = new System.Collections.Generic.HashSet<int>();
                if (!hazardIds.Add(shape.HazardId) || p[0].Y != p[1].Y || p[1].X != p[2].X || p[2].Y != p[3].Y || p[3].X != p[0].X
                    || p[0].X == p[2].X || p[0].Y == p[2].Y)
                    throw new InvalidDataException("Invalid or duplicate dream-void box.");
            }
            if (shape.Points.Length < (shape.Closed ? 3 : 2) || shape.Points.Length > MaxPathPoints
                || !Finite(shape.Radius) || shape.Radius < 0 || shape.Radius > 10
                || shape.Closed && (shape.Radius != 0 || shape.AdjacentStart || shape.AdjacentEnd)) throw new InvalidDataException("Invalid geometry shape.");
            total += shape.Points.Length;
            if (total > MaxPoints) throw new InvalidDataException("Geometry point budget exceeded.");
            CheckPoint(shape.Start); CheckPoint(shape.End);
            foreach (var p in shape.Points) CheckPoint(p);
            for (int i = 1; i < shape.Points.Length; i++)
                if (shape.Points[i].X == shape.Points[i - 1].X && shape.Points[i].Y == shape.Points[i - 1].Y)
                    throw new InvalidDataException("Repeated adjacent geometry point.");
        }
    }
    public static void Write(Stream stream, Snapshot s)
    {
        Validate(s);
        using var w = new YiGodhome.Bridge.WireWriter(stream);
        w.Write(Magic); w.Write((ushort)7); w.Write(s.SourceSession.ToByteArray());
        w.Write((byte)s.Scene.Length); w.Write(Encoding.ASCII.GetBytes(s.Scene)); w.Write(s.Frame);
        WritePoint(w, s.Floor); w.Write(s.PlayerWidth); w.Write(s.Shapes.Length);
        foreach (var shape in s.Shapes)
        {
            w.Write((byte)((shape.Closed ? 1 : 0) | (shape.AdjacentStart ? 2 : 0) | (shape.AdjacentEnd ? 4 : 0) | (shape.OneWay ? 8 : 0)));
            w.Write(shape.Radius); WritePoint(w, shape.Start); WritePoint(w, shape.End); w.Write(shape.Points.Length);
            foreach (var p in shape.Points) WritePoint(w, p);
            w.Write(shape.HazardId);
        }
        w.Write((byte)s.RestPoints.Length);
        foreach (var point in s.RestPoints) WritePoint(w, point);
        w.Write(s.EntryStyle); w.Write(s.EntryStartX); w.Write(s.EntryStartY);
        w.Flush();
    }
    public static Snapshot Read(Stream stream)
    {
        if (!stream.CanSeek || stream.Length > MaxBytes) throw new InvalidDataException("Geometry requires a bounded snapshot stream.");
        using var r = new BinaryReader(stream, Encoding.UTF8, true);
        if (r.ReadUInt32() != Magic || r.ReadUInt16() != 7) throw new InvalidDataException("Geometry format mismatch.");
        byte[] id = r.ReadBytes(16);
        if (id.Length != 16) throw new EndOfStreamException();
        int length = r.ReadByte();
        if (length < 4 || length > 64) throw new InvalidDataException("Invalid geometry scene length.");
        byte[] name = r.ReadBytes(length);
        if (name.Length != length) throw new EndOfStreamException();
        var s = new Snapshot { SourceSession = new Guid(id), Scene = Encoding.ASCII.GetString(name), Frame = r.ReadInt64(), Floor = ReadPoint(r), PlayerWidth = r.ReadSingle() };
        int count = r.ReadInt32();
        if (count < 1 || count > MaxShapes) throw new InvalidDataException("Invalid geometry shape count.");
        s.Shapes = new Shape[count];
        int total = 0;
        for (int i = 0; i < count; i++)
        {
            byte flags = r.ReadByte();
            if (flags > 15) throw new InvalidDataException("Invalid geometry flags.");
            var shape = new Shape { Closed = (flags & 1) != 0, AdjacentStart = (flags & 2) != 0, AdjacentEnd = (flags & 4) != 0,
                OneWay = (flags & 8) != 0, Radius = r.ReadSingle(), Start = ReadPoint(r), End = ReadPoint(r) };
            int points = r.ReadInt32();
            if (points < 2 || points > MaxPathPoints || (total += points) > MaxPoints) throw new InvalidDataException("Invalid geometry point count.");
            shape.Points = new Point[points];
            for (int j = 0; j < points; j++) shape.Points[j] = ReadPoint(r);
            shape.HazardId = r.ReadInt32();
            s.Shapes[i] = shape;
        }
        int rests = r.ReadByte();
        if (rests > 32) throw new InvalidDataException("Too many rest placements.");
        s.RestPoints = new Point[rests];
        for (int i = 0; i < rests; i++) s.RestPoints[i] = ReadPoint(r);
        s.EntryStyle = r.ReadByte(); s.EntryStartX = r.ReadSingle(); s.EntryStartY = r.ReadSingle();
        if (stream.Position != stream.Length) throw new InvalidDataException("Trailing geometry data.");
        Validate(s); return s;
    }
    private static void WritePoint(BinaryWriter w, Point p) { w.Write(p.X); w.Write(p.Y); }
    private static Point ReadPoint(BinaryReader r) => new Point(r.ReadSingle(), r.ReadSingle());
}

// Named across the two game processes: File.Replace briefly opens the target
// exclusively on Windows even when readers permit deletion.
public sealed class SnapshotFileLock : IDisposable
{
    private static bool reported;
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetHandleInformation(IntPtr handle, out uint flags);
    private readonly System.Threading.Mutex mutex;
    public bool Acquired { get; }
    public SnapshotFileLock(string path, int waitMilliseconds = 100)
    {
        string key;
        using (var hash = System.Security.Cryptography.SHA256.Create())
            key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant()))).Replace("-", "");
        mutex = new System.Threading.Mutex(false, "YiGodhome.Geometry." + key);
        if (!reported && Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG") is string boot && !string.IsNullOrEmpty(boot))
        {
            reported = true;
            bool nativeHandle = GetHandleInformation(mutex.SafeWaitHandle.DangerousGetHandle(), out _);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(boot)!, "geometry-lock.log"),
                "key=" + key + " path=" + Path.GetFullPath(path) + " nativeHandle=" + nativeHandle);
        }
        bool acquired;
        try { acquired = mutex.WaitOne(waitMilliseconds); }
        catch (System.Threading.AbandonedMutexException) { acquired = true; }
        Acquired = acquired;
        if (!acquired && waitMilliseconds != 0) { mutex.Dispose(); throw new IOException("Geometry exchange lock timed out."); }
    }
    public void Dispose() { if (Acquired) mutex.ReleaseMutex(); mutex.Dispose(); }
}
