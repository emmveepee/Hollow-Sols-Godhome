using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace YiGodhome.Bridge;

public enum Role : byte { HollowKnight = 1, NineSols = 2 }
public enum MessageKind : byte { Hello = 1, Snapshot = 2 }
public enum EncounterPhase : byte { Preparing = 1, Active = 2, Victory = 3, Defeated = 4, Restoring = 5, Ready = 6, Returned = 7, Advancing = 8, SequenceResults = 9 }

public sealed class EncounterStatus
{
    public readonly Guid Id;
    public readonly long GeometryFrame;
    public readonly EncounterPhase Phase;
    public EncounterStatus(Guid id, long geometryFrame, EncounterPhase phase) { Id = id; GeometryFrame = geometryFrame; Phase = phase; }
    public bool Matches(EncounterStatus? other) => other != null && Id == other.Id && GeometryFrame == other.GeometryFrame;
}

public sealed class PlayerVitals
{
    public readonly float Health, MaxHealth, Injury, Chi, Ammo;
    public readonly int Potions;
    public readonly bool Dead, TalismanActive;
    public readonly bool CounterCharging, CounterReady, EntryActive;
    public PlayerVitals(float health, float maxHealth, float injury, float chi, float ammo, int potions, bool dead, bool talismanActive, bool counterCharging = false, bool counterReady = false, bool entryActive = false)
    { Health = health; MaxHealth = maxHealth; Injury = injury; Chi = chi; Ammo = ammo; Potions = potions; Dead = dead; TalismanActive = talismanActive; CounterCharging = counterCharging; CounterReady = counterReady; EntryActive = entryActive; }
}

public sealed class Pose
{
    public readonly MeleeWindow[] Melee;
    public readonly float[][] ParryContacts;
    public readonly Guid GeometrySession;
    public readonly long GeometryFrame;
    public readonly float X, Y, VelocityX, VelocityY, Width, Height, JumpReach;
    public readonly bool Grounded;
    public readonly PlayerVitals? Vitals;
    public Pose(Guid geometrySession, long geometryFrame, float x, float y, float velocityX, float velocityY, float width, float height, bool grounded, PlayerVitals? vitals = null, MeleeWindow[]? melee = null, float jumpReach = 0, float[][]? parryContacts = null)
    { GeometrySession = geometrySession; GeometryFrame = geometryFrame; X = x; Y = y; VelocityX = velocityX; VelocityY = velocityY; Width = width; Height = height; JumpReach = jumpReach; Grounded = grounded; Vitals = vitals; Melee = melee ?? Array.Empty<MeleeWindow>(); ParryContacts = parryContacts ?? Array.Empty<float[]>(); }
}

public sealed class MeleeWindow
{
    public int Collider;
    public long Activation;
    public long Swing;
    public float X, Y, Width, Height, Direction;
}

public sealed class HostAudioState
{
    public readonly float Master, Effects;
    public readonly bool Paused;
    public HostAudioState(float master, float effects, bool paused) { Master = master; Effects = effects; Paused = paused; }
}

public sealed class Packet
{
    public MessageKind Kind;
    public Role Role;
    public Guid Session;
    public long Sequence;
    public long Frame;
    public long FrameTick;
    public long SentTick;
    public long EchoTick;
    public long Frequency = Stopwatch.Frequency;
    public uint Capabilities = 1; // 1 = telemetry; 3 adds NS pose; 7 adds HK controls. No damage.
    public HostAudioState? Audio;
    public Pose? Pose;
    public EncounterStatus? Encounter;
    public InputCommand[] Inputs = Array.Empty<InputCommand>();
    public long InputAck;
    public Guid DetailCapture; // Host request / NS active acknowledgement; independent of gameplay.
    public byte[] NativeBindings = Array.Empty<byte>(); // NS native gameplay bindings; changed snapshots only.
}

public static class Protocol
{
    public const ushort Version = 52;
    public const int PacketSize = 219; // Empty inputs/windows/bindings/optional parry contact contours.
    public const int MaxBindingsBytes = 16384;
    public const int MaxInputs = 16;
    private const uint Magic = 0x42484759;

    public static void Write(Stream stream, Packet packet)
    {
        Validate(packet);
        using var writer = new WireWriter(stream);
        writer.Write(Magic); writer.Write(Version);
        writer.Write((byte)packet.Kind); writer.Write((byte)packet.Role);
        writer.Write(packet.Session.ToByteArray()); writer.Write(packet.Sequence);
        writer.Write(packet.Frame); writer.Write(packet.FrameTick); writer.Write(packet.SentTick);
        writer.Write(packet.EchoTick); writer.Write(packet.Frequency); writer.Write(packet.Capabilities);
        var p = packet.Pose;
        writer.Write((byte)(p == null ? 0 : p.Grounded ? 3 : 1));
        writer.Write((p?.GeometrySession ?? Guid.Empty).ToByteArray()); writer.Write(p?.GeometryFrame ?? 0);
        writer.Write(p?.X ?? 0); writer.Write(p?.Y ?? 0); writer.Write(p?.VelocityX ?? 0); writer.Write(p?.VelocityY ?? 0);
        writer.Write(p?.Width ?? 0); writer.Write(p?.Height ?? 0); writer.Write(p?.JumpReach ?? 0);
        var v = p?.Vitals;
        writer.Write((byte)(v == null ? 0 : 1 | (v.Dead ? 2 : 0) | (v.TalismanActive ? 4 : 0) | (v.CounterCharging ? 8 : 0) | (v.CounterReady ? 16 : 0) | (v.EntryActive ? 32 : 0)));
        writer.Write(v?.Health ?? 0); writer.Write(v?.MaxHealth ?? 0); writer.Write(v?.Injury ?? 0); writer.Write(v?.Chi ?? 0); writer.Write(v?.Ammo ?? 0); writer.Write(v?.Potions ?? 0);
        writer.Write((byte)(packet.Encounter?.Phase ?? 0)); writer.Write((packet.Encounter?.Id ?? Guid.Empty).ToByteArray()); writer.Write(packet.Encounter?.GeometryFrame ?? 0);
        writer.Write((byte)(packet.Audio == null ? 0 : packet.Audio.Paused ? 3 : 1)); writer.Write(packet.Audio?.Master ?? 0); writer.Write(packet.Audio?.Effects ?? 0);
        writer.Write((byte)(p?.Melee.Length ?? 0));
        foreach (var window in p?.Melee ?? Array.Empty<MeleeWindow>()) {
            writer.Write(window.Collider); writer.Write(window.Activation); writer.Write(window.Swing);
            writer.Write(window.X); writer.Write(window.Y); writer.Write(window.Width); writer.Write(window.Height);
            writer.Write(window.Direction);
        }
        writer.Write((byte)(p?.ParryContacts.Length ?? 0));
        foreach (var points in p?.ParryContacts ?? Array.Empty<float[]>()) {
            writer.Write((byte)(points.Length / 2));
            foreach (float coordinate in points) writer.Write(coordinate);
        }
        writer.Write(packet.DetailCapture.ToByteArray());
        writer.Write(packet.NativeBindings.Length); writer.Write(packet.NativeBindings);
        writer.Write(packet.InputAck); writer.Write((byte)packet.Inputs.Length);
        foreach (var input in packet.Inputs)
        {
            writer.Write(input.Sequence); writer.Write(input.Frame); writer.Write(input.Tick);
            writer.Write(input.Controls.X); writer.Write(input.Controls.Y); writer.Write((uint)input.Controls.Buttons);
        }
        writer.Flush();
    }

    public static Packet Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, true);
        if (reader.ReadUInt32() != Magic || reader.ReadUInt16() != Version) throw new InvalidDataException("Protocol magic/version mismatch.");
        var packet = new Packet { Kind = (MessageKind)reader.ReadByte(), Role = (Role)reader.ReadByte() };
        var session = reader.ReadBytes(16);
        if (session.Length != 16) throw new EndOfStreamException();
        packet.Session = new Guid(session); packet.Sequence = reader.ReadInt64();
        packet.Frame = reader.ReadInt64(); packet.FrameTick = reader.ReadInt64(); packet.SentTick = reader.ReadInt64();
        packet.EchoTick = reader.ReadInt64(); packet.Frequency = reader.ReadInt64(); packet.Capabilities = reader.ReadUInt32();
        byte flags = reader.ReadByte();
        var geometryId = reader.ReadBytes(16); if (geometryId.Length != 16) throw new EndOfStreamException();
        var geometrySession = new Guid(geometryId); long geometryFrame = reader.ReadInt64();
        float x = reader.ReadSingle(), y = reader.ReadSingle(), vx = reader.ReadSingle(), vy = reader.ReadSingle(), width = reader.ReadSingle(), height = reader.ReadSingle(), jumpReach = reader.ReadSingle();
        byte vitalFlags = reader.ReadByte(); float health = reader.ReadSingle(), maxHealth = reader.ReadSingle(), injury = reader.ReadSingle(), chi = reader.ReadSingle(), ammo = reader.ReadSingle(); int potions = reader.ReadInt32();
        PlayerVitals? vitals = null;
        if ((vitalFlags & 1) != 0 && vitalFlags < 64 && ((vitalFlags & 16) == 0 || (vitalFlags & 8) != 0)) vitals = new PlayerVitals(health, maxHealth, injury, chi, ammo, potions, (vitalFlags & 2) != 0, (vitalFlags & 4) != 0, (vitalFlags & 8) != 0, (vitalFlags & 16) != 0, (vitalFlags & 32) != 0);
        else if (vitalFlags != 0 || health != 0 || maxHealth != 0 || injury != 0 || chi != 0 || ammo != 0 || potions != 0) throw new InvalidDataException("Invalid native vitals flags/payload.");
        if (flags == 1 || flags == 3) packet.Pose = new Pose(geometrySession, geometryFrame, x, y, vx, vy, width, height, flags == 3, vitals, jumpReach: jumpReach);
        else if (flags != 0 || geometrySession != Guid.Empty || geometryFrame != 0 || x != 0 || y != 0 || vx != 0 || vy != 0 || width != 0 || height != 0 || jumpReach != 0 || vitals != null)
            throw new InvalidDataException("Invalid pose flags or absent payload.");
        byte phase = reader.ReadByte(); var encounterBytes = reader.ReadBytes(16); if (encounterBytes.Length != 16) throw new EndOfStreamException();
        var encounterId = new Guid(encounterBytes); long encounterFrame = reader.ReadInt64();
        if (phase != 0) packet.Encounter = new EncounterStatus(encounterId, encounterFrame, (EncounterPhase)phase);
        else if (encounterId != Guid.Empty || encounterFrame != 0) throw new InvalidDataException("Absent encounter has a payload.");
        byte audioFlags = reader.ReadByte(); float master = reader.ReadSingle(), effects = reader.ReadSingle();
        if (audioFlags == 1 || audioFlags == 3) packet.Audio = new HostAudioState(master, effects, audioFlags == 3);
        else if (audioFlags != 0 || master != 0 || effects != 0) throw new InvalidDataException("Invalid audio payload.");
        int meleeCount = reader.ReadByte();
        if (meleeCount > 8 || meleeCount != 0 && packet.Pose == null) throw new InvalidDataException("Invalid melee window count.");
        var melee = meleeCount == 0 ? Array.Empty<MeleeWindow>() : new MeleeWindow[meleeCount];
        for (int i = 0; i < meleeCount; i++) melee[i] = new MeleeWindow { Collider = reader.ReadInt32(), Activation = reader.ReadInt64(), Swing = reader.ReadInt64(),
            X = reader.ReadSingle(), Y = reader.ReadSingle(), Width = reader.ReadSingle(), Height = reader.ReadSingle(), Direction = reader.ReadSingle() };
        int parryCount = reader.ReadByte();
        if (parryCount > 8 || parryCount != 0 && packet.Pose == null) throw new InvalidDataException("Invalid parry contact contour count.");
        var contacts = parryCount == 0 ? Array.Empty<float[]>() : new float[parryCount][];
        for (int i = 0; i < parryCount; i++) {
            int count = reader.ReadByte();
            if (count < 3 || count > 64) throw new InvalidDataException("Invalid parry contact vertex count.");
            contacts[i] = new float[count * 2];
            for (int j = 0; j < contacts[i].Length; j++) contacts[i][j] = reader.ReadSingle();
        }
        if (packet.Pose is Pose nativePose) packet.Pose = new Pose(nativePose.GeometrySession, nativePose.GeometryFrame, nativePose.X, nativePose.Y,
            nativePose.VelocityX, nativePose.VelocityY, nativePose.Width, nativePose.Height, nativePose.Grounded, nativePose.Vitals, melee, nativePose.JumpReach, contacts);
        var detail = reader.ReadBytes(16); if (detail.Length != 16) throw new EndOfStreamException();
        packet.DetailCapture = new Guid(detail);
        int bindingsLength = reader.ReadInt32();
        if (bindingsLength < 0 || bindingsLength > MaxBindingsBytes) throw new InvalidDataException("Native binding payload exceeds limit.");
        packet.NativeBindings = reader.ReadBytes(bindingsLength);
        if (packet.NativeBindings.Length != bindingsLength) throw new EndOfStreamException();
        packet.InputAck = reader.ReadInt64(); int inputCount = reader.ReadByte();
        if (inputCount > MaxInputs) throw new InvalidDataException("Input batch exceeds limit.");
        packet.Inputs = new InputCommand[inputCount];
        for (int i = 0; i < inputCount; i++) packet.Inputs[i] = new InputCommand(reader.ReadInt64(), reader.ReadInt64(), reader.ReadInt64(),
            new Controls(reader.ReadSingle(), reader.ReadSingle(), (Buttons)reader.ReadUInt32()));
        Validate(packet);
        return packet;
    }

    private static void Validate(Packet packet)
    {
        if (packet.NativeBindings == null || packet.NativeBindings.Length > MaxBindingsBytes
            || packet.NativeBindings.Length > 0 && (packet.Role != Role.NineSols || packet.Kind != MessageKind.Snapshot || packet.Capabilities != 7))
            throw new InvalidDataException("Invalid native binding authority.");
        if (packet.Kind != MessageKind.Hello && packet.Kind != MessageKind.Snapshot
            || packet.Role != Role.HollowKnight && packet.Role != Role.NineSols
            || packet.Session == Guid.Empty || packet.Sequence < 0 || packet.Frame < 0
            || packet.FrameTick <= 0 || packet.SentTick < packet.FrameTick || packet.EchoTick < 0
            || packet.Frequency <= 0 || packet.Frequency > 1000000000000 || packet.Capabilities != 1 && packet.Capabilities != 3 && packet.Capabilities != 7
            || packet.Kind == MessageKind.Hello && packet.Sequence != 0
            || packet.Kind == MessageKind.Snapshot && packet.Sequence == 0)
            throw new InvalidDataException("Invalid telemetry packet.");
        if (packet.Audio != null && (packet.Role != Role.HollowKnight || packet.Kind != MessageKind.Snapshot || packet.Capabilities != 7
            || !FiniteBound(packet.Audio.Master, 1) || packet.Audio.Master < 0 || !FiniteBound(packet.Audio.Effects, 1) || packet.Audio.Effects < 0)) throw new InvalidDataException("Invalid host audio state.");
        var p = packet.Pose;
        if (p != null) {
            if (p.ParryContacts.Length > 8 || p.ParryContacts.Length != 0 && packet.Capabilities != 7) throw new InvalidDataException("Invalid parry debug authority/capacity.");
            foreach (var contour in p.ParryContacts) {
                if (contour == null || contour.Length < 6 || contour.Length > 128 || contour.Length % 2 != 0) throw new InvalidDataException("Invalid parry contact contour.");
                foreach (float coordinate in contour) if (!FiniteBound(coordinate, 100000)) throw new InvalidDataException("Invalid parry contact coordinate.");
            }
            if (p.Melee.Length > 8) throw new InvalidDataException("Melee window capacity exceeded.");
            for (int i = 0; i < p.Melee.Length; i++) {
                var w = p.Melee[i];
                if (w == null || w.Collider == 0 || w.Activation <= 0 || w.Swing < 0
                    || !FiniteBound(w.X, 100000) || !FiniteBound(w.Y, 100000) || !FiniteBound(w.Width, 100) || !FiniteBound(w.Height, 100)
                    || w.Width <= 0 || w.Height <= 0 || !FiniteBound(w.Direction,360) || w.Direction<0) throw new InvalidDataException("Invalid melee window.");
                for (int j = 0; j < i; j++) if (p.Melee[j].Collider == w.Collider) throw new InvalidDataException("Duplicate melee collider.");
            }
        }
        if (packet.DetailCapture != Guid.Empty && (packet.Capabilities != 7 || packet.Kind != MessageKind.Snapshot))
            throw new InvalidDataException("Invalid detailed capture authority.");
        var e = packet.Encounter;
        // Ready acknowledges the retired arena while a newer hub generation already owns Yi.
        // Active/restoring encounters must still carry their exact arena geometry.
        if (e != null && (packet.Capabilities != 7 || packet.Kind != MessageKind.Snapshot || e.Id == Guid.Empty || e.GeometryFrame < 0
            || e.Phase < EncounterPhase.Preparing || e.Phase > EncounterPhase.SequenceResults
            || packet.Role == Role.HollowKnight && (e.Phase == EncounterPhase.Restoring || e.Phase == EncounterPhase.Ready)
            || packet.Role == Role.NineSols && (e.Phase == EncounterPhase.Preparing || e.Phase == EncounterPhase.Victory || e.Phase == EncounterPhase.Returned)
            || p != null && p.GeometryFrame != e.GeometryFrame
                && !((e.Phase == EncounterPhase.Ready || e.Phase == EncounterPhase.Advancing) && p.GeometryFrame > e.GeometryFrame))) throw new InvalidDataException("Invalid encounter state/authority.");
        if (p != null && ((packet.Capabilities & 2) == 0 || packet.Role != Role.NineSols || packet.Kind != MessageKind.Snapshot
            || p.GeometrySession != packet.Session || p.GeometryFrame < 0 || !FiniteBound(p.X, 100000) || !FiniteBound(p.Y, 100000)
            || !FiniteBound(p.VelocityX, 100000) || !FiniteBound(p.VelocityY, 100000) || !FiniteBound(p.Width, 100) || !FiniteBound(p.Height, 100)
            || p.Width <= 0 || p.Height <= 0 || !FiniteBound(p.JumpReach, 1000) || p.JumpReach < 0)) throw new InvalidDataException("Invalid native pose.");
        var v = p?.Vitals;
        if (v != null && (!FiniteBound(v.MaxHealth, 100000) || v.MaxHealth <= 0 || !FiniteBound(v.Health, v.MaxHealth) || v.Health < 0
            || !FiniteBound(v.Injury, v.MaxHealth) || v.Injury < 0 || !FiniteBound(v.Chi, 100) || v.Chi < 0 || !FiniteBound(v.Ammo, 100) || v.Ammo < 0
            || v.Potions < 0 || v.Potions > 100 || v.CounterReady && !v.CounterCharging)) throw new InvalidDataException("Invalid native vitals.");
        if (packet.Inputs.Length > MaxInputs || packet.InputAck < 0
            || packet.Capabilities != 7 && (packet.Inputs.Length != 0 || packet.InputAck != 0)
            || packet.Kind == MessageKind.Hello && (packet.Inputs.Length != 0 || packet.InputAck != 0)
            || packet.Role == Role.NineSols && packet.Inputs.Length != 0 || packet.Role == Role.HollowKnight && packet.InputAck != 0)
            throw new InvalidDataException("Invalid input capability/role.");
        long previous = 0;
        foreach (var input in packet.Inputs)
        {
            if (input.Sequence <= 0 || previous != 0 && input.Sequence != previous + 1 || input.Frame < 0 || input.Frame > packet.Frame
                || input.Tick <= 0 || input.Tick > packet.SentTick || !FiniteBound(input.Controls.X, 1) || !FiniteBound(input.Controls.Y, 1)
                || ((uint)input.Controls.Buttons & ~67108863u) != 0 || !MenuPointer.IsExclusive(input.Controls)) throw new InvalidDataException("Invalid input transition.");
            previous = input.Sequence;
        }
    }
    private static bool FiniteBound(float value, float limit) => !float.IsNaN(value) && !float.IsInfinity(value) && Math.Abs(value) <= limit;
}
