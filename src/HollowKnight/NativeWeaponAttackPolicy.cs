using System;
using System.Linq;
using UnityEngine;
using YiGodhome.Bridge;

namespace YiGodhome;

// Native weapon owners are distinct from their boss's health receiver.
// Counter through their authored FSMs without manufacturing another receiver.
internal sealed class NativeWeaponAttackPolicy : IDisposable
{
    private readonly Func<long> allocate;
    private readonly Action<string>? log;
    private HealthManager? boss;
    private GameObject? root;
    private PlayMakerFSM? control,attack;
    private Rigidbody2D? body;
    private MeshRenderer[] art=Array.Empty<MeshRenderer>();
    private DamageHero? spinDamage;
    private HutongGames.PlayMaker.Actions.Wait? recoveryWait;
    private HutongGames.PlayMaker.FsmFloat? originalWait;
    private HutongGames.PlayMaker.Actions.Rotate? recoveryRotation;
    private HutongGames.PlayMaker.Actions.FloatAddV2? recoverySpinChange;
    private HutongGames.PlayMaker.FsmFloat? originalRotationSpeed;
    private HutongGames.PlayMaker.FsmBool? originalSpinGate;
    private bool held,spinning,simulated;
    private long action;
    private float spinBegan,spinAntic,remaining,maxDrift;
    private Vector3 position;
    private int clock=-1;
    private int cueBoundsSamples;
    internal NativeWeaponAttackPolicy(Func<long> allocate,Action<string>? log) {this.allocate=allocate;this.log=log;}
    internal void Register(HealthManager health) {boss=health;}
    private bool Alive=>boss!=null && !boss.isDead && boss.hp>0 && boss.gameObject.activeInHierarchy && root!=null && root.activeInHierarchy;
    private static Transform? Owner(DamageHero damage)
    {
        var t=damage.transform;
        while(t!=null && t.name!="Galien Hammer" && t.name!="Galien Mini Hammer(Clone)") t=t.parent;
        return t;
    }
    private void Bind(Transform owner)
    {
        if(root==owner.gameObject) return;
        if(root!=null) throw new InvalidOperationException("Duplicate native Galien main weapon.");
        root=owner.gameObject;
        control=root.GetComponents<PlayMakerFSM>().Single(f=>f.FsmName=="Control");
        attack=root.GetComponents<PlayMakerFSM>().Single(f=>f.FsmName=="Attack");
        foreach(var state in new[]{"Start Spin","Spinning","Decel"})
            if(!control.FsmStates.Any(s=>s.Name==state)) throw new InvalidOperationException("Native weapon spin contract changed.");
        foreach(var state in new[]{"Decel","Recel","Anim","Chase"})
            if(!attack.FsmStates.Any(s=>s.Name==state)) throw new InvalidOperationException("Native weapon recovery contract changed.");
        spinAntic=control.FsmStates.Single(s=>s.Name=="Start Spin").Actions.OfType<HutongGames.PlayMaker.Actions.Wait>().Single().time.Value;
        body=root.GetComponent<Rigidbody2D>()??throw new InvalidOperationException("Native weapon body missing.");
        art=root.GetComponents<MeshRenderer>();
        if(art.Length!=1) throw new InvalidOperationException("Native weapon art owner changed.");
        var sources=root.GetComponentsInChildren<DamageHero>(true);
        if(sources.Length>4) throw new InvalidOperationException("Native weapon damage closure exceeded.");
        spinDamage=sources.Single(d=>d.name=="Hit Spin");
        if(!sources.Any(d=>d.name=="Hit Idle")) throw new InvalidOperationException("Native weapon dormant shape missing.");
        var animator=root.GetComponent<tk2dSpriteAnimator>()??throw new InvalidOperationException("Native weapon animator missing.");
        string idle=attack.FsmStates.Single(s=>s.Name=="Decel").Actions.OfType<HutongGames.PlayMaker.Actions.Tk2dPlayAnimation>().Single().clipName.Value;
        var idleClip=animator.GetClipByName(idle);
        if(idle!="Weapon Idle" || idleClip==null || idleClip.frames.Length==0)throw new InvalidOperationException("Native weapon idle animation changed.");
        var decel=attack.FsmStates.Single(s=>s.Name=="Decel");
        recoveryWait=decel.Actions.OfType<HutongGames.PlayMaker.Actions.Wait>().Single();
        recoveryRotation=decel.Actions.OfType<HutongGames.PlayMaker.Actions.Rotate>().Single();
        recoverySpinChange=decel.Actions.OfType<HutongGames.PlayMaker.Actions.FloatAddV2>().Single();
        if(!recoveryRotation.perSecond || FSMUtility.GetSafe(recoveryRotation.gameObject,recoveryRotation)!=root
            || recoveryRotation.zAngle.Name!=recoverySpinChange.floatVariable.Name)
            throw new InvalidOperationException("Native weapon idle rotation owner changed.");
        if(recoveryWait.realTime || recoveryWait.finishEvent.Name!="FINISHED"
            || !decel.Transitions.Any(t=>t.EventName=="FINISHED" && t.ToState=="Recel"))
            throw new InvalidOperationException("Native weapon recovery wait changed.");
        log?.Invoke("HK_NATIVE_WEAPON_BOUND owner="+root.name.Replace(' ','_')+" entity="+(boss?.GetInstanceID()??0)+" sources="+sources.Length+" nativeFSM=True");
        Update();
    }
    internal void Update()
    {
        if(!Alive) {Release(false);spinning=false;return;}
        if(held) {
            if(GameManager.instance?.IsGamePaused()!=true) {
                maxDrift=Math.Max(maxDrift,Vector3.Distance(root!.transform.position,position));root.transform.position=position;
                if(body!=null) body.linearVelocity=Vector2.zero;
                if(clock!=Time.frameCount) {remaining-=Time.deltaTime;clock=Time.frameCount;}
                // Native Decel keeps stepping: it owns idle artwork, rotation,
                // sound. Its stun-only rotation stays slow; the native Recel /
                // Start Spin states own acceleration when attacking resumes.
                if(remaining<=0) Release(true);
            }
        }
        bool next=!held && (control!.ActiveStateName=="Start Spin" || control.ActiveStateName=="Spinning");
        if(next && !spinning) {action=allocate();spinBegan=Time.time;log?.Invoke("HK_NATIVE_WEAPON_SPIN action="+action+" owner="+root!.name.Replace(' ','_')+" frame="+Time.frameCount);}
        spinning=next;
    }
    internal void Decorate(AttackVolume volume,DamageHero damage)
    {
        var owner=Owner(damage);
        if(owner==null) return;
        volume.ParryCooldown=1;volume.CooldownOnDamage=true;
        if(owner.name=="Galien Mini Hammer(Clone)") return;
        Bind(owner);
        volume.ActionId=action;volume.Active &= Alive && !held;
        if(damage==spinDamage) {
            volume.Traits|=AttackTraits.Crimson;
            volume.Active &= control!.ActiveStateName=="Spinning";
        }
        else if(control!.ActiveStateName=="Spinning" || control.ActiveStateName=="Start Spin") volume.Active=false;
    }
    internal MeshRenderer[] VisualParts=>Alive && spinning ? art : Array.Empty<MeshRenderer>();
    internal bool Precharge=>Alive && spinning && control!.ActiveStateName=="Start Spin";
    internal bool Presentation(ContactFrame frame)
    {
        if(!Alive || !spinning) return false;
        var collider=spinDamage!.GetComponent<Collider2D>();
        if(collider==null) throw new InvalidOperationException("Native spin collider missing.");
        bool shapeActive=collider.enabled && collider.gameObject.activeInHierarchy;
        var bounds=shapeActive?collider.bounds:art[0].bounds;
        if(SessionConfiguration.Current.RunFixtures && Precharge && cueBoundsSamples++<24)
            log?.Invoke("HK_NATIVE_WEAPON_CUE_BOUNDS action="+action+" shapeActive="+shapeActive+" width="+bounds.size.x+" height="+bounds.size.y+" nativeArtwork=True frame="+Time.frameCount);
        if(bounds.size.x<=0 || bounds.size.y<=0) return false;
        frame.X=bounds.center.x;frame.Y=bounds.center.y;frame.Width=bounds.size.x;frame.Height=bounds.size.y;
        frame.AttackId=frame.CrimsonSequence=action;frame.Crimson=true;frame.Windup=Precharge;
        frame.CrimsonCharge=spinAntic>0?Mathf.Clamp01((Time.time-spinBegan)/spinAntic):1;
        frame.CrimsonRight=root!.transform.localScale.x>=0;frame.Kickable=frame.KickWindup=false;
        return true;
    }
    internal bool Counter(long strike,HitKind kind)
    {
        if(kind!=HitKind.UnboundedCounter || strike!=action || !Alive || held || control!.ActiveStateName!="Spinning") return false;
        position=root!.transform.position;simulated=body!.simulated;
        originalWait=recoveryWait!.time;recoveryWait.time=new HutongGames.PlayMaker.FsmFloat {Value=3};
        // Extending Decel also extends its additive spin-speed update. Prevent
        // that accumulator from running past rest into a fast reverse spin.
        originalRotationSpeed=recoveryRotation!.zAngle;
        originalSpinGate=recoverySpinChange!.activeBool;
        recoveryRotation.zAngle=new HutongGames.PlayMaker.FsmFloat {Value=35};
        recoverySpinChange.activeBool=new HutongGames.PlayMaker.FsmBool {Value=false};
        held=true;remaining=3;clock=Time.frameCount;maxDrift=0;spinning=false;
        try {
            attack!.SetState("Decel");
            if(control.ActiveStateName!="Decel") throw new InvalidOperationException("Native weapon DECEL event did not reach spin controller; no replay.");
            body.linearVelocity=Vector2.zero;body.simulated=false;
        } catch {Release(false);throw;}
        log?.Invoke("HK_NATIVE_WEAPON_COUNTER action="+action+" state=Decel nativeIdle=True hold=3 frame="+Time.frameCount);
        return true;
    }
    private void Release(bool resume)
    {
        if(!held) return;
        held=false;
        if(originalWait!=null){recoveryWait!.time=originalWait;originalWait=null;}
        if(originalRotationSpeed!=null){recoveryRotation!.zAngle=originalRotationSpeed;originalRotationSpeed=null;}
        if(originalSpinGate!=null){recoverySpinChange!.activeBool=originalSpinGate;originalSpinGate=null;}
        if(body!=null) {body.simulated=simulated;body.linearVelocity=Vector2.zero;}
        // The native Wait may already have advanced in this frame. Do not
        // re-enter Recel and replay its OnEnter when it has done so naturally.
        if(resume && Alive && attack!.ActiveStateName=="Decel")attack.SetState("Recel");
        log?.Invoke("HK_NATIVE_WEAPON_HOLD_END action="+action+" gameplaySeconds="+(3-remaining)+" maxDrift="+maxDrift+" nativeResume="+resume+" frame="+Time.frameCount);
    }
    public void Dispose()=>Release(false);
}
