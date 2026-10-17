using System;
using System.IO;
using System.Text;

namespace HollowSols.Foundation;

public enum ControlKind : byte { Hello = 1, Offer = 2, Acknowledge = 3, Connected = 4, Heartbeat = 5, Load = 6, Loaded = 7, Start = 8, End = 9, Drained = 10, Disconnect = 11, Checkpoint = 12, CheckpointReady = 13, CheckpointCommitted = 14, SaveNeeded = 15, EndReady = 16, PrepareNative = 17, IntroductionWaiting = 18, NativeDefeat = 19 }

public sealed class ControlMessage
{
    public ControlKind Kind { get; }
    public GameSide Side { get; }
    public long Sequence { get; }
    public BridgeHello Hello { get; }
    public ConnectionOffer? Offer { get; }
    public SessionToken? Session { get; }
    public Guid Transaction { get; }
    public long Generation { get; }
    public bool Recovered { get; }
    public bool Creating { get; }
    public ControlMessage(ControlKind kind, BridgeHello hello, long sequence, ConnectionOffer? offer = null, SessionToken? session = null,
        Guid transaction = default, long generation = 0, bool recovered = false, bool creating = false)
    {
        Kind = kind; Side = hello.Side; Hello = hello; Sequence = sequence; Offer = offer; Session = session;
        Transaction = transaction; Generation = generation; Recovered = recovered; Creating = creating;
        ControlProtocol.Validate(this);
    }
}

// A separate versioned control plane. Existing combat protocols remain unchanged.
// No filesystem paths or native slot indexes cross this channel.
public static class ControlProtocol
{
    private const uint Magic = 0x43425348;
    public const int MessageBytes = 168;
    public static void Write(Stream stream, ControlMessage message)
    {
        Validate(message);
        // Stage one complete frame. Many tiny synchronous pipe writes can exhaust
        // the pipe's write quota before the receiver begins reading the frame.
        using var bytes = new MemoryStream(MessageBytes);
        using var writer = new BinaryWriter(bytes, Encoding.UTF8, true);
        writer.Write(Magic); writer.Write(BridgeHello.CurrentVersion); writer.Write((byte)message.Kind); writer.Write((byte)message.Side);
        writer.Write(message.Sequence); writer.Write(message.Hello.Activation.ToByteArray());
        writer.Write(message.Offer?.Connection.ToByteArray() ?? new byte[16]);
        writer.Write(message.Offer?.HollowKnightActivation.ToByteArray() ?? new byte[16]);
        writer.Write(message.Offer?.NineSolsActivation.ToByteArray() ?? new byte[16]);
        writer.Write(message.Session?.Connection.ToByteArray() ?? new byte[16]);
        writer.Write(message.Session?.Session.ToByteArray() ?? new byte[16]);
        writer.Write(message.Session?.Pair.ToByteArray() ?? new byte[16]);
        writer.Write((byte)(message.Session?.Slot.Number ?? 0)); writer.Write(message.Session?.Ordinal ?? 0);
        writer.Write(message.Transaction.ToByteArray()); writer.Write(message.Generation); writer.Write(message.Recovered);
        // Explicit reserved bytes allow rejecting accidental future layouts.
        writer.Write(message.Creating); writer.Write(new byte[5]);
        writer.Flush();
        var payload = bytes.ToArray(); stream.Write(payload, 0, payload.Length);
    }
    public static ControlMessage Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, true);
        if (reader.ReadUInt32() != Magic || reader.ReadUInt16() != BridgeHello.CurrentVersion) throw new InvalidDataException("Incompatible Hollow Sols control protocol.");
        var kind = (ControlKind)reader.ReadByte(); var side = (GameSide)reader.ReadByte(); long sequence = reader.ReadInt64();
        var hello = new BridgeHello(side, GuidBytes(reader));
        Guid connection = GuidBytes(reader), hk = GuidBytes(reader), ns = GuidBytes(reader);
        ConnectionOffer? offer = null;
        if (connection != Guid.Empty || hk != Guid.Empty || ns != Guid.Empty) offer = new ConnectionOffer(connection, hk, ns);
        Guid sessionConnection = GuidBytes(reader), session = GuidBytes(reader), pair = GuidBytes(reader);
        byte slot = reader.ReadByte(); long ordinal = reader.ReadInt64(); SessionToken? token = null;
        if (sessionConnection != Guid.Empty || session != Guid.Empty || pair != Guid.Empty || slot != 0 || ordinal != 0)
            token = new SessionToken(sessionConnection, session, pair, new ModSlot(slot), ordinal);
        Guid transaction = GuidBytes(reader); long generation = reader.ReadInt64(); byte recovered = reader.ReadByte();
        if (recovered > 1) throw new InvalidDataException("Invalid recovery marker.");
        byte creating = reader.ReadByte(); if (creating > 1) throw new InvalidDataException("Invalid creation marker.");
        for (int i = 0; i < 5; i++) if (reader.ReadByte() != 0) throw new InvalidDataException("Unsupported control extensions.");
        return new ControlMessage(kind, hello, sequence, offer, token, transaction, generation, recovered == 1, creating == 1);
    }
    internal static void Validate(ControlMessage message)
    {
        if (message.Kind < ControlKind.Hello || message.Kind > ControlKind.NativeDefeat || message.Sequence < 1
            || message.Hello.Version != BridgeHello.CurrentVersion || message.Hello.Activation == Guid.Empty
            || message.Side != GameSide.HollowKnight && message.Side != GameSide.NineSols) throw new InvalidDataException("Invalid control identity.");
        bool offerKind = message.Kind == ControlKind.Offer || message.Kind == ControlKind.Acknowledge || message.Kind == ControlKind.Connected || message.Kind == ControlKind.Heartbeat;
        bool sessionKind = message.Kind >= ControlKind.Load && message.Kind <= ControlKind.Drained || message.Kind >= ControlKind.Checkpoint;
        bool checkpoint = message.Kind >= ControlKind.Checkpoint && message.Kind <= ControlKind.CheckpointCommitted;
        if (message.Offer.HasValue != offerKind || message.Session.HasValue != sessionKind
            || checkpoint != (message.Transaction != Guid.Empty) || (checkpoint || message.Kind == ControlKind.Start) != (message.Generation > 0)
            || !checkpoint && message.Kind != ControlKind.Start && message.Generation != 0 || message.Recovered && message.Kind != ControlKind.Loaded || message.Creating && message.Kind != ControlKind.Load)
            throw new InvalidDataException("Control payload does not match its operation.");
        if ((message.Kind == ControlKind.Offer || message.Kind == ControlKind.Connected || message.Kind == ControlKind.Load || message.Kind == ControlKind.Start
                || message.Kind == ControlKind.Checkpoint || message.Kind == ControlKind.CheckpointCommitted || message.Kind == ControlKind.PrepareNative || message.Kind == ControlKind.IntroductionWaiting) && message.Side != GameSide.HollowKnight
            || (message.Kind == ControlKind.Acknowledge || message.Kind == ControlKind.Loaded || message.Kind == ControlKind.CheckpointReady || message.Kind == ControlKind.SaveNeeded || message.Kind == ControlKind.NativeDefeat) && message.Side != GameSide.NineSols)
            throw new InvalidDataException("Control operation has the wrong authority.");
        if (message.Offer.HasValue && (message.Side == GameSide.HollowKnight ? message.Offer.Value.HollowKnightActivation : message.Offer.Value.NineSolsActivation) != message.Hello.Activation)
            throw new InvalidDataException("Connection offer does not match sender activation.");
        if (message.Session.HasValue)
        {
            var session = message.Session.Value; session.Slot.Validate();
            if (session.Connection == Guid.Empty || session.Session == Guid.Empty || session.Pair == Guid.Empty || session.Ordinal < 1)
                throw new InvalidDataException("Invalid control session token.");
        }
    }
    private static Guid GuidBytes(BinaryReader reader)
    { byte[] bytes = reader.ReadBytes(16); if (bytes.Length != 16) throw new EndOfStreamException(); return new Guid(bytes); }
}
