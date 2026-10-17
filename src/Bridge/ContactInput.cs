using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace YiGodhome.Bridge;

// All incoming contact/RTT reads use this same view, including prefetched
// bytes. ACK writes stay on the independent outgoing direction of the pipe.
internal sealed class ContactInput : Stream
{
    private sealed class Reads : Stream
    {
        internal readonly Stream Inner;
        internal long Calls, Bytes;
        internal Reads(Stream inner) { Inner=inner; }
        public override bool CanRead => true;
        public override bool CanWrite => false;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer,int offset,int count) { Calls++; int n=Inner.Read(buffer,offset,count); Bytes+=n; return n; }
        public override int ReadByte() { Calls++; int n=Inner.ReadByte(); if(n>=0) Bytes++; return n; }
        public override void Flush() { }
        public override void Write(byte[] buffer,int offset,int count) => throw new NotSupportedException();
        public override long Seek(long offset,SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
    private readonly Reads view;
    private readonly BufferedStream buffered;
    private readonly ConcurrentQueue<string> events;
    private readonly bool diagnostic;
    private long growth, calls, bytes, vertices;
    private int samples, skipped;
    private double totalMs;
    private long before, started, startCalls, startBytes, window;
    private int collections;
    internal ContactInput(Stream pipe, ConcurrentQueue<string> events)
    {
        this.events=events; view=new Reads(pipe); buffered=new BufferedStream(view,32768);
        diagnostic=SessionConfiguration.Current.RunFixtures && SessionConfiguration.Current.Has("trace-probe")
            && EncounterProfile.Selected.Family==EncounterProfile.NightmareGrimm;
    }
    internal void Begin()
    {
        if(!diagnostic)return;
        before=GC.GetTotalMemory(false); collections=GC.CollectionCount(0);
        startCalls=view.Calls; startBytes=view.Bytes; started=Stopwatch.GetTimestamp();
    }
    internal void Complete(ContactFrame frame)
    {
        if(!diagnostic)return;
        long now=Stopwatch.GetTimestamp(), after=GC.GetTotalMemory(false), count=0;
        foreach(var v in frame.Volumes) count+=v.Points.Length/2;
        if(collections==GC.CollectionCount(0)) {
            growth+=after-before; calls+=view.Calls-startCalls; bytes+=view.Bytes-startBytes; vertices+=count;
            totalMs+=(now-started)*1000d/Stopwatch.Frequency; samples++;
        } else skipped++;
        if(window==0)window=now;
        if(now-window<Stopwatch.Frequency*2)return;
        events.Enqueue("CONTACT_READ_HEAP mode=buffered samples=" + samples
            + " processHeapDeltaBytes=" + growth + " nativeReads=" + calls + " wireBytes=" + bytes
            + " vertices=" + vertices + " readWaitMs=" + totalMs + " gcSkipped=" + skipped
            + " sequence=" + frame.Sequence + " sameReadView=True");
        growth=calls=bytes=vertices=0; samples=skipped=0; totalMs=0;
        window=now;
    }
    public override bool CanRead=>true;
    public override bool CanWrite=>false;
    public override bool CanSeek=>false;
    public override long Length=>throw new NotSupportedException();
    public override long Position { get=>throw new NotSupportedException(); set=>throw new NotSupportedException(); }
    public override int Read(byte[] buffer,int offset,int count)=>buffered.Read(buffer,offset,count);
    public override int ReadByte()=>buffered.ReadByte();
    public override void Flush() { }
    public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
    public override void SetLength(long value)=>throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        if(disposing) buffered.Dispose();
        base.Dispose(disposing);
    }
}
