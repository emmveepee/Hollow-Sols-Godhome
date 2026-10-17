using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YiGodhome.Bridge;

namespace YiGodhome;

// Profile discovery selects authored objects/states. Shape transport and activation
// tracking are shared by every projectile using this path.
internal sealed class HostAttackVolumes : IDisposable
{
    private sealed class Source
    {
        public PolygonCollider2D Shape = null!;
        public DamageHero Damage = null!;
        public PlayMakerFSM Fsm = null!;
        public bool Active;
        public long Activation;
        public bool Near;
        public bool Recovering;
        public float NativeSpeed;
        public HostReflectedProjectile? Reflection;
    }
    private readonly List<AttackVolume> captured = new();
    private readonly List<Vector2> polygonScratch = new();
    private readonly Vector2[] boxScratch = new Vector2[4];
    private Source[]? sources;
    private DamageHero[]? hazards;
    private readonly Action<string>? log;
    private HostPooledAttackVolumes? pooled;
    private HostPooledAttackVolumes? passive;
    private NativeEncounterPools? pools;
    private NativeShockwaveStrikes? projectileGroups;
    private HealthManager? mawlekBody;
    private long nextProjectileGroup;
    private readonly bool contact, trueContact;
    public bool Resolve(int id, long activation, bool reflected, HealthManager? target, TargetHealthState health)
    {
        var source=sources?.SingleOrDefault(s=>s.Shape!=null && s.Shape.GetInstanceID()==id);
        if(source==null) return pooled?.Resolve(id,activation,reflected,target,health)==true;
        if(!source.Active || source.Recovering || source.Activation!=activation || source.Damage==null) return false;
        if(reflected) {
            if(target==null || target.isDead || source.NativeSpeed<=0) return false;
            source.Reflection ??= source.Damage.gameObject.AddComponent<HostReflectedProjectile>();
            source.Reflection.Begin(source.Fsm,source.Damage,source.Shape,target,health,activation,log);
        } else HostReflectedProjectile.RecycleSword(source.Damage);
        source.Recovering=true;source.Active=false;
        log?.Invoke("HK_XERO_SWORD_RESOLVED id="+id+" activation="+activation+" reflected="+reflected+" nativeReturn=True frame="+Time.frameCount);
        return true;
    }
    public void Dispose() { if(sources!=null) foreach(var source in sources) {source.Reflection?.Retire();if(source.Reflection!=null) UnityEngine.Object.Destroy(source.Reflection);} pooled?.Dispose(); passive?.Dispose();projectileGroups?.Dispose();pools?.Dispose();if(EncounterProfile.Selected.Family==EncounterProfile.Xero) NativeFsmStepLease.ReleaseHooks(); }
    public HostAttackVolumes(Action<string>? log) { this.log = log; contact=HollowSolsSettings.Contact; trueContact=HollowSolsSettings.TrueContact;if(EncounterProfile.Selected.Family==EncounterProfile.Xero) NativeFsmStepLease.Prepare(); }
    public AttackVolume[] Capture(bool alive, Action<AttackVolume, DamageHero>? decorate = null, bool primaryStrike = false)
    {
        if (EncounterProfile.Selected.Family != EncounterProfile.Xero) sources = Array.Empty<Source>();
        if (sources == null)
        {
            sources = Resources.FindObjectsOfTypeAll<PlayMakerFSM>()
                .Where(f => f.gameObject.scene.name == EncounterProfile.Selected.Scene && f.FsmName == "xero_nail")
                .Select(f => new Source { Fsm = f, Shape = f.GetComponent<PolygonCollider2D>(), Damage = f.GetComponent<DamageHero>() }).ToArray();
            if (sources.Length != 4 || sources.Any(s => s.Shape == null || s.Damage == null || s.Shape.pathCount != 1 || s.Damage.damageDealt != 1 || s.Damage.hazardType != 1))
                throw new InvalidOperationException("Xero projectile profile differs from audited four native swords.");
        }
        var result = captured; result.Clear();
        if (contact && EncounterProfile.Selected.Family != EncounterProfile.Mawlek) {
            passive ??= new HostPooledAttackVolumes(EncounterProfile.Selected.Scene, d => {
                var health=d.GetComponent<HealthManager>();
                if(health==null && EncounterProfile.Selected.Family==EncounterProfile.Gruz && d.name=="Hero Damager") health=d.GetComponentInParent<HealthManager>();
                return health!=null && EncounterProfile.Selected.IsBossName(health.name) && d.damageDealt>0
                    ? AttackCategory.Contact : (AttackCategory?)null;
            }, log);
            result.AddRange(passive.Capture(alive, (v,d) => {
                v.Active &= !primaryStrike;
                v.PassiveContact=true;
                v.ParryCooldown=trueContact ? .0001f : 1;
                v.CooldownOnDamage=true;
            }));
        }
        if (EncounterProfile.Selected.Family == EncounterProfile.PureVessel)
        {
            pooled ??= new HostPooledAttackVolumes(EncounterProfile.Selected.Scene, ClassifyPureVessel, log);
            result.AddRange(pooled.Capture(alive, decorate));
        }
        if (EncounterProfile.Selected.Family == EncounterProfile.Mawlek)
        {
            mawlekBody ??= Resources.FindObjectsOfTypeAll<HealthManager>().Single(h=>h.gameObject.scene.name==EncounterProfile.Selected.Scene && h.name==EncounterProfile.Mawlek.Boss);
            if(pools==null) {
                pools=new NativeEncounterPools(EncounterProfile.Selected.Scene,log);
                try{projectileGroups=new NativeShockwaveStrikes(pools,()=>++nextProjectileGroup,log,(_,_)=>default);}
                catch{pools.Dispose();pools=null;throw;}
            }
            pooled ??= new HostPooledAttackVolumes(EncounterProfile.Selected.Scene, d =>
                d.name == "Mawlek Body" ? AttackCategory.Contact
                : d.name == "Mawlek Arm L" || d.name == "Mawlek Arm R" ? AttackCategory.Melee
                : d.name.StartsWith("Shot Mawlek", StringComparison.Ordinal) && d.GetComponent<EnemyBullet>() != null ? AttackCategory.Projectile : (AttackCategory?)null, log,
                nativePools:true,globalOwnership:d=>pools.Owns(d.gameObject));
            result.AddRange(pooled.Capture(alive, (v,d) => {
                if(v.Category == AttackCategory.Melee && HeroController.instance != null)
                    v.Traits |= HeroController.instance.transform.position.x >= mawlekBody.transform.position.x ? AttackTraits.PushRight : AttackTraits.PushLeft;
                if(v.Category == AttackCategory.Contact) { v.ParryCooldown=1; v.CooldownOnDamage=true; v.TalismanContactImmunity=true; }
                projectileGroups?.Decorate(v,d);
                if(v.DamageGroupId>0)v.InitialDamageGroupHold=1;
            }));
        }
        foreach (var s in sources)
        {
            if (s.Shape == null || s.Fsm == null) continue;
            NativeBossContracts.CaptureWeapon(s.Damage,EncounterProfile.Selected,log);
            string state = s.Fsm.ActiveStateName;
            if(SessionConfiguration.Current.RunFixtures && SessionConfiguration.Current.Has("trace-probe") && Time.frameCount%120==0)
                log?.Invoke("HK_XERO_SWORD_STATE id="+s.Shape.GetInstanceID()+" state="+state+" enabled="+s.Fsm.enabled+" attacking="+s.Fsm.FsmVariables.FindFsmBool("Attacking")?.Value+" recovering="+s.Recovering+" timeScale="+Time.timeScale+" frame="+Time.frameCount);
            if(s.Recovering && s.Fsm.enabled && s.Fsm.Fsm.Started && !s.Fsm.Fsm.Finished && (state=="Home" && s.Fsm.FsmVariables.FindFsmBool("Attacking")?.Value==false || state=="Antic Spin" || state=="Antic Point")) {
                s.Recovering=false;
                log?.Invoke("HK_XERO_RECOVERY_COMPLETE id="+s.Shape.GetInstanceID()+" activation="+s.Activation+" state="+state+" position="+s.Shape.transform.position+" nativeRunning=True frame="+Time.frameCount);
            }
            if(state=="Shoot" && !s.Recovering) {
                var body=s.Damage.GetComponent<Rigidbody2D>();
                if(body!=null && body.linearVelocity.sqrMagnitude>0) s.NativeSpeed=body.linearVelocity.magnitude;
            }
            bool active = !s.Recovering && alive && s.Damage.isActiveAndEnabled && s.Shape.enabled && s.Shape.gameObject.activeInHierarchy
                && (state == "Shoot" || state == "Decelerate" || state == "Return Pause" || state == "Returning");
            if (active && !s.Active) s.Activation++;
            if (active != s.Active) log?.Invoke("HK_ATTACK_VOLUME id=" + s.Shape.GetInstanceID() + " activation=" + s.Activation + " active=" + active + " category=Projectile name=" + s.Shape.name + " state=" + state);
            s.Active = active;
            bool near = active && s.Shape.bounds.SqrDistance(HeroController.instance.transform.position) < 4;
            if (near && !s.Near) log?.Invoke("HK_ATTACK_NEAR id=" + s.Shape.GetInstanceID() + " state=" + state + " bounds=" + s.Shape.bounds + " hero=" + HeroController.instance.transform.position);
            s.Near = near;
            if (active && state == "Shoot" && !s.Near && Time.frameCount % 120 == 0)
                log?.Invoke("HK_ATTACK_AIM id=" + s.Shape.GetInstanceID() + " vars=" + string.Join(";", s.Fsm.FsmVariables.GameObjectVariables.Select(v => v.Name + "=" + (v.Value == null ? "none" : v.Value.name + ":" + v.Value.transform.position))));
            s.Shape.GetPath(0, polygonScratch);
            float[] points = new float[polygonScratch.Count * 2];
            var matrix = s.Shape.transform.localToWorldMatrix; var offset = s.Shape.offset;
            for (int i = 0; i < polygonScratch.Count; i++) {
                var w = matrix.MultiplyPoint3x4(polygonScratch[i] + offset);
                points[i * 2] = w.x; points[i * 2 + 1] = w.y;
            }
            result.Add(new AttackVolume { Id = s.Shape.GetInstanceID(), Activation = s.Activation, Category = AttackCategory.Projectile, Active = active, Points = points,
                Traits=AttackTraits.Reflectable, ConsumeOnContact=true });
        }
        if (hazards == null) hazards = Resources.FindObjectsOfTypeAll<DamageHero>().Where(d => d.gameObject.scene.name == EncounterProfile.Selected.Scene && d.hazardType == 2).ToArray();
        foreach (var hazard in hazards)
        {
            if (hazard == null) continue;
            var shape = hazard.GetComponent<BoxCollider2D>();
            if (shape == null || shape.edgeRadius != 0 || hazard.damageDealt != 1) throw new InvalidOperationException("Unsupported environmental hazard geometry/damage.");
            Vector2 h = shape.size * 0.5f;
            boxScratch[0] = new Vector2(-h.x,-h.y); boxScratch[1] = new Vector2(h.x,-h.y);
            boxScratch[2] = h; boxScratch[3] = new Vector2(-h.x,h.y);
            var points = new float[8]; var matrix = shape.transform.localToWorldMatrix; var offset = shape.offset;
            for (int i = 0; i < boxScratch.Length; i++) {
                var w = matrix.MultiplyPoint3x4(boxScratch[i] + offset);
                points[i * 2] = w.x; points[i * 2 + 1] = w.y;
            }
            result.Add(new AttackVolume { Id = shape.GetInstanceID(), Activation = 1, Active = alive && shape.enabled && hazard.isActiveAndEnabled, Category = AttackCategory.Hazard, Points = points });
        }
        return result.ToArray();
    }
    private static AttackCategory? ClassifyPureVessel(DamageHero damage)
    {
        if (damage.name.StartsWith("Shot HK Shadow", StringComparison.Ordinal)) return AttackCategory.Projectile;
        if (damage.name.StartsWith("HK Plume Prime", StringComparison.Ordinal)) return AttackCategory.Melee;
        if (damage.gameObject.scene.name != EncounterProfile.PureVessel.Scene) return null;
        switch (damage.name) {
            case "Slash1": case "Slash2": case "Slash3": case "Dash Stab":
            case "Dstab Damage": case "Focus Hit": case "hero_damager": case "T Hit": return AttackCategory.Melee;
            default: return null;
        }
    }
}
