using System;
using System.IO;
using System.Collections.Generic;

namespace YiGodhome.Bridge;

public sealed class SummonedTarget
{
    public int BarHealth=-1, BarMaxHealth=-1; // Optional display values; raw health remains authoritative.
    public int Entity, Collider, Health, MaxHealth, Damage;
    public long Generation;
    public bool Kickable;
    public long AttackId;
    public int[] Notches = Array.Empty<int>();
    // Minion visibility is a fixed receiver role. A BossBar may hide dynamically
    // only as an inactive, nondamaging terminal presentation, retaining its role.
    public bool Active, BossBar, PassiveRepel, SharedHealth, HideHealthBar;
    public float X, Y, Width, Height;
    public AttackVolume Attack() => new AttackVolume {
        Id = Collider, Activation = Generation, Active = Active && Damage > 0, Damage = Damage, Category = AttackCategory.Contact, TalismanContactImmunity=Damage>0 && !BossBar, ParryCooldown = Damage > 0 ? 1 : 0, CooldownOnDamage = Damage > 0,
        Points = new[] { X-Width/2, Y-Height/2, X+Width/2, Y-Height/2, X+Width/2, Y+Height/2, X-Width/2, Y+Height/2 }
    };
    public ContactFrame Project(ContactFrame parent) => new ContactFrame {
        Session = parent.Session, Encounter = parent.Encounter, GeometryFrame = parent.GeometryFrame,
        NativeFrame = parent.NativeFrame, CaptureTick = parent.CaptureTick, Sequence = parent.Sequence,
        Entity = Entity, Collider = Collider, HurtCollider = Collider, Health = Health, MaxHealth = MaxHealth,
        BarHealth=BarHealth, BarMaxHealth=BarMaxHealth, Notches = Notches, Damage = Damage, Kickable = Kickable, AttackId = AttackId, Difficulty = parent.Difficulty, BossTitle = parent.BossTitle, BossBarsReady=parent.BossBarsReady, Active = Active, HurtActive = Active && Health > 0, TargetGeneration = Generation, SecondaryTarget = true,
        X = X, Y = Y, Width = Width, Height = Height, HurtX = X, HurtY = Y, HurtWidth = Width, HurtHeight = Height
    };
}
public static class SummonedTargets
{
    public const int Maximum = 32;
    // Primitive IDs only; each caller thread owns bounded scratch. No frame,
    // receiver or points escape into this cache, and exceptions cannot taint
    // the next validation because every invocation clears it first.
    [ThreadStatic] private static HashSet<int>? validationIds;
    public static void Validate(SummonedTarget[] targets)
    {
        if (targets == null || targets.Length > Maximum) throw new InvalidDataException("Summon capacity exceeded.");
        var identities=validationIds??=new HashSet<int>();
        identities.Clear();
        foreach (var t in targets)
        {
            if (t == null) throw new InvalidDataException("Null summon target.");
            ContactCodec.ValidateBar(t.BarHealth,t.BarMaxHealth);
            if(t.BarHealth>=0 && !t.BossBar)throw new InvalidDataException("Minion cannot override presentation health.");
            HealthNotches.Validate(t.Notches, t.MaxHealth);
            if (t == null || t.Entity == 0 || t.Collider == 0 || t.Generation <= 0 || !identities.Add(t.Entity)
                || t.AttackId < 0 || t.Kickable && t.AttackId <= 0 || t.PassiveRepel && (!t.BossBar || t.Damage != 0)
                || t.HideHealthBar && (t.SharedHealth || t.BossBar && (t.Active || t.Damage!=0 || t.Kickable || t.AttackId!=0 || t.PassiveRepel))
                || t.Health < 0 || t.MaxHealth < 1 || t.MaxHealth > 1000000 || t.Health > t.MaxHealth || t.Damage < 0 || t.Damage > 100
                || !Finite(t.X) || !Finite(t.Y) || Math.Abs(t.X) > 100000 || Math.Abs(t.Y) > 100000
                || !Finite(t.Width) || !Finite(t.Height) || t.Width <= 0 || t.Height <= 0 || t.Width > 1000 || t.Height > 1000)
                throw new InvalidDataException("Invalid summon target.");
        }
    }
    private static bool Finite(float n) => !float.IsNaN(n) && !float.IsInfinity(n);
    public static void Write(BinaryWriter w, SummonedTarget[] targets)
    {
        Validate(targets); w.Write(targets.Length);
        foreach (var t in targets) { w.Write(t.Entity); w.Write(t.Collider); w.Write(t.Generation); w.Write(t.Health); w.Write(t.MaxHealth); w.Write(t.Damage); w.Write(t.Active); w.Write(t.BossBar); w.Write(t.PassiveRepel); w.Write(t.SharedHealth); w.Write(t.HideHealthBar); w.Write(t.Kickable); w.Write(t.AttackId); w.Write(t.X); w.Write(t.Y); w.Write(t.Width); w.Write(t.Height); HealthNotches.Write(w, t.Notches); w.Write(t.BarHealth); w.Write(t.BarMaxHealth); }
    }
    public static SummonedTarget[] Read(BinaryReader r)
    {
        int count = r.ReadInt32(); if (count < 0 || count > Maximum) throw new InvalidDataException("Invalid summon count.");
        var targets = new SummonedTarget[count];
        for (int i = 0; i < count; i++) {
            var t = new SummonedTarget { Entity = r.ReadInt32(), Collider = r.ReadInt32(), Generation = r.ReadInt64(), Health = r.ReadInt32(), MaxHealth = r.ReadInt32(), Damage = r.ReadInt32() };
            byte active = r.ReadByte(); if (active > 1) throw new InvalidDataException("Invalid summon flag.");
            byte boss = r.ReadByte(); if (boss > 1) throw new InvalidDataException("Invalid target presentation flag.");
            byte repel = r.ReadByte(); if (repel > 1) throw new InvalidDataException("Invalid passive recoil flag."); t.PassiveRepel = repel == 1;
            byte shared = r.ReadByte(); if(shared>1) throw new InvalidDataException("Invalid shared health flag."); t.SharedHealth=shared==1;
            byte hidden = r.ReadByte(); if(hidden>1) throw new InvalidDataException("Invalid hidden health bar flag."); t.HideHealthBar=hidden==1;
            byte kick = r.ReadByte(); if (kick > 1) throw new InvalidDataException("Invalid target kick flag.");
            t.Kickable = kick == 1; t.AttackId = r.ReadInt64(); t.Active = active == 1; t.BossBar = boss == 1; t.X = r.ReadSingle(); t.Y = r.ReadSingle(); t.Width = r.ReadSingle(); t.Height = r.ReadSingle(); t.Notches = HealthNotches.Read(r); t.BarHealth=r.ReadInt32();t.BarMaxHealth=r.ReadInt32();targets[i] = t;
        }
        Validate(targets); return targets;
    }
}
