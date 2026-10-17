using System;
using System.Collections.Generic;

namespace HollowSols.Foundation;

// UI/transport adapters call this on Unity's main thread. Only explicit Activate
// emits an activation Hello. Its counterpart may adopt that explicit user request;
// idle process presence/transport connection never activates either game.
public sealed class MenuBridgeController
{
    private const int QueueLimit = 64;
    public BridgeLifecycle Lifecycle { get; }
    private readonly Queue<ControlMessage> outgoing = new Queue<ControlMessage>();
    private BridgeHello? local, peer;
    private ConnectionOffer? connection;
    private long sent, received;
    public MenuBridgeController(GameSide side) { Lifecycle = new BridgeLifecycle(side); }
    public void Activate(double now)
    {
        var hello = Lifecycle.RequestBridge(now);
        if (local.HasValue && local.Value.Activation == hello.Activation) return;
        outgoing.Clear(); sent = received = 0; connection = null; peer = null; local = hello;
        Enqueue(ControlKind.Hello);
    }
    public bool TryTakeOutgoing(out ControlMessage? message)
    {
        if (outgoing.Count == 0) { message = null; return false; }
        message = outgoing.Dequeue(); return true;
    }
    // Return session operations to the native adapter. Handshake/heartbeat is fully
    // handled here; a native operation still needs its own completion acknowledgement.
    public ControlMessage? Receive(ControlMessage message, double now)
    {
        try
        {
            if (Lifecycle.State == BridgeState.Vanilla && message.Kind == ControlKind.Hello && message.Sequence == 1
                && message.Side != Lifecycle.Side && message.Hello.Version == BridgeHello.CurrentVersion) Activate(now);
            if (!local.HasValue || message.Side == Lifecycle.Side || message.Sequence != checked(received + 1))
                throw new InvalidOperationException("Unrequested, out-of-order or wrong-role control message.");
            if (peer.HasValue && peer.Value.Activation != message.Hello.Activation) throw new InvalidOperationException("Stale counterpart activation.");
            if (message.Kind != ControlKind.Hello && !peer.HasValue) throw new InvalidOperationException("A counterpart Hello is required.");
            if (message.Session.HasValue && message.Session.Value.Connection != Lifecycle.Connection)
                throw new InvalidOperationException("Foreign control connection.");
            // Cancellation can cross completion already queued by the other
            // process. Authenticate/order it, but never restart this ended load.
            if (Lifecycle.State == BridgeState.Ending && Lifecycle.Session.HasValue
                && message.Session.HasValue && Lifecycle.Session.Value.Equals(message.Session.Value)
                && (message.Kind == ControlKind.Loaded || message.Kind == ControlKind.PrepareNative
                    || message.Kind == ControlKind.Start || message.Kind == ControlKind.IntroductionWaiting)) {
                received = message.Sequence; return null;
            }
            if (message.Kind == ControlKind.Hello)
            {
                Lifecycle.ObserveHello(message.Hello, now);
                if (Lifecycle.State == BridgeState.Failed) throw new InvalidOperationException(Lifecycle.Failure);
                peer = message.Hello;
                if (Lifecycle.Side == GameSide.HollowKnight) { connection = Lifecycle.OfferConnection(now); Enqueue(ControlKind.Offer, connection); }
            }
            else if (message.Kind == ControlKind.Offer)
            { connection = Lifecycle.AcceptOffer(message.Offer!.Value, now); Enqueue(ControlKind.Acknowledge, connection); }
            else if (message.Kind == ControlKind.Acknowledge)
            {
                Lifecycle.ConfirmConnection(message.Offer!.Value, now);
                Enqueue(ControlKind.Connected, connection);
            }
            else if (message.Kind == ControlKind.Connected) Lifecycle.ConfirmConnection(message.Offer!.Value, now);
            else if (message.Kind == ControlKind.Heartbeat)
            {
                if (!connection.HasValue || !connection.Value.Equals(message.Offer!.Value)) throw new InvalidOperationException("Foreign connection heartbeat.");
                // A valid heartbeat may already be in the pipe when either
                // side requests vanilla. Disconnect invalidates the gameplay
                // connection immediately; authenticated ordered tail traffic
                // must not turn that graceful shutdown into a failure.
                if(Lifecycle.State!=BridgeState.Disconnecting)
                    Lifecycle.PeerHeartbeat(message.Offer.Value.Connection, now);
            }
            else if (message.Kind == ControlKind.Load) Lifecycle.AcceptLoad(message.Session!.Value, now);
            else if (message.Kind == ControlKind.Loaded) Lifecycle.PeerLoaded(message.Session!.Value, now, message.Recovered);
            else if (message.Kind == ControlKind.Start) Lifecycle.StartSession(message.Session!.Value, now);
            else if (message.Kind == ControlKind.End) Lifecycle.EndSession(message.Session!.Value, now);
            else if (message.Kind == ControlKind.Drained) Lifecycle.Drained(message.Session!.Value, message.Side, now);
            else if (message.Kind == ControlKind.Disconnect) Lifecycle.Disconnect(now);
            else if (message.Kind == ControlKind.IntroductionWaiting) Lifecycle.IntroductionWaiting(message.Session!.Value, now);
            else if (message.Kind == ControlKind.PrepareNative) {
                if (Lifecycle.State != BridgeState.Loading || !Lifecycle.Session.HasValue || !Lifecycle.Session.Value.Equals(message.Session!.Value)) throw new InvalidOperationException("Native preparation belongs to a different load.");
            }
            else if (message.Kind == ControlKind.NativeDefeat) {
                if (!Lifecycle.Session.HasValue || !Lifecycle.Session.Value.Equals(message.Session!.Value) || Lifecycle.State != BridgeState.Active && Lifecycle.State != BridgeState.Ending) throw new InvalidOperationException("Native defeat belongs to a different active session.");
                if (Lifecycle.State == BridgeState.Ending) { received = message.Sequence; return null; }
            }
            else if (!Lifecycle.AcceptsSave(message.Session!.Value)) throw new InvalidOperationException("Checkpoint belongs to an inactive session.");
            received = message.Sequence;
            if (message.Kind >= ControlKind.Load && message.Kind != ControlKind.Loaded) return message;
            return null;
        }
        catch
        {
            Fail("The bridge received an invalid or stale operation. Return to the main menu and activate Hollow Sols Bridge in either game again.");
            throw;
        }
    }
    public SessionToken Select(SavePair pair, double now, bool creating = false)
    {
        var token = Lifecycle.BeginLoad(pair, now); Enqueue(ControlKind.Load, session: token, creating: creating); return token;
    }
    public void NativeLoaded(SessionToken token, bool recovered, double now)
    {
        Lifecycle.LocalLoaded(token, recovered, now);
        if (Lifecycle.Side == GameSide.NineSols) Enqueue(ControlKind.Loaded, session: token, recovered: recovered);
    }
    public void Start(SessionToken token, double now, long generation = 1)
    {
        if (Lifecycle.Side != GameSide.HollowKnight) throw new InvalidOperationException("HK starts the paired session.");
        if (generation < 1) throw new ArgumentOutOfRangeException(nameof(generation));
        Lifecycle.StartSession(token, now); Enqueue(ControlKind.Start, session: token, generation: generation);
    }
    public void End(SessionToken token, double now)
    {
        bool ending = Lifecycle.State == BridgeState.Ending;
        Lifecycle.EndSession(token, now);
        if (!ending) Enqueue(ControlKind.End, session: token);
    }
    public void NativeDrained(SessionToken token, double now)
    { Lifecycle.Drained(token, Lifecycle.Side, now); Enqueue(ControlKind.Drained, session: token); }
    public void Disconnect(double now)
    {
        Lifecycle.Disconnect(now);
        // Disconnection must survive local identity invalidation until the transport
        // sends this final authenticated activation message.
        Enqueue(ControlKind.Disconnect);
    }
    public void Heartbeat()
    {
        if (Lifecycle.Connection != Guid.Empty && connection.HasValue) Enqueue(ControlKind.Heartbeat, connection);
    }
    public void RequestSave(SessionToken token) => Enqueue(ControlKind.SaveNeeded, session: token);
    public void NativeDefeat(SessionToken token) {
        if (Lifecycle.Side != GameSide.NineSols || Lifecycle.State != BridgeState.Active || !Lifecycle.Session.HasValue || !Lifecycle.Session.Value.Equals(token)) throw new InvalidOperationException("Only active native Yi reports its defeat.");
        Enqueue(ControlKind.NativeDefeat, session: token);
    }
    public void PrepareNative(SessionToken token) {
        if (Lifecycle.Side != GameSide.HollowKnight || Lifecycle.State != BridgeState.Loading || !Lifecycle.Session.HasValue || !Lifecycle.Session.Value.Equals(token)) throw new InvalidOperationException("Only HK prepares the selected native pair.");
        Enqueue(ControlKind.PrepareNative, session: token);
    }
    public void IntroductionWaiting(SessionToken token, double now) {
        if (Lifecycle.Side != GameSide.HollowKnight) throw new InvalidOperationException("Only HK observes its native introduction.");
        Lifecycle.IntroductionWaiting(token, now); Enqueue(ControlKind.IntroductionWaiting, session: token);
    }
    public void EndReady(SessionToken token) => Enqueue(ControlKind.EndReady, session: token);
    public void Checkpoint(ControlKind kind, CheckpointRequest request) => Enqueue(kind, session: request.Session, transaction: request.Transaction, generation: request.PreviousGeneration);
    public void Tick(double now)
    {
        Lifecycle.Tick(now);
        if (Lifecycle.State == BridgeState.Failed) { outgoing.Clear(); local = peer = null; connection = null; }
    }
    public void Fail(string reason)
    { Lifecycle.Fail(reason); outgoing.Clear(); local = peer = null; connection = null; }
    public void NativeVanillaRestored(double now)
    { Lifecycle.NativeVanillaRestored(now); outgoing.Clear(); local = peer = null; connection = null; sent = received = 0; }
    private void Enqueue(ControlKind kind, ConnectionOffer? offer = null, SessionToken? session = null, bool recovered = false, bool creating = false, Guid transaction = default, long generation = 0)
    {
        if (!local.HasValue || outgoing.Count >= QueueLimit)
        { Fail("The bridge control queue is unavailable."); throw new InvalidOperationException("Control queue overflow or missing activation."); }
        outgoing.Enqueue(new ControlMessage(kind, local.Value, checked(++sent), offer, session, transaction, generation, recovered, creating));
    }
}
