using System;
using System.Collections.Generic;
using System.Linq;
using HutongGames.PlayMaker.Actions;
using UnityEngine;
using YiGodhome.Bridge;
namespace YiGodhome;

// Translate authored attack windows; native FSMs retain phase/attack authority.
internal sealed class AbsoluteRadianceCombat : IDisposable
{
    private sealed class Lease { internal DamageHero Source=null!; internal MeshRenderer? Renderer; internal Collider2D? Shape; internal NativeWaveStrikeLease? Wave; internal Transform? Parent; internal PlayMakerFSM? Comb, Orb; internal Rigidbody2D? Motion; internal bool Beam, Nail, Rain, BelowFloor; internal long Native=-1,Activation=-1,Action,Group; }
    private readonly HealthManager body;
    private readonly HealthManager[] visualBody;
    private readonly PlayMakerFSM control, choices, commands, phases;
    private readonly SpawnObjectFromGlobalPool rainEmission;
    private readonly Func<long> allocate;
    private readonly Action<string>? log;
    private readonly Dictionary<int,Lease> leases=new();
    private readonly List<int> dead=new();
    private string lastChoice="",lastCommand="",lastPhase="",lastControl="";
    private long castGroup,beamBurst,visual;
    private bool warning,ending;
    private readonly int finalThreshold;
    internal bool HidePlayer=>body!=null && body.gameObject.scene==UnityEngine.SceneManagement.SceneManager.GetActiveScene()
        && (ending || RadianceAttackWindows.Ending(control.ActiveStateName));
    private readonly Dictionary<Renderer,bool> knightEndingArtwork=new();
    private readonly ActivateGameObject[] knightEndingActivations;
    private bool knightEndingBound;
    private void HideKnightEndingArtwork() {
        if(knightEndingBound)return;
        // Init resolves these scene references asynchronously after binding.
        // Wait for the exact authored references, never search the whole scene.
        foreach(var action in knightEndingActivations)if(FSMUtility.GetSafe(action.gameObject,action)==null)return;
        foreach(var action in knightEndingActivations) {
            var prop=FSMUtility.GetSafe(action.gameObject,action);
            if(prop.name!=action.gameObject.GameObject.Name || prop.scene!=body.gameObject.scene)
                throw new InvalidOperationException("Native Radiance Knight ending artwork owner changed.");
            var parts=prop.GetComponentsInChildren<Renderer>(true);
            if(parts.Length==0 || parts.Length>32)throw new InvalidOperationException("Native Radiance Knight artwork scope changed.");
            foreach(var part in parts)if(!knightEndingArtwork.ContainsKey(part)) {
                knightEndingArtwork.Add(part,part.forceRenderingOff);part.forceRenderingOff=true;
            }
        }
        knightEndingBound=true;
        log?.Invoke("HK_RADIANCE_KNIGHT_END_ART_HIDDEN renderers="+knightEndingArtwork.Count+" nativeEndingUnchanged=True");
    }
    public void Dispose() {
        foreach(var part in knightEndingArtwork)if(part.Key!=null)part.Key.forceRenderingOff=part.Value;
        knightEndingArtwork.Clear();
    }
    internal void ProjectBar(HealthManager h,SummonedTarget target) {
        if(h!=body || !target.BossBar)return;
        var bar=RadiancePresentation.Health(target.Health,target.MaxHealth,finalThreshold,control.ActiveStateName=="Death Ready");
        target.BarHealth=bar.Health;target.BarMaxHealth=bar.Maximum;
    }
    private float? standableFloor;
    private int belowFloorSamples;
    private float warningAt,warningDuration,nextPrune;
    internal AbsoluteRadianceCombat(HealthManager body,Func<long> allocate,Action<string>? log) {
        this.body=body;visualBody=new[]{body};this.allocate=allocate;this.log=log;
        var fsms=body.GetComponents<PlayMakerFSM>();
        control=fsms.Single(f=>f.FsmName=="Control");choices=fsms.Single(f=>f.FsmName=="Attack Choices");
        commands=fsms.Single(f=>f.FsmName=="Attack Commands");phases=fsms.Single(f=>f.FsmName=="Phase Control");
        var rains=control.FsmStates.Where(s=>s.Name=="Rage1 Loop" || s.Name=="Rage Comb")
            .SelectMany(s=>s.Actions.OfType<SpawnObjectFromGlobalPool>().Select(a=>(State:s.Name,Action:a)))
            .Where(p=>p.Action.gameObject.Value!=null && p.Action.gameObject.Value.name=="Radiant Nail Comb").ToArray();
        if(rains.Length!=1)throw new InvalidOperationException("Native Radiance stationary rain spawn manifest changed; expected one authored comb emission.");
        rainEmission=rains[0].Action;
        log?.Invoke("HK_RADIANCE_RAIN_EMISSION state="+rains[0].State.Replace(' ','_')+" exactNativeAction=True");
        if(!control.FsmStates.Any(s=>s.Name=="Death Ready") || !commands.FsmStates.Any(s=>s.Name=="EB 1")
            || !phases.FsmStates.Any(s=>s.Name=="Set Phase 2"))throw new InvalidOperationException("Native Absolute Radiance phase/attack manifest changed.");
        var threshold=control.FsmStates.Single(s=>s.Name=="Final Idle").Actions.OfType<IntCompare>()
            .Single(a=>a.lessThan?.Name=="NEXT");
        finalThreshold=threshold.integer2.Value;
        if(finalThreshold<=0 || finalThreshold>=body.hp)throw new InvalidOperationException("Native Radiance final health comparator changed.");
        var final=control.FsmStates.Single(s=>s.Name=="Death Ready").Actions.OfType<Trigger2dEvent>().Single();
        if(final.sendEvent?.Name!="FINAL HIT")throw new InvalidOperationException("Native Radiance final nail event changed.");
        // These are separate native cutscene props, not the hidden Hero sprite.
        // Keep their FSMs, animation events, sound and transition timing intact.
        knightEndingActivations=new[]{(State:"Knight Break Antic",Name:"Knight Split"),(State:"Knight Break",Name:"Knight Ball")}
            .Select(entry=>control.FsmStates.Single(s=>s.Name==entry.State).Actions.OfType<ActivateGameObject>()
                .Single(a=>a.activate.Value && a.gameObject.GameObject?.Name==entry.Name)).ToArray();
        HideKnightEndingArtwork();
        log?.Invoke("HK_RADIANCE_BOUND entity="+body.GetInstanceID()+" phases=Native attacks=Native finalHit=NativeNailEvent");
    }
    internal bool Owns(HealthManager h)=>h==body;
    internal bool RainSpawn(HutongGames.PlayMaker.FsmStateAction source,GameObject prefab)=>ReferenceEquals(source,rainEmission) && prefab==rainEmission.gameObject.Value;
    internal HealthManager[] VisualBodies=>warning && body!=null?visualBody:Array.Empty<HealthManager>();
    // Beam collision/counter authority remains; beams have no silhouette pass.
    internal MeshRenderer[] VisualParts=>leases.Values.Where(RainVisible).Select(l=>l.Renderer!).ToArray();
    private static bool RainVisible(Lease lease)=>lease.Rain && !lease.BelowFloor && lease.Source!=null && lease.Renderer!=null && BeamVisible(lease.Renderer)
        && lease.Shape?.enabled==true && (lease.Wave?.Traits & AttackTraits.Crimson)==AttackTraits.Crimson;
    internal void SetStandableFloor(float? floor)=>standableFloor=floor;
    internal bool OutsideNail(DamageHero source,UnityEngine.Bounds bounds,long nativeActivation) {
        if(!leases.TryGetValue(source.GetInstanceID(),out var lease) || lease.Native!=nativeActivation || lease.Parent!=source.transform.parent)return false;
        // Whole native hitbox must clear the validated flat floor. No camera,
        // emitter HP or phase guess; above-floor/reparented/reused blades rearm.
        float top=Math.Max(bounds.max.y,lease.Renderer?.bounds.max.y??bounds.max.y);
        bool outside=lease.Nail && standableFloor.HasValue && top<standableFloor.Value-.01f;
        if(outside!=lease.BelowFloor && belowFloorSamples<16 && HollowSolsMenuPlugin.Instance?.FixtureRadiancePerformance==true) {
            belowFloorSamples++;
            log?.Invoke("HK_RADIANCE_NAIL_FLOOR_CULL id="+source.GetInstanceID()+" omitted="+outside+" top="+top+" colliderTop="+bounds.max.y+" floor="+standableFloor+" wholeBounds=True nativeLifetime=True frame="+Time.frameCount);
        }
        lease.BelowFloor=outside;return outside;
    }
    private static bool BeamName(string name)=>name.StartsWith("Radiant Beam",StringComparison.Ordinal) || name=="Ascend Beam";
    private static bool BeamVisible(MeshRenderer renderer)=>renderer!=null && renderer.enabled && renderer.gameObject.activeInHierarchy;
    internal bool HideBar(HealthManager h)=>h==body && (ending || RadianceAttackWindows.Ending(control.ActiveStateName));
    internal bool Damageable(HealthManager h)=>h==body && !ending && !RadianceAttackWindows.Ending(control.ActiveStateName);
    internal bool Counter(long attack,int entity)=>body!=null && !body.isDead && !ending && (entity==0 || entity==body.GetInstanceID()) && attack>0 && leases.Values.Any(l=>l.Action==attack);
    internal AttackTraits? VisualTraits(HealthManager h)=>h==body && warning?AttackTraits.Crimson:(AttackTraits?)null;
    internal void Update() {
        if(body==null)return;
        HideKnightEndingArtwork();
        string choice=choices.ActiveStateName,command=commands.ActiveStateName,phase=phases.ActiveStateName,state=control.ActiveStateName;
        if(RadianceAttackWindows.Ending(state))ending=true;
        if(choice!=lastChoice) {
            if(choice.StartsWith("Nail ",StringComparison.Ordinal) && !choice.EndsWith("Recover",StringComparison.Ordinal)
                || choice=="Orb Wait")castGroup=allocate();
            if(choice.StartsWith("Beam Sweep",StringComparison.Ordinal))beamBurst=allocate();
            lastChoice=choice;
        }
        if(command!=lastCommand) {
            // Each stationary burst is a strike; the visual warning sequence
            // remains independent and is not restarted for bursts two/three.
            if(RadianceAttackWindows.BeamBurstState(command))beamBurst=allocate();
            if(command=="Orb Pos" && (castGroup==0 || state=="Final Idle"))castGroup=allocate();
            lastCommand=command;
        }
        // Native pooled renderers can remain enabled after their attack ends.
        // Authored state windows own the warning, never residual artwork.
        bool now=!ending && !body.isDead && RadianceAttackWindows.BodyWarning(choice,state);
        if(now!=warning)log?.Invoke("HK_RADIANCE_WARNING active="+now+" choice="+choice.Replace(' ','_')+" control="+state.Replace(' ','_')+" authoredWindow=True sweepCue=False frame="+Time.frameCount);
        if(now && !warning) {
            visual=allocate();if(beamBurst==0)beamBurst=allocate();warningAt=Time.time;
            var nativeWait=commands.FsmStates.Single(s=>s.Name=="NF Glow").Actions.OfType<Wait>().FirstOrDefault();
            warningDuration=nativeWait?.time.Value??0;
        }
        warning=now;
        if(state!=lastControl || phase!=lastPhase) {
            log?.Invoke("HK_RADIANCE_PHASE entity="+body.GetInstanceID()+" control="+state.Replace(' ','_')+" phase="+phase.Replace(' ','_')
                +" health="+body.hp+" invincible="+body.IsInvincible+" position="+body.transform.position+" nativeAuthority=True frame="+Time.frameCount);
            lastControl=state;lastPhase=phase;
        }
        if(Time.time>=nextPrune) {
            nextPrune=Time.time+1;dead.Clear();
            foreach(var pair in leases)if(pair.Value.Source==null)dead.Add(pair.Key);
            foreach(var id in dead)leases.Remove(id);
        }
    }
    internal void Decorate(AttackVolume volume,DamageHero damage,long nativeActivation) {
        if(body!=null && (ending || RadianceAttackWindows.Ending(control.ActiveStateName))) {volume.Active=false;return;}
        if(body==null || !volume.Active || volume.Category==AttackCategory.Hazard)return;
        int id=damage.GetInstanceID();
        if(!leases.TryGetValue(id,out var lease)) {
            if(leases.Count>=512)throw new InvalidOperationException("Radiance attack lease capacity exceeded.");
            leases.Add(id,lease=new Lease {Source=damage});
        }
        // Pool activation or direct reparenting invalidates native ownership.
        // Read motion/FSM state each frame; only component identity is retained.
        var parent=damage.transform.parent;
        if(lease.Native!=nativeActivation || lease.Parent!=parent) {
            lease.BelowFloor=false;
            string name=damage.name;
            lease.Beam=BeamName(name);lease.Nail=HostReflectedProjectile.RadianceNailName(name);
            lease.Orb=lease.Beam || lease.Nail?null:HostReflectedProjectile.SoulOrb(damage);
            if(lease.Orb!=null && !HostReflectedProjectile.RadianceOrbName(lease.Orb.name))lease.Orb=null;
            lease.Comb=lease.Nail?HostReflectedProjectile.RadianceComb(damage):null;
            lease.Motion=lease.Nail?damage.GetComponent<Rigidbody2D>():null;
            lease.Shape=damage.GetComponent<Collider2D>();
            lease.Wave=lease.Nail?damage.GetComponentInParent<NativeWaveStrikeLease>():null;
            lease.Rain=lease.Nail && (lease.Wave?.Traits & AttackTraits.Crimson)==AttackTraits.Crimson;
            lease.Renderer=lease.Nail?damage.GetComponent<MeshRenderer>():null;
            if(lease.Native!=nativeActivation && lease.Rain)lease.Action=allocate();
            lease.Native=nativeActivation;lease.Parent=parent;
        }
        bool beam=lease.Beam,nail=lease.Nail,orb=lease.Orb!=null;
        if(!beam && !nail && !orb)return;
        if(nail || orb)volume.Traits|=AttackTraits.Reflectable;
        if(nail && !RadianceAttackWindows.NailFlight(lease.Motion?.linearVelocity.sqrMagnitude>=.001f,
            lease.Comb?.ActiveStateName)) {volume.Active=false;return;}
        if(orb && lease.Orb!.ActiveStateName=="Orbiting") {volume.Active=false;return;}
        if(lease.Activation!=volume.Activation) {
            lease.Activation=volume.Activation;
            // Rays within one stationary burst share a strike; its next burst
            // and every ascent beam enable have independent strike identities.
            if(!lease.Rain)lease.Action=beam?RadianceAttackWindows.BeamAction(damage.name=="Ascend Beam",beamBurst,allocate):0;
            lease.Group=beam?0:castGroup!=0?castGroup:allocate();
        }
        // Eye beams physically attached to Radiance carry counter injury.
        // Independent moving walls, nails and orbs do not injure their emitter.
        volume.CounterInjuryEntity=beam && damage.transform.IsChildOf(body.transform)?body.GetInstanceID():0;
        if(beam) {
            volume.Traits|=AttackTraits.Crimson;volume.ActionId=lease.Action;
        }
        else {
            volume.DamageGroupId=lease.Group;volume.InitialDamageGroupHold=1;
            if(lease.Rain) {volume.Traits|=AttackTraits.Crimson;volume.ActionId=lease.Action;volume.RadianceRainCounter=true;}
        }
    }
    internal bool Presentation(ContactFrame frame) {
        if(body==null || !warning)return false;
        var sprite=body.GetComponent<Renderer>();if(sprite==null)return false;
        // Presentation belongs to the visible emitter's authored warning.
        if(!sprite.enabled || !body.gameObject.activeInHierarchy)return false;
        var bounds=sprite.bounds;
        frame.Crimson=true;frame.Windup=choices.ActiveStateName=="Eye Beam Wait";
        if(frame.Windup)frame.Active=false;
        frame.CrimsonSequence=visual;frame.AttackId=beamBurst;
        frame.CrimsonCharge=warningDuration<=0?1:Mathf.Clamp01((Time.time-warningAt)/warningDuration);
        frame.X=bounds.center.x;frame.Y=bounds.center.y;frame.Width=Math.Max(1,bounds.size.x);frame.Height=Math.Max(1,bounds.size.y);
        return true;
    }
    internal static bool FinalNailWindow(HealthManager target) {
        if(EncounterProfile.Selected.Family!=EncounterProfile.AbsoluteRadiance || target.name!="Absolute Radiance")return false;
        return target.GetComponents<PlayMakerFSM>().Single(f=>f.FsmName=="Control").ActiveStateName=="Death Ready";
    }
    internal static bool TryFinalNail(HealthManager target,HitKind kind,Action<string> log) {
        if(EncounterProfile.Selected.Family!=EncounterProfile.AbsoluteRadiance || kind!=HitKind.Melee && kind!=HitKind.ProjectileReflected || target.name!="Absolute Radiance")return false;
        var fsm=target.GetComponents<PlayMakerFSM>().Single(f=>f.FsmName=="Control");
        if(fsm.ActiveStateName!="Death Ready")return false;
        var native=fsm.FsmStates.Single(s=>s.Name=="Death Ready").Actions.OfType<Trigger2dEvent>().Single();
        if(native.sendEvent?.Name!="FINAL HIT")throw new InvalidOperationException("Native Radiance finale event changed.");
        fsm.SendEvent(native.sendEvent.Name);
        log("HK_RADIANCE_FINAL_NAIL nativeEvent=True kind="+kind+" target="+target.GetInstanceID()+" state="+fsm.ActiveStateName.Replace(' ','_')
            +" nativeHealth="+target.hp+" inSequence="+BossSequenceController.IsInSequence+" nativeStatueSequenceBranchRetained=True frame="+Time.frameCount);
        return true;
    }
}
