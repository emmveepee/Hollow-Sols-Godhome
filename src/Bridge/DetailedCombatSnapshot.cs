using System;
using System.Globalization;
using System.Linq;
namespace YiGodhome.Bridge;
public static class DetailedCombatSnapshot
{
    private static string F(float v) => v.ToString("R",CultureInfo.InvariantCulture);
    public static void Write(Action<string> log, Pose? p, ContactFrame? f)
    {
        if(p != null) {
            log("DETAIL_POSE geometry="+p.GeometryFrame+" x="+F(p.X)+" y="+F(p.Y)+" vx="+F(p.VelocityX)+" vy="+F(p.VelocityY)
                +" width="+F(p.Width)+" height="+F(p.Height)+" grounded="+p.Grounded+" health="+p.Vitals?.Health+" injury="+p.Vitals?.Injury+" chi="+p.Vitals?.Chi+" ammo="+p.Vitals?.Ammo);
            foreach(var w in p.Melee) log("DETAIL_MELEE collider="+w.Collider+" activation="+w.Activation+" x="+F(w.X)+" y="+F(w.Y)+" width="+F(w.Width)+" height="+F(w.Height));
        }
        if(f == null) { log("DETAIL_CONTACT unavailable=True"); return; }
        log("DETAIL_CONTACT encounter="+f.Encounter+" geometry="+f.GeometryFrame+" frame="+f.NativeFrame+" sequence="+f.Sequence+" entity="+f.Entity+" activation="+f.TargetGeneration
            +" active="+f.Active+" action="+f.AttackId+" crimson="+f.Crimson+" windup="+f.Windup+" kick="+f.Kickable+" health="+f.Health+" notches="+string.Join(",",f.Notches));
        log("DETAIL_PRIMARY_ATTACK collider="+f.Collider+" active="+f.Active+" action="+f.AttackId+" damage="+f.Damage+" x="+F(f.X)+" y="+F(f.Y)+" width="+F(f.Width)+" height="+F(f.Height));
        log("DETAIL_PRIMARY_TARGET collider="+f.HurtCollider+" activation="+f.TargetGeneration+" active="+f.HurtActive+" x="+F(f.HurtX)+" y="+F(f.HurtY)+" width="+F(f.HurtWidth)+" height="+F(f.HurtHeight));
        foreach(var t in f.Summons) log("DETAIL_TARGET entity="+t.Entity+" activation="+t.Generation+" active="+t.Active+" health="+t.Health+" maximum="+t.MaxHealth+" x="+F(t.X)+" y="+F(t.Y)+" width="+F(t.Width)+" height="+F(t.Height));
        foreach(var v in f.Volumes) log("DETAIL_VOLUME id="+v.Id+" activation="+v.Activation+" action="+v.ActionId+" active="+v.Active+" category="+v.Category+" traits="+v.Traits+" damage="+v.Damage
            +" cooldown="+F(v.ParryCooldown)+" cooldownOnDamage="+v.CooldownOnDamage+" initialGroupHold="+F(v.InitialDamageGroupHold)+" repeatGroupHold="+v.RepeatDamageGroupHold+" blocks="+v.BlocksProjectiles+" points="+string.Join(",",v.Points.Select(F)));
    }
}
