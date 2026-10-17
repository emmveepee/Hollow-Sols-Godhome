using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace YiGodhome.Bridge;

public sealed class ContactEndpoint : IDisposable
{
    private sealed class Received
    {
        public readonly ContactFrame Frame;
        public readonly long Tick = Stopwatch.GetTimestamp();
        public Received(ContactFrame frame) { Frame = frame; }
    }
    private readonly Guid session;
    private readonly Role role;
    private readonly Guid generation;
    private readonly ManualResetEvent stopping = new ManualResetEvent(false);
    private readonly object shutdown = new object();
    private readonly ConcurrentQueue<string> events = new ConcurrentQueue<string>();
    private ContactFrame? outgoing;
    private Received? incoming;
    private PipeStream? pipe;
    private int stopped;
    public bool Stopped => Volatile.Read(ref stopped) != 0;
    public ContactEndpoint(Guid session, Role role, Guid generation = default)
    {
        if (session == Guid.Empty || role != Role.NineSols && role != Role.HollowKnight) throw new ArgumentException("Invalid contact identity.");
        this.session = session; this.role = role; this.generation = generation;
        new Thread(Run) { IsBackground = true, Name = "YiGodhome contacts" }.Start();
    }
    public void Publish(ContactFrame frame)
    {
        if (role != Role.HollowKnight || frame.Session != session) throw new InvalidOperationException("Invalid contact publisher.");
        if (Volatile.Read(ref stopped) == 0) Volatile.Write(ref outgoing, frame);
    }
    public ContactFrame? FreshFrame
    {
        get
        {
            var sample = Volatile.Read(ref incoming);
            if (Volatile.Read(ref stopped) != 0 || sample == null) return null;
            var f = sample.Frame;
            double age = (f.SentTick - f.CaptureTick) * 1000.0 / f.Frequency + f.RoundTripMs
                + (Stopwatch.GetTimestamp() - sample.Tick) * 1000.0 / Stopwatch.Frequency;
            return age <= 250 ? f : null;
        }
    }
    public bool TryGetEvent(out string message) => events.TryDequeue(out message!);
    public void Dispose()
    {
        lock (shutdown) {
        if (Interlocked.Exchange(ref stopped, 1) != 0) return;
        Volatile.Write(ref incoming, null); Volatile.Write(ref outgoing, null); stopping.Set();
        try { PipeShutdown.Close(Volatile.Read(ref pipe)); } catch (IOException) { }
        events.Enqueue("CONTACT_DISCONNECTED role=" + role);
        }
    }
    private void Run()
    {
        try
        {
            string name = "YiGodhome." + session.ToString("N") + (generation == Guid.Empty ? "" : "." + generation.ToString("N")) + ".contact";
            using PipeStream stream = role == Role.NineSols
                ? new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.None)
                : new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.None);
            Volatile.Write(ref pipe, stream); if (Volatile.Read(ref stopped) != 0) return;
            if (stream is NamedPipeServerStream server) server.WaitForConnection(); else ((NamedPipeClientStream)stream).Connect(45000);
            using var input = role == Role.NineSols ? new ContactInput(stream, events) : null;
            using var reader = new BinaryReader(input ?? (Stream)stream, System.Text.Encoding.UTF8, true);
            using var writer = new WireWriter(stream);
            using var output = role == Role.HollowKnight ? new ContactOutput(stream, events) : null;
            long sequence = 0, nativeFrame = -1; double rtt = 0;
            while (!stopping.WaitOne(0))
            {
                if (role == Role.HollowKnight)
                {
                    var frame = Interlocked.Exchange(ref outgoing, null);
                    if (frame == null) { stopping.WaitOne(2); continue; }
                    frame.Sequence = ++sequence; frame.SentTick = Stopwatch.GetTimestamp(); frame.RoundTripMs = rtt;
                    output!.Write(frame);
                    long ackSequence = reader.ReadInt64(), ackTick = reader.ReadInt64();
                    if (ackSequence != sequence || ackTick != frame.SentTick) throw new InvalidDataException("Contact acknowledgement mismatch.");
                    rtt = (Stopwatch.GetTimestamp() - frame.SentTick) * 1000.0 / Stopwatch.Frequency;
                    writer.Write(rtt); writer.Flush();
                    if (sequence == 1 || sequence % 30 == 0) events.Enqueue("CONTACT_SENT " + Describe(frame));
                }
                else
                {
                    input!.Begin();
                    var frame = ContactCodec.Read(input);
                    if (frame.Session != session || frame.Sequence != sequence + 1 || frame.NativeFrame < nativeFrame)
                        throw new InvalidDataException("Contact session/sequence/frame mismatch.");
                    sequence = frame.Sequence; nativeFrame = frame.NativeFrame;
                    writer.Write(sequence); writer.Write(frame.SentTick); writer.Flush();
                    double transferRoundTrip = reader.ReadDouble();
                    if (double.IsNaN(transferRoundTrip) || transferRoundTrip < 0 || transferRoundTrip > 60000)
                        throw new InvalidDataException("Invalid contact transfer age.");
                    input!.Complete(frame);
                    frame.RoundTripMs = Math.Max(frame.RoundTripMs, transferRoundTrip);
                    if (Volatile.Read(ref stopped) != 0) return;
                    Volatile.Write(ref incoming, new Received(frame));
                    if (Volatile.Read(ref stopped) != 0) Volatile.Write(ref incoming, null);
                    if (sequence == 1 || sequence % 30 == 0) events.Enqueue("CONTACT_RECEIVED " + Describe(frame));
                }
            }
        }
        catch (Exception error) { events.Enqueue("CONTACT_STOP reason=" + error.GetType().Name + ":" + error.Message); }
        finally { lock (shutdown) { Dispose(); stopping.Dispose(); } }
    }
    private static string Describe(ContactFrame f) => "sequence=" + f.Sequence + " frame=" + f.NativeFrame
        + " entity=" + f.Entity + " collider=" + f.Collider + " active=" + f.Active + " health=" + f.Health
        + " geometryFrame=" + f.GeometryFrame + " x=" + f.X + " y=" + f.Y + " transferMs=" + f.RoundTripMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
}
