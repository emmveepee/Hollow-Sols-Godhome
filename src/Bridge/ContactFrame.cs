using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace YiGodhome.Bridge;

public enum BossTitleVariant : byte { Native, NailmasterOro, BrothersOroMato, MantisLords, SistersOfBattle }

// One audited axis-aligned native host body collider. No hit verdicts or health writes.
public sealed class ContactFrame
{
    public Guid Session, Encounter;
    public long GeometryFrame, NativeFrame, Sequence, CaptureTick, SentTick;
    public long Frequency = Stopwatch.Frequency;
    public double RoundTripMs;
    public int BarHealth=-1, BarMaxHealth=-1; // Optional display values; raw health remains authoritative.
    public int Entity, Collider, Damage, Health, MaxHealth, Difficulty;
    public BossTitleVariant BossTitle;
    public bool BossBarsReady; // Host title/HUD authorization for native widget entry.
    public bool Active;
    public bool HasHazardCheckpoint;
    public float HazardCheckpointX, HazardCheckpointY; // Native HK ground point, not Knight pivot.
    public long AttackId;
    public bool Crimson, Windup;
    public bool Kickable;
    public bool KickWindup; // Cue only; does not enable native weakness/kick collision.
    public bool QuickWarning; // Complete this native cue within120ms; no collision/timing authority.
    public long CrimsonSequence; // Presentation lifetime, independent of strike/counter identity.
    public float CrimsonCharge; // Initial native anticipation progress; 1 means released.
    public bool CrimsonRight;
    public bool StationaryFlame; // Authored NKG balloon visual, independent of strike activations.
    public float X, Y, Width, Height;
    public int HurtCollider;
    public bool HurtActive;
    public float HurtX, HurtY, HurtWidth, HurtHeight;
    public long TargetGeneration; // Damageable body activation, independent of AttackId and encounter completion.
    public bool SecondaryTarget; // Local projection role; not inferred from activation numbering.
    public SummonedTarget[] Summons = Array.Empty<SummonedTarget>();
    public int[] Notches = Array.Empty<int>();
    public AttackVolume[] Volumes = Array.Empty<AttackVolume>();
}

public static class ContactCodec
{
    public const int Size = 216; // Includes title-authorized native boss bar entry.
    public static void Write(Stream stream, ContactFrame f)
    {
        Validate(f);
        using var w = new WireWriter(stream);
        w.Write(0x43484759u); w.Write((ushort)38); w.Write(f.Session.ToByteArray()); w.Write(f.Encounter.ToByteArray());
        w.Write(f.GeometryFrame); w.Write(f.NativeFrame); w.Write(f.Sequence); w.Write(f.CaptureTick); w.Write(f.SentTick); w.Write(f.Frequency); w.Write(f.RoundTripMs);
        w.Write(f.Entity); w.Write(f.Collider); w.Write(f.Damage); w.Write(f.Health); w.Write(f.Active);
        w.Write(f.X); w.Write(f.Y); w.Write(f.Width); w.Write(f.Height);
        w.Write(f.AttackId); w.Write(f.Crimson); w.Write(f.Windup);
        w.Write(f.HurtCollider); w.Write(f.HurtActive); w.Write(f.HurtX); w.Write(f.HurtY); w.Write(f.HurtWidth); w.Write(f.HurtHeight); AttackVolumes.Write(w, f.Volumes); w.Write(f.MaxHealth);
        w.Write(f.Kickable); w.Write(f.Difficulty); w.Write(f.TargetGeneration); SummonedTargets.Write(w, f.Summons); w.Write(f.KickWindup); w.Write(f.QuickWarning);
        w.Write(f.CrimsonSequence); w.Write(f.CrimsonCharge); w.Write(f.CrimsonRight); HealthNotches.Write(w, f.Notches); w.Write(f.StationaryFlame); w.Write((byte)f.BossTitle);
        w.Write(f.HasHazardCheckpoint); w.Write(f.HazardCheckpointX); w.Write(f.HazardCheckpointY); w.Write(f.BarHealth); w.Write(f.BarMaxHealth); w.Write(f.BossBarsReady); w.Flush();
    }
    public static ContactFrame Read(Stream stream)
    {
        using var r = new BinaryReader(stream, Encoding.UTF8, true);
        if (r.ReadUInt32() != 0x43484759u || r.ReadUInt16() != 38) throw new InvalidDataException("Contact header mismatch.");
        Guid Id() { var b = r.ReadBytes(16); if (b.Length != 16) throw new EndOfStreamException(); return new Guid(b); }
        var f = new ContactFrame { Session = Id(), Encounter = Id(), GeometryFrame = r.ReadInt64(), NativeFrame = r.ReadInt64(), Sequence = r.ReadInt64(), CaptureTick = r.ReadInt64(), SentTick = r.ReadInt64(), Frequency = r.ReadInt64(), RoundTripMs = r.ReadDouble(), Entity = r.ReadInt32(), Collider = r.ReadInt32(), Damage = r.ReadInt32(), Health = r.ReadInt32() };
        byte active = r.ReadByte(); if (active > 1) throw new InvalidDataException("Invalid contact active flag."); f.Active = active == 1;
        f.X = r.ReadSingle(); f.Y = r.ReadSingle(); f.Width = r.ReadSingle(); f.Height = r.ReadSingle();
        f.AttackId = r.ReadInt64(); byte crimson = r.ReadByte(), windup = r.ReadByte();
        if (crimson > 1 || windup > 1) throw new InvalidDataException("Invalid attack flags.");
        f.Crimson = crimson == 1; f.Windup = windup == 1;
        f.HurtCollider = r.ReadInt32(); byte hurt = r.ReadByte(); if (hurt > 1) throw new InvalidDataException("Invalid hurtbox active flag."); f.HurtActive = hurt == 1;
        f.HurtX = r.ReadSingle(); f.HurtY = r.ReadSingle(); f.HurtWidth = r.ReadSingle(); f.HurtHeight = r.ReadSingle(); f.Volumes = AttackVolumes.Read(r); f.MaxHealth = r.ReadInt32();
        byte kick = r.ReadByte(); if (kick > 1) throw new InvalidDataException("Invalid kick flag.");
        f.Kickable = kick == 1; f.Difficulty = r.ReadInt32(); f.TargetGeneration = r.ReadInt64(); f.Summons = SummonedTargets.Read(r);
        byte kickWindup = r.ReadByte(); if (kickWindup > 1) throw new InvalidDataException("Invalid kick windup flag."); f.KickWindup = kickWindup == 1;
        byte quick = r.ReadByte(); if (quick > 1) throw new InvalidDataException("Invalid quick warning flag."); f.QuickWarning = quick == 1;
        f.CrimsonSequence = r.ReadInt64(); f.CrimsonCharge = r.ReadSingle();
        byte right = r.ReadByte(); if (right > 1) throw new InvalidDataException("Invalid sequence facing."); f.CrimsonRight = right == 1; f.Notches = HealthNotches.Read(r);
        byte flame=r.ReadByte(); if(flame>1)throw new InvalidDataException("Invalid flame presentation flag."); f.StationaryFlame=flame==1;
        f.BossTitle=(BossTitleVariant)r.ReadByte();
        byte checkpoint=r.ReadByte(); if(checkpoint>1)throw new InvalidDataException("Invalid native hazard checkpoint flag.");
        f.HasHazardCheckpoint=checkpoint==1; f.HazardCheckpointX=r.ReadSingle(); f.HazardCheckpointY=r.ReadSingle(); f.BarHealth=r.ReadInt32(); f.BarMaxHealth=r.ReadInt32();
        byte bars=r.ReadByte();if(bars>1)throw new InvalidDataException("Invalid boss bar entry authorization.");f.BossBarsReady=bars==1;
        Validate(f); return f;
    }
    internal static void ValidateBar(int health,int maximum) {
        if(!(health==-1 && maximum==-1) && (health<0 || maximum<=0 || maximum>1000000 || health>maximum))
            throw new InvalidDataException("Invalid presentation health override.");
    }
    public static void Validate(ContactFrame f)
    {
        if((byte)f.BossTitle>(byte)BossTitleVariant.SistersOfBattle) throw new InvalidDataException("Invalid authored boss title.");
        if(f.StationaryFlame && (!f.Crimson || f.CrimsonSequence==0 || f.Kickable || f.KickWindup)) throw new InvalidDataException("Stationary flame requires a crimson sequence.");
        ValidateBar(f.BarHealth,f.BarMaxHealth);
        HealthNotches.Validate(f.Notches, f.MaxHealth); AttackVolumes.Validate(f.Volumes); SummonedTargets.Validate(f.Summons);
        if (f.Difficulty < 0 || f.Difficulty > 2 || f.TargetGeneration < 0) throw new InvalidDataException("Invalid encounter difficulty or target activation.");
        foreach (var target in f.Summons) if (target.Entity == f.Entity) throw new InvalidDataException("Summon aliases boss identity.");
        if (f.Kickable && (f.AttackId <= 0 || f.Crimson || !f.Active && !Array.Exists(f.Volumes, v => v.Active && v.ActionId == f.AttackId && (v.Traits & AttackTraits.Kickable) != 0))) throw new InvalidDataException("Invalid kick profile.");
        bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        if(!Finite(f.HazardCheckpointX) || !Finite(f.HazardCheckpointY) || Math.Abs(f.HazardCheckpointX)>100000 || Math.Abs(f.HazardCheckpointY)>100000
            || !f.HasHazardCheckpoint && (f.HazardCheckpointX!=0 || f.HazardCheckpointY!=0))
            throw new InvalidDataException("Invalid native hazard checkpoint.");
        if (f.CrimsonSequence < 0 || !Finite(f.CrimsonCharge) || f.CrimsonCharge < 0 || f.CrimsonCharge > 1
            || f.CrimsonSequence == 0 && (f.CrimsonCharge != 0 || f.CrimsonRight)
            || f.CrimsonSequence > 0 && (!(f.Crimson || f.Kickable || f.KickWindup) || f.AttackId <= 0))
            throw new InvalidDataException("Invalid prolonged crimson presentation.");
        if (f.QuickWarning && (f.AttackId <= 0 || !(f.Crimson || f.KickWindup || f.Kickable))) throw new InvalidDataException("Quick warning requires a native cue action.");
        if (f.AttackId < 0 || f.KickWindup && (f.AttackId <= 0 || f.Active || f.Kickable || f.Crimson || f.Windup) || f.Crimson && f.AttackId == 0 || f.Windup && (!f.Crimson || f.Active)
            || f.Session == Guid.Empty || f.Encounter == Guid.Empty || f.GeometryFrame < 0 || f.NativeFrame < 0 || f.Sequence < 1
            || f.CaptureTick < 0 || f.SentTick < f.CaptureTick || f.Frequency <= 0 || double.IsNaN(f.RoundTripMs) || f.RoundTripMs < 0 || f.RoundTripMs > 60000
            || f.Entity == 0 || f.Collider == 0 || f.Damage < 0 || f.Damage > 100 || f.Health < 0 || f.MaxHealth < 1 || f.MaxHealth > 1000000 || f.Health > f.MaxHealth
            || !Finite(f.X) || !Finite(f.Y) || Math.Abs(f.X) > 100000 || Math.Abs(f.Y) > 100000
            || !Finite(f.Width) || !Finite(f.Height) || f.Width <= 0 || f.Height <= 0 || f.Width > 1000 || f.Height > 1000
            || f.HurtCollider == 0 || !Finite(f.HurtX) || !Finite(f.HurtY) || Math.Abs(f.HurtX) > 100000 || Math.Abs(f.HurtY) > 100000
            || !Finite(f.HurtWidth) || !Finite(f.HurtHeight) || f.HurtWidth <= 0 || f.HurtHeight <= 0 || f.HurtWidth > 1000 || f.HurtHeight > 1000)
            throw new InvalidDataException("Invalid contact frame.");
    }
}
