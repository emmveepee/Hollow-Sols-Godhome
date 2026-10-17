using System;
using System.Collections.Generic;
using System.IO;

namespace YiGodhome.Bridge;

public enum AttackCategory : byte { Projectile = 1, Hazard = 2, Melee = 3, Burn = 4, Contact = 5 }
[Flags] public enum AttackTraits : byte { None = 0, Crimson = 1, Kickable = 2, Reflectable = 4, LargeParryPush = 8, RetireOnParry = 16, InitialParryPush = 32, PushRight = 64, PushLeft = 128 }
public sealed class AttackVolume
{
    public int Id;
    public int Damage = 1;
    public long Activation;
    public long ActionId;
    public int CounterInjuryEntity; // Exact attached attacker; zero means a detached hazard.
    public int ContactOwner;
    public long ContactOwnerGeneration;
    public long DamageGroupId; // Shared injury retirement; defenses stay per collider activation.
    public float InitialDamageGroupHold; // First injury holds the group, then surviving strikes rearm independently.
    public bool RepeatDamageGroupHold; // Persistent shared hazards restart their injury window after each injury.
    public long CounterGroupId; // Shared crimson counter; injury keeps its per-strike action.
    public AttackTraits Traits;
    public AttackCategory Category;
    public bool Active;
    public bool ConsumeOnContact;
    public bool CooldownOnDamage;
    public bool BlocksProjectiles; // Collision role independent of incoming damage/retirement.
    public bool TalismanContactImmunity;
    public bool PassiveContact; // Passive contact can share an authored body collider with strikes.
    public bool RadianceRainCounter; // Counter proximity only; never changes damage geometry.
    public bool NotifyParry; // Opt-in native boss reaction to a real ordinary strike parry.
    public float ParryCooldown; // Paused gameplay seconds; zero uses ordinary retirement.
    public float[] Points = Array.Empty<float>(); // world-space polygon, x/y pairs
    public float CircleX, CircleY, CircleRadius; // Explicit native circle; zero radius retains polygon.
}
public static class AttackVolumes
{
    public const int MaxCount = 128, MaxVertices = 64;
    // Primitive IDs only; each caller thread owns bounded scratch. No frame,
    // receiver or points escape into this cache, and exceptions cannot taint
    // the next validation because every invocation clears it first.
    [ThreadStatic] private static HashSet<int>? validationIds;
    public static void Validate(AttackVolume[] volumes)
    {
        if (volumes == null || volumes.Length > MaxCount) throw new InvalidDataException("Attack volume capacity exceeded.");
        var ids=validationIds??=new HashSet<int>();
        ids.Clear();
        foreach (var v in volumes)
        {
            bool recurringProjectileParry = v != null && v.Category == AttackCategory.Projectile && v.NotifyParry
                && v.Traits == AttackTraits.RetireOnParry && !v.ConsumeOnContact && v.ActionId > 0 && v.DamageGroupId > 0
                && v.InitialDamageGroupHold == 1 && v.RepeatDamageGroupHold && v.ParryCooldown == 1 && v.CooldownOnDamage;
            bool detachedCounterProjectile = v != null && v.Category == AttackCategory.Projectile && v.ConsumeOnContact
                && ProjectileDefense.CounterDeflects(v.Traits) && v.ActionId>0 && v.CounterInjuryEntity==0 && v.CounterGroupId==0;
            bool recurringPuddleParry=v!=null && v.Category==AttackCategory.Melee && v.NotifyParry && v.ActionId>0
                && v.Traits==AttackTraits.None && v.DamageGroupId>0 && v.InitialDamageGroupHold==1 && v.RepeatDamageGroupHold
                && v.ParryCooldown==1 && v.CooldownOnDamage && !v.ConsumeOnContact && !v.PassiveContact && v.CounterGroupId==0;
            bool recurringBodyContact=v!=null && (v.Category==AttackCategory.Contact || v.Category==AttackCategory.Melee && v.PassiveContact)
                && v.ActionId==0 && v.Traits==AttackTraits.None && v.DamageGroupId>0 && v.InitialDamageGroupHold==1 && v.RepeatDamageGroupHold
                && v.ParryCooldown==1 && v.CooldownOnDamage && !v.ConsumeOnContact && !v.BlocksProjectiles && v.CounterGroupId==0;
            if (v != null && v.RadianceRainCounter && (!detachedCounterProjectile || v.Traits!=(AttackTraits.Crimson|AttackTraits.Reflectable)))
                throw new InvalidDataException("Expanded counter is restricted to detached crimson rain blades.");
            if (v == null || v.Id == 0 || v.Damage < 0 || v.Damage > 100 || !ids.Add(v.Id) || v.Activation < 0 || v.Active && v.Activation == 0
                || (v.ContactOwner==0 ? v.ContactOwnerGeneration!=0 : v.ContactOwnerGeneration<=0 || v.Category!=AttackCategory.Contact || v.ActionId<=0)
                || v.NotifyParry && (v.ActionId<=0 || !(recurringPuddleParry || v.Category==AttackCategory.Melee && v.ParryCooldown==0 || v.Category==AttackCategory.Projectile && v.ParryCooldown==1 && v.CooldownOnDamage
                    || v.Category==AttackCategory.Contact && v.TalismanContactImmunity && v.ParryCooldown>0 && v.ParryCooldown<=1 && v.CooldownOnDamage && v.DamageGroupId==0)
                    || v.Traits!=AttackTraits.None && !recurringProjectileParry || v.PassiveContact || v.ConsumeOnContact || v.CounterGroupId!=0)
                || v.TalismanContactImmunity && v.Category != AttackCategory.Contact
                || v.PassiveContact && (v.Category != AttackCategory.Melee || v.ActionId != 0 || v.Traits != AttackTraits.None || v.ParryCooldown <= 0 || !v.CooldownOnDamage)
                || float.IsNaN(v.ParryCooldown) || v.ParryCooldown < 0 || v.ParryCooldown > 10 || v.CooldownOnDamage && v.ParryCooldown == 0
                || v.ParryCooldown > 0 && (v.Category == AttackCategory.Hazard || v.ConsumeOnContact && (!v.CooldownOnDamage || (v.Traits & AttackTraits.Reflectable) == 0))
                || v.BlocksProjectiles && (v.Category != AttackCategory.Melee || v.ConsumeOnContact || v.Activation <= 0)
                || v.ConsumeOnContact && v.Category != AttackCategory.Projectile || v.ActionId < 0
                || v.CounterGroupId < 0 || v.CounterGroupId > 0 && (v.ActionId <= 0 || v.Category != AttackCategory.Melee
                    || (v.Traits & AttackTraits.Crimson) == 0 || v.DamageGroupId != 0 || v.ParryCooldown != 0 || v.PassiveContact || v.TalismanContactImmunity)
                || !float.IsFinite(v.InitialDamageGroupHold) || v.InitialDamageGroupHold<0 || v.InitialDamageGroupHold>10
                || v.RepeatDamageGroupHold && v.InitialDamageGroupHold<=0
                || v.InitialDamageGroupHold>0 && (v.DamageGroupId<=0 || v.PassiveContact && !recurringBodyContact || v.BlocksProjectiles || v.CounterGroupId!=0
                    || v.Category!=AttackCategory.Melee && v.Category!=AttackCategory.Projectile && !recurringBodyContact)
                || v.DamageGroupId < 0 || v.DamageGroupId > 0
                    && !(recurringBodyContact || v.Category == AttackCategory.Projectile && v.ConsumeOnContact && (v.ActionId==0 || detachedCounterProjectile)
                        || v.Category==AttackCategory.Projectile && !v.ConsumeOnContact && v.ActionId>0 && v.CounterGroupId==0
                            && (v.InitialDamageGroupHold>0 || v.Traits==AttackTraits.Crimson)
                        || v.Category == AttackCategory.Melee && !v.ConsumeOnContact && !v.PassiveContact && !v.BlocksProjectiles
                            && (v.ActionId==0 ? v.Traits==AttackTraits.None : v.CounterGroupId==0 && (v.ParryCooldown==0
                                || v.RepeatDamageGroupHold && (v.Traits==AttackTraits.None || v.Traits==AttackTraits.RetireOnParry) && v.ParryCooldown==1 && v.CooldownOnDamage)))
                || (v.Traits & (AttackTraits.PushLeft | AttackTraits.PushRight)) == (AttackTraits.PushLeft | AttackTraits.PushRight)
                || (v.Traits & (AttackTraits.PushLeft | AttackTraits.PushRight)) != 0 && v.Category != AttackCategory.Melee
                || (v.Traits & AttackTraits.RetireOnParry) != 0 && v.Category != AttackCategory.Melee && !recurringProjectileParry
                || (v.Traits & AttackTraits.InitialParryPush) != 0 && (v.Traits & AttackTraits.LargeParryPush) == 0
                || (v.Traits & (AttackTraits.Crimson | AttackTraits.Kickable)) != 0 && v.ActionId <= 0
                || (v.Traits & AttackTraits.Crimson) != 0 && (v.Traits & AttackTraits.Kickable) != 0
                || (v.Traits & AttackTraits.Reflectable) != 0 && (!v.ConsumeOnContact || v.Category != AttackCategory.Projectile)
                || ProjectileDefense.CounterDeflects(v.Traits) && !detachedCounterProjectile
                || v.Category == AttackCategory.Burn && (v.Damage != 0 || v.Traits != AttackTraits.None || v.ConsumeOnContact || v.ParryCooldown != 0 || v.BlocksProjectiles)
                || v.Category < AttackCategory.Projectile || v.Category > AttackCategory.Contact || v.Points == null
                || v.Points.Length < 6 || v.Points.Length > MaxVertices * 2 || v.Points.Length % 2 != 0)
                throw new InvalidDataException("Invalid attack volume identity or shape.");
            double area = 0;
            for (int i = 0; i < v.Points.Length; i += 2)
            {
                float x = v.Points[i], y = v.Points[i + 1];
                if (float.IsNaN(x) || float.IsNaN(y) || Math.Abs(x) > 100000 || Math.Abs(y) > 100000) throw new InvalidDataException("Invalid attack vertex.");
                int next = (i + 2) % v.Points.Length;
                area += (double)x * v.Points[next + 1] - (double)y * v.Points[next];
            }
            if (Math.Abs(area) < 0.00001) throw new InvalidDataException("Degenerate attack polygon.");
            ValidateCircle(v);
        }
    }
    private static void ValidateCircle(AttackVolume v)
    {
        if (float.IsNaN(v.CircleRadius) || float.IsInfinity(v.CircleRadius) || v.CircleRadius < 0 || v.CircleRadius > 1000
            || float.IsNaN(v.CircleX) || float.IsNaN(v.CircleY) || Math.Abs(v.CircleX) > 100000 || Math.Abs(v.CircleY) > 100000)
            throw new InvalidDataException("Invalid native circle metadata.");
        if (v.CircleRadius == 0) {
            if (v.CircleX != 0 || v.CircleY != 0) throw new InvalidDataException("Polygon carries a circle center.");
            return;
        }
        if (v.Points.Length != MaxVertices * 2) throw new InvalidDataException("Circle requires its complete native contour.");
        if (!CircleContourMatches(v.Points, v.CircleX, v.CircleY, v.CircleRadius, out int vertex))
            throw new InvalidDataException("Circle metadata differs from native contour: id=" + v.Id + " vertex=" + vertex
                + " centerX=" + v.CircleX + " centerY=" + v.CircleY + " radius=" + v.CircleRadius + ".");
    }
    // Primitive metadata is optional. Producers may attach it only when the
    // independently owned native world contour proves the same geometry.
    // A failed optimization retains the contour; received metadata stays strict.
    public static bool TryUseNativeCircle(AttackVolume v, float x, float y, float radius)
    {
        v.CircleX = v.CircleY = v.CircleRadius = 0;
        if (float.IsNaN(x) || float.IsInfinity(x) || Math.Abs(x) > 100000
            || float.IsNaN(y) || float.IsInfinity(y) || Math.Abs(y) > 100000
            || float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0 || radius > 1000
            || v.Points == null || v.Points.Length != MaxVertices * 2
            || !CircleContourMatches(v.Points, x, y, radius, out _)) return false;
        v.CircleX = x; v.CircleY = y; v.CircleRadius = radius;
        return true;
    }
    private static bool CircleContourMatches(float[] points, float centerX, float centerY, float radius, out int rejectedVertex)
    {
        double tolerance = Math.Max(.0001, radius * .0001), chord = 2 * radius * Math.Sin(Math.PI / MaxVertices);
        rejectedVertex = -1;
        int winding = 0;
        for (int i = 0; i < points.Length; i += 2) {
            int next = (i + 2) % points.Length;
            double x = points[i] - centerX, y = points[i+1] - centerY;
            double nx = points[next] - centerX, ny = points[next+1] - centerY;
            if (double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(y) || double.IsInfinity(y)
                || double.IsNaN(nx) || double.IsInfinity(nx) || double.IsNaN(ny) || double.IsInfinity(ny)) { rejectedVertex = i / 2; return false; }
            int side = Math.Sign(x * ny - y * nx);
            if (Math.Abs(Math.Sqrt(x*x+y*y) - radius) > tolerance
                || Math.Abs(Math.Sqrt((x-nx)*(x-nx)+(y-ny)*(y-ny))-chord) > tolerance
                || side == 0 || winding != 0 && side != winding)
                { rejectedVertex = i / 2; return false; }
            winding = side;
        }
        return true;
    }
    public static void Write(BinaryWriter w, AttackVolume[] volumes)
    {
        Validate(volumes); w.Write((byte)volumes.Length);
        foreach (var v in volumes) { w.Write(v.Id); w.Write(v.Damage); w.Write(v.Activation); w.Write(v.ActionId); w.Write(v.CounterInjuryEntity); w.Write(v.ContactOwner); w.Write(v.ContactOwnerGeneration); w.Write(v.DamageGroupId); w.Write(v.CounterGroupId); w.Write((byte)v.Traits); w.Write((byte)v.Category); w.Write(v.Active); w.Write(v.ConsumeOnContact); w.Write(v.ParryCooldown); w.Write(v.CooldownOnDamage); w.Write(v.BlocksProjectiles); w.Write(v.TalismanContactImmunity); w.Write(v.PassiveContact); w.Write((byte)(v.Points.Length / 2)); foreach (float p in v.Points) w.Write(p); w.Write(v.CircleX); w.Write(v.CircleY); w.Write(v.CircleRadius); w.Write(v.NotifyParry); w.Write(v.InitialDamageGroupHold); w.Write(v.RepeatDamageGroupHold); w.Write(v.RadianceRainCounter); }
    }
    public static AttackVolume[] Read(BinaryReader r)
    {
        int count = r.ReadByte(); if (count > MaxCount) throw new InvalidDataException("Attack volume capacity exceeded.");
        var result = new AttackVolume[count];
        for (int i = 0; i < count; i++)
        {
            var v = new AttackVolume { Id = r.ReadInt32(), Damage = r.ReadInt32(), Activation = r.ReadInt64(), ActionId = r.ReadInt64(), CounterInjuryEntity = r.ReadInt32(), ContactOwner = r.ReadInt32(), ContactOwnerGeneration = r.ReadInt64(), DamageGroupId = r.ReadInt64(), CounterGroupId = r.ReadInt64(), Traits = (AttackTraits)r.ReadByte(), Category = (AttackCategory)r.ReadByte() };
            byte active = r.ReadByte(), consume = r.ReadByte(); v.ParryCooldown = r.ReadSingle(); byte cooldown = r.ReadByte(); v.CooldownOnDamage = cooldown == 1; byte blocks = r.ReadByte(); v.BlocksProjectiles = blocks == 1;
            byte talisman=r.ReadByte(), passive=r.ReadByte(); v.TalismanContactImmunity=talisman==1; v.PassiveContact=passive==1; int vertices = r.ReadByte();
            if (active > 1 || consume > 1 || cooldown > 1 || blocks > 1 || talisman>1 || passive>1 || vertices < 3 || vertices > MaxVertices) throw new InvalidDataException("Invalid attack volume flags/size.");
            v.Active = active == 1; v.ConsumeOnContact = consume == 1; v.Points = new float[vertices * 2];
            for (int j = 0; j < v.Points.Length; j++) v.Points[j] = r.ReadSingle();
            v.CircleX = r.ReadSingle(); v.CircleY = r.ReadSingle(); v.CircleRadius = r.ReadSingle();
            byte notify=r.ReadByte();if(notify>1)throw new InvalidDataException("Invalid parry notification flag.");v.NotifyParry=notify==1;v.InitialDamageGroupHold=r.ReadSingle();
            byte repeat=r.ReadByte();if(repeat>1)throw new InvalidDataException("Invalid recurring damage-group flag.");v.RepeatDamageGroupHold=repeat==1; byte rain=r.ReadByte();if(rain>1)throw new InvalidDataException("Invalid rain counter flag.");v.RadianceRainCounter=rain==1;result[i] = v;
        }
        Validate(result); return result;
    }
}
