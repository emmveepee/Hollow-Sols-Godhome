using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
namespace YiGodhome.Bridge;

// File stalls must not hold either game or its IPC callbacks. The queue owns
// strings only; fixed byte/count budgets bound a stalled disk's memory cost.
public sealed class AsyncLogWriter : IDisposable
{
    private readonly object gate=new();
    private readonly Queue<string> pending=new();
    private readonly Thread thread;
    private readonly Action<string> write;
    private readonly Action close;
    private readonly int budget;
    private int bytes;
    private long dropped;
    private bool ending;
    private Exception? failure;
    public Exception? Failure=>Volatile.Read(ref failure);
    public int QueuedBytes {get {lock(gate)return bytes;}}
    public AsyncLogWriter(Action<string> write,Action close,int budget=2*1024*1024)
    {
        if(budget<128)throw new ArgumentOutOfRangeException(nameof(budget));
        this.write=write;this.close=close;this.budget=budget;
        thread=new Thread(Run){IsBackground=true,Name="HollowSols bounded log writer"};thread.Start();
    }
    public void WriteLine(string line)
    {
        int size=line.Length*2;
        lock(gate) {
            if(ending || Failure!=null)return;
            if(size>budget-bytes || pending.Count>=4096){dropped++;return;}
            bytes+=size;pending.Enqueue(line);Monitor.Pulse(gate);
        }
    }
    private void Run()
    {
        try {
            while(true) {
                string? line;long lost;
                lock(gate) {
                    while(pending.Count==0 && !ending)Monitor.Wait(gate);
                    lost=dropped;dropped=0;
                    if(pending.Count==0)line=null;
                    else {line=pending.Dequeue();bytes-=line.Length*2;}
                }
                if(lost>0)write(DateTime.UtcNow.ToString("O")+" LOG_QUEUE_OVERFLOW dropped="+lost+" bounded=True");
                if(line==null)break;
                long start=Stopwatch.GetTimestamp();write(line);
                double ms=(Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency;
                if(ms>=25)write(DateTime.UtcNow.ToString("O")+" LOG_IO_STALL ms="+ms.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+" gameThreadBlocked=False");
            }
        } catch(Exception error) {lock(gate){Volatile.Write(ref failure,error);ending=true;pending.Clear();bytes=0;}}
        finally {try{close();}catch(Exception error){Volatile.Write(ref failure,error);}}
    }
    public void Dispose()
    {
        lock(gate){ending=true;Monitor.Pulse(gate);}
        // A broken filesystem cannot prevent the game process from exiting.
        thread.Join(5000);
    }
}
