using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace YiGodhome.Bridge;

// Reliable ordered event pipe. Native host calls occur only in ProcessPending,
// which the host invokes on its Unity main thread, never on this IO worker.
public sealed class HitEndpoint : IDisposable
{
    private sealed class Pending
    {
        public readonly HitOutcome Request;
        public readonly TaskCompletionSource<HitAck> Ready = new TaskCompletionSource<HitAck>(TaskCreationOptions.RunContinuationsAsynchronously);
        public Pending(HitOutcome request) { Request = request; }
    }
    private readonly Guid session;
    private readonly Role role;
    private readonly Guid generation;
    private readonly ConcurrentQueue<HitOutcome> outgoing = new ConcurrentQueue<HitOutcome>();
    private readonly ConcurrentQueue<Pending> pending = new ConcurrentQueue<Pending>();
    private readonly ConcurrentQueue<HitAck> acknowledgements = new ConcurrentQueue<HitAck>();
    private readonly ConcurrentQueue<string> events = new ConcurrentQueue<string>();
    private readonly ManualResetEvent stopping = new ManualResetEvent(false);
    private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
    private readonly Timer watchdog;
    private readonly object shutdown = new object();
    private PipeStream? pipe;
    private int stopped, count;
    private long sequence, waitingAt;
    public bool Stopped => Volatile.Read(ref stopped) != 0;
    public int PendingCount => Volatile.Read(ref count) + pending.Count + acknowledgements.Count;
    public HitEndpoint(Guid session, Role role, Guid generation = default)
    {
        if (session == Guid.Empty || role != Role.HollowKnight && role != Role.NineSols) throw new ArgumentException("Invalid hit endpoint.");
        this.session = session; this.role = role; this.generation = generation;
        watchdog = new Timer(_ => {
            long waiting = Interlocked.Read(ref waitingAt);
            if (waiting != 0 && (Stopwatch.GetTimestamp() - waiting) * 1000.0 / Stopwatch.Frequency > 5000)
            { events.Enqueue("HIT_STOP reason=acknowledgement-timeout"); Dispose(); }
        }, null, 1000, 1000);
        new Thread(Run) { IsBackground = true, Name = "YiGodhome.Hits." + role }.Start();
    }
    public long Submit(HitOutcome request)
    {
        if (role != Role.NineSols || Stopped || request.Session != session) throw new InvalidOperationException("Hit publisher unavailable.");
        if (Interlocked.Increment(ref count) > 64) { Dispose(); throw new InvalidOperationException("Outgoing hit queue overflow."); }
        // Copy the mutable DTO before assigning transport sequence/ownership.
        var h = new HitOutcome { Session = request.Session, Encounter = request.Encounter, Sequence = ++sequence, GeometryFrame = request.GeometryFrame,
            HostTick = request.HostTick, NativeFrame = request.NativeFrame, AttackId = request.AttackId, TargetActivation = request.TargetActivation, Entity = request.Entity, Dealer = request.Dealer, Kind = request.Kind, Damage = request.Damage, Direction = request.Direction };
        try { HitCodec.Validate(h); }
        catch { Dispose(); throw; }
        outgoing.Enqueue(h); return h.Sequence;
    }
    public bool TryGetAck(out HitAck ack) => acknowledgements.TryDequeue(out ack!);
    public bool TryGetEvent(out string message) => events.TryDequeue(out message!);
    public void ProcessPending(Func<HitOutcome, HitAck> apply)
    {
        if (role != Role.HollowKnight) throw new InvalidOperationException("Wrong hit consumer role.");
        while (!Stopped && pending.TryDequeue(out var item))
        {
            try
            {
                var ack = apply(item.Request); HitCodec.ValidateAck(ack);
                if (ack.Session != session || ack.Sequence != item.Request.Sequence) throw new InvalidDataException("Host acknowledgement identity mismatch.");
                item.Ready.TrySetResult(ack);
            }
            catch { Dispose(); throw; }
        }
    }
    private void Run()
    {
        try
        {
            string name = "YiGodhome." + session.ToString("N") + (generation == Guid.Empty ? "" : "." + generation.ToString("N")) + ".hits";
            using PipeStream stream = role == Role.NineSols
                ? new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.None)
                : new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.None);
            Volatile.Write(ref pipe, stream); if (Stopped) return;
            if (stream is NamedPipeServerStream server) server.WaitForConnection(); else ((NamedPipeClientStream)stream).Connect(45000);
            events.Enqueue("HIT_CONNECTED role=" + role);
            while (!Stopped)
            {
                if (role == Role.NineSols)
                {
                    if (!outgoing.TryDequeue(out var request)) { stopping.WaitOne(2); continue; }
                    Interlocked.Exchange(ref waitingAt, Stopwatch.GetTimestamp());
                    HitCodec.Write(stream, request); var ack = HitCodec.ReadAck(stream);
                    if (ack.Session != session || ack.Sequence != request.Sequence) throw new InvalidDataException("Unsent or incorrect hit acknowledgement.");
                    Interlocked.Exchange(ref waitingAt, 0); Interlocked.Decrement(ref count);
                    acknowledgements.Enqueue(ack);
                    events.Enqueue("HIT_ACK sequence=" + ack.Sequence + " result=" + ack.Disposition + " health=" + ack.Health);
                }
                else
                {
                    var request = HitCodec.Read(stream);
                    if (request.Session != session) throw new InvalidDataException("Hit session mismatch.");
                    var item = new Pending(request); pending.Enqueue(item);
                    if (!item.Ready.Task.Wait(5000, cancellation.Token)) throw new TimeoutException("Host did not resolve hit on main thread.");
                    if (Stopped) return;
                    HitCodec.WriteAck(stream, item.Ready.Task.Result);
                    events.Enqueue("HIT_RECEIVED sequence=" + request.Sequence + " entity=" + request.Entity + " damage=" + request.Damage + " kind=" + request.Kind);
                }
            }
        }
        catch (Exception e) { if (!Stopped) events.Enqueue("HIT_STOP reason=" + e.GetType().Name + ":" + e.Message); }
        finally { lock (shutdown) { Dispose(); stopping.Dispose(); cancellation.Dispose(); } }
    }
    public void Dispose()
    {
        lock (shutdown) {
        if (Interlocked.Exchange(ref stopped, 1) != 0) return;
        stopping.Set(); cancellation.Cancel(); watchdog.Dispose(); Interlocked.Exchange(ref waitingAt, 0);
        try { PipeShutdown.Close(Volatile.Read(ref pipe)); } catch (ObjectDisposedException) { }
        while (outgoing.TryDequeue(out _)) { }
        while (pending.TryDequeue(out var item)) item.Ready.TrySetCanceled();
        events.Enqueue("HIT_DISCONNECTED role=" + role);
        }
    }
}
