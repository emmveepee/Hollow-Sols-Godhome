using System;
using System.IO;
using System.Text;

namespace YiGodhome.Bridge;

// One file-owner worker; preserves startup plus a bounded rolling tail.
public sealed class BoundedEventLog : IDisposable
{
    private readonly string path;
    private readonly long limit;
    private StreamWriter writer;
    private readonly StreamWriter startup;
    private readonly BoundedEventLog? performance;
    private long bytes, startupBytes;
    private readonly AsyncLogWriter? asynchronous;
    public Exception? Failure=>asynchronous?.Failure;
    public BoundedEventLog(string path, long limit = 8 * 1024 * 1024, bool retainPerformance = false, bool backgroundIO = true)
    {
        if (limit < 128) throw new ArgumentOutOfRangeException(nameof(limit));
        this.path = path; this.limit = limit;
        writer = Open(path); startup = Open(path + ".startup");
        if(retainPerformance)performance=new BoundedEventLog(path+".perf",limit,backgroundIO:false);
        if(backgroundIO)asynchronous=new AsyncLogWriter(WriteDirect,Close);
    }
    private static StreamWriter Open(string path) => new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
    public void WriteLine(string line)
    {
        if(line.Length>32768)line=line.Substring(0,32768)+" [truncated]";
        if(asynchronous!=null)asynchronous.WriteLine(line);else WriteDirect(line);
    }
    private void WriteDirect(string line)
    {
        if (line.Length > 32768) line = line.Substring(0, 32768) + " [truncated]";
        if(performance!=null && (line.IndexOf(" FRAME_PERF ",StringComparison.Ordinal)>=0 || line.IndexOf(" HK_FRAME_SPIKE ",StringComparison.Ordinal)>=0 || line.IndexOf(" NATIVE_GC_POLICY ",StringComparison.Ordinal)>=0))performance.WriteLine(line);
        long length = Encoding.UTF8.GetByteCount(line) + Encoding.UTF8.GetByteCount(Environment.NewLine);
        if (bytes != 0 && bytes + length > limit) {
            writer.Dispose();
            if (File.Exists(path + ".2")) File.Delete(path + ".2");
            if (File.Exists(path + ".1")) File.Move(path + ".1", path + ".2");
            File.Move(path, path + ".1"); writer = Open(path); bytes = 0;
        }
        writer.WriteLine(line); bytes += length;
        if (startupBytes + length <= Math.Min(limit, 256 * 1024)) { startup.WriteLine(line); startupBytes += length; }
    }
    private void Close() { writer.Dispose(); startup.Dispose(); performance?.Dispose(); }
    public void Dispose() { if(asynchronous!=null)asynchronous.Dispose();else Close(); }
}
