#if HOLLOWSOLS_PUBLIC_RELEASE
using System;
namespace YiGodhome.Bridge;
public sealed class DetailedEventCapture : IDisposable
{
    public const int MaxCaptures = 8;
    public const long MaxBytes = 8 * 1024 * 1024;
    public const double MaxSeconds = 300;
    public Guid ActiveId => Guid.Empty;
    public bool Active => false;
    public string Status => "Unavailable in public builds";
    public DetailedEventCapture(string directory, string identity, long limit = MaxBytes, double duration = MaxSeconds) { }
    public void Observe(string line) { }
    public void Sync(Guid id, double now, string context) { }
    public void Tick(double now) { }
    public void Write(string line) { }
    public void Stop(string reason) { }
    public void Dispose() { }
}
#else
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace YiGodhome.Bridge;

// Opt-in incident evidence. No game discovery, gameplay effects or silent overwrite.
public sealed class DetailedEventCapture : IDisposable
{
    public const int MaxCaptures = 8;
    public const long MaxBytes = 8 * 1024 * 1024;
    public const double MaxSeconds = 300;
    private readonly string directory, identity;
    private readonly long limit;
    private readonly double duration;
    private readonly Queue<string> prelude = new Queue<string>();
    private StreamWriter? writer;
    private long bytes;
    private double started;
    private int count;
    private Guid requested;
    public Guid ActiveId { get; private set; }
    public bool Active => writer != null;
    public string Status { get; private set; } = "Off";
    public DetailedEventCapture(string directory, string identity, long limit = MaxBytes, double duration = MaxSeconds)
    {
        if (limit < 1024 || limit > MaxBytes || duration <= 0 || duration > MaxSeconds) throw new ArgumentOutOfRangeException();
        this.directory = directory; this.identity = identity; this.limit = limit; this.duration = duration;
    }
    public void Observe(string line)
    {
        prelude.Enqueue(line.Length > 4096 ? line.Substring(0,4096) : line);
        while (prelude.Count > 32) prelude.Dequeue();
        Write(line);
    }
    // A stopped/expired request is never restarted until the host supplies a new ID.
    public void Sync(Guid id, double now, string context)
    {
        if (id == requested) return;
        requested = id; Stop("manual-or-peer-stop");
        if (id == Guid.Empty) return;
        if (count >= MaxCaptures) { Status = "Stopped: capture limit (restart session)"; return; }
        try {
            Directory.CreateDirectory(directory);
            writer = new StreamWriter(new FileStream(Path.Combine(directory,"detail-" + id.ToString("N") + ".log"),FileMode.CreateNew,FileAccess.Write,FileShare.Read),new UTF8Encoding(false)) { AutoFlush = true };
            count++; bytes = 0; started = now; ActiveId = id; Status = "Recording " + id.ToString("N").Substring(0,8);
            Write(DateTime.UtcNow.ToString("O") + " DETAIL_BEGIN id=" + id + " " + identity + " " + context);
            foreach (var line in prelude) { if (!Active) break; Write("PRELUDE " + line); }
        } catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { Stop("io-failure"); Status = "Stopped: logging I/O failure"; }
    }
    public void Tick(double now) { if (Active && now - started >= duration) Stop("time-limit"); }
    public void Write(string line)
    {
        if (writer == null) return;
        if (line.Length > 16384) line = line.Substring(0,16384) + " [truncated]";
        long size = Encoding.UTF8.GetByteCount(line) + Encoding.UTF8.GetByteCount(Environment.NewLine);
        if (bytes + size > limit - 256) { Stop("size-limit"); return; }
        try { writer.WriteLine(line); bytes += size; }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { Stop("io-failure"); Status = "Stopped: logging I/O failure"; }
    }
    public void Stop(string reason)
    {
        if (writer == null) return;
        var closing = writer; writer = null;
        try { closing.WriteLine(DateTime.UtcNow.ToString("O") + " DETAIL_END id=" + ActiveId + " reason=" + reason + " bytes=" + bytes.ToString(CultureInfo.InvariantCulture)); closing.Dispose(); }
        catch (IOException) { try { closing.Dispose(); } catch (IOException) { } }
        string shortId = ActiveId.ToString("N").Substring(0,8);
        ActiveId = Guid.Empty; Status = "Stopped: " + reason + " (" + shortId + ")";
    }
    public void Dispose() => Stop("shutdown");
}

#endif
