using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace YiGodhome.Bridge;

// A duplex pipe needs independent buffers. These directional views never own
// the pipe; its endpoint still closes it to interrupt blocked native I/O.
internal static class PipeBuffers
{
    private sealed class Output : Stream
    {
        private readonly Stream pipe;
        internal Output(Stream pipe) { this.pipe=pipe; }
        public override bool CanRead=>false;
        public override bool CanWrite=>true;
        public override bool CanSeek=>false;
        public override long Length=>throw new NotSupportedException();
        public override long Position { get=>throw new NotSupportedException(); set=>throw new NotSupportedException(); }
        public override void Write(byte[] buffer,int offset,int count)=>pipe.Write(buffer,offset,count);
        public override void WriteByte(byte value)=>pipe.WriteByte(value);
        public override void Flush()=>pipe.Flush();
        public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
        public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
        public override void SetLength(long value)=>throw new NotSupportedException();
    }
    internal static BufferedStream Outgoing(Stream pipe)=>new BufferedStream(new Output(pipe),32768);
}

internal sealed class PipeReadBuffer : Stream
{
    private sealed class Input : Stream
    {
        private readonly Stream pipe;
        internal long Calls,Bytes;
        internal Input(Stream pipe) { this.pipe=pipe; }
        public override bool CanRead=>true;
        public override bool CanWrite=>false;
        public override bool CanSeek=>false;
        public override long Length=>throw new NotSupportedException();
        public override long Position { get=>throw new NotSupportedException(); set=>throw new NotSupportedException(); }
        public override int Read(byte[] buffer,int offset,int count) { Calls++; int n=pipe.Read(buffer,offset,count); Bytes+=n; return n; }
        public override int ReadByte() { Calls++; int n=pipe.ReadByte(); if(n>=0)Bytes++; return n; }
        public override void Flush() { }
        public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
        public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
        public override void SetLength(long value)=>throw new NotSupportedException();
    }
    private readonly Input input;
    private readonly BufferedStream buffer;
    private readonly ConcurrentQueue<string>? events;
    private bool buffered;
    private int frames;
    private long calls,bytes,window;
    internal PipeReadBuffer(Stream pipe, ConcurrentQueue<string>? actorControl=null)
    {
        input=new Input(pipe); buffer=new BufferedStream(input,32768);
        events=actorControl; buffered=actorControl==null;
    }
    // Only a bounded authorized actor fixture uses sixteen raw frames. Switching
    // occurs once, with no prefetched data, after the complete frame and RTT.
    internal void Complete(long sequence)
    {
        if(events==null)return;
        frames++; long now=Stopwatch.GetTimestamp();
        if(window==0)window=now;
        if(!buffered && frames<16 || buffered && now-window<Stopwatch.Frequency*2)return;
        events.Enqueue("PIPE_READ_CALLS channel=visual mode=" + (buffered?"buffered":"direct")
            + " frames=" + frames + " nativeReads=" + (input.Calls-calls) + " wireBytes=" + (input.Bytes-bytes)
            + " sequence=" + sequence + " sameReadView=True");
        buffered=true; frames=0; calls=input.Calls; bytes=input.Bytes; window=now;
    }
    public override bool CanRead=>true;
    public override bool CanWrite=>false;
    public override bool CanSeek=>false;
    public override long Length=>throw new NotSupportedException();
    public override long Position { get=>throw new NotSupportedException(); set=>throw new NotSupportedException(); }
    public override int Read(byte[] value,int offset,int count)=>(buffered?buffer:(Stream)input).Read(value,offset,count);
    public override int ReadByte()=>(buffered?buffer:(Stream)input).ReadByte();
    public override void Flush() { }
    public override void Write(byte[] value,int offset,int count)=>throw new NotSupportedException();
    public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
    public override void SetLength(long value)=>throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if(disposing)buffer.Dispose(); base.Dispose(disposing); }
}
