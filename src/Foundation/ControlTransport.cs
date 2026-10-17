using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HollowSols.Foundation;

// Created only by a local bridge-button action. No process enumeration or auto-
// activation. Workers move immutable control frames; native calls stay on Unity.
public sealed class ControlTransport : IDisposable
{
    private readonly ConcurrentQueue<ControlMessage> received = new ConcurrentQueue<ControlMessage>();
    private readonly ConcurrentQueue<ControlMessage> outgoing = new ConcurrentQueue<ControlMessage>();
    private readonly CancellationTokenSource stopping = new CancellationTokenSource();
    private readonly SemaphoreSlim available = new SemaphoreSlim(0);
    private PipeStream? stream;
    private string? failure;
    private int disposed;
    private int pendingWrites;
    public bool WritesPending => Volatile.Read(ref pendingWrites) != 0;
    public string? Failure => Volatile.Read(ref failure);
    public bool Connected => stream?.IsConnected == true && Volatile.Read(ref disposed) == 0;
    public static string RendezvousName()
    {
        using var hash = SHA256.Create();
        string user = Environment.UserDomainName + "\\" + Environment.UserName;
        return "HollowSols.Control." + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(user))).Replace("-", "").Substring(0, 24);
    }
    public ControlTransport(GameSide side, string? fixtureName = null)
    {
        if (side != GameSide.HollowKnight && side != GameSide.NineSols) throw new ArgumentException("Invalid control role.");
        _ = Run(side, fixtureName ?? RendezvousName());
    }
    public bool TryReceive(out ControlMessage? message) => received.TryDequeue(out message);
    public void Send(ControlMessage message)
    {
        if (Volatile.Read(ref disposed) != 0 || Failure != null) throw new IOException("Control transport stopped.");
        if (outgoing.Count >= 64) { Fail("Control send queue overflow."); throw new IOException("Control queue overflow."); }
        Interlocked.Increment(ref pendingWrites); outgoing.Enqueue(message); available.Release();
    }
    private async Task Run(GameSide side, string name)
    {
        try
        {
            using PipeStream pipe = side == GameSide.HollowKnight
                ? new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous)
                : new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            Volatile.Write(ref stream, pipe);
            if (side == GameSide.HollowKnight) await ((NamedPipeServerStream)pipe).WaitForConnectionAsync(stopping.Token).ConfigureAwait(false);
            else
            {
                while (true)
                {
                    stopping.Token.ThrowIfCancellationRequested();
                    try { await ((NamedPipeClientStream)pipe).ConnectAsync(1000, stopping.Token).ConfigureAwait(false); break; }
                    catch (TimeoutException) { }
                }
            }
            var read = Read(pipe); var write = Write(pipe);
            await Task.WhenAny(read, write).ConfigureAwait(false);
            CancelWorker(); pipe.Dispose();
            await Task.WhenAll(read, write).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (Volatile.Read(ref disposed) == 0) Fail(error.GetType().Name + ": " + error.Message); }
        finally { Volatile.Write(ref stream, null); }
    }
    private async Task Read(Stream pipe)
    {
        var frame = new byte[ControlProtocol.MessageBytes];
        while (true)
        {
            int offset = 0;
            while (offset < frame.Length)
            {
                int read = await pipe.ReadAsync(frame, offset, frame.Length - offset, stopping.Token).ConfigureAwait(false);
                if (read == 0) throw new EndOfStreamException("The counterpart closed its connection.");
                offset += read;
            }
            var message = ControlProtocol.Read(new MemoryStream(frame, false));
            if (received.Count >= 64) throw new IOException("Control receive queue overflow.");
            received.Enqueue(message);
        }
    }
    private async Task Write(Stream pipe)
    {
        while (true)
        {
            await available.WaitAsync(stopping.Token).ConfigureAwait(false);
            if (!outgoing.TryDequeue(out var message)) continue;
            using var frame = new MemoryStream(ControlProtocol.MessageBytes); ControlProtocol.Write(frame, message);
            var bytes = frame.ToArray(); await pipe.WriteAsync(bytes, 0, bytes.Length, stopping.Token).ConfigureAwait(false);
            await pipe.FlushAsync(stopping.Token).ConfigureAwait(false);
            Interlocked.Decrement(ref pendingWrites);
        }
    }
    private void CloseWorker()
    {
        var pipe = Volatile.Read(ref stream);
        if (pipe != null) _ = Task.Run(() => { try { pipe.Dispose(); } catch (IOException) { } catch (ObjectDisposedException) { } });
    }
    private void CancelWorker()
    {
        try { stopping.Cancel(); }
        catch (ObjectDisposedException) { }
        catch (AggregateException error) when (error.InnerExceptions.All(e => e is ObjectDisposedException || e is IOException)) { }
    }
    private void Fail(string reason) { Volatile.Write(ref failure, reason); CancelWorker(); CloseWorker(); }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        CancelWorker(); CloseWorker();
        while (received.TryDequeue(out _)) { } while (outgoing.TryDequeue(out _)) { }
    }
}
