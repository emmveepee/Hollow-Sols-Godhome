using System;
using System.Collections.Generic;

namespace YiGodhome.Bridge;

// Exact lengths keep wire validation and Texture2D upload semantics unchanged.
public sealed class PixelBuffers
{
    private sealed class Bucket { internal readonly Stack<byte[]> Free=new(); internal long Used; }
    private readonly Dictionary<int, Bucket> free = new Dictionary<int, Bucket>();
    private readonly object gate = new object();
    private readonly long budget;
    private readonly int maxFreePerSize;
    private long retained,clock;
    private long allocatedBytes,evictedBytes;
    public PixelBuffers():this(128L*1024*1024) { }
    internal PixelBuffers(long budget,int maxFreePerSize=3) {
        if(budget<=0)throw new ArgumentOutOfRangeException(nameof(budget));
        if(maxFreePerSize<1 || maxFreePerSize>8)throw new ArgumentOutOfRangeException(nameof(maxFreePerSize));
        this.budget=budget;this.maxFreePerSize=maxFreePerSize;
    }
    internal long RetainedBytes {get{lock(gate)return retained;}}
    internal int Created, Outstanding;
    internal long AllocatedBytes => System.Threading.Interlocked.Read(ref allocatedBytes);
    internal long EvictedBytes {get{lock(gate)return evictedBytes;}}
    public byte[] Rent(int bytes)
    {
        if (bytes <= 0 || bytes > Math.Max(VisualCodec.MaxHudPixels,VisualCodec.MaxSide*VisualCodec.MaxSide) * 4) throw new ArgumentOutOfRangeException(nameof(bytes));
        System.Threading.Interlocked.Increment(ref Outstanding);
        lock (gate) if (free.TryGetValue(bytes, out var bucket)) {
            bucket.Used=++clock;
            if(bucket.Free.Count>0){retained-=bytes;return bucket.Free.Pop();}
        }
        System.Threading.Interlocked.Increment(ref Created);
        System.Threading.Interlocked.Add(ref allocatedBytes,bytes);
        return new byte[bytes];
    }
    public void Return(byte[] bytes)
    {
        System.Threading.Interlocked.Decrement(ref Outstanding);
        lock (gate)
        {
            if(bytes.Length>budget)return;
            if (!free.TryGetValue(bytes.Length, out var bucket)) {
                if(free.Count>=16)Evict(-1);
                free.Add(bytes.Length,bucket=new Bucket());
            }
            bucket.Used=++clock;
            if(bucket.Free.Count>=maxFreePerSize)return;
            while(retained+bytes.Length>budget) {
                // Trim other sizes first. Keeping every cold singleton while
                // discarding the current size's second/third lease starves the
                // active producer/worker and allocates tens of MiB per frame.
                // Never remove this bucket while holding its reference.
                if(TrimSpare(bytes.Length))continue;
                // Only free buffers are evicted. If every other size is
                // leased, discard this return rather than touching live data.
                if(!Evict(bytes.Length))return;
            }
            bucket.Free.Push(bytes);retained+=bytes.Length;
        }
    }
    private bool TrimSpare(int except) {
        Bucket? oldest=null;long used=long.MaxValue;
        foreach(var pair in free)if(pair.Key!=except && pair.Value.Free.Count>1 && pair.Value.Used<used){oldest=pair.Value;used=pair.Value.Used;}
        if(oldest==null)return false;
        int bytes=oldest.Free.Pop().Length;retained-=bytes;evictedBytes+=bytes;return true;
    }
    private bool Evict(int except) {
        int oldest=-1;long used=long.MaxValue;
        foreach(var pair in free)if(pair.Key!=except && pair.Value.Used<used){oldest=pair.Key;used=pair.Value.Used;}
        if(oldest<0)return false;
        var bucket=free[oldest];long bytes=(long)oldest*bucket.Free.Count;retained-=bytes;evictedBytes+=bytes;free.Remove(oldest);return true;
    }
}
