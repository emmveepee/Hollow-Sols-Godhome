using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YiGodhome.Bridge;

namespace YiGodhome;

internal sealed class HornetAttackPolicy
{
    private readonly HealthManager boss;
    private readonly PlayMakerFSM control;
    private readonly DamageHero needle;
    private readonly Action<string>? log;
    private readonly Dictionary<DamageHero, (PlayMakerFSM Fsm, bool Returning, long Action)> needles = new();
    private string state = "";
    private long action;
    private bool kick, crimson, windup;
    private bool counterSequence;
    private long counterAction;
    private Collider2D? airShape;
    private bool airActive;
    private int airEvidence,airFrames,airOverlap,airGroundedOverlap,airLastFrame=-1;
    private float airMinDistance;
    internal HornetAttackPolicy(HealthManager boss, Action<string>? log)
    {
        this.boss = boss; this.log = log;
        string[] required = { "GDash Antic", "G Dash", "GDash Recover1", "Sphere", "Sphere A", "Sphere Recover", "Sphere Recover A", "Stun Start", "Stun Recover" };
        control = boss.GetComponents<PlayMakerFSM>().Single(f => required.All(s => f.FsmStates.Any(v => v.Name == s)));
        var thrown = control.FsmStates.Single(s => s.Name == "Throw").Actions.OfType<HutongGames.PlayMaker.Actions.ActivateGameObject>().Single(a => a.activate.Value && a.gameObject.GameObject.Value != null && a.gameObject.GameObject.Value.name == "Needle");
        var nativeNeedle = thrown.gameObject.GameObject.Value;
        if (nativeNeedle == null || nativeNeedle.name != "Needle" || nativeNeedle.scene != boss.gameObject.scene)
            throw new InvalidOperationException("Hornet authored needle ownership changed.");
        needle = nativeNeedle.GetComponent<DamageHero>();
        if (needle == null) throw new InvalidOperationException("Hornet needle damage receiver missing.");
        if(SessionConfiguration.Current.RunFixtures && SessionConfiguration.Current.Has("trace-probe")) {
            var activation=control.FsmStates.Single(s=>s.Name=="Fire").Actions.OfType<HutongGames.PlayMaker.Actions.ActivateGameObject>().Single(a=>a.activate.Value && a.gameObject.GameObject.Value?.name=="Hit ADash");
            var attack=FSMUtility.GetSafe(activation.gameObject,activation);
            if(attack==null || attack.GetComponentInParent<HealthManager>()!=boss)throw new InvalidOperationException("Hornet native air-dash evidence owner changed.");
            airShape=attack.GetComponent<PolygonCollider2D>()??throw new InvalidOperationException("Hornet native air-dash polygon missing.");
        }
    }
    internal bool Present => kick || crimson || windup;
    internal HealthManager Boss => boss;
    internal bool SuppressDamage(DamageHero damage) => boss.gameObject.scene.name=="GG_Hornet_2" && counterSequence
        && damage!=needle && damage.GetComponentInParent<HealthManager>()==boss
        && damage.name!="Hit Counter 1" && damage.name!="Hit Counter 2";
    internal void Update(ref long nextAction)
    {
        if (boss == null) return;
        string current = control.ActiveStateName;
        bool counter=current=="CA Antic" || current=="CA 1" || current=="CA 2" || current=="CA Recover";
        if(counter && !counterSequence) counterAction=++nextAction;
        counterSequence=counter;
        bool w = current == "GDash Antic" || current == "Sphere Antic G" || current == "Sphere Antic A";
        bool k = current == "G Dash";
        bool c = current == "Sphere" || current == "Sphere A" || current == "Sphere Antic G" || current == "Sphere Antic A";
        if ((w || k || c) && !Present) action = ++nextAction;
        windup = w; kick = k; crimson = c;
        if (state != current) {
            state = current;
            if (Present) log?.Invoke("HK_HORNET_ATTACK entity=" + boss.GetInstanceID() + " state=" + current.Replace(' ', '_') + " action=" + action + " kick=" + kick + " crimson=" + crimson + " frame=" + Time.frameCount);
        }
        foreach (var dead in needles.Keys.Where(d => d == null).ToArray()) needles.Remove(dead);
        if(airShape!=null && airEvidence<16) {
            bool airborne=current=="A Dash";
            if(airActive && !airborne) {
                log?.Invoke("HK_HORNET_AIR_TARGET_EVIDENCE entity="+boss.GetInstanceID()+" collider="+airShape.GetInstanceID()+" sample="+(++airEvidence)+" frames="+airFrames
                    +" minNativeDistance="+airMinDistance+" nativeOverlapFrames="+airOverlap+" groundedOverlapFrames="+airGroundedOverlap+" aimedOffsetY=-0.5 frame="+Time.frameCount);
            }
            if(airborne && !airActive){airFrames=airOverlap=airGroundedOverlap=0;airMinDistance=float.PositiveInfinity;}
            airActive=airborne;
            if(airborne && airFrames<128 && airLastFrame!=Time.frameCount && airShape.enabled && airShape.gameObject.activeInHierarchy && HeroController.instance?.GetComponent<BoxCollider2D>() is BoxCollider2D target) {
                airLastFrame=Time.frameCount;airFrames++;
                var distance=Physics2D.Distance(airShape,target);airMinDistance=Math.Min(airMinDistance,distance.distance);
                if(distance.isOverlapped){airOverlap++;if(HeroController.instance.cState.onGround)airGroundedOverlap++;}
            }
        }
    }
    internal void Decorate(AttackVolume volume, DamageHero damage, ref long nextAction)
    {
        if (damage != needle && damage.GetComponentInParent<HealthManager>() != boss) return;
        if(damage!=needle) volume.CounterInjuryEntity=boss.GetInstanceID();
        if(damage.name=="Hit ADash" && state=="A Dash")NativeBodyStrike.Combine(volume,boss);
        if(damage.name=="Hit Counter 1" || damage.name=="Hit Counter 2") {
            volume.Active &= counterSequence && (state=="CA 1" || state=="CA 2");
            if(boss.gameObject.scene.name=="GG_Hornet_2")
                volume.Active &= state=="CA 1"?damage.name=="Hit Counter 1":damage.name=="Hit Counter 2";
            volume.ActionId=counterAction;
            volume.Traits|=AttackTraits.RetireOnParry;
        } else if (damage.name == "Hit GDash") {
            volume.Active &= kick; volume.ActionId = action;
            volume.Traits |= AttackTraits.Kickable;
        } else if (damage.name == "Sphere Ball") {
            volume.Active &= crimson && !windup; volume.ActionId = action;
            volume.Traits |= AttackTraits.Crimson;
        } else if (damage.name == "Needle") {
            if (!needles.TryGetValue(damage, out var entry)) {
                if (needles.Count >= 16) throw new InvalidOperationException("Hornet needle owner capacity exceeded.");
                var fsm = damage.GetComponents<PlayMakerFSM>().Single(f => f.FsmName == "Control");
                if (!new[] { "Out", "Decel", "Return", "Notify" }.All(s => fsm.FsmStates.Any(v => v.Name == s)))
                    throw new InvalidOperationException("Hornet native needle contract changed.");
                entry = (fsm, false, ++nextAction);
            }
            bool returning = entry.Fsm.ActiveStateName == "Return";
            if (returning != entry.Returning) {
                entry = (entry.Fsm, returning, ++nextAction);
                log?.Invoke("HK_HORNET_NEEDLE_LEG entity=" + damage.GetInstanceID() + " returning=" + returning + " action=" + entry.Action + " frame=" + Time.frameCount);
            }
            needles[damage] = entry;
            volume.Active &= returning || entry.Fsm.ActiveStateName == "Out" || entry.Fsm.ActiveStateName == "Decel";
            volume.ActionId = entry.Action; volume.ConsumeOnContact = false;
            volume.Traits |= AttackTraits.RetireOnParry;
        }
    }
    internal void Target(SummonedTarget target) { if (target.Entity == boss.GetInstanceID()) { target.Kickable = kick; target.AttackId = kick ? action : 0; } }
    internal void Presentation(ContactFrame frame)
    {
        if (!Present) return;
        var b = boss.GetComponent<Collider2D>().bounds;
        frame.AttackId = frame.CrimsonSequence = action; frame.CrimsonCharge = 1;
        frame.Crimson = crimson; frame.Windup = crimson && windup;
        frame.Kickable = kick; frame.KickWindup = windup && !crimson;
        frame.X = b.center.x; frame.Y = b.center.y; frame.Width = Math.Max(1, b.size.x); frame.Height = Math.Max(1, b.size.y);
    }
    internal bool Counter(long id, HitKind kind, int entity)
    {
        if (id != action || entity != 0 && entity != boss.GetInstanceID()) return false;
        if (kick && (kind == HitKind.TaiChiKick || kind == HitKind.UnboundedCounter)) {
            boss.GetComponent<Rigidbody2D>().linearVelocity = Vector2.zero;
            control.SetState("GDash Recover1"); kick = windup = false;
            log?.Invoke("HK_HORNET_KICK entity=" + boss.GetInstanceID() + " action=" + id + " nativeRecovery=True frame=" + Time.frameCount);
            return true;
        }
        if (!crimson || windup || kind != HitKind.UnboundedCounter) return false;
        control.SetState(control.ActiveStateName == "Sphere A" ? "Sphere Recover A" : "Sphere Recover");
        control.SetState("Stun Start");
        crimson = windup = false;
        log?.Invoke("HK_HORNET_CIRCLE_COUNTER entity=" + boss.GetInstanceID() + " action=" + id + " fullNativeStagger=True gameplayTime=" + Time.time + " frame=" + Time.frameCount);
        return true;
    }
}
