using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using YiGodhome.Bridge;

namespace YiGodhome;

// Redirect the native body; only actual swept contact can apply a native hit.
internal sealed class HostReflectedProjectile : MonoBehaviour
{
    private PlayMakerFSM? control;
    private DamageHero damage = null!;
    private Collider2D shape = null!;
    private HealthManager target = null!;
    private TargetHealthState health = null!;
    private Action<string>? log;
    private bool reflected, originalDamage, originalControl;
    private Rigidbody2D flight = null!;
    private NativeFsmStepLease? stepLease;
    private Func<bool>? targetLease;
    private bool gravityLease;
    private float originalGravity;
    private int suppressedEnemyCallbacks;
    private HiveKnightStinger? stinger;
    private bool originalStinger;
    private Transform? stingerParent;
    private Quaternion stingerLocalRotation;
    private Vector3 stingerLocalScale;
    private bool detachedStinger;
    private Vector2 reflectedVelocity;
    private float originalDamping;
    private bool hiveBee;
    private Recoil? beeRecoil;
    private bool originalBeeRecoil;
    private NativeFsmStepLease? beeRecoilLease;
    private Transform? orbitParent;
    private bool detachedOrbit;
    private Transform? combParent;
    private bool detachedComb;
    private int orbitFlightSamples;
    internal static bool HiveBeeName(string name)=>name=="Bee Dropper" || name=="Bee Dropper (1)" || name=="Bee Dropper (2)"
        || name=="Bee Dropper (3)" || name=="Bee Dropper (4)" || name=="Bee Dropper (5)" || name=="Bee Dropper (6)";
    internal static PlayMakerFSM? HiveBee(DamageHero source)=>HiveBeeName(source.name)
        ?source.GetComponents<PlayMakerFSM>().SingleOrDefault(f=>f.FsmName=="Control"):null;
    internal bool IsReflected=>reflected;
    internal static bool RadianceNailName(string name)=>name=="Radiant Nail" || name=="Radiant Nail(Clone)";
    internal static bool RadianceOrbName(string name)=>name=="Radiant Orb" || name=="Radiant Orb(Clone)";
    internal static PlayMakerFSM? RadianceNail(DamageHero source)=>RadianceNailName(source.name)
        ?source.GetComponents<PlayMakerFSM>().SingleOrDefault(f=>f.FsmName=="Control"):null;
    internal static PlayMakerFSM? RadianceComb(DamageHero source) {
        if(!RadianceNailName(source.name))return null;
        var parent=source.transform.parent;
        if(parent!=null && parent.name=="Nails")parent=parent.parent;
        return parent!=null && (parent.name=="Radiant Nail Comb" || parent.name=="Radiant Nail Comb(Clone)")
            ?parent.GetComponents<PlayMakerFSM>().SingleOrDefault(f=>f.FsmName=="Control"):null;
    }
    internal static Vector2 RadianceCombVelocity(DamageHero source) {
        var comb=RadianceComb(source);
        if(comb==null || comb.ActiveStateName!="Tween")return Vector2.zero;
        var tween=comb.FsmStates.Single(s=>s.Name=="Tween").Actions.OfType<HutongGames.PlayMaker.Actions.iTweenMoveBy>().Single();
        var moving=comb.Fsm.GetOwnerDefaultTarget(tween.gameObject);
        if(moving==null || moving.transform!=source.transform.parent || moving.name!="Nails")
            throw new InvalidOperationException("Native Radiance comb moving-container ownership changed.");
        if(tween.speed.IsNone || tween.speed.Value<=0 || float.IsNaN(tween.speed.Value) || float.IsInfinity(tween.speed.Value))
            throw new InvalidOperationException("Native Radiance comb tween speed changed.");
        var direction=tween.space==Space.Self?moving.transform.TransformDirection(tween.vector.Value):tween.vector.Value;
        if(direction.sqrMagnitude<=0)throw new InvalidOperationException("Native Radiance comb tween direction changed.");
        return (Vector2)direction.normalized*tween.speed.Value;
    }
    internal static PlayMakerFSM? FallingDebris(DamageHero source)=>source.name.StartsWith("Falling Barrel",StringComparison.Ordinal)
        ? source.GetComponents<PlayMakerFSM>().SingleOrDefault(f=>f.FsmName=="Fall Barrel Control") : null;
    internal static bool EnemyCallback(HutongGames.PlayMaker.FsmStateAction action)
    {
        if(action.Fsm.Name!="damages_enemy" || action.Owner==null) return true;
        var owner=action.Owner.GetComponent<HostReflectedProjectile>();
        if(owner==null || !owner.reflected || owner.control?.FsmName!="Fall Barrel Control") return true;
        if(owner.suppressedEnemyCallbacks++<16) owner.log?.Invoke("HK_REFLECTED_NATIVE_ENEMY_CALLBACK_SUPPRESSED id="+owner.shape.GetInstanceID()+" activation="+owner.activation+" nativeCallback=True targetFilterNotRun=True frame="+Time.frameCount);
        return false;
    }
    internal static PlayMakerFSM? SoulOrb(DamageHero source) {
        var root = source.transform.parent;
        if (source.name != "Hero Hurter" || root == null || !(RadianceOrbName(root.name) || root.name == "Mage Orb" || root.name == "Mage Orb Over" || root.name.StartsWith("Mage Orb(Clone)",StringComparison.Ordinal) || root.name.StartsWith("Mage Orb Over(Clone)",StringComparison.Ordinal))) return null;
        return root.GetComponents<PlayMakerFSM>().SingleOrDefault(f => f.FsmName == "Orb Control");
    }
    private readonly Collider2D[] overlaps = new Collider2D[256];
    private float expires;
    private long activation;
    private int outgoingDamage;
    private readonly RaycastHit2D[] sweep = new RaycastHit2D[256];
    private Collider2D[] receivers = Array.Empty<Collider2D>();
    private static bool IsTerrain(Collider2D collider) => !collider.isTrigger && collider.gameObject.layer==LayerMask.NameToLayer("Terrain");
    private static readonly System.Reflection.MethodInfo Recycle = AccessTools.Method(typeof(AutoRecycleSelf), "RecycleSelf");
    private static readonly System.Reflection.FieldInfo PaintActive = AccessTools.Field(typeof(PaintBullet), "active");
    private static readonly System.Reflection.MethodInfo PaintCollision = AccessTools.Method(typeof(PaintBullet), "Collision");
    internal static bool PaintIsActive(PaintBullet paint)=>(bool)PaintActive.GetValue(paint);
    internal static void ImpactPaint(PaintBullet paint) {
        if(!PaintIsActive(paint)) return;
        PaintActive.SetValue(paint,false);
        paint.StartCoroutine((System.Collections.IEnumerator)PaintCollision.Invoke(paint,new object[]{Vector2.zero,false}));
    }
    public static void RecycleSword(DamageHero source, bool terminal=false) {
        var paint=source.GetComponent<PaintBullet>();
        if(paint!=null) {ImpactPaint(paint);return;}
        if(source.GetComponent<HiveKnightStinger>()!=null) {source.gameObject.SetActive(false);return;}
        var bee=HiveBee(source);
        if(bee!=null) {
            if(bee.ActiveStateName!="Spell Death" && bee.ActiveStateName!="Reset" && bee.ActiveStateName!="Idle") bee.SetState("Spell Death");
            return;
        }
        var debris=FallingDebris(source);
        if(debris!=null) {
            if(debris.ActiveStateName!="Pause" && debris.ActiveStateName!="Break" && debris.ActiveStateName!="Recycle") debris.SetState("Break");
            return;
        }
        var radNail=RadianceNail(source);
        if(radNail!=null) {radNail.SetState("Recycle");return;}
        var xero=source.GetComponents<PlayMakerFSM>().SingleOrDefault(f=>f.FsmName=="xero_nail");
        if(xero!=null) {
            bool dissipate=terminal || xero.ActiveStateName=="Dissipate";
            bool restart=xero.Fsm.RestartOnEnable;
            xero.Fsm.RestartOnEnable=false;xero.enabled=true;xero.Fsm.RestartOnEnable=restart;
            source.enabled=true;
            xero.SetState(dissipate?"Dissipate":"Returning");return;
        }
        var orb = SoulOrb(source);
        if(source.name.StartsWith("Hornet Barb",StringComparison.Ordinal)) {
            var barb=source.GetComponents<PlayMakerFSM>().Single(f=>f.FsmName=="Control");
            barb.enabled=true;barb.SetState("Land");return;
        }
        if (orb != null) { bool restart = orb.Fsm.RestartOnEnable; orb.Fsm.RestartOnEnable = false; orb.enabled = true; orb.Fsm.RestartOnEnable = restart; orb.SetState("Impact"); return; }
        var bullet = source.GetComponent<EnemyBullet>();
        if (bullet != null) {
            AccessTools.Field(typeof(EnemyBullet), "active").SetValue(bullet, false);
            bullet.StartCoroutine((System.Collections.IEnumerator)AccessTools.Method(typeof(EnemyBullet), "Collision").Invoke(bullet, new object[] { Vector2.zero, false }));
            return;
        }
        var recycle = source.GetComponent<AutoRecycleSelf>();
        if (recycle != null) { Recycle.Invoke(recycle, Array.Empty<object>()); return; }
        if (!source.name.StartsWith("Shot Markoth Nail", StringComparison.Ordinal) && !source.name.StartsWith("Shot Slug Spear",StringComparison.Ordinal)) throw new InvalidOperationException("Unaudited reflected projectile recycle.");
        var fsm = source.GetComponents<PlayMakerFSM>().Single(f => f.FsmName == "Control");
        fsm.enabled = true; fsm.SetState("Recycle");
    }
    public void Begin(PlayMakerFSM? native, DamageHero source, Collider2D collider, HealthManager boss, TargetHealthState targetHealth, long id, Action<string>? logger,Func<bool>? validTargetLease=null,bool crimsonRain=false)
    {
        if (boss.GetInstanceID() != targetHealth.Entity) throw new InvalidOperationException("Reflected projectile target identity mismatch.");
        health = targetHealth;
        targetLease=validTargetLease;
        suppressedEnemyCallbacks=0;
        control = native; damage = source; shape = collider; target = boss; log = logger; activation = id;
        outgoingDamage=EncounterProfile.Selected.ReflectedProjectileDamage;
        if(crimsonRain) {
            if(EncounterProfile.Selected.Family!=EncounterProfile.AbsoluteRadiance || !RadianceNailName(source.name))
                throw new InvalidOperationException("Crimson rain reflection escaped its native Radiance sword owner.");
            outgoingDamage=RadianceAttackWindows.ReflectedBladeDamage(true);
        }
        if(SessionConfiguration.Current.RunFixtures && SessionConfiguration.Current.Has("trace-probe") && SoulOrb(source) is PlayMakerFSM soulOrb)
            log?.Invoke("HK_SOUL_REFLECTION_BINDING id="+shape.GetInstanceID()+" activation="+activation+" root="+soulOrb.gameObject.GetInstanceID()+" parent="+(soulOrb.transform.parent?.name.Replace(' ','_')??"none")
                +" parentId="+(soulOrb.transform.parent?.gameObject.GetInstanceID()??0)+" state="+soulOrb.ActiveStateName.Replace(' ','_')+" tweens="+soulOrb.GetComponents<iTween>().Length+" frame="+Time.frameCount);
        originalDamage = damage.enabled; originalControl = control?.enabled == true;
        stinger=source.GetComponent<HiveKnightStinger>();originalStinger=stinger?.enabled==true;
        hiveBee=HiveBeeName(source.name) && native?.FsmName=="Control";
        receivers = target.GetComponentsInChildren<Collider2D>(true).Where(c => IsReceiver(c)).ToArray();
        if (receivers.Length == 0) throw new InvalidOperationException("Reflected projectile has no native damage receiver.");
        var body = source.GetComponent<Rigidbody2D>() ?? SoulOrb(source)?.GetComponent<Rigidbody2D>();
        if (body == null) throw new InvalidOperationException("Reflected projectile body missing.");
        flight = body;
        var incomingVelocity=body.linearVelocity;
        var comb=RadianceComb(source);
        if(comb!=null)incomingVelocity=RadianceCombVelocity(source);
        const float speed=ProjectileReflection.Speed;
        var previous = (Vector2)shape.bounds.center;
        var liveReceiver=receivers.FirstOrDefault(c=>c.enabled && c.gameObject.activeInHierarchy && c.bounds.size.sqrMagnitude>0);
        var aim=(liveReceiver!=null?(Vector2)liveReceiver.bounds.center:(Vector2)target.transform.position)-previous;
        var velocity=ProjectileReflection.Velocity(aim.x,aim.y);
        reflectedVelocity=new Vector2(velocity.X,velocity.Y);
        Vector2 direction=reflectedVelocity/ProjectileReflection.Speed;
        if(stinger!=null) {
            stingerParent=source.transform.parent;
            if(stingerParent==null || stingerParent.name!="Stingers" || stingerParent.parent==null
                || !stingerParent.parent.name.StartsWith("Hive Knight Glob",StringComparison.Ordinal)
                || stingerParent.gameObject.scene!=boss.gameObject.scene)
                throw new InvalidOperationException("Native Hive needle parent changed.");
            stingerLocalRotation=source.transform.localRotation;stingerLocalScale=source.transform.localScale;
            detachedStinger=true;source.transform.SetParent(null,true);
            // direction is the native authored world flight angle. Sprite scales
            // are mirrored/nonuniform, so retain that artwork basis explicitly.
            float turn=Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg-stinger.direction;
            source.transform.rotation=Quaternion.AngleAxis(turn,Vector3.forward)*source.transform.rotation;
            Physics2D.SyncTransforms();
            // Rotating an offset needle collider must not move its launch point.
            var shift=previous-(Vector2)shape.bounds.center;
            var corrected=source.transform.position+new Vector3(shift.x,shift.y,0);
            body.position=(Vector2)corrected;source.transform.position=corrected;
            Physics2D.SyncTransforms();
            log?.Invoke("HK_HIVE_NEEDLE_REFLECTED id="+shape.GetInstanceID()+" activation="+activation+" detached=True authoredAngle="+stinger.direction
                +" launch="+previous+" aim="+aim+" velocity="+reflectedVelocity+" poolRotationRestore=True frame="+Time.frameCount);
        }
        if(comb!=null) {
            // Detach only the parried sword. The native comb and every sibling
            // continue their authored tween/cleanup without being stopped.
            combParent=source.transform.parent;detachedComb=true;
            source.transform.SetParent(null,true);
            log?.Invoke("HK_RADIANCE_COMB_REFLECTED parent="+comb.GetInstanceID()+" source="+source.GetInstanceID()+" nativeSpeed="+speed+" siblingsUnchanged=True frame="+Time.frameCount);
        }
        if(hiveBee) {
            beeRecoil=source.GetComponent<Recoil>();originalBeeRecoil=beeRecoil?.enabled==true;
            beeRecoil?.CancelRecoil();if(beeRecoil!=null)beeRecoil.enabled=false;
            var recoilFsm=source.GetComponents<PlayMakerFSM>().Single(f=>f.FsmName=="Recoil");
            beeRecoilLease=new NativeFsmStepLease(recoilFsm.Fsm);
        }
        if(control?.FsmName=="xero_nail" || control?.FsmName=="Fall Barrel Control" || RadianceNailName(source.name) || hiveBee || source.name.StartsWith("Hornet Barb",StringComparison.Ordinal) || SoulOrb(source)==control && control!=null) {
            stepLease=new NativeFsmStepLease(control!.Fsm);
            if(SoulOrb(source)==control && control.ActiveStateName=="Orbiting" && !RadianceOrbName(control.name)) {
                var parent=control.transform.parent;
                if(parent==null || parent.name!="Orb Spinner" || parent.gameObject.scene!=boss.gameObject.scene)
                    throw new InvalidOperationException("Native reflected orbit spinner ownership changed.");
                orbitParent=parent;detachedOrbit=true;orbitFlightSamples=0;
                iTween.Stop(control.gameObject);
                NativeOrbTweenCompletion.CompleteStopped(control.gameObject,parent,shape.GetInstanceID(),activation,log);
                control.transform.SetParent(null,true);
                log?.Invoke("HK_SOUL_ORBIT_DETACHED id="+shape.GetInstanceID()+" activation="+activation+" root="+control.gameObject.GetInstanceID()
                    +" spinner="+parent.gameObject.GetInstanceID()+" worldPosePreserved=True nativeTweenStopped=True fsmEnabled="+control.enabled+" frame="+Time.frameCount);
            }
        }
        else if (control != null) control.enabled = false;
        originalGravity=body.gravityScale;originalDamping=body.linearDamping;gravityLease=true;
        body.gravityScale=0;body.linearDamping=0;
        if(stinger!=null) stinger.enabled=false;
        var originalVelocity = incomingVelocity;
        body.linearVelocity = reflectedVelocity;
        // Markoth nails orient their point to native flight; PV swords retain
        // their accepted native sprite rotation behavior.
        if (native?.FsmName=="xero_nail" || RadianceNailName(source.name) || source.name.StartsWith("Shot Markoth Nail", StringComparison.Ordinal) || source.name.StartsWith("Shot Slug Spear",StringComparison.Ordinal))
            source.transform.Rotate(0, 0, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg
                - Mathf.Atan2(originalVelocity.y, originalVelocity.x) * Mathf.Rad2Deg);
        if(hiveBee) {
            // Native bees face by flight angle, not by flipping their X scale.
            var face=control!.FsmStates.Single(s=>s.Name=="Swarm").Actions
                .OfType<HutongGames.PlayMaker.Actions.FaceAngleV2>().Single();
            float angle=Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg+face.angleOffset.Value;
            if(face.worldSpace.Value)source.transform.eulerAngles=new Vector3(0,0,angle);
            else source.transform.localEulerAngles=new Vector3(0,0,angle);
            log?.Invoke("HK_HIVE_BEE_REFLECTED_FACING angle="+angle+" direction="+direction+" nativeAngleOffset="+face.angleOffset.Value+" recoilSuspended=True");
        }
        damage.enabled = false; reflected = true; expires = Time.time + 3;
        var orbKind=SoulOrb(source);
        string kind=orbKind!=null?(RadianceOrbName(orbKind.name)?"RadianceOrb":"SoulOrb"):RadianceNailName(source.name)?"RadianceNail":"Other";
        log?.Invoke("HK_PROJECTILE_REFLECTED id=" + shape.GetInstanceID() + " activation=" + activation + " speed=" + speed + " nativeBody=True damage="+outgoingDamage+" kind="+kind+" frame=" + Time.frameCount);
    }
    private void FixedUpdate()
    {
        if (!reflected) return;
        if(detachedOrbit && control!=null && orbitFlightSamples++<3 && SessionConfiguration.Current.RunFixtures && SessionConfiguration.Current.Has("trace-probe"))
            log?.Invoke("HK_SOUL_ORBIT_FLIGHT id="+shape.GetInstanceID()+" activation="+activation+" root="+control.gameObject.GetInstanceID()+" parent="+(control.transform.parent?.name.Replace(' ','_')??"none")
                +" position="+control.transform.position+" velocity="+flight.linearVelocity+" frame="+Time.frameCount);
        if (damage == null || !damage.gameObject.activeInHierarchy) { reflected = false;stepLease?.Dispose();stepLease=null;RestoreGravity(); return; }
        if(target==null) {End(false,"target-removed");return;}
        if(target.isDead) {End(false,"native-target-dead");return;}
        if(targetLease?.Invoke()==false) {End(false,"target-activation-ended");return;}
        if(Time.time>=expires) {End(false,"flight-timeout");return;}
        if(control?.FsmName=="Fall Barrel Control" && (control.ActiveStateName=="Pause" || control.ActiveStateName=="Break" || control.ActiveStateName=="Recycle")) {End(false,"native-cleanup");return;}

        // Physics remains native; prevent gravity, damping or residual native
        // velocity writes from changing the shared reflected-flight speed.
        flight.linearVelocity=reflectedVelocity;
        var motion = reflectedVelocity * Time.fixedDeltaTime;
        var filter = new ContactFilter2D { useTriggers = true };
        int count = shape.Cast(motion.normalized, filter, sweep, motion.magnitude);
        if (count == sweep.Length) throw new InvalidOperationException("Reflected projectile sweep capacity exceeded.");
        Collider2D? collision = null; float distance = float.PositiveInfinity;
        for (int i = 0; i < count; i++) {
            var hit = sweep[i];
            if (!IsReceiver(hit.collider) && !MarkothShieldPolicy.IsShield(hit.collider) && !IsTerrain(hit.collider)) continue;
            if (hit.distance < distance || hit.distance == distance && MarkothShieldPolicy.IsShield(hit.collider)) { collision = hit.collider; distance = hit.distance; }
        }
        // A moving native shield may already overlap this projectile before its
        // next step; shield contact wins a simultaneous receiver overlap.
        int overlapping = Physics2D.OverlapBox(shape.bounds.center, shape.bounds.size, 0, filter, overlaps);
        if (overlapping == overlaps.Length) throw new InvalidOperationException("Reflected overlap capacity exceeded.");
        for (int i = 0; i < overlapping; i++)
            if (MarkothShieldPolicy.IsShield(overlaps[i]) && Physics2D.Distance(shape, overlaps[i]).isOverlapped) { collision = overlaps[i]; distance = 0; break; }
        if (collision != null) {
            if (MarkothShieldPolicy.IsShield(collision)) {
                log?.Invoke("HK_REFLECTED_SHIELD_BLOCK projectile=" + shape.GetInstanceID() + " activation=" + activation + " shield=" + collision.GetInstanceID() + " target=" + health.Entity + " targetActivation=" + health.Activation + " damage=0 frame=" + Time.frameCount);
                End(false);
            } else if(IsTerrain(collision)) {
                log?.Invoke("HK_REFLECTED_TERRAIN_POP projectile="+shape.GetInstanceID()+" activation="+activation+" terrain="+collision.GetInstanceID()+" damage=0 frame="+Time.frameCount);
                End(false);
            } else End(true,"receiver-collision");
            return;
        }
        foreach (var receiver in receivers)
            if (receiver.enabled && receiver.gameObject.activeInHierarchy && Physics2D.Distance(shape, receiver).isOverlapped) { End(true); return; }
    }
    private bool IsReceiver(Collider2D collider) => collider.GetComponent<HealthManager>() == target
        || collider.GetComponent<ExtraDamageableProxy>() != null && collider.GetComponentInParent<HealthManager>() == target;
    private void End(bool hit,string reason="resolved")
    {
        if (!reflected) return; reflected = false;
        log?.Invoke("HK_REFLECTED_END id="+shape.GetInstanceID()+" activation="+activation+" hit="+hit+" reason="+reason+" nativeState="+control?.ActiveStateName
            +" position="+transform.position+" target="+(target==null?"removed":target.transform.position.ToString())+" frame="+Time.frameCount);
        stepLease?.Dispose();stepLease=null;
        RestoreGravity();
        RestoreCombParent();
        if (hit && target != null) {
            int before = target.hp;
            var nativeHit=new HitInstance { Source = gameObject, AttackType = AttackTypes.Spell, DamageDealt = outgoingDamage, Multiplier = 1, MagnitudeMultiplier = 1,
                Direction = Mathf.Repeat(Mathf.Atan2(target.transform.position.y - transform.position.y, target.transform.position.x - transform.position.x) * Mathf.Rad2Deg, 360) };
            int after;
            if(EncounterProfile.Selected.ArmorAndHead) after=HostHitReceiver.ReflectedArmorHit(target,nativeHit);
            else {
                // Death Ready requires the native finale event; ordinary Hit
                // can kill HP without advancing that authored state machine.
                bool finale=AbsoluteRadianceCombat.TryFinalNail(target,HitKind.ProjectileReflected,log??(_=>{}));
                if(!finale)target.Hit(nativeHit);
                after=target==null?0:Math.Max(0,target.hp);
            }
            if (!health.ObserveNative(health.Entity, health.Activation, after)) throw new InvalidOperationException("Reflected native health observation refused.");
            log?.Invoke("HK_REFLECTED_HEALTH entity=" + health.Entity + " targetActivation=" + health.Activation + " health=" + health.Health + " projectileActivation=" + activation + " frame=" + Time.frameCount);
            log?.Invoke("HK_REFLECTED_HIT id=" + shape.GetInstanceID() + " activation=" + activation + " before=" + before + " after=" + after + " collision=True frame=" + Time.frameCount);
        }
        RecycleSword(damage,target==null || target.isDead || target.hp<=0);
        if(detachedOrbit && control!=null)
            log?.Invoke("HK_SOUL_ORBIT_IMPACT id="+shape.GetInstanceID()+" activation="+activation+" state="+control.ActiveStateName.Replace(' ','_')
                +" parent="+(control.transform.parent?.name.Replace(' ','_')??"none")+" frame="+Time.frameCount);
        if(control?.FsmName=="Fall Barrel Control")
            log?.Invoke("HK_DEBRIS_NATIVE_CLEANUP id="+shape.GetInstanceID()+" activation="+activation+" state="+control.ActiveStateName
                +" colliderEnabled="+shape.enabled+" frame="+Time.frameCount);
        if(stinger!=null || hiveBee)
            log?.Invoke("HK_HIVE_PROJECTILE_CLEANUP id="+shape.GetInstanceID()+" activation="+activation+" kind="+(hiveBee?"bee":"stinger")
                +" state="+(control?.ActiveStateName??"inactive")+" objectActive="+damage.gameObject.activeInHierarchy+" colliderEnabled="+shape.enabled+" frame="+Time.frameCount);
    }
    public void Retire() { if (reflected && damage != null && damage.gameObject.activeInHierarchy) End(false); }
    private void RestoreBeeRecoil(){beeRecoilLease?.Dispose();beeRecoilLease=null;if(beeRecoil!=null)beeRecoil.enabled=originalBeeRecoil;beeRecoil=null;}
    private void RestoreGravity(){RestoreBeeRecoil();if(gravityLease && flight!=null) {flight.gravityScale=originalGravity;flight.linearDamping=originalDamping;log?.Invoke("HK_REFLECTED_PHYSICS_RESTORED id="+shape.GetInstanceID()+" activation="+activation+" gravity="+flight.gravityScale+" frame="+Time.frameCount);}gravityLease=false;}
    private void RestoreCombParent(){var parent=combParent;bool detached=detachedComb;detachedComb=false;combParent=null;if(detached && damage!=null && parent!=null)damage.transform.SetParent(parent,true);}
    private void RestoreStingerParent() {
        if(!detachedStinger)return;detachedStinger=false;
        if(stinger!=null && stingerParent!=null) {
            stinger.transform.SetParent(stingerParent,true);
            stinger.transform.localRotation=stingerLocalRotation;
            stinger.transform.localScale=stingerLocalScale;
        }
        stingerParent=null;
    }
    private void OnDisable() { reflected = false;stepLease?.Dispose();stepLease=null;RestoreGravity();
        RestoreCombParent();
        if(detachedOrbit && control!=null && orbitParent!=null)control.transform.SetParent(orbitParent,true);
        detachedOrbit=false;orbitParent=null;
        RestoreStingerParent();
        if(stinger!=null) stinger.enabled=originalStinger; if (damage != null) damage.enabled = originalDamage; if (control != null) control.enabled = originalControl; }
}
