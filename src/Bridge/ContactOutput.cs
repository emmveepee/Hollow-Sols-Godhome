using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace YiGodhome.Bridge;

// One transport-worker output buffer. Complete frames still flush before ACK;
// the native pipe remains the sole reader and owner of shutdown.
internal sealed class ContactOutput : IDisposable
{
    private sealed class Writes : Stream
    {
        internal readonly Stream Inner;
        internal long Calls, Bytes;
        internal Writes(Stream inner) { Inner = inner; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) { Calls++; Bytes += count; Inner.Write(buffer,offset,count); }
        public override void WriteByte(byte value) { Calls++; Bytes++; Inner.WriteByte(value); }
        public override void Flush() => Inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        // Disposing this view must leave the native pipe for its owning endpoint.
    }
    private readonly BufferedStream buffered;
    private readonly Writes? writes;
    private readonly ConcurrentQueue<string> events;
    private long growth, calls, bytes, vertices;
    private int samples, skipped;
    private double totalMs;
    private long window;
    internal ContactOutput(Stream pipe, ConcurrentQueue<string> events)
    {
        this.events = events;
        bool diagnostic = SessionConfiguration.Current.RunFixtures && SessionConfiguration.Current.Has("trace-probe")
            && EncounterProfile.Selected.Family == EncounterProfile.NightmareGrimm;
        var view = new Writes(pipe);
        if (diagnostic) writes = view;
        buffered = new BufferedStream(view, 32768);
    }
    internal void Write(ContactFrame frame)
    {
        if (writes == null) { ContactCodec.Write(buffered, frame); return; }
        long before = GC.GetTotalMemory(false), count = writes.Calls, amount = writes.Bytes, started = Stopwatch.GetTimestamp();
        int collections = GC.CollectionCount(0);
        ContactCodec.Write(buffered, frame);
        long now = Stopwatch.GetTimestamp(), after = GC.GetTotalMemory(false);
        if (collections == GC.CollectionCount(0)) {
            growth += after-before; calls += writes.Calls-count; bytes += writes.Bytes-amount;
            foreach (var volume in frame.Volumes) vertices += volume.Points.Length / 2;
            totalMs += (now-started)*1000d/Stopwatch.Frequency; samples++;
        } else skipped++;
        if (window == 0) window = now;
        if (now-window < Stopwatch.Frequency*2) return;
        events.Enqueue("CONTACT_WRITE_HEAP mode=buffered samples=" + samples
            + " processHeapDeltaBytes=" + growth + " nativeWrites=" + calls + " wireBytes=" + bytes
            + " vertices=" + vertices + " writeMs=" + totalMs + " gcSkipped=" + skipped
            + " sequence=" + frame.Sequence + " completeFrameFlush=True");
        growth=calls=bytes=vertices=0; samples=skipped=0; totalMs=0;
        window=now;
    }
    public void Dispose()
    {
        // Every successful frame already flushed. The view leaves native pipe
        // ownership untouched even when shutdown races this empty flush.
        try { buffered.Dispose(); }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }
}
