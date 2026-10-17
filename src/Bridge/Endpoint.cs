using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace YiGodhome.Bridge;

public sealed class PeerState
{
    public readonly long Frame;
    public readonly double SenderSampleAgeMs;
    public readonly long ReceivedTick;
    public readonly double RoundTripMs;
    public readonly long Sequence;
    public readonly HostAudioState? Audio;
    public readonly Pose? Pose;
    public readonly EncounterStatus? Encounter;
    public readonly long InputAck;
    public readonly Guid DetailCapture;
    public PeerState(Packet packet, long received)
    {
        Frame = packet.Frame; SenderSampleAgeMs = (packet.SentTick - packet.FrameTick) * 1000.0 / packet.Frequency;
        ReceivedTick = received; Sequence = packet.Sequence;
        Pose = packet.Pose; Audio = packet.Audio;
        Encounter = packet.Encounter;
        InputAck = packet.InputAck; DetailCapture = packet.DetailCapture;
        RoundTripMs = packet.EchoTick == 0 ? 0 : (received - packet.EchoTick) * 1000.0 / Stopwatch.Frequency;
    }
}

// No Unity APIs, gameplay effects or disk I/O. Connection teardown invalidates peer state.
public sealed class Endpoint : IDisposable
{
    private sealed class FrameSample
    {
        public readonly long Frame, Tick;
        public readonly Pose? Pose;
        public readonly EncounterStatus? Encounter;
        public readonly HostAudioState? Audio;
        public readonly Guid DetailCapture;
        public FrameSample(long frame, long tick, Pose? pose = null, EncounterStatus? encounter = null, HostAudioState? audio = null, Guid detailCapture = default) { DetailCapture = detailCapture; Frame = frame; Tick = tick; Pose = pose; Encounter = encounter; Audio = audio; }
    }
    private byte[] nativeBindings = Array.Empty<byte>();
    private byte[] sentBindings = Array.Empty<byte>();
    public byte[] NativeBindings => Volatile.Read(ref nativeBindings);
    public void PublishNativeBindings(byte[] bindings) {
        if (role != Role.NineSols || capabilities != 7 || bindings == null || bindings.Length == 0 || bindings.Length > Protocol.MaxBindingsBytes)
            throw new InvalidOperationException("Only the gameplay peer publishes bounded native bindings.");
        Volatile.Write(ref nativeBindings, (byte[])bindings.Clone());
    }
    private readonly Guid session;
    private readonly Role role;
    private readonly uint capabilities;
    private readonly ConcurrentQueue<InputCommand> outgoingInputs = new ConcurrentQueue<InputCommand>();
    // Only the IPC worker fills this list; published command arrays remain owned.
    private readonly List<InputCommand> packetInputs = new List<InputCommand>(Protocol.MaxInputs);
    private readonly ConcurrentQueue<PendingInput> incomingInputs = new ConcurrentQueue<PendingInput>();
    private sealed class PendingInput
    {
        public readonly InputCommand Command;
        public readonly long Received;
        public readonly double Age;
        public PendingInput(InputCommand command, long received, double age) { Command = command; Received = received; Age = age; }
    }
    private Controls? lastControls;
    private long producedInput, sentInput, receivedInput, consumedInput, acknowledgedInput;
    private readonly ManualResetEvent stopping = new ManualResetEvent(false);
    private readonly ConcurrentQueue<string> events = new ConcurrentQueue<string>();
    private readonly long started = Stopwatch.GetTimestamp();
    private FrameSample sample = new FrameSample(0, Stopwatch.GetTimestamp());
    private PeerState? peer;
    private PipeStream? stream;
    private int stopped;
    private long receivedTick;
    private long lastPeerSent;
    private long lastSent;
    private volatile string state = "connecting";

    public string State => Volatile.Read(ref stopped) != 0 ? "disconnected" : state;
    public PeerState? Peer => Volatile.Read(ref peer);
    public Pose? FreshPose
    {
        get
        {
            var p = Peer;
            if (!IsPeerFresh || p == null) return null;
            return p.Pose;
        }
    }
    public bool IsPeerFresh
    {
        get { var p = Peer; return p != null && p.RoundTripMs + p.SenderSampleAgeMs + (Stopwatch.GetTimestamp() - p.ReceivedTick) * 1000.0 / Stopwatch.Frequency <= 250; }
    }
    public long ConsumedInputSequence => Interlocked.Read(ref consumedInput);
    // Presentation survives stale-input neutralization. Only an authenticated,
    // fresh host command can change it; missing input is not controller input.
    public bool KeyboardInput { get; private set; } = true;
    private bool parryDebugRequested;
    public bool ParryDebugRequested => IsPeerFresh && parryDebugRequested;
    public Guid FreshDetailCapture => IsPeerFresh ? Peer?.DetailCapture ?? Guid.Empty : Guid.Empty;
    public EncounterStatus? FreshEncounter => IsPeerFresh ? Peer?.Encounter : null;

    // Every change is retained. Lifecycle callbacks may enqueue before this frame
    // is published; Create holds those commands until their frame is available.
    private long lastInputPublished;
    public void PublishControls(long frame, Controls controls, bool renewNeutralEntry = false, bool renewMenuRequest=false)
    {
        if (role != Role.HollowKnight || capabilities != 7) throw new InvalidOperationException("This endpoint cannot send input.");
        const Buttons entryMetadata=Buttons.EntryRevealed|Buttons.HostPaused|Buttons.Keyboard|Buttons.ParryDebug;
        if(renewNeutralEntry && (controls.X!=0 || controls.Y!=0 || (controls.Buttons&Buttons.EntryRevealed)==0 || (controls.Buttons&~entryMetadata)!=0))
            throw new ArgumentException("Entry renewal must contain only neutral input and entry metadata.");
        const Buttons menuMetadata=entryMetadata|Buttons.NativePauseMenu;
        if(renewMenuRequest && (controls.X!=0 || controls.Y!=0 || (controls.Buttons&Buttons.NativePauseMenu)==0 || (controls.Buttons&~menuMetadata)!=0))
            throw new ArgumentException("Menu renewal must contain only a neutral menu request and metadata.");
        long now=Stopwatch.GetTimestamp();
        bool renewDebug=(controls.Buttons&Buttons.ParryDebug)!=0 && controls.X==0 && controls.Y==0
            && (controls.Buttons&~(menuMetadata|Buttons.DoorEnter|Buttons.StatueEnter))==0;
        bool renewMouse=(controls.Buttons&Buttons.BindingMouse)!=0 && MenuPointer.IsExclusive(controls);
        if (Volatile.Read(ref stopped) != 0 || lastControls.HasValue && lastControls.Value.Equals(controls)
            && (!(renewNeutralEntry || renewMenuRequest || renewDebug || renewMouse) || (now-lastInputPublished)/(double)Stopwatch.Frequency<.1)) return;
        if (outgoingInputs.Count >= 128) { Stop("input-send-overflow"); return; }
        lastControls = controls;
        lastInputPublished=now;
        var input = new InputCommand(++producedInput, frame, now, controls);
        outgoingInputs.Enqueue(input);
        events.Enqueue("IPC_INPUT_QUEUED id=" + input.Sequence + " frame=" + frame + " x=" + controls.X + " y=" + controls.Y + " buttons=" + controls.Buttons);
    }

    // Once per NS native input tick, not once per action: press/release retain separate ticks.
    public Controls ConsumeControls(Controls held)
    {
        if (role != Role.NineSols || capabilities != 7) throw new InvalidOperationException("This endpoint cannot consume input.");
        if (!IsPeerFresh) { held = default; parryDebugRequested = false; }
        while (incomingInputs.TryDequeue(out var pending))
        {
            Interlocked.Exchange(ref consumedInput, pending.Command.Sequence);
            double age = pending.Age + (Stopwatch.GetTimestamp() - pending.Received) * 1000.0 / Stopwatch.Frequency;
            if (!IsPeerFresh || age > 250) { held = default; parryDebugRequested = false; events.Enqueue("IPC_INPUT_EXPIRED id=" + pending.Command.Sequence); continue; }
            events.Enqueue("IPC_INPUT_CONSUMED id=" + pending.Command.Sequence + " sourceFrame=" + pending.Command.Frame);
            KeyboardInput = (pending.Command.Controls.Buttons & Buttons.Keyboard) != 0;
            parryDebugRequested = (pending.Command.Controls.Buttons & Buttons.ParryDebug) != 0;
            return pending.Command.Controls;
        }
        return held;
    }
    public bool TryGetEvent(out string message) => events.TryDequeue(out message!);

    public Endpoint(Guid session, Role role, uint capabilities = 1)
    {
        if (session == Guid.Empty || role != Role.HollowKnight && role != Role.NineSols) throw new ArgumentException("Invalid endpoint identity.");
        if (capabilities != 1 && capabilities != 3 && capabilities != 7) throw new ArgumentException("Invalid endpoint capabilities.");
        this.session = session; this.role = role; this.capabilities = capabilities;
        new Thread(Run) { IsBackground = true, Name = "YiGodhome telemetry" }.Start();
    }

    public void PublishFrame(long frame, Pose? pose = null, EncounterStatus? encounter = null, HostAudioState? audio = null, Guid detailCapture = default) => Volatile.Write(ref sample, new FrameSample(frame, Stopwatch.GetTimestamp(), pose, encounter, audio, detailCapture));

    public void CheckHealth()
    {
        var now = Stopwatch.GetTimestamp();
        var last = Interlocked.Read(ref receivedTick);
        if (state == "connected" && Peer is PeerState currentPeer
            && currentPeer.SenderSampleAgeMs + (now - currentPeer.ReceivedTick) * 1000d / Stopwatch.Frequency > 5000)
            Stop("gameplay-frame-timeout");
        else if (state == "connected" && (now - last) / (double)Stopwatch.Frequency > 3) Stop("heartbeat-timeout");
        else if (state == "connecting" && (now - started) / (double)Stopwatch.Frequency > 30) Stop("connect-timeout");
    }

    public void Stop(string reason)
    {
        if (Interlocked.Exchange(ref stopped, 1) != 0) return;
        state = "disconnected";
        Volatile.Write(ref peer, null);
        Volatile.Write(ref nativeBindings, Array.Empty<byte>());
        while (outgoingInputs.TryDequeue(out _)) { }
        while (incomingInputs.TryDequeue(out _)) { }
        stopping.Set();
        try { PipeShutdown.Close(Volatile.Read(ref stream)); } catch (IOException) { }
        events.Enqueue("IPC_DISCONNECTED reason=" + reason);
    }
    public void Dispose() => Stop("shutdown");

    private void Run()
    {
        try
        {
            var name = "YiGodhome." + session.ToString("N");
            using PipeStream pipe = role == Role.HollowKnight
                ? new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.None)
                : new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.None);
            Volatile.Write(ref stream, pipe);
            if (Volatile.Read(ref stopped) != 0) return;
            if (pipe is NamedPipeServerStream server) server.WaitForConnection();
            else ((NamedPipeClientStream)pipe).Connect(45000);
            using var input = new PipeReadBuffer(pipe);
            using var output = PipeBuffers.Outgoing(pipe);
            if (role == Role.HollowKnight)
            {
                Protocol.Write(output, Create(MessageKind.Hello, 0));
                Accept(Protocol.Read(input), MessageKind.Hello, 0);
            }
            else
            {
                Accept(Protocol.Read(input), MessageKind.Hello, 0);
                Protocol.Write(output, Create(MessageKind.Hello, 0));
            }
            if (Volatile.Read(ref stopped) != 0) return;
            state = "connected";
            events.Enqueue("IPC_CONNECTED role=" + role + " version=" + Protocol.Version + " capabilities=" + capabilities);
            for (long sequence = 1; !stopping.WaitOne(0); sequence++)
            {
                if (role == Role.HollowKnight)
                {
                    Protocol.Write(output, Create(MessageKind.Snapshot, sequence));
                    Accept(Protocol.Read(input), MessageKind.Snapshot, sequence);
                    stopping.WaitOne(capabilities >= 3 ? 4 : 50);
                }
                else
                {
                    Accept(Protocol.Read(input), MessageKind.Snapshot, sequence);
                    Protocol.Write(output, Create(MessageKind.Snapshot, sequence));
                }
            }
        }
        catch (Exception error) { Stop(error.GetType().Name + ":" + error.Message); }
        finally { Stop("peer-closed"); Volatile.Write(ref stream, null); }
    }

    private Packet Create(MessageKind kind, long sequence)
    {
        byte[] bindings = Array.Empty<byte>();
        if (kind == MessageKind.Snapshot && role == Role.NineSols && capabilities == 7) {
            var latestBindings = Volatile.Read(ref nativeBindings);
            if (!ReferenceEquals(latestBindings, sentBindings)) { bindings = latestBindings; sentBindings = latestBindings; }
        }
        var current = Volatile.Read(ref sample);
        var inputs = packetInputs; inputs.Clear();
        if (kind == MessageKind.Snapshot && capabilities == 7 && role == Role.HollowKnight)
            while (inputs.Count < Protocol.MaxInputs && outgoingInputs.TryPeek(out var pending) && pending.Frame <= current.Frame
                && outgoingInputs.TryDequeue(out var input)) { inputs.Add(input); sentInput = input.Sequence; }
        lastSent = Stopwatch.GetTimestamp();
        if (kind == MessageKind.Snapshot && (sequence == 1 || sequence % 100 == 0))
        {
            events.Enqueue("IPC_SENT sequence=" + sequence + " frame=" + current.Frame + " sentTick=" + lastSent);
            if (current.Pose != null) events.Enqueue("IPC_POSE_SENT sequence=" + sequence + " frame=" + current.Frame + " " + Describe(current.Pose));
        }
        return new Packet { Kind = kind, Role = role, Session = session, Sequence = sequence,
            NativeBindings = bindings,
            Frame = current.Frame, FrameTick = current.Tick, SentTick = lastSent, EchoTick = lastPeerSent,
            Audio = kind == MessageKind.Snapshot ? current.Audio : null, Capabilities = capabilities, Pose = kind == MessageKind.Snapshot ? current.Pose : null, Encounter = kind == MessageKind.Snapshot ? current.Encounter : null, Inputs = inputs.Count == 0 ? Array.Empty<InputCommand>() : inputs.ToArray(),
            DetailCapture = kind == MessageKind.Snapshot ? current.DetailCapture : Guid.Empty,
            InputAck = kind == MessageKind.Snapshot && capabilities == 7 && role == Role.NineSols ? Interlocked.Read(ref consumedInput) : 0 };
    }

    private void Accept(Packet packet, MessageKind kind, long sequence)
    {
        long now = Stopwatch.GetTimestamp();
        if (kind == MessageKind.Hello) events.Enqueue("IPC_CLOCK localNow=" + now + " peerSent=" + packet.SentTick + " localSent=" + lastSent + " peerEcho=" + packet.EchoTick + " frequency=" + Stopwatch.Frequency);
        if (packet.Session != session || packet.Role == role || packet.Kind != kind || packet.Sequence != sequence || packet.Capabilities != capabilities)
            throw new InvalidDataException("Peer identity or sequence mismatch.");
        if (packet.SentTick < lastPeerSent || peer != null && packet.Frame < peer.Frame)
            throw new InvalidDataException("Peer time/frame regressed.");
        // Mono builds can use different timer epochs. Only compare ticks within their originating process.
        if (packet.EchoTick != lastSent) throw new InvalidDataException("Echo mismatch: expected=" + lastSent + " received=" + packet.EchoTick);
        if (capabilities == 7)
        {
            if (role == Role.HollowKnight)
            {
                if (packet.InputAck < acknowledgedInput || packet.InputAck > sentInput) throw new InvalidDataException("Invalid input acknowledgement.");
                acknowledgedInput = packet.InputAck;
            }
            else foreach (var input in packet.Inputs)
            {
                if (input.Sequence != receivedInput + 1 || incomingInputs.Count >= 128) throw new InvalidDataException("Input gap or receive overflow.");
                receivedInput = input.Sequence;
                incomingInputs.Enqueue(new PendingInput(input, now, (packet.SentTick - input.Tick) * 1000.0 / packet.Frequency
                    + (packet.EchoTick == 0 ? 0 : (now - packet.EchoTick) * 1000.0 / Stopwatch.Frequency)));
            }
        }
        lastPeerSent = packet.SentTick;
        Interlocked.Exchange(ref receivedTick, now);
        if (kind == MessageKind.Snapshot && (sequence == 1 || sequence % 100 == 0))
        {
            events.Enqueue("IPC_RECEIVED sequence=" + sequence + " frame=" + packet.Frame + " peerSentTick=" + packet.SentTick + " receivedTick=" + now);
            if (packet.Pose != null) events.Enqueue("IPC_POSE_RECEIVED sequence=" + sequence + " frame=" + packet.Frame + " " + Describe(packet.Pose));
        }
        if (kind == MessageKind.Snapshot && Volatile.Read(ref stopped) == 0)
        {
            if (role == Role.HollowKnight && packet.NativeBindings.Length > 0)
                Volatile.Write(ref nativeBindings, packet.NativeBindings);
            Volatile.Write(ref peer, new PeerState(packet, now));
            // Teardown can race publication. Never retain a sample after stop.
            if (Volatile.Read(ref stopped) != 0) { Volatile.Write(ref peer, null); Volatile.Write(ref nativeBindings, Array.Empty<byte>()); }
        }
    }
    private static string Describe(Pose p) => string.Format(System.Globalization.CultureInfo.InvariantCulture,
        "geometryFrame={0} x={1:R} y={2:R} vx={3:R} vy={4:R} width={5:R} height={6:R} grounded={7}",
        p.GeometryFrame, p.X, p.Y, p.VelocityX, p.VelocityY, p.Width, p.Height, p.Grounded)
        + (p.Vitals == null ? "" : string.Format(System.Globalization.CultureInfo.InvariantCulture,
            " health={0:R} maxHealth={1:R} injury={2:R} chi={3:R} ammo={4:R} potions={5} dead={6} talismanActive={7}",
            p.Vitals.Health, p.Vitals.MaxHealth, p.Vitals.Injury, p.Vitals.Chi, p.Vitals.Ammo, p.Vitals.Potions, p.Vitals.Dead, p.Vitals.TalismanActive));
}
