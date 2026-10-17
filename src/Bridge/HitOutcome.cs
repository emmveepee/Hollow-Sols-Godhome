using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace YiGodhome.Bridge;

public enum HitKind : byte { Melee = 1, Bow = 2, Talisman = 3, UnboundedCounter = 4, TaiChiKick = 5, ProjectileResolved = 6, ProjectileReflected = 7, StrikeParried = 8, StrikeDeflected = 9 }
public enum HitDisposition : byte { Applied = 1, Refused = 2, Stale = 3 }
public sealed class HitOutcome
{
    public Guid Session, Encounter;
    public long Sequence, GeometryFrame, HostTick, NativeFrame;
    public long AttackId, TargetActivation;
    public int Entity, Dealer;
    public HitKind Kind;
    public float Damage, Direction;
    public bool Same(HitOutcome b) => Session == b.Session && Encounter == b.Encounter && Sequence == b.Sequence && GeometryFrame == b.GeometryFrame
        && HostTick == b.HostTick && NativeFrame == b.NativeFrame && AttackId == b.AttackId && TargetActivation == b.TargetActivation && Entity == b.Entity && Dealer == b.Dealer && Kind == b.Kind && Damage == b.Damage && Direction == b.Direction;
}
public sealed class HitAck
{
    public Guid Session;
    public long Sequence;
    public HitDisposition Disposition;
    public int Health;
    public bool ParryPause, TargetKilled;
}
public static class HitCodec
{
    public const int OutcomeSize = 103, AckSize = 37;
    private static Guid ReadId(BinaryReader r) { var b = r.ReadBytes(16); if (b.Length != 16) throw new EndOfStreamException(); return new Guid(b); }
    public static void Write(Stream stream, HitOutcome h)
    {
        Validate(h); using var w = new WireWriter(stream);
        w.Write(0x48494759u); w.Write((ushort)6); w.Write(h.Session.ToByteArray()); w.Write(h.Encounter.ToByteArray());
        w.Write(h.Sequence); w.Write(h.GeometryFrame); w.Write(h.HostTick); w.Write(h.NativeFrame);
        w.Write(h.AttackId); w.Write(h.TargetActivation);
        w.Write(h.Entity); w.Write(h.Dealer); w.Write((byte)h.Kind); w.Write(h.Damage); w.Write(h.Direction); w.Flush();
    }
    public static HitOutcome Read(Stream stream)
    {
        using var r = new BinaryReader(stream, Encoding.UTF8, true);
        if (r.ReadUInt32() != 0x48494759u || r.ReadUInt16() != 6) throw new InvalidDataException("Hit header mismatch.");
        var h = new HitOutcome { Session = ReadId(r), Encounter = ReadId(r), Sequence = r.ReadInt64(), GeometryFrame = r.ReadInt64(), HostTick = r.ReadInt64(), NativeFrame = r.ReadInt64(), AttackId = r.ReadInt64(), TargetActivation = r.ReadInt64(), Entity = r.ReadInt32(), Dealer = r.ReadInt32(), Kind = (HitKind)r.ReadByte(), Damage = r.ReadSingle(), Direction = r.ReadSingle() };
        Validate(h); return h;
    }
    public static void WriteAck(Stream stream, HitAck a)
    {
        ValidateAck(a); using var w = new WireWriter(stream);
        w.Write(0x41494759u); w.Write((ushort)3); w.Write(a.Session.ToByteArray()); w.Write(a.Sequence); w.Write((byte)a.Disposition); w.Write(a.Health); w.Write(a.ParryPause); w.Write(a.TargetKilled); w.Flush();
    }
    public static HitAck ReadAck(Stream stream)
    {
        using var r = new BinaryReader(stream, Encoding.UTF8, true);
        if (r.ReadUInt32() != 0x41494759u || r.ReadUInt16() != 3) throw new InvalidDataException("Hit acknowledgement header mismatch.");
        var a = new HitAck { Session = ReadId(r), Sequence = r.ReadInt64(), Disposition = (HitDisposition)r.ReadByte(), Health = r.ReadInt32() };
        byte pause = r.ReadByte(), killed = r.ReadByte(); if (pause > 1 || killed > 1) throw new InvalidDataException("Invalid hit acknowledgement flags."); a.ParryPause = pause == 1; a.TargetKilled = killed == 1; ValidateAck(a); return a;
    }
    public static void Validate(HitOutcome h)
    {
        if (h.Session == Guid.Empty || h.Encounter == Guid.Empty || h.Sequence <= 0 || h.GeometryFrame < 0 || h.HostTick <= 0 || h.NativeFrame < 0 || h.Entity == 0 || h.Dealer == 0
            || h.Kind < HitKind.Melee || h.Kind > HitKind.StrikeDeflected || h.AttackId < 0 || h.TargetActivation < 0
            || float.IsNaN(h.Damage) || (h.Kind >= HitKind.UnboundedCounter ? h.Damage != 0 || h.AttackId == 0 : h.Damage <= 0) || h.Damage > 100000
            || float.IsNaN(h.Direction) || h.Direction < 0 || h.Direction >= 360) throw new InvalidDataException("Invalid native hit outcome.");
    }
    public static void ValidateAck(HitAck a)
    {
        if (a.Session == Guid.Empty || a.Sequence <= 0 || a.Disposition < HitDisposition.Applied || a.Disposition > HitDisposition.Stale || a.Health < 0 || a.Health > 1000000 || a.ParryPause && a.Disposition != HitDisposition.Refused || a.TargetKilled && (a.Disposition != HitDisposition.Applied || a.Health != 0))
            throw new InvalidDataException("Invalid hit acknowledgement.");
    }
}

// Main-thread ledger. Callback failure poisons the ledger: an uncertain native
// side effect must never be retried. Cached completed acknowledgements are safe.
public sealed class HitLedger
{
    private static HitOutcome Copy(HitOutcome h) { using var s = new MemoryStream(); HitCodec.Write(s, h); s.Position = 0; return HitCodec.Read(s); }
    private static HitAck Copy(HitAck a) => new HitAck { Session = a.Session, Sequence = a.Sequence, Disposition = a.Disposition, Health = a.Health, ParryPause = a.ParryPause, TargetKilled = a.TargetKilled };
    private readonly Guid session, encounter;
    private readonly long geometryFrame;
    private readonly int entity;
    private readonly Func<HitOutcome, bool>? additionalTarget;
    private readonly Dictionary<long, (HitOutcome request, HitAck ack)> cache = new Dictionary<long, (HitOutcome, HitAck)>();
    private long sequence;
    private bool poisoned;
    public HitLedger(Guid session, Guid encounter, long geometryFrame, int entity, Func<HitOutcome, bool>? additionalTarget = null)
    { this.additionalTarget = additionalTarget; this.session = session; this.encounter = encounter; this.geometryFrame = geometryFrame; this.entity = entity; }
    public HitAck Process(HitOutcome h, long hostNow, long frequency, int currentHealth, Func<HitOutcome, HitAck> apply)
    {
        if (poisoned) throw new InvalidOperationException("Hit ledger stopped after uncertain native result.");
        HitCodec.Validate(h);
        h = Copy(h);
        if (h.Session != session || h.Encounter != encounter || h.GeometryFrame != geometryFrame) throw new InvalidDataException("Hit target identity mismatch.");
        if (cache.TryGetValue(h.Sequence, out var old))
        {
            if (!h.Same(old.request)) throw new InvalidDataException("Duplicate hit identity changed payload.");
            return Copy(old.ack);
        }
        // A cached exact receipt remains valid after native body destruction or
        // tombstone expiry. It returns its original ack and never reapplies.
        if(h.Entity!=entity && additionalTarget?.Invoke(h)!=true)throw new InvalidDataException("Hit target identity mismatch.");
        if (h.Sequence != sequence + 1) throw new InvalidDataException("Hit sequence gap or expired replay.");
        if (frequency <= 0 || hostNow < h.HostTick) throw new InvalidDataException("Invalid echoed host clock.");
        HitAck ack;
        poisoned = true;
        if ((hostNow - h.HostTick) * 1000.0 / frequency > 250)
            ack = new HitAck { Session = session, Sequence = h.Sequence, Disposition = HitDisposition.Stale, Health = currentHealth };
        else ack = apply(Copy(h));
        HitCodec.ValidateAck(ack);
        if (ack.Session != session || ack.Sequence != h.Sequence) throw new InvalidDataException("Native hit result identity mismatch.");
        // Own copies prevent a caller from changing a cached event after acceptance.
        cache.Add(h.Sequence, (h, Copy(ack)));
        sequence = h.Sequence; cache.Remove(sequence - 64); poisoned = false; return Copy(ack);
    }
}
