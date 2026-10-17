using System;

namespace HollowSols.Foundation;

public enum BridgeState
{
    Vanilla, WaitingForCounterpart, Handshaking, Connected, SaveSelection,
    Loading, Active, Ending, Disconnecting, Failed
}

public readonly struct BridgeHello
{
    public const ushort CurrentVersion = 6;
    public GameSide Side { get; }
    public Guid Activation { get; }
    public ushort Version { get; }
    public BridgeHello(GameSide side, Guid activation, ushort version = CurrentVersion)
    {
        if (side != GameSide.HollowKnight && side != GameSide.NineSols || activation == Guid.Empty)
            throw new ArgumentException("Invalid bridge activation.");
        Side = side; Activation = activation; Version = version;
    }
}

public readonly struct ConnectionOffer : IEquatable<ConnectionOffer>
{
    public Guid Connection { get; }
    public Guid HollowKnightActivation { get; }
    public Guid NineSolsActivation { get; }
    public ConnectionOffer(Guid connection, Guid hk, Guid ns)
    {
        if (connection == Guid.Empty || hk == Guid.Empty || ns == Guid.Empty) throw new ArgumentException("Invalid connection offer.");
        Connection = connection; HollowKnightActivation = hk; NineSolsActivation = ns;
    }
    public bool Equals(ConnectionOffer other) => Connection == other.Connection && HollowKnightActivation == other.HollowKnightActivation && NineSolsActivation == other.NineSolsActivation;
    public override bool Equals(object? other) => other is ConnectionOffer value && Equals(value);
    public override int GetHashCode() => Connection.GetHashCode();
}

public readonly struct SessionToken : IEquatable<SessionToken>
{
    public Guid Connection { get; }
    public Guid Session { get; }
    public Guid Pair { get; }
    public ModSlot Slot { get; }
    public long Ordinal { get; }
    public SessionToken(Guid connection, Guid session, Guid pair, ModSlot slot, long ordinal)
    {
        slot.Validate();
        if (connection == Guid.Empty || session == Guid.Empty || pair == Guid.Empty || ordinal < 1) throw new ArgumentException("Invalid session identity.");
        Connection = connection; Session = session; Pair = pair; Slot = slot; Ordinal = ordinal;
    }
    public bool Equals(SessionToken other) => Connection == other.Connection && Session == other.Session && Pair == other.Pair && Slot.Equals(other.Slot) && Ordinal == other.Ordinal;
    public override bool Equals(object? other) => other is SessionToken value && Equals(value);
    public override int GetHashCode() => Session.GetHashCode();
}

// Engine-free transition policy. Call on the main thread. A transition never
// asserts that native cleanup, menu rendering or a disk write happened.
public sealed class BridgeLifecycle
{
    public const double HandshakeSeconds = 30;
    public const double OperationSeconds = 60;
    public const double LoadingSeconds = 180;
    public const double PeerSilenceSeconds = 5;
    public GameSide Side { get; }
    public BridgeState State { get; private set; } = BridgeState.Vanilla;
    public Guid Connection { get; private set; }
    public SessionToken? Session { get; private set; }
    public string? Failure { get; private set; }
    public bool CanSimulate => State == BridgeState.Active;
    public bool RecoveryNoticePending { get; private set; }
    public bool RestorationsReady => State == BridgeState.Loading && localReady && (Side == GameSide.NineSols || peerReady);
    private BridgeHello? local, remote;
    private ConnectionOffer? offer;
    private long ordinal;
    private bool localReady, peerReady, localDrained, peerDrained;
    private bool nativeCleanupRequired;
    private double deadline = double.PositiveInfinity, lastPeer = double.NegativeInfinity, lastTime = double.NegativeInfinity;

    public BridgeLifecycle(GameSide side)
    {
        if (side != GameSide.HollowKnight && side != GameSide.NineSols) throw new ArgumentOutOfRangeException(nameof(side));
        Side = side;
    }

    public BridgeHello RequestBridge(double now)
    {
        Time(now);
        if (State == BridgeState.WaitingForCounterpart || State == BridgeState.Handshaking) return local!.Value;
        if (State != BridgeState.Vanilla && State != BridgeState.Failed) throw new InvalidOperationException("Already in bridge mode.");
        if (nativeCleanupRequired) throw new InvalidOperationException("Restore native menu state before reconnecting.");
        Reset(); Failure = null; State = BridgeState.WaitingForCounterpart;
        local = new BridgeHello(Side, Guid.NewGuid()); deadline = double.PositiveInfinity;
        return local.Value;
    }

    public void ObserveHello(BridgeHello hello, double now)
    {
        Time(now);
        if (!local.HasValue || State != BridgeState.WaitingForCounterpart && State != BridgeState.Handshaking)
            throw new InvalidOperationException("The local bridge button has not authorized a handshake.");
        if (hello.Side == Side || hello.Activation == Guid.Empty || hello.Version != BridgeHello.CurrentVersion)
        { Fail("The games use incompatible bridge versions or identities."); return; }
        if (remote.HasValue && remote.Value.Activation != hello.Activation)
        { Fail("The counterpart restarted during connection. Activate Hollow Sols Bridge in either game again."); return; }
        remote = hello;
    }

    public ConnectionOffer OfferConnection(double now)
    {
        Time(now); HostOnly();
        if (State == BridgeState.Handshaking && offer.HasValue) return offer.Value;
        if (State != BridgeState.WaitingForCounterpart || !remote.HasValue) throw new InvalidOperationException("Both bridge activations are required.");
        offer = new ConnectionOffer(Guid.NewGuid(), local!.Value.Activation, remote.Value.Activation);
        State = BridgeState.Handshaking; deadline = now + HandshakeSeconds; return offer.Value;
    }

    public ConnectionOffer AcceptOffer(ConnectionOffer value, double now)
    {
        Time(now); PeerOnly();
        if (State == BridgeState.Handshaking && offer.HasValue && offer.Value.Equals(value)) return value;
        if (State != BridgeState.WaitingForCounterpart || !local.HasValue || !remote.HasValue
            || value.Connection == Guid.Empty || value.NineSolsActivation != local.Value.Activation || value.HollowKnightActivation != remote.Value.Activation)
            throw new InvalidOperationException("Unrequested or stale connection offer.");
        offer = value; State = BridgeState.Handshaking; deadline = now + HandshakeSeconds; return value;
    }

    public void ConfirmConnection(ConnectionOffer value, double now)
    {
        Time(now);
        if ((State == BridgeState.Connected || State == BridgeState.SaveSelection) && offer.HasValue && offer.Value.Equals(value)) return;
        if (State != BridgeState.Handshaking || !offer.HasValue || !offer.Value.Equals(value)) throw new InvalidOperationException("Connection acknowledgement is stale.");
        Connection = value.Connection; lastPeer = now; deadline = double.PositiveInfinity;
        State = Side == GameSide.HollowKnight ? BridgeState.SaveSelection : BridgeState.Connected;
    }

    public SessionToken BeginLoad(SavePair pair, double now)
    {
        Time(now); HostOnly();
        if (State != BridgeState.SaveSelection || Connection == Guid.Empty) throw new InvalidOperationException("End the prior session before selecting another slot.");
        var token = new SessionToken(Connection, Guid.NewGuid(), pair.Id, pair.Slot, checked(ordinal + 1));
        StartLoad(token, now); return token;
    }

    public void AcceptLoad(SessionToken token, double now)
    {
        Time(now); PeerOnly();
        if (State == BridgeState.Loading && Session.HasValue && Session.Value.Equals(token)) return;
        if (State != BridgeState.Connected || token.Connection != Connection || Connection == Guid.Empty || token.Ordinal != checked(ordinal + 1))
            throw new InvalidOperationException("Only the connected HK may select the next paired session.");
        StartLoad(token, now);
    }
    private void StartLoad(SessionToken token, double now)
    {
        Session = token; ordinal = token.Ordinal; State = BridgeState.Loading;
        localReady = peerReady = localDrained = peerDrained = false; RecoveryNoticePending = false;
        deadline = now + LoadingSeconds;
    }

    public void LocalLoaded(SessionToken token, bool recovered, double now)
    {
        Time(now); RequireSession(token, BridgeState.Loading);
        localReady = true;
        // Host UI must visibly acknowledge recovery before sending Start.
        if (recovered && Side == GameSide.HollowKnight) RecoveryNoticePending = true;
    }
    public void IntroductionWaiting(SessionToken token, double now)
    {
        Time(now); RequireSession(token, BridgeState.Loading);
        // An observed native dialogue input wait is progress, not a frozen load.
        // Peer-silence detection remains independent of this operation deadline.
        deadline = now + LoadingSeconds;
    }
    public void PeerLoaded(SessionToken token, double now, bool recovered = false)
    { Time(now); HostOnly(); RequireSession(token, BridgeState.Loading); peerReady = true; RecoveryNoticePending |= recovered; }
    public void AcknowledgeRecoveryNotice(SessionToken token)
    { HostOnly(); RequireSession(token, BridgeState.Loading); RecoveryNoticePending = false; }

    public void StartSession(SessionToken token, double now)
    {
        Time(now);
        if (State == BridgeState.Active && Session.HasValue && Session.Value.Equals(token)) return;
        RequireSession(token, BridgeState.Loading);
        if (!localReady || Side == GameSide.HollowKnight && !peerReady || RecoveryNoticePending)
            throw new InvalidOperationException("Both restored states and any recovery notice must be ready.");
        State = BridgeState.Active; deadline = double.PositiveInfinity;
    }

    public void EndSession(SessionToken token, double now)
    {
        Time(now);
        if (State == BridgeState.Ending && Session.HasValue && Session.Value.Equals(token)) return;
        if (State != BridgeState.Active && State != BridgeState.Loading) throw new InvalidOperationException("No session to end.");
        RequireToken(token); State = BridgeState.Ending; deadline = now + OperationSeconds;
        localDrained = peerDrained = false;
    }
    public void Drained(SessionToken token, GameSide side, double now)
    {
        Time(now); RequireSession(token, BridgeState.Ending);
        if (side != GameSide.HollowKnight && side != GameSide.NineSols) throw new ArgumentOutOfRangeException(nameof(side));
        if (side == Side) localDrained = true; else peerDrained = true;
        if (!localDrained || !peerDrained) return;
        Session = null; RecoveryNoticePending = false; deadline = double.PositiveInfinity;
        State = Side == GameSide.HollowKnight ? BridgeState.SaveSelection : BridgeState.Connected;
    }

    public bool AcceptsGameplay(SessionToken token) => State == BridgeState.Active && Session.HasValue && Session.Value.Equals(token);
    public bool AcceptsSave(SessionToken token) => (State == BridgeState.Active || State == BridgeState.Ending) && Session.HasValue && Session.Value.Equals(token);

    // Call only after pending save/owner drains, or loss of the peer. Invalidate
    // identities immediately; restoring native menu state is a separate adapter gate.
    public void Disconnect(double now)
    {
        Time(now);
        if (Session.HasValue) throw new InvalidOperationException("End and drain the session before entering vanilla selection.");
        nativeCleanupRequired = Connection != Guid.Empty;
        Reset(); State = BridgeState.Disconnecting; deadline = now + OperationSeconds;
    }
    public void NativeVanillaRestored(double now)
    {
        Time(now, false);
        if (State != BridgeState.Disconnecting && State != BridgeState.Failed) throw new InvalidOperationException("Native restoration was not requested.");
        Reset(); nativeCleanupRequired = false; State = BridgeState.Vanilla;
    }
    public void PeerHeartbeat(Guid connection, double now)
    {
        Time(now);
        if (connection == Guid.Empty || connection != Connection) throw new InvalidOperationException("Stale heartbeat.");
        lastPeer = now;
    }
    public void Tick(double now)
    {
        Time(now, false);
        if (State == BridgeState.Vanilla || State == BridgeState.Failed) return;
        if (now >= deadline) { Fail("The bridge operation timed out. Activate Hollow Sols Bridge in either game to retry."); return; }
        if (Connection != Guid.Empty && now - lastPeer > PeerSilenceSeconds) Fail("The connection to the other game was lost.");
    }
    public void Fail(string message)
    { nativeCleanupRequired |= Session.HasValue || Connection != Guid.Empty; Reset(); Failure = message; State = BridgeState.Failed; }
    private void Reset()
    {
        Connection = Guid.Empty; Session = null; local = remote = null; offer = null; ordinal = 0;
        localReady = peerReady = localDrained = peerDrained = RecoveryNoticePending = false;
        deadline = double.PositiveInfinity; lastPeer = double.NegativeInfinity;
    }
    private void RequireSession(SessionToken token, BridgeState state)
    { if (State != state) throw new InvalidOperationException("Invalid session transition."); RequireToken(token); }
    private void RequireToken(SessionToken token)
    { if (!Session.HasValue || !Session.Value.Equals(token) || token.Connection != Connection) throw new InvalidOperationException("Stale or foreign session operation."); }
    private void HostOnly() { if (Side != GameSide.HollowKnight) throw new InvalidOperationException("HK owns paired save selection and coordination."); }
    private void PeerOnly() { if (Side != GameSide.NineSols) throw new InvalidOperationException("Only NS accepts host selections."); }
    private void Time(double now, bool enforceDeadline = true)
    {
        if (double.IsNaN(now) || double.IsInfinity(now) || now < lastTime) throw new ArgumentException("A monotonic clock is required.");
        lastTime = now;
        if (enforceDeadline && now >= deadline && State != BridgeState.Vanilla && State != BridgeState.Failed)
        {
            Fail("The bridge operation timed out. Activate Hollow Sols Bridge in either game to retry.");
            throw new InvalidOperationException("Expired bridge operation.");
        }
    }
}
