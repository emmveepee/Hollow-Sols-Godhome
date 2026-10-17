using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YiGodhome.Bridge;
using HarmonyLib;

namespace YiGodhome;

// Pinned native states select strikes; native FSMs still own the attack and exit.
// Requested stationary counter recoveries use paused gameplay time on the boss.
internal sealed class NativeAuthoredMoves : IDisposable
{
    private sealed class Move {
        internal readonly string[] Antic, Active, Retained;
        internal readonly AttackTraits Traits;
        internal readonly string? Exit;
        internal readonly float Hold;
        internal readonly bool Body;
        internal readonly string? HoldAnimation, AfterHold;
        internal readonly bool StrongPush;
        internal readonly int RequiredCounters;
        internal readonly string? ResumeAfterStagger;
        internal Move(string[] antic,string[] active,AttackTraits traits,string? exit,float hold=0,bool body=true,string? holdAnimation=null,string? afterHold=null,bool strongPush=false,int requiredCounters=1,string? resumeAfterStagger=null,string[]? retained=null) { Antic=antic; Active=active; Retained=retained??Array.Empty<string>(); Traits=traits; Exit=exit; Hold=hold; Body=body; HoldAnimation=holdAnimation; AfterHold=afterHold; StrongPush=strongPush; RequiredCounters=requiredCounters; ResumeAfterStagger=resumeAfterStagger; }
        internal bool IsRetained(string state)=>Array.IndexOf(Retained,state)>=0;
        internal bool Contains(string state)=>Array.IndexOf(Antic,state)>=0 || Array.IndexOf(Active,state)>=0 || IsRetained(state);
    }
    private sealed class Actor {
        internal PairStrike? Pair;
        internal HealthManager Health=null!;
        internal int Entity;
        internal int SequenceSamples;
        internal PlayMakerFSM Control=null!;
        internal Move[] Moves=null!;
        internal GameObject? CounterEffect;
        internal Move? Move;
        internal long Action;
        internal DefenderEruptionSequence? Eruptions;
        internal bool WhiteEruptions;
        internal HutongGames.PlayMaker.FsmFloat? DefenderLaunchY,DefenderApexY;
        internal long CounterRetiredAction;
        internal bool LastChoiceKick;
        internal bool LastChoiceSkyFall, ZoteCounterLanding;
        internal HutongGames.PlayMaker.FsmTransition? ZoteLanding;
        internal HutongGames.PlayMaker.FsmState? ZoteOriginalLanding;
        internal DamageHero? WholeBodySource, TraitorDiveSource;
        internal Move? LastDefensibleMove;
        internal long LastDefensibleAction;
        internal float LastDefensibleAt;
        internal bool Windup, Active, Holding;
        internal NativeFsmStepLease? StepLease;
        internal NativeHeroRangeQuery? RangeQuery;
        internal GameObject? WarriorSlash;
        internal GameObject[] LateWeapons=Array.Empty<GameObject>();
        internal MeshRenderer[] GuardianBeamCleanup=Array.Empty<MeshRenderer>();
        internal GameObject[] BodySources=Array.Empty<GameObject>();
        internal (DamageHero Source,Collider2D Shape)[] SlyWeapons=Array.Empty<(DamageHero,Collider2D)>();
        internal bool SlySwordActive;
        internal readonly HashSet<(int,long)> WeaponEvidence=new();
        internal readonly HashSet<(long,bool)> HeightEvidence=new();
        internal readonly Dictionary<HutongGames.PlayMaker.Fsm,GameObject> VesselEmitters=new();
        internal GameObject? VesselSlamPrefab, TraitorWavePrefab, VesselDashHit;
        internal int VesselVolleyFrame=-1,VesselSpawnSamples;
        internal long VesselVolleyGroup;
        internal float Remaining;
        internal int ClockFrame=-1;
        internal Vector3 Position;
        internal Vector2 Velocity;
        internal Rigidbody2D? Body;
        internal tk2dSpriteAnimator? Animation;
        internal bool AnimationPaused;
        internal bool Simulated;
        internal float HoldDuration;
        internal float MaximumDrift;
        internal string? AfterHold;
        internal string? HoldAnimation;
        internal int LastSprite, SpriteChanges;
        internal tk2dSpriteAnimationClip? GetDown, GetUp, GuardianIdle, StunSettle;
        internal tk2dSpriteAnimationClip? NailmasterAir;
        internal bool NailmasterLaunching, NailmasterKnockdown;
        internal float NailmasterLaunchElapsed, NailmasterGravity, LaunchX, LaunchY, LaunchGravity;
        internal readonly ContactPoint2D[] LandingContacts=new ContactPoint2D[8];
        internal readonly Dictionary<string,HutongGames.PlayMaker.Actions.SendEventByName[]> ArtCompletions=new();
        internal bool GettingUp;
        internal float StunElapsed,StunGroundY;
        internal bool StunSnapshot;
        internal bool HarmlessRecovery;
        internal string ObservedState="";
        internal string StrikeState="";
        internal readonly ConsecutiveCounters CounterStreak=new();
        internal HutongGames.PlayMaker.FsmBool? FinalPhase;
        internal HutongGames.PlayMaker.FsmBool? NailmasterCyclone;
        internal bool NailmasterCycloneWhenTrue;
        internal RigidbodyType2D NailmasterLandingBodyType;
        internal string? StaggerResume;
        internal HutongGames.PlayMaker.FsmTransition? StaggerLanding;
        internal HutongGames.PlayMaker.FsmState? StaggerOriginalLanding;
        internal float SlyFlightGravity;
        internal RigidbodyType2D SlyFlightBodyType;
        internal float StaggerDeadline;
        internal readonly HashSet<int> SuppressedRecoverySources=new();
        internal DamageHero[] SlamPillars=Array.Empty<DamageHero>();
        internal long SlamPillarAction;
        internal int BallProbeStarts;
        internal long SuppressedPaintAction;
    }
    private readonly Dictionary<int,Actor> actors=new();
    private readonly Dictionary<int,Actor> damageOwners=new();
    private readonly EncounterProfile profile;
    private readonly Func<long> allocate;
    private readonly Action<string>? log;
    private readonly List<HealthManager> presented=new();
    private readonly Dictionary<HutongGames.PlayMaker.Actions.WaitRandom,HutongGames.PlayMaker.FsmEvent> repairedWaits=new();
    private sealed class PairStrike {
        internal Actor First=null!,Second=null!;
        internal long Group,FirstSwing,SecondSwing,OtherFirstSwing,OtherSecondSwing;
        internal long Strike(Actor actor,bool second)=>actor==First?(second?SecondSwing:FirstSwing):(second?OtherSecondSwing:OtherFirstSwing);
    }
    private static NativeAuthoredMoves? pairedOwner;
    private static NativeAuthoredMoves? choiceOwner;
    private static NativeAuthoredMoves? defenderOwner;
    private static NativeAuthoredMoves? slyOwner;
    private readonly long defenderDungGroup;
    private sealed class ChoiceScope {
        internal Actor Actor=null!;
        internal HutongGames.PlayMaker.Actions.SendRandomEventV3 Choice=null!;
        internal HutongGames.PlayMaker.FsmFloat[] Weights=null!;
        internal HutongGames.PlayMaker.FsmInt[] Max=null!,Missed=null!;
    }
    private sealed class OtherChoiceScope {
        internal Actor Actor=null!;
        internal HutongGames.PlayMaker.FsmStateAction Choice=null!;
        internal HutongGames.PlayMaker.FsmFloat[] Weights=null!;
        internal HutongGames.PlayMaker.FsmInt[]? Max;
    }
    private readonly Harmony pairHarmony=new("local.yigodhome.nailmaster.paired");
    private int dispatchFrame=-1;
    private HutongGames.PlayMaker.FsmState? dispatchState;
    internal NativeAuthoredMoves(EncounterProfile profile,Func<long> allocate,Action<string>? log) {
        this.profile=profile.Family;this.allocate=allocate;this.log=log;
        if(this.profile==EncounterProfile.WhiteDefender || this.profile==EncounterProfile.DungDefender) {
            defenderDungGroup=allocate();
            if(defenderOwner!=null)throw new InvalidOperationException("Duplicate Defender sequence owner.");
            defenderOwner=this;
            try {pairHarmony.Patch(AccessTools.Method(typeof(HutongGames.PlayMaker.Fsm),"SwitchState",new[]{typeof(HutongGames.PlayMaker.FsmState)}),
                prefix:new HarmonyMethod(typeof(NativeAuthoredMoves),nameof(DefenderState)));}
            catch {defenderOwner=null;pairHarmony.UnpatchSelf();throw;}
        }
        if(this.profile==EncounterProfile.Nosk || this.profile==EncounterProfile.WingedNosk || this.profile==EncounterProfile.TraitorLord || this.profile==EncounterProfile.SoulWarrior || this.profile==EncounterProfile.GreyPrinceZote) {
            if(choiceOwner!=null)throw new InvalidOperationException("Duplicate native attack choice owner.");
            choiceOwner=this;
            try {
                pairHarmony.Patch(AccessTools.Method(typeof(HutongGames.PlayMaker.Actions.SendRandomEventV3),"OnEnter"),prefix:new HarmonyMethod(typeof(NativeAuthoredMoves),nameof(FilterChoice)),finalizer:new HarmonyMethod(typeof(NativeAuthoredMoves),nameof(RestoreChoice)));
                foreach(var type in new[]{typeof(HutongGames.PlayMaker.Actions.SendRandomEventV2),typeof(HutongGames.PlayMaker.Actions.SendRandomEvent)})
                    pairHarmony.Patch(AccessTools.Method(type,"OnEnter"),prefix:new HarmonyMethod(typeof(NativeAuthoredMoves),nameof(FilterOtherChoice)),finalizer:new HarmonyMethod(typeof(NativeAuthoredMoves),nameof(RestoreOtherChoice)));
            }
            catch {choiceOwner=null;pairHarmony.UnpatchSelf();throw;}
        }
        if(this.profile==EncounterProfile.OroMato) {
            if(pairedOwner!=null) throw new InvalidOperationException("Duplicate paired nailmaster owner.");
            pairedOwner=this;
            try {pairHarmony.Patch(AccessTools.Method(typeof(HutongGames.PlayMaker.Actions.SendEventByName),"OnEnter"),prefix:new HarmonyMethod(typeof(NativeAuthoredMoves),nameof(PairedDispatch)));}
            catch {pairedOwner=null;pairHarmony.UnpatchSelf();throw;}
        }
        if(this.profile==EncounterProfile.Sly) {
            if(slyOwner!=null)throw new InvalidOperationException("Duplicate Sly recovery owner.");
            slyOwner=this;
            try {pairHarmony.Patch(AccessTools.Method(typeof(HutongGames.PlayMaker.Fsm),"SwitchState",new[]{typeof(HutongGames.PlayMaker.FsmState)}),
                prefix:new HarmonyMethod(typeof(NativeAuthoredMoves),nameof(SlyRecoveryState)));}
            catch {slyOwner=null;pairHarmony.UnpatchSelf();throw;}
        }
    }
    private static void SlyRecoveryState(HutongGames.PlayMaker.Fsm __instance,HutongGames.PlayMaker.FsmState __0) {
        var owner=slyOwner;if(owner==null || __0?.Name!="Fire D")return;
        foreach(var actor in owner.actors.Values) {
            if(actor.Control.Fsm!=__instance || actor.StaggerResume!="Fire D" || actor.FinalPhase?.Value!=true
                || actor.Health.isDead || actor.Health.hp<=0)continue;
            var body=actor.Body!;float previous=body.gravityScale;
            body.bodyType=actor.SlyFlightBodyType;body.gravityScale=actor.SlyFlightGravity;
            RestoreStaggerLanding(actor);actor.StaggerResume=null;
            owner.log?.Invoke("HK_SLY_FINAL_FLIGHT_RESTORED previousGravity="+previous+" gravity="+body.gravityScale+" bodyType="+body.bodyType
                +" nativeLaunch=Fire_D nativeSpeed=55 nativeDirection=35..145 hp="+actor.Health.hp+" frame="+Time.frameCount);
        }
    }
    private static bool KickDestination(Actor actor,string state,int depth=0) {
        if(actor.Moves.Any(m=>m.Traits==AttackTraits.Kickable && m.Contains(state)))return true;
        if(depth>=2)return false;
        var target=actor.Control.FsmStates.SingleOrDefault(s=>s.Name==state);
        // Traitor Lord's SLASH choice passes through its native feint test.
        return target!=null && (state=="Feint?" || state=="Slash Aim") && target.Transitions.Any(t=>KickDestination(actor,t.ToState,depth+1));
    }
    private static void FilterChoice(HutongGames.PlayMaker.Actions.SendRandomEventV3 __instance,out ChoiceScope? __state) {
        __state=null;var owner=choiceOwner;
        var actor=owner?.actors.Values.FirstOrDefault(a=>a.Control.Fsm==__instance.Fsm);
        if(owner?.profile==EncounterProfile.GreyPrinceZote)return;
        if(actor==null || !actor.LastChoiceKick)return;
        var events=__instance.events;
        if(events.Length>8 || __instance.weights.Length!=events.Length || __instance.eventMax.Length!=events.Length || __instance.missedMax.Length!=events.Length)
            throw new InvalidOperationException("Native attack choice dimensions changed.");
        bool[] blocked=events.Select(e=>__instance.State.Transitions.Any(t=>t.EventName==e.Name && KickDestination(actor,t.ToState))).ToArray();
        if(!blocked.Any(b=>b) || !blocked.Where((b,i)=>!b && __instance.weights[i].Value>0).Any())return;
        var scope=new ChoiceScope {Actor=actor,Choice=__instance,Weights=__instance.weights,Max=__instance.eventMax,Missed=__instance.missedMax};
        __state=scope;
        __instance.weights=(HutongGames.PlayMaker.FsmFloat[])scope.Weights.Clone();
        __instance.eventMax=(HutongGames.PlayMaker.FsmInt[])scope.Max.Clone();
        __instance.missedMax=(HutongGames.PlayMaker.FsmInt[])scope.Missed.Clone();
        for(int i=0;i<events.Length;i++) {
            __instance.eventMax[i]=new HutongGames.PlayMaker.FsmInt {Value=blocked[i]?0:int.MaxValue};
            if(blocked[i]) {
                __instance.weights[i]=new HutongGames.PlayMaker.FsmFloat {Value=0};
                __instance.missedMax[i]=new HutongGames.PlayMaker.FsmInt {Value=int.MaxValue};
            }
        }
        owner!.log?.Invoke("HK_NATIVE_NONCONSECUTIVE_CHOICE entity="+actor.Entity+" state="+__instance.State.Name.Replace(' ','_')+" nativeWeightedSelection=True excluded="+string.Join(",",events.Where((e,i)=>blocked[i]).Select(e=>e.Name)));
    }
    private static void RestoreChoice(ChoiceScope? __state) {
        if(__state==null)return;
        __state.Choice.weights=__state.Weights;__state.Choice.eventMax=__state.Max;__state.Choice.missedMax=__state.Missed;
    }
    private static void FilterOtherChoice(HutongGames.PlayMaker.FsmStateAction __instance,out OtherChoiceScope? __state) {
        __state=null;var owner=choiceOwner;
        var actor=owner?.actors.Values.FirstOrDefault(a=>a.Control.Fsm==__instance.Fsm);
        bool skyFall=owner?.profile==EncounterProfile.GreyPrinceZote && actor?.LastChoiceSkyFall==true && __instance.State.Name=="Fall Through?";
        if(owner?.profile==EncounterProfile.GreyPrinceZote && !skyFall)return;
        if(actor==null || !actor.LastChoiceKick && !skyFall)return;
        var type=__instance.GetType();var weightsField=AccessTools.Field(type,"weights");
        var events=(HutongGames.PlayMaker.FsmEvent[])AccessTools.Field(type,"events").GetValue(__instance);
        var weights=(HutongGames.PlayMaker.FsmFloat[])weightsField.GetValue(__instance);
        if(events.Length>8 || weights.Length!=events.Length)throw new InvalidOperationException("Native attack choice dimensions changed.");
        var blocked=events.Select(e=>__instance.State.Transitions.Any(t=>t.EventName==e.Name && (skyFall?t.ToState=="FT Through":KickDestination(actor,t.ToState)))).ToArray();
        if(!blocked.Any(b=>b) || !blocked.Where((b,i)=>!b && weights[i].Value>0).Any())return;
        var scope=new OtherChoiceScope{Actor=actor,Choice=__instance,Weights=weights};__state=scope;
        var replacement=(HutongGames.PlayMaker.FsmFloat[])weights.Clone();
        for(int i=0;i<events.Length;i++)if(blocked[i])replacement[i]=new HutongGames.PlayMaker.FsmFloat{Value=0};
        weightsField.SetValue(__instance,replacement);
        if(skyFall)actor.LastChoiceSkyFall=false;
        var maxField=AccessTools.Field(type,"eventMax");
        if(maxField!=null) {
            scope.Max=(HutongGames.PlayMaker.FsmInt[])maxField.GetValue(__instance);
            if(scope.Max.Length!=events.Length)throw new InvalidOperationException("Native attack tracking dimensions changed.");
            var max=(HutongGames.PlayMaker.FsmInt[])scope.Max.Clone();
            for(int i=0;i<events.Length;i++)max[i]=new HutongGames.PlayMaker.FsmInt{Value=blocked[i]?0:int.MaxValue};
            maxField.SetValue(__instance,max);
        }
        owner!.log?.Invoke("HK_NATIVE_NONCONSECUTIVE_CHOICE entity="+actor.Entity+" state="+__instance.State.Name.Replace(' ','_')+" nativeWeightedSelection=True variant="+type.Name);
    }
    private static void RestoreOtherChoice(OtherChoiceScope? __state) {
        if(__state==null)return;
        var type=__state.Choice.GetType();AccessTools.Field(type,"weights").SetValue(__state.Choice,__state.Weights);
        if(__state.Max!=null)AccessTools.Field(type,"eventMax").SetValue(__state.Choice,__state.Max);
    }
    private static void PairedDispatch(HutongGames.PlayMaker.Actions.SendEventByName __instance) {
        var owner=pairedOwner;if(owner==null)return;
        var state=__instance.State;
        if(state.Name!="2 Stomp")return;
        var first=owner.actors.Values.FirstOrDefault(a=>a.Control.Fsm==__instance.Fsm);
        if(first==null)return;
        if(owner.dispatchFrame==Time.frameCount && owner.dispatchState==state)return;
        var brother=first.Control.FsmVariables.GetFsmGameObject("Brother")?.Value;
        var second=owner.actors.Values.SingleOrDefault(a=>a.Health.gameObject==brother);
        if(second==null || second==first)throw new InvalidOperationException("Native paired strike brother identity changed.");
        var events=state.Actions.OfType<HutongGames.PlayMaker.Actions.SendEventByName>().ToArray();
        if(events.Length!=2 || events.Any(a=>a.sendEvent.Value!="DO SLASH" && a.sendEvent.Value!="DO STOMP")) {
            owner.log?.Invoke("HK_NAILMASTER_PAIRED_DISPATCH_REJECTED state="+state.Name.Replace(' ','_')+" events="+string.Join(";",events.Select(a=>a.sendEvent.Value.Replace(' ','_')))+" nativeSequenceContinues=True frame="+Time.frameCount);
            return;
        }
        owner.dispatchFrame=Time.frameCount;owner.dispatchState=state;
        var pair=new PairStrike {First=first,Second=second,Group=owner.allocate(),FirstSwing=owner.allocate(),SecondSwing=owner.allocate(),OtherFirstSwing=owner.allocate(),OtherSecondSwing=owner.allocate()};
        first.Pair=second.Pair=pair;
        owner.log?.Invoke("HK_NAILMASTER_PAIRED_STRIKE group="+pair.Group+" first="+first.Entity+" second="+second.Entity+" state="+state.Name.Replace(' ','_')+" frame="+Time.frameCount);
    }
    private static bool PairedMove(Actor actor)=>actor.Pair!=null && actor.Move==actor.Moves[4];
    private static AttackTraits Traits(Actor actor)=>actor.WhiteEruptions && actor.Eruptions?.Sequence>0?AttackTraits.Crimson
        :actor.Move?.IsRetained(actor.Control.ActiveStateName)==true?AttackTraits.None:PairedMove(actor)?AttackTraits.Crimson:actor.Move?.Traits??AttackTraits.None;
    private static void DefenderState(HutongGames.PlayMaker.Fsm __instance,HutongGames.PlayMaker.FsmState __0) {
        var owner=defenderOwner;if(owner==null || __0==null)return;
        foreach(var actor in owner.actors.Values) {
            if(actor.Control.Fsm!=__instance || actor.Eruptions==null)continue;
            long previous=actor.Eruptions.Sequence;
            bool launch=actor.Eruptions.Enter(__0.Name,owner.allocate);
            if(previous==0 && actor.Eruptions.Sequence>0)actor.Action=actor.Eruptions.Sequence;
            if(!launch)return;
            actor.Action=actor.Eruptions.Strike;actor.Move=actor.Moves[3];actor.Windup=false;actor.Active=true;
            owner.log?.Invoke("HK_DEFENDER_ERUPTION entity="+actor.Entity+" sequence="+actor.Eruptions.Sequence+" action="+actor.Action
                +" crimson="+(actor.WhiteEruptions && actor.Eruptions.Sequence>0)+" nativeLaunch=True frame="+Time.frameCount);
            return;
        }
    }
    private static string[] S(params string[] states)=>states;
    internal bool Register(HealthManager health) {
        if(actors.ContainsKey(health.GetInstanceID())) return true;
        Move[] moves; string fsm;
        const AttackTraits kick=AttackTraits.Kickable, crimson=AttackTraits.Crimson;
        // Native hit reactions return directly to Charge; keep their visual
        // sequence/action identity so ordinary hits cannot replay the warning.
        if(profile==EncounterProfile.MassiveMossCharger) {fsm="Mossy Control";moves=new[]{new Move(S("Emerge"),S("Charge","Hit Left","Hit Right"),kick,"Pause",2,holdAnimation:"Shake",afterHold:"Submerge 1"),new Move(S("Leap Start"),S("Leap Launch","In Air"),AttackTraits.None,null)};}
        else if(profile==EncounterProfile.GodTamer) {
            fsm="Control";
            bool lobster=health.GetComponents<PlayMakerFSM>().Any(f=>f.FsmName==fsm && f.FsmStates.Any(s=>s.Name=="RC Land"));
            moves=lobster ? new[]{new Move(S("RC Antic"),S("RC Charge","RC Charging","RC Launch","RC Air"),kick,"RC Land")}
                : new[]{new Move(S("Aim","First Aim"),S("Launch","In Air","Slash 1","Slash 2"),crimson,"Idle",3,holdAnimation:"Lancer Defeat",afterHold:"Idle",retained:S("Slash Recover"))};
        }
        else if(profile==EncounterProfile.DungDefender || profile==EncounterProfile.WhiteDefender) {
            fsm="Dung Defender";
            var ball=S("RJ Launch","RJ In Air","RJ Bounce","RJ Wall","Ball Hit Up","Ball Hit Left","Ball Hit Right");
            if(profile==EncounterProfile.WhiteDefender) ball=ball.Concat(S("Air Dive?")).ToArray();
            moves=new[]{
                new Move(S("RJ Set","RJ Speed Adjust","Roll Voice","RJ Antic"),ball,crimson,"Stun Set"),
                new Move(S("Dolph Dir","Dolph Dives","Dolph Antic"),S("Diving","Repeat?"),kick,"Dolphin Recover"),
                new Move(S("Throw Antic 1","Throw Antic 2","Throw Antic 3"),S("Throw 1","Throw 2"),AttackTraits.None,null,body:false),
                new Move(S("Erupt Antic","Erupt Antic R"),S("Erupt Out","Erupt Out First","Erupt Fall","Erupt Land"),AttackTraits.None,null)
            };
            if(profile==EncounterProfile.WhiteDefender) moves=moves.Concat(new[]{
                new Move(S("G Slam Antic"),S("G Slam","G Slam Recover"),crimson,null),
                new Move(S("Air Dive Antic"),S("Air Dive","AD In","Under","Pillar","Pillar Increment"),crimson,null)
            }).ToArray();
        }
        else if(profile==EncounterProfile.Sheo) {
            fsm="nailmaster_sheo";moves=new[]{
                new Move(S("Slash Antic"),S("S 1","Slash Dir","Slash L","Slash R","S 2","S 3"),AttackTraits.None,null,body:false),
                new Move(S("Stab Antic"),S("Stab1","Stab2"),crimson,"Stun Reset",body:false),
                new Move(S("GSlash Charge","GSlash Charged"),S("Gslash1","GSlash 2","GSlash 3","Drops","GSlash 4"),crimson,null,body:false),
                new Move(S("Jump Antic","Aim Jump"),S("Jump","JumpSlash1","JumpSlash2","Fire Dir","RedShot L","RedShot R","JumpSlash3","Dstab Antic","Dstab","Dstab Land"),AttackTraits.None,null)
            };
        }
        else if(profile==EncounterProfile.OroMato) {
            fsm="nailmaster"; moves=new[]{
                new Move(S("Combo Antic1"),S("Combo Slash1","Combo Slash2"),AttackTraits.None,null,body:false),
                new Move(S("Combo Antic2","Combo Antic3"),S("Combo Slash3","Combo Slash4"),AttackTraits.None,null,body:false),
                new Move(S("NA Charge","NA Charged","Art Choice","Cyclone Start"),S("Cyclone","Cyc Dir","Cyc L","Cyc R","Cyclone Up"),kick,"Cyclone End"),
                new Move(S("NA Charge","NA Charged","Art Choice"),S("Dash Forward","Dashing L","Dashing R","D Slash","D Slash Recover"),kick,"D Slash End"),
                new Move(S("Dstab Antic"),S("Dstab","Dstab Land"),AttackTraits.None,null)
            };
        }
        else if(profile==EncounterProfile.Sly) {
            fsm="Control";moves=new[]{
                new Move(S("Slash Antic"),S("Slash S1","Slash S2","Slash S3"),AttackTraits.None,null),
                new Move(S("BSlash Antic","BSlash Dash","BSlash Antic 2","BSlash Dash 2"),S("BSlash S1","BSlash S2","BSlash S3","BSlash S1 2","BSlash S2 2","BSlash S3 2"),AttackTraits.None,null),
                new Move(Array.Empty<string>(),S("Spin Slash Launch","Spin Slash Air","After SpinSlash"),AttackTraits.None,null),
                new Move(S("Cyc Charge","Cyc Charged"),S("Cyclone Start","Cycloning","Cyc Decel"),kick,"Cyclone End"),
                new Move(S("Dash Charge","Dash Charged"),S("Dash Forward","Dashing L","Dashing R","D Slash S1","DSlash S2"),kick,"Fall"),
                new Move(S("GSlash Charge","GSlash Charged"),S("GSlash 1","GSlash 2","GSlash 3","GSlash 4"),crimson,"Stun Reset",strongPush:true),
                new Move(Array.Empty<string>(),S("Stomp","Dstab Land","Dstab Slash"),AttackTraits.None,null),
                new Move(S("Rage Slash Antic"),S("Rage Slash"),crimson,"Stun Reset",strongPush:true,requiredCounters:3,resumeAfterStagger:"Fire D")
            };
        }
        else if(profile==EncounterProfile.HiveKnight) {fsm="Control";moves=new[]{new Move(S("Dash Antic"),S("Dash"),kick,"Dash Recover"),new Move(S("TeleIn 2"),S("Slash 1","Slash 2"),AttackTraits.None,null)};}
        else if(profile==EncounterProfile.Collector) {fsm="Control";moves=new[]{new Move(S("Hop Start Antic"),S("Hop Start","Hop","Hop Wall"),AttackTraits.None,null)};}
        else if(profile==EncounterProfile.FalseKnight || profile==EncounterProfile.FailedChampion) {
            if(health.name!=profile.Boss) return false;
            fsm="FalseyControl";moves=new[]{new Move(S("S Attack Antic"),S("S Attack","Slam","S Attack Recover"),crimson,null,body:false)};
        }
        else if(profile==EncounterProfile.BrokenVessel || profile==EncounterProfile.LostKin) {fsm="IK Control";moves=new[]{new Move(S("Dash Antic","Dash Antic 2","Dash Antic 3"),S("Dash Attack","Dash Attack 2","Dash Attack 3"),kick,"Dash Recover"),new Move(S("Dstab Antic"),S("Dstab Fall","Dstab Land"),AttackTraits.None,null),new Move(S("Shake Antic","Shaking Start"),S("Shaking Spawn"),crimson,null,body:false,retained:S("Shake End")),new Move(S("Ohead Antic"),S("Ohead Slashing"),AttackTraits.None,null,body:false,retained:S("Ohead Recover"))};}
        else if(profile==EncounterProfile.Nosk) {fsm="Mimic Spider";moves=new[]{new Move(S("Charge Init","Charge Start","C Antic 2","C Antic 3"),S("Charge L","Charge R"),kick,"Wait",1,holdAnimation:"Idle"),new Move(S("Jump Init","Jump Antic","RS Jump Antic"),S("Jump","Launch","RS Jump"),AttackTraits.None,null)};}
        else if(profile==EncounterProfile.WingedNosk) {fsm="Hornet Nosk";moves=new[]{new Move(S("Swoop Antic","Swoop Direction"),S("Swoop L","Swoop R"),kick,"Swoop Rise",1,holdAnimation:"Fly",retained:S("Swoop Rise"))};}
        else if(profile==EncounterProfile.SoulWarrior) {fsm="Mage Knight";moves=new[]{new Move(S("Slash Antic"),S("Dash"),kick,"Idle",retained:S("Slash","Slash Recover"))};}
        else if(profile==EncounterProfile.Gorb) {fsm="Attacking";moves=Array.Empty<Move>();}
        else if(profile==EncounterProfile.Marmu) {fsm="Control";moves=new[]{new Move(S("Antic"),S("Chase","Fire"),crimson,"Unroll",2,holdAnimation:"Roll End")};}
        else if(profile==EncounterProfile.CrystalGuardian) {fsm="Beam Miner";moves=new[]{new Move(S("Shoot Antic","Beam Antic"),S("Beam","Beam End"),crimson,"Beam End",2,false,holdAnimation:"Idle",afterHold:"Beam Recover",retained:S("Beam Recover"))};}
        else if(profile==EncounterProfile.EnragedGuardian) {fsm="Beam Miner";moves=new[]{new Move(S("Shoot Antic 2","Beam Antic 2"),S("Beam 2","Beam End"),crimson,"Beam End",2,false,holdAnimation:"Idle",afterHold:"Beam Recover",retained:S("Beam Recover"))};}
        else if(profile==EncounterProfile.TraitorLord) {fsm="Mantis";moves=new[]{new Move(S("Attack Antic"),S("Attack 1","Attack Swipe"),kick,"Attack Recover"),
            new Move(S("Slam Antic"),S("Slamming","Waves"),crimson,null,body:false,retained:S("Slam End")),new Move(S("Jump Antic","Jump"),S("DSlash Antic","DSlash","Land"),AttackTraits.None,null)};}
        else if(profile==EncounterProfile.GreyPrinceZote) {fsm="Control";moves=new[]{
            new Move(S("Charge Antic"),S("Charge Start","Charge L","Charge R","Turn L","Turn R","Charge Through?","To CT","Charge Through","CT Dir","CT Left","CT Right","Charge Through 2","CT Dir 2","CT In L","CT In R"),kick,"Charge Fall",retained:S("Charge Fall","Charge Slam")),
            new Move(S("FT Through"),S("FT Fall","Ft Waves","FT Slam"),crimson,"Stun Start"),
            new Move(S("Jump Antic","Aim Jump","JS Antic","JS Aim"),S("Jump","In Air","JS Jump","JS In Air","Stomp Antic","Stomp","Stomp Slash","Slash End","Slash Recover","Stomp Shift L","Stomp Shift R"),AttackTraits.None,null,retained:S("Land Dir","Slash Waves R","Slash Waves L","Land Waves","Land Normal","Set Jumps","After Jump","Shift?","Shift Dir"))
        };}
        else return false;
        var control=health.GetComponents<PlayMakerFSM>().Single(f=>f.FsmName==fsm);
        if(profile==EncounterProfile.Gorb) {
            var wait=control.FsmStates.Single(s=>s.Name=="Wait");
            var timer=wait.Actions.OfType<HutongGames.PlayMaker.Actions.WaitRandom>().Single();
            if(timer.finishEvent.Name=="READY" && !wait.Transitions.Any(t=>t.EventName=="READY") && wait.Transitions.Any(t=>t.EventName=="FINISHED")) {
                repairedWaits.Add(timer,timer.finishEvent);timer.finishEvent=HutongGames.PlayMaker.FsmEvent.GetFsmEvent("FINISHED");
                log?.Invoke("HK_GORB_WAIT_EVENT_REPAIRED entity="+health.GetInstanceID()+" old=READY nativeTransition=FINISHED healthUnchanged=True");
            }
        }
        foreach(var move in moves) foreach(var state in move.Antic.Concat(move.Active).Concat(move.Exit==null?Array.Empty<string>():S(move.Exit)))
            if(!control.FsmStates.Any(s=>s.Name==state)) throw new InvalidOperationException("Native authored move contract changed: "+profile.Key+"/"+state);
        foreach(var move in moves) {
            if(move.AfterHold!=null && !control.FsmStates.Any(s=>s.Name==move.AfterHold)) throw new InvalidOperationException("Native counter recovery contract changed.");
            if(move.HoldAnimation!=null && health.GetComponent<tk2dSpriteAnimator>()?.GetClipByName(move.HoldAnimation)==null) throw new InvalidOperationException("Native stationary animation missing.");
            if(move.ResumeAfterStagger!=null && !control.FsmStates.Any(s=>s.Name==move.ResumeAfterStagger)) throw new InvalidOperationException("Native stagger continuation missing.");
        }
        GameObject? counterEffect=null;
        if(moves.Any(m=>m.Hold>0)) NativeFsmStepLease.Prepare();
        if(profile==EncounterProfile.GodTamer && moves[0].Traits==kick) {
            var cleanup=control.FsmStates.Single(s=>s.Name=="RC Launch").Actions.OfType<HutongGames.PlayMaker.Actions.ActivateGameObject>().Single(a=>!a.activate.Value);
            counterEffect=FSMUtility.GetSafe(cleanup.gameObject,cleanup);
            if(counterEffect==null || counterEffect==health.gameObject) throw new InvalidOperationException("Native God Tamer roll effect owner changed.");
            log?.Invoke("HK_GOD_TAMER_ROLL_EFFECT owner="+health.GetInstanceID()+" effect="+counterEffect.name+" source=RC_Launch nativeDeactivate=True");
        }
        HutongGames.PlayMaker.FsmBool? finalPhase=null;
        if(profile==EncounterProfile.Sly) {
            finalPhase=control.FsmVariables.GetFsmBool("Final Rage");
            if(finalPhase==null || !control.FsmStates.Single(s=>s.Name=="Begin Rage").Actions.OfType<HutongGames.PlayMaker.Actions.SetBoolValue>().Any(a=>a.boolVariable.Name==finalPhase.Name && a.boolValue.Value)
                || control.FsmStates.Single(s=>s.Name=="Stun Reset").Actions.OfType<HutongGames.PlayMaker.Actions.SetBoolValue>().Any(a=>a.boolVariable.Name==finalPhase.Name))
                throw new InvalidOperationException("Native Sly final stagger phase contract changed.");
        }
        var actor=new Actor {Health=health,Entity=health.GetInstanceID(),Control=control,Moves=moves,CounterEffect=counterEffect,Body=health.GetComponent<Rigidbody2D>(),Animation=health.GetComponent<tk2dSpriteAnimator>(),FinalPhase=finalPhase};
        if(profile==EncounterProfile.Sly) {
            actor.SlyFlightGravity=control.FsmStates.Single(s=>s.Name=="Air Catch").Actions.OfType<HutongGames.PlayMaker.Actions.SetGravity2dScale>().Single().gravityScale.Value;
            var dynamicBody=control.FsmStates.Single(s=>s.Name=="Death Away").Actions.OfType<HutongGames.PlayMaker.Actions.SetIsKinematic2d>().Single();
            var launch=control.FsmStates.Single(s=>s.Name=="Fire D").Actions.OfType<HutongGames.PlayMaker.Actions.FlingObject>().Single();
            if(actor.Body==null || actor.SlyFlightGravity!=0 || dynamicBody.isKinematic.Value || launch.speedMin.Value!=55 || launch.speedMax.Value!=55
                || launch.angleMin.Value!=35 || launch.angleMax.Value!=145
                || control.FsmStates.Single(s=>s.Name=="Fire D").Transitions.Single(t=>t.EventName=="FINISHED").ToState!="Flipping")
                throw new InvalidOperationException("Native Sly final flight restoration contract changed.");
            actor.SlyFlightBodyType=RigidbodyType2D.Dynamic;
        }
        if(profile==EncounterProfile.WhiteDefender || profile==EncounterProfile.DungDefender) {
            var set=control.FsmStates.Single(s=>s.Name=="Set Rage").Actions.OfType<HutongGames.PlayMaker.Actions.SetIntValue>().Single();
            var decrement=control.FsmStates.Single(s=>s.Name=="Rage In").Actions.OfType<HutongGames.PlayMaker.Actions.IntAdd>().Single();
            if(set.intValue.Value!=5 || decrement.add.Value!=-1 || set.intVariable.Name!=decrement.intVariable.Name
                || !control.FsmStates.Single(s=>s.Name=="Rage?").Actions.Any(a=>a is GetHP))
                throw new InvalidOperationException("Native Defender threshold sequence contract changed.");
            actor.Eruptions=new DefenderEruptionSequence();actor.WhiteEruptions=profile==EncounterProfile.WhiteDefender;
            actor.DefenderLaunchY=control.FsmStates.Single(s=>s.Name=="Erupt Out").Actions.OfType<HutongGames.PlayMaker.Actions.SetPosition>().Single().y;
            actor.DefenderApexY=control.FsmStates.Single(s=>s.Name=="Erupt Fall").Actions.OfType<HutongGames.PlayMaker.Actions.SetPosition>().Single().y;
            if(actor.DefenderLaunchY.IsNone || actor.DefenderApexY.IsNone || actor.DefenderApexY.Value<=actor.DefenderLaunchY.Value)
                throw new InvalidOperationException("Native Defender eruption heights changed.");
        }
        if(profile==EncounterProfile.TraitorLord) {
            actor.TraitorDiveSource=health.GetComponentsInChildren<DamageHero>(true).Single(d=>d.name=="Attack Box");
            if(actor.TraitorDiveSource.GetComponent<Collider2D>()==null)
                throw new InvalidOperationException("Native Traitor dive weapon collider missing.");
        }
        if(profile==EncounterProfile.GodTamer && moves[0].HoldAnimation=="Lancer Defeat") {
            var clip=actor.Animation!.GetClipByName("Lancer Defeat");
            if(clip.frames.Length<2 || clip.fps<=0) throw new InvalidOperationException("Native tamer defeat animation contract changed.");
            // Owned wrappers only: never change the shared native animation library.
            actor.GetDown=new tk2dSpriteAnimationClip(clip){name="YiGodhome Lancer Counter Defeat",wrapMode=tk2dSpriteAnimationClip.WrapMode.Once};
            actor.GetUp=new tk2dSpriteAnimationClip(clip){name="YiGodhome Lancer Get Up",wrapMode=tk2dSpriteAnimationClip.WrapMode.Once};
            actor.GetUp.frames=clip.frames.Reverse().ToArray();
            var ground=control.FsmStates.Single(s=>s.Name=="Defeat").Actions.OfType<HutongGames.PlayMaker.Actions.SetPosition>().Single();
            if(ground.space!=Space.World || ground.y.IsNone || float.IsNaN(ground.y.Value) || float.IsInfinity(ground.y.Value))
                throw new InvalidOperationException("Native tamer defeat ground contract changed.");
            actor.StunGroundY=ground.y.Value;
        }
        if(profile==EncounterProfile.GodTamer || profile==EncounterProfile.WingedNosk) {
            var names=profile==EncounterProfile.WingedNosk?new[]{"Head Box"}:counterEffect!=null?new[]{"Hit Ball","Hit Idle","Hit Spit Antic","Hit Spit"}:new[]{"Damage Box"};
            var init=control.FsmStates.Single(s=>s.Name=="Init");
            actor.BodySources=names.Select(name=> {
                if(!init.Actions.OfType<HutongGames.PlayMaker.Actions.FindChild>().Any(a=>a.childName.Value==name
                    && (a.gameObject.OwnerOption==HutongGames.PlayMaker.OwnerDefaultOption.UseOwner || FSMUtility.GetSafe(a.gameObject,a)==health.gameObject)))
                    throw new InvalidOperationException("Native authored body source reference changed: "+profile.Key);
                var source=health.transform.Find(name)?.gameObject;
                if(source==null || source.GetComponent<DamageHero>()==null || source.GetComponent<Collider2D>()==null || source.GetComponentInParent<HealthManager>(true)!=health)
                    throw new InvalidOperationException("Native authored body collider ownership changed: "+profile.Key+" entity="+health.name+" child="+name
                        +" rootActive="+health.gameObject.activeInHierarchy+" found="+(source!=null)+" damage="+(source?.GetComponent<DamageHero>()!=null)
                        +" collider="+(source?.GetComponent<Collider2D>()!=null)+" parentDefault="+(source?.GetComponentInParent<HealthManager>()?.GetInstanceID()??0)
                        +" parentIncludingInactive="+(source?.GetComponentInParent<HealthManager>(true)?.GetInstanceID()??0)+" expected="+health.GetInstanceID());
                log?.Invoke((profile==EncounterProfile.GodTamer?"HK_GOD_TAMER_BODY_SOURCE":"HK_WINGED_NOSK_BODY_SOURCE")+" entity="+actor.Entity+" source="+source.name.Replace(' ','_')+" id="+source.GetComponent<DamageHero>().GetInstanceID()+" nativeReference=True defaultPassive=False");
                return source;
            }).ToArray();
        }
        if(profile==EncounterProfile.TraitorLord) {
            var activation=control.FsmStates.Single(s=>s.Name=="Waves").Actions.OfType<HutongGames.PlayMaker.Actions.ActivateGameObject>().Single(a=>a.activate.Value && a.gameObject.GameObject?.Name=="Wave Hitter");
            var wave=FSMUtility.GetSafe(activation.gameObject,activation);
            if(wave==null || wave.name!="Wave Hitter" || wave.scene!=health.gameObject.scene || wave.GetComponent<DamageHero>()==null
                || wave.GetComponent<BoxCollider2D>()==null || wave.GetComponents<DamageHero>().Length!=1
                || wave.GetComponentInParent<HealthManager>() is HealthManager other && other!=health)
                throw new InvalidOperationException("Native Traitor wave damage owner changed.");
            var emitted=control.FsmStates.Single(s=>s.Name=="Waves").Actions.OfType<HutongGames.PlayMaker.Actions.SpawnObjectFromGlobalPool>().ToArray();
            if(emitted.Length!=2 || emitted[0].gameObject.Value==null || emitted[0].gameObject.Value.name!="mega_mantis_tall_slash"
                || emitted[1].gameObject.Value!=emitted[0].gameObject.Value)
                throw new InvalidOperationException("Native Traitor emitted wave contract changed.");
            actor.TraitorWavePrefab=emitted[0].gameObject.Value;
            actor.LateWeapons=new[]{wave};
            log?.Invoke("HK_TRAITOR_WAVE_MANIFEST entity="+actor.Entity+" wave="+wave.GetInstanceID()+" nativeActivation=Waves singleDamageOwner=True counterInterrupts=False");
        }
        if(profile==EncounterProfile.CrystalGuardian || profile==EncounterProfile.EnragedGuardian) {
            string antic=profile==EncounterProfile.CrystalGuardian?"Beam Antic":"Beam Antic 2";
            var activation=control.FsmStates.Single(s=>s.Name==antic).Actions.OfType<HutongGames.PlayMaker.Actions.ActivateGameObject>().Single(a=>a.activate.Value);
            var beam=FSMUtility.GetSafe(activation.gameObject,activation);
            if(beam==null || beam==health.gameObject || beam.name!="Beam" || beam.scene!=health.gameObject.scene
                || activation.gameObject.GameObject?.Name!="Beam" || beam.GetComponents<PlayMakerFSM>().Count(f=>f.FsmName=="Beam Extender")!=1)
                throw new InvalidOperationException("Native guardian beam activation ownership changed.");
            var idle=actor.Animation!.GetClipByName("Idle");
            if(idle.frames.Length==0 || idle.fps<=0)throw new InvalidOperationException("Native guardian idle contract changed.");
            actor.GuardianIdle=new tk2dSpriteAnimationClip(idle){name="YiGodhome Guardian Stun Idle",wrapMode=tk2dSpriteAnimationClip.WrapMode.Loop};
            var beamCleanup=control.FsmStates.Single(s=>s.Name=="Beam Recover").Actions.OfType<HutongGames.PlayMaker.Actions.SetMeshRenderer>().ToArray();
            if(beamCleanup.Length!=2 || beamCleanup.Any(a=>a.active.Value))throw new InvalidOperationException("Native guardian beam cleanup contract changed.");
            actor.GuardianBeamCleanup=beamCleanup.Select(a=> {
                var target=FSMUtility.GetSafe(a.gameObject,a);
                var renderer=target?.GetComponent<MeshRenderer>();
                if(renderer==null || renderer==health.GetComponent<MeshRenderer>() || target!.scene!=health.gameObject.scene)
                    throw new InvalidOperationException("Native guardian beam cleanup renderer ownership changed.");
                return renderer;
            }).Distinct().ToArray();
            actor.LateWeapons=new[]{beam};
            log?.Invoke("HK_GUARDIAN_OWNED_BEAM entity="+actor.Entity+" root="+beam.GetInstanceID()+" lateColliderBinding=True ceilingLasersIndependent=True");
        }
        if(profile==EncounterProfile.BrokenVessel || profile==EncounterProfile.LostKin) {
            var dashOff=control.FsmStates.Single(s=>s.Name=="Dash Attack 3").Actions.OfType<HutongGames.PlayMaker.Actions.ActivateGameObject>()
                .Single(a=>!a.activate.Value && a.gameObject.GameObject?.Name=="Dash Slash Hit");
            actor.VesselDashHit=FSMUtility.GetSafe(dashOff.gameObject,dashOff);
            if(actor.VesselDashHit==null || actor.VesselDashHit.GetComponent<DamageHero>()==null
                || actor.VesselDashHit.GetComponentInParent<HealthManager>()!=health)
                throw new InvalidOperationException("Native vessel dash cleanup owner changed.");
            var landing=control.FsmStates.Single(s=>s.Name=="Dstab Land").Actions.OfType<HutongGames.PlayMaker.Actions.SpawnObjectFromGlobalPool>().ToArray();
            int expected=profile==EncounterProfile.BrokenVessel?4:6;
            if(landing.Length!=expected || landing.Any(a=>a.gameObject.Value==null || a.gameObject.Value.name!="IK Projectile DS"
                || a.gameObject.Value.GetComponent<AutoRecycleSelf>()==null || a.spawnPoint.Value!=health.gameObject)
                || landing.Select(a=>a.gameObject.Value).Distinct().Count()!=1)throw new InvalidOperationException("Native vessel landing projectile manifest changed.");
            actor.VesselSlamPrefab=landing[0].gameObject.Value;
            var overheadOff=control.FsmStates.Single(s=>s.Name=="Ohead Recover").Actions.OfType<HutongGames.PlayMaker.Actions.ActivateGameObject>()
                .Single(a=>!a.activate.Value && a.gameObject.GameObject?.Name=="Overhead Slash");
            var overhead=FSMUtility.GetSafe(overheadOff.gameObject,overheadOff);
            if(overhead==null || overhead.GetComponent<DamageHero>()==null || overhead.GetComponentInParent<HealthManager>()!=health)
                throw new InvalidOperationException("Native vessel overhead damage owner changed.");
            foreach(var name in new[]{"Shake Projectiles L","Shake Projectiles R","Shake Projectiles U"}) {
                var emitter=health.GetComponents<PlayMakerFSM>().SingleOrDefault(f=>f.FsmName==name);
                if(emitter==null)continue;
                var spawn=emitter.FsmStates.Single(s=>s.Name=="Spawn").Actions.OfType<HutongGames.PlayMaker.Actions.SpawnObjectFromGlobalPool>().Single();
                var prefab=spawn.gameObject.Value;
                if(prefab==null || prefab.name!="IK Projectile SH" || prefab.GetComponent<BrokenVesselGlob>()==null
                    || prefab.GetComponent<AutoRecycleSelf>()==null || spawn.spawnPoint.Value!=health.gameObject)throw new InvalidOperationException("Native vessel shake emitter manifest changed.");
                actor.VesselEmitters.Add(emitter.Fsm,prefab);
            }
            if(actor.VesselEmitters.Count!=(profile==EncounterProfile.BrokenVessel?3:0))throw new InvalidOperationException("Native vessel shake emitter closure changed.");
            log?.Invoke("HK_VESSEL_PROJECTILE_MANIFEST entity="+actor.Entity+" landingPieces="+expected+" shakeEmitters="+actor.VesselEmitters.Count+" nativeReferences=True");
        }
        if(profile==EncounterProfile.SoulWarrior) {
            var cleanup=control.FsmStates.Single(s=>s.Name=="Slash Recover").Actions.OfType<HutongGames.PlayMaker.Actions.ActivateGameObject>().Single(a=>!a.activate.Value);
            var slash=FSMUtility.GetSafe(cleanup.gameObject,cleanup);
            if(slash==null || slash==health.gameObject || slash.name!="Slash" || !slash.transform.IsChildOf(health.transform)
                || !control.FsmStates.Single(s=>s.Name=="Slash").Actions.OfType<HutongGames.PlayMaker.Actions.ActivateGameObject>()
                    .Any(a=>a.activate.Value && FSMUtility.GetSafe(a.gameObject,a)==slash))throw new InvalidOperationException("Native warrior slash cleanup contract changed.");
            actor.WarriorSlash=slash;
            actor.RangeQuery=new NativeHeroRangeQuery(health,log);
        }
        if(profile==EncounterProfile.OroMato) {
            var choiceState=control.FsmStates.Single(s=>s.Name=="Art Choice");
            var choice=choiceState.Actions.OfType<HutongGames.PlayMaker.Actions.BoolTest>().Single();
            string trueState=choiceState.Transitions.Single(t=>t.EventName==choice.isTrue?.Name).ToState;
            string falseState=choiceState.Transitions.Single(t=>t.EventName==choice.isFalse?.Name).ToState;
            if(!((trueState=="Cyclone Start" && falseState=="Dash Forward") || (trueState=="Dash Forward" && falseState=="Cyclone Start")))
                throw new InvalidOperationException("Native nailmaster art selection contract changed.");
            actor.NailmasterCyclone=choice.boolVariable;
            actor.NailmasterCycloneWhenTrue=trueState=="Cyclone Start";
            var airHold=control.FsmStates.Single(s=>s.Name=="Dstab Antic").Actions.OfType<HutongGames.PlayMaker.Actions.SetIsKinematic2d>().Single();
            var airRelease=control.FsmStates.Single(s=>s.Name=="Dstab").Actions.OfType<HutongGames.PlayMaker.Actions.SetIsKinematic2d>().Single();
            if(!airHold.isKinematic.Value || airRelease.isKinematic.Value || actor.Body==null
                || FSMUtility.GetSafe(airHold.gameObject,airHold)!=health.gameObject || FSMUtility.GetSafe(airRelease.gameObject,airRelease)!=health.gameObject)
                throw new InvalidOperationException("Native nailmaster airborne physics cleanup contract changed.");
            actor.NailmasterLandingBodyType=RigidbodyType2D.Dynamic;
            foreach(var ending in new[]{"Cyclone End","D Slash End"}) {
                var notifications=control.FsmStates.Single(s=>s.Name==ending).Actions.OfType<HutongGames.PlayMaker.Actions.SendEventByName>().ToArray();
                if(notifications.Length!=2 || notifications.Any(a=>a.sendEvent.Value!="COMBO COMPLETED" || a.delay.Value>=.001f || a.everyFrame)
                    || !notifications.Any(a=>a.eventTarget.gameObject.GameObject?.Name=="Brother")
                    || !notifications.Any(a=>a.eventTarget.gameObject.GameObject?.Name=="Controller"))
                    throw new InvalidOperationException("Native nailmaster art completion handshake changed.");
                actor.ArtCompletions.Add(ending,notifications);
            }
            var fallen=control.FsmStates.Single(s=>s.Name=="Death Land").Actions.OfType<HutongGames.PlayMaker.Actions.Tk2dPlayAnimation>().Single().clipName.Value;
            var rise=control.FsmStates.Single(s=>s.Name=="Getup").Actions.OfType<HutongGames.PlayMaker.Actions.Tk2dPlayAnimationWithEvents>().Single().clipName.Value;
            var settle=control.FsmStates.Single(s=>s.Name=="Recover Roar End").Actions.OfType<HutongGames.PlayMaker.Actions.Tk2dPlayAnimationWithEvents>().Single().clipName.Value;
            tk2dSpriteAnimationClip Clip(string name) {
                var clip=actor.Animation!.GetClipByName(name);
                if(clip==null || clip.frames.Length==0 || clip.fps<=0)throw new InvalidOperationException("Native nailmaster stun artwork missing.");
                return new tk2dSpriteAnimationClip(clip){name="YiGodhome Nailmaster "+name,wrapMode=tk2dSpriteAnimationClip.WrapMode.Once};
            }
            actor.GetDown=Clip(fallen);actor.GetUp=Clip(rise);actor.StunSettle=Clip(settle);
            var launch=control.FsmStates.Single(s=>s.Name=="Death Launch");
            actor.NailmasterAir=Clip(launch.Actions.OfType<HutongGames.PlayMaker.Actions.Tk2dPlayAnimationWithEvents>().Single().clipName.Value);
            actor.LaunchX=launch.Actions.OfType<HutongGames.PlayMaker.Actions.FloatOperator>().Single().float2.Value;
            actor.LaunchY=launch.Actions.OfType<HutongGames.PlayMaker.Actions.SetVelocity2d>().Single().y.Value;
            actor.LaunchGravity=control.FsmStates.Single(s=>s.Name=="Death Start").Actions.OfType<HutongGames.PlayMaker.Actions.SetGravity2dScale>().Single().gravityScale.Value;
            if(actor.LaunchX<=0 || actor.LaunchY<=0 || actor.LaunchGravity<=0)throw new InvalidOperationException("Native nailmaster knockdown physics changed.");
            log?.Invoke("HK_NAILMASTER_PHYSICS_CONTRACT entity="+actor.Entity+" anticKinematic=True diveKinematic=False sameNativeBody=True");
            log?.Invoke("HK_NAILMASTER_ART_SELECTION_CONTRACT entity="+actor.Entity+" variable="+choice.boolVariable.Name.Replace(' ','_')
                +" trueState="+trueState.Replace(' ','_')+" falseState="+falseState.Replace(' ','_')+" nativeTransitions=True");
        }
        if(profile==EncounterProfile.WhiteDefender) {
            var emission=control.FsmStates.Single(s=>s.Name=="G Slam").Actions.OfType<HutongGames.PlayMaker.Actions.SendEventByName>().Single(a=>a.sendEvent.Value=="PILLAR UP");
            var pillars=FSMUtility.GetSafe(emission.eventTarget.gameObject,emission);
            if(pillars==null || pillars.name!="Slam Pillars" || !emission.eventTarget.sendToChildren.Value) throw new InvalidOperationException("Native White slam pillar owner changed.");
            actor.SlamPillars=pillars.GetComponentsInChildren<DamageHero>(true);
            if(actor.SlamPillars.Length!=6 || actor.SlamPillars.Any(d=>d.GetComponentInParent<HealthManager>()!=health)) throw new InvalidOperationException("Native White slam pillar closure changed.");
        }
        actors.Add(health.GetInstanceID(),actor);
        var sources=health.GetComponentsInChildren<DamageHero>(true).Where(d=>d.GetComponentInParent<HealthManager>()==health).ToList();
        if(profile==EncounterProfile.Sly) foreach(var name in new[]{"S1","S2","S3","S4","DS1","GS1","GS2","Spin Tink","Cyclone Tink"}) {
            var native=control.FsmVariables.GetFsmGameObject(name)?.Value;
            if(native==null || native.scene!=health.gameObject.scene) throw new InvalidOperationException("Native Sly attack owner reference missing: "+name);
            foreach(var source in native.GetComponentsInChildren<DamageHero>(true)) if(!sources.Contains(source)) sources.Add(source);
        }
        if(profile==EncounterProfile.OroMato) foreach(var name in new[]{"S1","S2","S3","S4","Cyclone Slash","Dash Slash"}) {
            var native=control.FsmVariables.GetFsmGameObject(name)?.Value;
            if(native==null || native.scene!=health.gameObject.scene) throw new InvalidOperationException("Native nailmaster attack owner reference missing: "+name);
            foreach(var source in native.GetComponentsInChildren<DamageHero>(true)) if(!sources.Contains(source)) sources.Add(source);
        }
        if(profile==EncounterProfile.Sheo) foreach(var name in new[]{"S1","GS1","GS2","GS3","Stab Effect","Stab Lines","Slam Effect"}) {
            var native=control.FsmVariables.GetFsmGameObject(name)?.Value;
            if(native==null || native==health.gameObject || native.scene!=health.gameObject.scene)throw new InvalidOperationException("Native Sheo attack owner missing: "+name);
            foreach(var source in native.GetComponentsInChildren<DamageHero>(true))if(!sources.Contains(source))sources.Add(source);
        }
        if(sources.Count>64) throw new InvalidOperationException("Authored damage owner capacity exceeded.");
        if(profile==EncounterProfile.Sly)actor.SlyWeapons=sources.Where(d=>d.gameObject!=health.gameObject)
            .SelectMany(d=>d.GetComponents<Collider2D>().Select(c=>(d,c))).ToArray();
        foreach(var source in sources) {
            if(damageOwners.TryGetValue(source.GetInstanceID(),out var prior) && prior!=actor) throw new InvalidOperationException("Native attack has competing authored owners.");
            damageOwners[source.GetInstanceID()]=actor;
            if(SessionConfiguration.Current.RunFixtures) log?.Invoke("HK_AUTHORED_DAMAGE_OWNER entity="+actor.Entity+" source="+source.name.Replace(' ','_')+" collider="+source.GetInstanceID()+" nativeReference=True");
        }
        log?.Invoke("HK_AUTHORED_MOVES_BOUND entity="+health.GetInstanceID()+" profile="+profile.Key+" fsm="+fsm+" moves="+moves.Length);
        return true;
    }
    internal bool Owns(HealthManager health)=>actors.ContainsKey(health.GetInstanceID());
    internal bool ScriptedSkipCounter=>profile==EncounterProfile.WhiteDefender && SessionConfiguration.Current.RunFixtures
        && SessionConfiguration.Current.Has("crimson-probe") && actors.Values.Any(a=>a.Move?.Exit=="Stun Set" && a.BallProbeStarts%3!=1);
    internal bool ScriptedCounterPrecharge=>profile==EncounterProfile.Sly && SessionConfiguration.Current.RunFixtures
        && SessionConfiguration.Current.Has("crimson-probe") && actors.Values.Any(a=>a.Health!=null && !a.Health.isDead && a.Health.hp>0 && a.FinalPhase?.Value==true && a.Control.ActiveStateName=="Rage Dash");
    internal NativeSpawnStrike SpawnStrike(HutongGames.PlayMaker.FsmStateAction source, GameObject prefab) {
        if(profile==EncounterProfile.BrokenVessel || profile==EncounterProfile.LostKin) {
            foreach(var vessel in actors.Values) {
                if(vessel.Health==null || vessel.Health.isDead || !vessel.Health.gameObject.activeInHierarchy)continue;
                if(source.Fsm==vessel.Control.Fsm && source.State.Name=="Dstab Land" && prefab==vessel.VesselSlamPrefab) {
                    var dive=vessel.Moves.Single(m=>Array.IndexOf(m.Active,"Dstab Land")>=0);
                    if(vessel.Move!=dive)vessel.Action=allocate();
                    vessel.Move=dive;vessel.Windup=false;vessel.Active=true;
                    long projectile=allocate();
                    if(vessel.VesselSpawnSamples++<128)log?.Invoke("HK_VESSEL_SLAM_PROJECTILE entity="+vessel.Entity+" action="+projectile+" group="+vessel.Action+" initialHold=1 independentAfterHold=True frame="+Time.frameCount);
                    return new NativeSpawnStrike(projectile,AttackTraits.None,vessel.Action);
                }
                if(source.State.Name!="Spawn" || !vessel.VesselEmitters.TryGetValue(source.Fsm,out var pinned) || pinned!=prefab)continue;
                if(vessel.VesselVolleyFrame!=Time.frameCount){vessel.VesselVolleyFrame=Time.frameCount;vessel.VesselVolleyGroup=allocate();}
                long shot=allocate();
                if(vessel.VesselSpawnSamples++<128)log?.Invoke("HK_VESSEL_SHAKE_PROJECTILE entity="+vessel.Entity+" action="+shot+" group="+vessel.VesselVolleyGroup+" fsm="+source.Fsm.Name.Replace(' ','_')+" crimson=True nativeSequenceContinues=True frame="+Time.frameCount);
                return new NativeSpawnStrike(shot,AttackTraits.Crimson,vessel.VesselVolleyGroup);
            }
            return default;
        }
        if(profile==EncounterProfile.GreyPrinceZote && prefab.name=="Shockwave Wave Z") {
            var zote=actors.Values.SingleOrDefault(a=>a.Control.Fsm==source.Fsm);
            var zoteMove=zote?.Moves.SingleOrDefault(m=>m.Contains(source.State.Name));
            if(zote==null || zoteMove==null)return default;
            if(source.State.Name=="Ft Waves" && zote.CounterRetiredAction==zote.Action)
                return new NativeSpawnStrike(zote.Action,AttackTraits.None,suppress:true);
            if(zote.Move!=zoteMove)zote.Action=allocate();
            zote.Move=zoteMove;
            log?.Invoke("HK_ZOTE_NATIVE_WAVE_STRIKE entity="+zote.Entity+" action="+zote.Action+" state="+source.State.Name.Replace(' ','_')+" ordinaryWave=True frame="+Time.frameCount);
            return new NativeSpawnStrike(zote.Action,AttackTraits.None,cancelOnCounter:source.State.Name=="Ft Waves");
        }
        if(profile==EncounterProfile.Sheo) {
            var sheo=actors.Values.SingleOrDefault(a=>a.Control.Fsm==source.Fsm);
            if(sheo==null || prefab.GetComponent<PaintBullet>()==null)return default;
            var nativeMove=sheo.Moves.SingleOrDefault(m=>Array.IndexOf(m.Active,source.State.Name)>=0);
            if(nativeMove==null)return default;
            if(sheo.Move!=nativeMove)sheo.Action=allocate();
            sheo.Move=nativeMove;sheo.Windup=false;sheo.Active=true;
            bool suppressed=sheo.SuppressedPaintAction==sheo.Action;
            if(suppressed)log?.Invoke("HK_SHEO_PAINT_SPAWN_SUPPRESSED action="+sheo.Action+" state="+source.State.Name.Replace(' ','_')+" prefab="+prefab.name.Replace(' ','_')+" nativeSequenceContinues=True frame="+Time.frameCount);
            return new NativeSpawnStrike(0,AttackTraits.None,sheo.Action,suppressed);
        }
        if(profile==EncounterProfile.TraitorLord && source.State.Name=="Waves") {
            var traitor=actors.Values.SingleOrDefault(a=>a.Control.Fsm==source.Fsm && a.TraitorWavePrefab==prefab);
            if(traitor==null)return default;
            var slam=traitor.Moves.Single(m=>Array.IndexOf(m.Active,"Waves")>=0);
            if(traitor.Move!=slam)traitor.Action=allocate();
            traitor.Move=slam;traitor.Windup=false;traitor.Active=true;
            // These detached waves inherit counter-only damage, not projectile
            // reflection or a fresh audiovisual warning for each spawn.
            return new NativeSpawnStrike(traitor.Action,AttackTraits.Crimson);
        }
        bool eruption=(profile==EncounterProfile.DungDefender || profile==EncounterProfile.WhiteDefender) && prefab.name=="Dung Ball Small" && (source.State.Name=="Erupt Out" || source.State.Name=="Erupt Out First");
        bool defenderThrow=(profile==EncounterProfile.DungDefender || profile==EncounterProfile.WhiteDefender)
            && (prefab.name=="Dung Ball Large" || prefab.name=="Dung Ball Large W") && source.State.Name=="Throw 1";
        bool spikes=profile==EncounterProfile.WhiteDefender && prefab.name=="Dung Pillar" && source.State.Name=="Pillar";
        bool armorWave=(profile==EncounterProfile.FalseKnight || profile==EncounterProfile.FailedChampion) && prefab.name=="Shockwave Wave" && source.State.Name=="S Attack Recover";
        if(!eruption && !spikes && !armorWave && !defenderThrow) return default;
        var actor=actors.Values.FirstOrDefault(a=>a.Control.Fsm==source.Fsm);
        if(actor==null) return default;
        // OnEnter can spawn before the contact sampler observes the state change.
        // Bind the exact authored move now, preserving its anticipation lease.
        var move=actor.Moves.Single(m=>Array.IndexOf(m.Active,source.State.Name)>=0);
        // Consecutive throws can traverse transient states between samples.
        // Every exact native Throw1 spawn owns a fresh body swipe.
        if(defenderThrow || actor.Move!=move) actor.Action=allocate();
        actor.Move=move;actor.Windup=false;actor.Active=true;
        // Each emitted dung ball owns a separate retirement identity, never
        // the boss's rolling/launch action or a sibling ball's identity. The
        // crimson sequence remains visual-only and cannot group their hits.
        return new NativeSpawnStrike(eruption || defenderThrow?allocate():actor.Action,eruption?Traits(actor):move.Traits,
            damageGroup:eruption || defenderThrow?defenderDungGroup:0,
            visualSequence:eruption && actor.WhiteEruptions?actor.Eruptions!.Sequence:0);
    }
    internal void Update() {
        presented.Clear();
        foreach(var actor in actors.Values) {
            if(profile==EncounterProfile.Sly) {
                actor.SlySwordActive=false;
                foreach(var weapon in actor.SlyWeapons)if(weapon.Source!=null && weapon.Shape!=null
                    && weapon.Source.gameObject.activeInHierarchy && weapon.Source.damageDealt>0 && weapon.Shape.isActiveAndEnabled) {actor.SlySwordActive=true;break;}
            }
            if(actor.Health==null) continue;
            var choiceState=actor.Control.ActiveStateName;
            if(actor.ZoteCounterLanding && choiceState!="Stun Start" && choiceState!="Stun Air") {
                actor.ZoteLanding!.ToState="Stun";actor.ZoteLanding.ToFsmState=actor.ZoteOriginalLanding;actor.ZoteCounterLanding=false;
                log?.Invoke("HK_ZOTE_COUNTER_LANDING entity="+actor.Entity+" state="+choiceState.Replace(' ','_')+" nativeTransitionRestored=True frame="+Time.frameCount);
            }
            if(profile==EncounterProfile.GreyPrinceZote && (choiceState=="Land Waves" || choiceState=="Charge Start" || choiceState=="JS Jump"))actor.LastChoiceSkyFall=false;
            if(profile==EncounterProfile.Nosk && (choiceState=="Set Spit" || choiceState=="Roar Init")
                || profile==EncounterProfile.WingedNosk && (choiceState=="Acid Roar Antic" || choiceState=="Summon Roar" || choiceState=="Roof Antic")
                || profile==EncounterProfile.SoulWarrior && choiceState=="Shoot Aim")actor.LastChoiceKick=false;
            if(profile==EncounterProfile.GodTamer && actor.CounterEffect!=null) {
                var audio=actor.Health.GetComponent<AudioSource>();var state=actor.Control.ActiveStateName;
                if(audio!=null && audio.loop && audio.isPlaying && state!="Scuttle" && !state.StartsWith("RC ",StringComparison.Ordinal))audio.Stop();
            }
            if(actor.StaggerResume!=null) {
                if(actor.Health.isDead || actor.Health.hp<=0 || !actor.Health.gameObject.activeInHierarchy) {RestoreStaggerLanding(actor);actor.StaggerResume=null;}
                else if(actor.Control.ActiveStateName=="Idle") {
                    if(actor.FinalPhase?.Value!=true) throw new InvalidOperationException("Native final stagger lost its phase identity.");
                    string resume=actor.StaggerResume;actor.Control.SetState(resume);
                    log?.Invoke("HK_AUTHORED_FINAL_STAGGER_RESUME entity="+actor.Entity+" state="+resume+" phasePreserved=True hp="+actor.Health.hp+" frame="+Time.frameCount);
                } else if(Time.time>actor.StaggerDeadline) throw new InvalidOperationException("Native final stagger recovery deadline exceeded.");
            }
            if(actor.HarmlessRecovery && !actor.Control.ActiveStateName.StartsWith("Submerge",StringComparison.Ordinal)) {
                actor.HarmlessRecovery=false;
                log?.Invoke("HK_MOSS_COUNTER_BURROW_IMMUNITY entity="+actor.Entity+" action="+actor.Action+" active=False nativeState="+actor.Control.ActiveStateName.Replace(' ','_')+" frame="+Time.frameCount);
            }
            if(actor.Holding) {
                if(actor.Health.isDead || actor.Health.hp<=0 || !actor.Health.gameObject.activeInHierarchy) Release(actor,false);
                else if(actor.NailmasterLaunching) {
                    if(GameManager.instance?.IsGamePaused()!=true) TickNailmasterLanding(actor);
                }
                else if(GameManager.instance?.IsGamePaused()!=true) {
                    if(actor.HoldAnimation!=null) {
                        int sprite=actor.Health.GetComponent<tk2dBaseSprite>()?.spriteId??-1;
                        if(sprite!=actor.LastSprite) {actor.SpriteChanges++;actor.LastSprite=sprite;}
                        if(!actor.StunSnapshot && actor.HoldDuration-actor.Remaining>.3f && SessionConfiguration.Current.RunFixtures) {
                            actor.StunSnapshot=true;
                            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!,"native-moss-stun.png"));
                        }
                    }
                    actor.MaximumDrift=Math.Max(actor.MaximumDrift,Vector3.Distance(actor.Health.transform.position,actor.Position));
                    actor.Health.transform.position=actor.Position;
                    if(actor.Body!=null) actor.Body.linearVelocity=Vector2.zero;
                    if(actor.ClockFrame!=Time.frameCount) {actor.Remaining-=Time.deltaTime;actor.ClockFrame=Time.frameCount;}
                    if(actor.GetUp!=null) {
                        // Paused native frame sampling avoids completion handlers
                        // replacing a fallen sprite with Jump/Idle during the hold.
                        actor.StunElapsed=actor.HoldDuration-actor.Remaining;
                        var sample=CounterStunTimeline.Sample(actor.StunElapsed,actor.HoldDuration,actor.GetDown!.fps,actor.GetDown.frames.Length,actor.GetUp.fps,actor.GetUp.frames.Length);
                        var clip=sample.Rising?actor.GetUp:actor.GetDown;
                        if(sample.Complete && actor.StunSettle!=null) {
                            clip=actor.StunSettle;
                            float elapsed=actor.StunElapsed-Math.Max(actor.HoldDuration,actor.GetDown.frames.Length/actor.GetDown.fps)-actor.GetUp.frames.Length/actor.GetUp.fps;
                            sample=new CounterStunFrame(Math.Min(clip.frames.Length-1,Math.Max(0,(int)(elapsed*clip.fps))),true,elapsed>=clip.frames.Length/clip.fps);
                        }
                        if(actor.Animation!.CurrentClip!=clip)actor.Animation.PlayFromFrame(clip,sample.Index);
                        actor.Animation.Pause();actor.Animation.SetFrame(sample.Index,false);
                        if(sample.Rising && !actor.GettingUp) {
                            actor.GettingUp=true;
                            log?.Invoke((profile==EncounterProfile.OroMato?"HK_NAILMASTER_GET_UP":"HK_GOD_TAMER_GET_UP")+" entity="+actor.Entity+" stunSeconds="+actor.HoldDuration+" reversedNativeClip="+(profile!=EncounterProfile.OroMato)+" sampledNativeFrames=True frame="+Time.frameCount);
                        }
                        if(sample.Complete)Release(actor);
                    } else {
                        if(actor.GuardianIdle!=null && actor.Animation!=null && actor.Animation.CurrentClip!=actor.GuardianIdle) {
                            actor.Animation.Play(actor.GuardianIdle);actor.Animation.Resume();
                            log?.Invoke("HK_GUARDIAN_STUN_IDLE_RESTORED entity="+actor.Entity+" nativeState="+actor.Control.ActiveStateName.Replace(' ','_')+" frame="+Time.frameCount);
                        }
                        if(actor.Remaining<=0) Release(actor);
                    }
                }
            }
            var next=actor.Holding?null:actor.Moves.FirstOrDefault(m=>m.Contains(actor.Control.ActiveStateName));
            bool pairedJump=profile==EncounterProfile.OroMato && actor.Pair!=null
                && (actor.Control.ActiveStateName=="Jump Antic" || actor.Control.ActiveStateName=="Aim Jump" || actor.Control.ActiveStateName=="Jump");
            if(pairedJump) next=actor.Moves[4];
            if(profile==EncounterProfile.OroMato && (actor.Control.ActiveStateName=="NA Charge" || actor.Control.ActiveStateName=="NA Charged" || actor.Control.ActiveStateName=="Art Choice"))
                next=actor.Moves.Single(m=>m.Exit==(actor.NailmasterCyclone!.Value==actor.NailmasterCycloneWhenTrue?"Cyclone End":"D Slash End"));
            // The native introduction also submerges before the first leap or
            // charge. Give its real landing shapes one trait-free strike.
            if(profile==EncounterProfile.MassiveMossCharger && actor.Action==0
                && actor.Control.ActiveStateName.StartsWith("Submerge",StringComparison.Ordinal)) actor.Action=allocate();
            if(next!=actor.Move && next!=null) {
                actor.LastChoiceKick=next.Traits==AttackTraits.Kickable;
                if(profile==EncounterProfile.GreyPrinceZote && next==actor.Moves[1])actor.LastChoiceSkyFall=true;
                actor.Action=actor.Pair!=null && profile==EncounterProfile.OroMato && (next==actor.Moves[0] || next==actor.Moves[1] || next==actor.Moves[4])
                    ? actor.Pair.Strike(actor,next==actor.Moves[1]):allocate();
                if(profile==EncounterProfile.WhiteDefender) {
                    if(Array.IndexOf(next.Antic,"G Slam Antic")>=0) actor.SlamPillarAction=actor.Action;
                    if(next.Exit=="Stun Set") actor.BallProbeStarts++;
                }
                if((profile==EncounterProfile.BrokenVessel || profile==EncounterProfile.LostKin) && next==actor.Moves[3])
                    log?.Invoke("HK_VESSEL_OVERHEAD_STRIKE entity="+actor.Entity+" action="+actor.Action+" priorCounter="+actor.CounterRetiredAction+" independentNativeMove=True frame="+Time.frameCount);
                if(next.RequiredCounters>1) actor.CounterStreak.Begin(actor.Action);
            }
            if(profile==EncounterProfile.GreyPrinceZote && next!=null && next==actor.Move
                && (actor.Control.ActiveStateName=="Jump Antic" || actor.Control.ActiveStateName=="JS Antic")
                && actor.StrikeState!=actor.Control.ActiveStateName)actor.Action=allocate();
            actor.StrikeState=actor.Control.ActiveStateName;
            actor.Move=next; actor.Windup=pairedJump || next!=null && Array.IndexOf(next.Antic,actor.Control.ActiveStateName)>=0;
            actor.Active=next!=null && !actor.Windup && !next.IsRetained(actor.Control.ActiveStateName);
            if(actor.Active && next!=null && (next.Traits&(AttackTraits.Crimson|AttackTraits.Kickable))!=0) {
                actor.LastDefensibleMove=next;actor.LastDefensibleAction=actor.Action;actor.LastDefensibleAt=Time.time;
            }
            if((profile==EncounterProfile.BrokenVessel || profile==EncounterProfile.LostKin || profile==EncounterProfile.TraitorLord)
                && SessionConfiguration.Current.RunFixtures && SessionConfiguration.Current.Has("trace-probe")
                && actor.ObservedState!=actor.Control.ActiveStateName) {
                actor.ObservedState=actor.Control.ActiveStateName;
                if(actor.SequenceSamples++<256) log?.Invoke("HK_CONTINUING_MOVE_STATE entity="+actor.Entity+" action="+actor.Action
                    +" state="+actor.ObservedState.Replace(' ','_')+" windup="+actor.Windup+" active="+actor.Active
                    +" nativeFSM=True frame="+Time.frameCount);
                else if(actor.SequenceSamples==257)log?.Invoke("HK_CONTINUING_MOVE_EVIDENCE_CAPPED entity="+actor.Entity+" transitions=256 frame="+Time.frameCount);
            }
            if(profile==EncounterProfile.GreyPrinceZote && SessionConfiguration.Current.RunFixtures && SessionConfiguration.Current.Has("trace-probe")
                && actor.ObservedState!=actor.Control.ActiveStateName) {
                actor.ObservedState=actor.Control.ActiveStateName;
                if(actor.SequenceSamples++<256) log?.Invoke("HK_ZOTE_MOVE_SEQUENCE entity="+actor.Entity+" action="+actor.Action+" state="+actor.ObservedState.Replace(' ','_')
                    +" windup="+actor.Windup+" active="+actor.Active+" retained="+(next?.IsRetained(actor.Control.ActiveStateName)??false)+" frame="+Time.frameCount);
                else if(actor.SequenceSamples==257)log?.Invoke("HK_ZOTE_SEQUENCE_EVIDENCE_CAPPED entity="+actor.Entity+" frame="+Time.frameCount+" transitions=256");
            }
            if(profile==EncounterProfile.SoulWarrior && SessionConfiguration.Current.RunFixtures && actor.ObservedState!=actor.Control.ActiveStateName) {
                actor.ObservedState=actor.Control.ActiveStateName;
                log?.Invoke("HK_WARRIOR_MOVE_SEQUENCE entity="+actor.Entity+" action="+actor.Action+" state="+actor.ObservedState.Replace(' ','_')
                    +" active="+actor.Active+" traits="+Traits(actor)+" retained="+(next?.IsRetained(actor.Control.ActiveStateName)??false)+" frame="+Time.frameCount);
            }
            if((profile==EncounterProfile.Sly || profile==EncounterProfile.WhiteDefender) && SessionConfiguration.Current.RunFixtures && actor.ObservedState!=actor.Control.ActiveStateName) {
                actor.ObservedState=actor.Control.ActiveStateName;
                log?.Invoke((profile==EncounterProfile.Sly?"HK_SLY_MOVE_STATE entity=":"HK_WHITE_MOVE_STATE entity=")+actor.Entity+" action="+actor.Action+" state="+actor.ObservedState.Replace(' ','_')
                    +" final="+(actor.FinalPhase?.Value??false)+" hp="+actor.Health.hp+" streak="+actor.CounterStreak.Count+" frame="+Time.frameCount);
            }
            if(profile==EncounterProfile.MassiveMossCharger && SessionConfiguration.Current.RunFixtures && actor.ObservedState!=actor.Control.ActiveStateName) {
                actor.ObservedState=actor.Control.ActiveStateName;
                log?.Invoke("HK_MOSS_MOVE_SEQUENCE entity="+actor.Entity+" action="+actor.Action+" state="+actor.ObservedState.Replace(' ','_')+" taichi="+(next?.Traits==AttackTraits.Kickable)+" frame="+Time.frameCount);
            }
            if(profile==EncounterProfile.OroMato && SessionConfiguration.Current.RunFixtures && actor.ObservedState!=actor.Control.ActiveStateName) {
                actor.ObservedState=actor.Control.ActiveStateName;
                log?.Invoke("HK_NAILMASTER_MOVE_STATE entity="+actor.Entity+" action="+actor.Action+" state="+actor.ObservedState.Replace(' ','_')
                    +" taichi="+(next?.Traits==AttackTraits.Kickable)+" active="+actor.Active+" facingScale="+actor.Health.transform.lossyScale.x+" frame="+Time.frameCount);
            }
            if(actor.Pair!=null && (actor.Control.ActiveStateName=="Combo Recover" || actor.Control.ActiveStateName=="Idle Stance" || actor.Control.ActiveStateName=="Death Start" || actor.Control.ActiveStateName=="Land")) actor.Pair=null;
            if((next!=null || actor.WhiteEruptions && actor.Eruptions?.Sequence>0) && Traits(actor)!=AttackTraits.None) presented.Add(actor.Health);
        }
    }
    internal IEnumerable<HealthManager> VisualBodies=>presented;
    internal AttackTraits? VisualTraits(HealthManager health) => actors.TryGetValue(health.GetInstanceID(),out var actor)
        ? Traits(actor) : null;
    private Actor? DamageOwner(DamageHero source) {
        if(actors.Count==0)return null;
        if(damageOwners.TryGetValue(source.GetInstanceID(),out var actor)) return actor;
        foreach(var candidate in actors.Values)foreach(var weapon in candidate.LateWeapons) {
            if(weapon==null || !(source.gameObject==weapon || source.transform.IsChildOf(weapon.transform)))continue;
            if(damageOwners.Count>=64*actors.Count)throw new InvalidOperationException("Late native weapon source budget exceeded.");
            var receiver=source.GetComponentInParent<HealthManager>();
            if(receiver!=null && receiver!=candidate.Health)throw new InvalidOperationException("Late native weapon has competing receiver ownership.");
            damageOwners.Add(source.GetInstanceID(),candidate);
            log?.Invoke("HK_NATIVE_LATE_WEAPON_BOUND entity="+candidate.Entity+" root="+weapon.GetInstanceID()+" source="+source.GetInstanceID()+" owner="+source.gameObject.GetInstanceID()+" nativeReference=True");
            return candidate;
        }
        var health=source.GetComponentInParent<HealthManager>();
        return health!=null && actors.TryGetValue(health.GetInstanceID(),out actor)?actor:null;
    }
    private bool ZoteBeforeApex(Actor actor) {
        if(profile!=EncounterProfile.GreyPrinceZote)return false;
        var state=actor.Control.ActiveStateName;
        // Native In Air spans ascent AND descent, unlike a separate apex state.
        // Stomp Antic halts vertical motion; FT Fall starts at the sky apex.
        return actor.Move==actor.Moves[2] && actor.Windup || state=="Jump" || state=="JS Jump"
            || (state=="In Air" || state=="JS In Air") && (actor.Body==null || actor.Body.linearVelocity.y>0);
    }
    internal void Decorate(AttackVolume volume,DamageHero damage) {
        var actor=DamageOwner(damage);
        if(actor==null) return;
        var health=actor.Health;
        // Native counters/staggers can bypass Dash Attack3's object cleanup.
        // The pinned slash belongs only to Dash Attack2, never idle/overhead.
        if(actor.VesselDashHit!=null && damage.gameObject==actor.VesselDashHit
            && actor.Control.ActiveStateName!="Dash Attack 2") {volume.Active=false;return;}
        if(actor.Holding || actor.HarmlessRecovery || actor.Action>0 && actor.CounterRetiredAction==actor.Action) {
            if(actor.HarmlessRecovery && volume.Active && SessionConfiguration.Current.RunFixtures && actor.SuppressedRecoverySources.Add(damage.GetInstanceID()))
                log?.Invoke("HK_MOSS_BURROW_DAMAGE_SUPPRESSED entity="+actor.Entity+" action="+actor.Action+" source="+damage.name.Replace(' ','_')+" id="+volume.Id+" nativeState="+actor.Control.ActiveStateName.Replace(' ','_')+" nativeActive=True translatedActive=False frame="+Time.frameCount);
            volume.Active=false;return;
        }
        if(volume.Category!=AttackCategory.Melee) return;
        if(DefenderLandingHarmless(actor)) {volume.ActionId=actor.Action;volume.Active=false;return;}
        if(ZoteBeforeApex(actor)) {volume.ActionId=actor.Action;volume.Active=false;return;}
        // Native landing/submerge shapes belong to the leap or charge that
        // just ended. They must not become fresh action-zero strikes between
        // adjacent host snapshots while the native boss changes its collider.
        if(profile==EncounterProfile.MassiveMossCharger && actor.Action>0
            && actor.Control.ActiveStateName.StartsWith("Submerge",StringComparison.Ordinal)
            && (damage.name=="Crouch Hit" || damage.name=="Burrow Hit")) {
            volume.ActionId=actor.Action;volume.ParryCooldown=1;volume.CooldownOnDamage=true;return;
        }
        if(actor.SlamPillarAction>0 && Array.IndexOf(actor.SlamPillars,damage)>=0) {
            volume.ActionId=actor.SlamPillarAction;volume.Traits|=AttackTraits.Crimson;return;
        }
        bool root=damage.gameObject==health.gameObject || Array.IndexOf(actor.BodySources,damage.gameObject)>=0;
        if(root) volume.Active &= actor.Active && actor.Move?.Body==true;
        if(actor.Move==null) return;
        volume.ActionId=actor.Action;
        if(profile==EncounterProfile.Sly) {
            // Sword and body share one action throughout each authored strike.
            // Native collider replacement cannot rearm a parried/damaging dash.
            volume.Active &= actor.Active && (!root || !actor.SlySwordActive);
            if(volume.Active && !root)NativeBodyStrike.Combine(volume,health);
        }
        // Body/weapon strikes and the Guardian's connected beam are attached.
        // Emitted hazards keep their own defense without injuring their emitter.
        volume.CounterInjuryEntity=volume.Category==AttackCategory.Melee
            || profile==EncounterProfile.CrystalGuardian || profile==EncounterProfile.EnragedGuardian?actor.Entity:0;
        // Hive keeps its native body AND sword polygons. Their shared ActionId
        // retires both synchronously after one injury or accepted defense.
        bool bodyStrike=profile==EncounterProfile.Collector;
        if(bodyStrike && volume.Active) {
            var selected=actor.WholeBodySource;
            // DamageHero is native data even when its Behaviour is disabled.
            // Re-selecting on that flag can admit both siblings in one frame.
            if(selected==null || !selected.gameObject.activeInHierarchy || selected.damageDealt<=0 || selected.GetComponent<Collider2D>()?.isActiveAndEnabled!=true)actor.WholeBodySource=damage;
            volume.Active &= actor.WholeBodySource==damage;
            if(volume.Active)NativeBodyStrike.Replace(volume,actor.Health);
        }
        if(profile==EncounterProfile.TraitorLord && actor.Move==actor.Moves[2]) {
            var weapon=actor.TraitorDiveSource!;
            // Native DamageHero behaviours are disabled by bridge interception;
            // use authored object/collider activation, not behaviour.enabled.
            bool weaponActive=weapon.gameObject.activeInHierarchy && weapon.damageDealt>0
                && weapon.GetComponent<Collider2D>()!.isActiveAndEnabled;
            var selected=weaponActive?weapon:health.GetComponent<DamageHero>();
            volume.Active &= actor.Active && damage==selected;
            if(volume.Active && weaponActive)NativeBodyStrike.Combine(volume,health);
            // Native DSlash Antic halts ascent at the apex; Jump stays harmless.
            // Jump, DSlash Antic, DSlash and Land keep actor.Action, including
            // native collider gaps. NS already retires this entire action on
            // one normal parry or injury; landing cannot rearm body contact.
        }
        volume.Traits|=Traits(actor);
        if(actor.Eruptions!=null && actor.Move==actor.Moves[0]) {
            // One native rolling sequence can injure/parry repeatedly. Keep
            // siblings and bounces quiet for one paused-gameplay second only.
            volume.ParryCooldown=1;volume.CooldownOnDamage=true;
        }
        if((profile==EncounterProfile.BrokenVessel || profile==EncounterProfile.LostKin) && Array.IndexOf(actor.Move.Active,"Dstab Land")>=0) {
            volume.DamageGroupId=actor.Action;volume.InitialDamageGroupHold=1;
        }
        if((profile==EncounterProfile.CrystalGuardian || profile==EncounterProfile.EnragedGuardian) && SessionConfiguration.Current.RunFixtures
            && volume.Active && actor.WeaponEvidence.Count<64 && actor.WeaponEvidence.Add((volume.Id,actor.Action)))
            log?.Invoke("HK_GUARDIAN_STRIKE_SOURCE entity="+actor.Entity+" action="+actor.Action+" collider="+volume.Id+" activation="+volume.Activation
                +" role="+(damage.gameObject==actor.LateWeapons[0] || damage.transform.IsChildOf(actor.LateWeapons[0].transform)?"beam":"arm")
                +" crimson="+((volume.Traits&AttackTraits.Crimson)!=0)+" state="+actor.Control.ActiveStateName.Replace(' ','_')+" nativeOwner=True");
        if(profile==EncounterProfile.Sheo)volume.DamageGroupId=actor.Action;
        if(profile==EncounterProfile.Sheo && actor.Move==actor.Moves[0])volume.NotifyParry=true;
        if(PairedMove(actor)) volume.CounterGroupId=actor.Pair!.Group;
        if(actor.Move.StrongPush) volume.Traits|=AttackTraits.LargeParryPush
            |(actor.Health.transform.lossyScale.x<0?AttackTraits.PushRight:AttackTraits.PushLeft);
        if(profile==EncounterProfile.Sly || profile==EncounterProfile.OroMato)
            volume.Traits|=actor.Health.transform.lossyScale.x<0?AttackTraits.PushRight:AttackTraits.PushLeft;
        if(profile==EncounterProfile.OroMato)
            volume.Traits|=NailmasterRecoil.Traits(actor.Move==actor.Moves[0] || actor.Move==actor.Moves[1],PairedMove(actor) || actor.Move.Exit=="D Slash End");
        if(profile==EncounterProfile.MassiveMossCharger) {volume.ParryCooldown=1;volume.CooldownOnDamage=true;}
        if(Traits(actor)!=AttackTraits.None) volume.Active &= actor.Active;
    }
    private static bool DefenderLandingHarmless(Actor actor)=>actor.Eruptions?.HarmlessLanding(
        actor.Control.ActiveStateName,actor.Health.transform.position.y,actor.DefenderLaunchY!.Value,actor.DefenderApexY!.Value)==true;
    internal bool SuppressDamage(DamageHero damage) {
        var actor=DamageOwner(damage);
        // A combined hop/dive owns body contact throughout its native sequence.
        // The optional global-contact fallback must not resurrect the suppressed
        // body as an independent action-zero hit alongside its weapon union.
        return actor!=null && (actor.Holding || actor.HarmlessRecovery || ZoteBeforeApex(actor) || DefenderLandingHarmless(actor)
            || profile==EncounterProfile.Sly && (actor.Move!=null || actor.Control.ActiveStateName=="Fall" || actor.Control.ActiveStateName=="Land" || actor.StaggerResume!=null)
            || actor.Eruptions!=null && actor.Move==actor.Moves[0]
            || profile==EncounterProfile.TraitorLord && actor.Move==actor.Moves[2]
            || actor.Action>0 && actor.CounterRetiredAction==actor.Action);
    }
    internal void Target(SummonedTarget target) {
        if(!actors.TryGetValue(target.Entity,out var actor)) return;
        target.Kickable=actor.Active && actor.Move?.Traits==AttackTraits.Kickable;
        // Exact authored sequence identity survives native collider gaps.
        // Kickable alone controls whether this is a native kick target.
        target.AttackId=actor.Move!=null && actor.Health!=null && !actor.Health.isDead
            && actor.Health.gameObject.activeInHierarchy?actor.Action:0;
    }
    internal void TraceBodyHeight(YiGodhome.Bridge.Pose? pose) {
        if(pose==null || !pose.Grounded || !SessionConfiguration.Current.RunFixtures || !SessionConfiguration.Current.Has("trace-probe")
            || !(profile==EncounterProfile.BrokenVessel || profile==EncounterProfile.LostKin || profile==EncounterProfile.HiveKnight || profile==EncounterProfile.TraitorLord))return;
        foreach(var actor in actors.Values) {
            if(actor.Health==null || !actor.Active || actor.Move?.Body!=true || actor.HeightEvidence.Count>=32
                || !actor.Control.ActiveStateName.StartsWith("Dash",StringComparison.Ordinal) && actor.Control.ActiveStateName!="Attack 1")continue;
            var damage=actor.Health.GetComponent<DamageHero>();var shape=actor.Health.GetComponent<Collider2D>();
            if(damage==null || !damage.isActiveAndEnabled || damage.damageDealt<=0 || shape==null || !shape.enabled)continue;
            var b=shape.bounds;
            if(pose.X+pose.Width*.5f<b.min.x || pose.X-pose.Width*.5f>b.max.x)continue;
            bool vertical=pose.Y+pose.Height>=b.min.y && pose.Y<=b.max.y;
            if(!actor.HeightEvidence.Add((actor.Action,vertical)))continue;
            log?.Invoke("HK_NATIVE_DASH_HEIGHT entity="+actor.Entity+" source="+damage.GetInstanceID()+" action="+actor.Action+" state="+actor.Control.ActiveStateName.Replace(' ','_')
                +" horizontalOverlap=True verticalOverlap="+vertical+" nativeBottom="+b.min.y+" nativeTop="+b.max.y+" yiFeet="+pose.Y+" yiTop="+(pose.Y+pose.Height)
                +" grounded=True aimedTargetUnchanged=True geometryFrame="+pose.GeometryFrame+" frame="+Time.frameCount);
        }
    }
    internal HealthManager? BodyContactOwner(DamageHero damage) {
        var actor=DamageOwner(damage);
        return actor!=null && (damage.gameObject==actor.Health.gameObject || Array.IndexOf(actor.BodySources,damage.gameObject)>=0)?actor.Health:null;
    }
    internal bool Presentation(ContactFrame frame) {
        foreach(var actor in actors.Values) {
            // Native body removal can precede the encounter completion event.
            // Retire its warning without inferring victory or touching artwork.
            if(actor.Health==null || actor.Health.isDead || !actor.Health.gameObject.activeInHierarchy) continue;
            var move=actor.Move;
            if(actor.WhiteEruptions && actor.Eruptions?.Sequence>0) {
                var bounds=actor.Health.GetComponent<Collider2D>().bounds;
                frame.AttackId=actor.Action;frame.CrimsonSequence=actor.Eruptions.Sequence;frame.CrimsonCharge=1;
                frame.Crimson=true;frame.Windup=!actor.Active;frame.Kickable=frame.KickWindup=false;
                frame.X=bounds.center.x;frame.Y=bounds.center.y;frame.Width=Math.Max(1,bounds.size.x);frame.Height=Math.Max(1,bounds.size.y);
                return true;
            }
            if(move==null || Traits(actor)==AttackTraits.None) continue;
            var shape=actor.Health.GetComponent<Collider2D>(); if(shape==null) continue;
            var b=shape.bounds;
            frame.AttackId=actor.Action;frame.CrimsonSequence=actor.Pair?.Group??actor.Action;frame.CrimsonCharge=1;
            frame.Crimson=Traits(actor)==AttackTraits.Crimson;frame.Windup=frame.Crimson&&actor.Windup;
            // Authored swimming/combo states can span native collider gaps.
            // Keep their sequence cue, but publish a kick target only while
            // the actual strike geometry is active.
            frame.Kickable=move.Traits==AttackTraits.Kickable&&actor.Active
                && (frame.Active || Array.Exists(frame.Volumes,v=>v.Active && v.ActionId==actor.Action && (v.Traits&AttackTraits.Kickable)!=0));
            // Same cue-only contract as RosterAttackPolicy: the sequence stays
            // visible across collider gaps, without granting kick collision.
            frame.KickWindup=move.Traits==AttackTraits.Kickable&&!frame.Kickable;
            frame.X=b.center.x;frame.Y=b.center.y;frame.Width=Math.Max(1,b.size.x);frame.Height=Math.Max(1,b.size.y);
            return true;
        }
        return false;
    }
    internal bool Counter(long action,HitKind kind,int entity) {
        foreach(var actor in actors.Values) {
            if(actor.Health==null || actor.Health.isDead || actor.Health.hp<=0 || !actor.Health.gameObject.activeInHierarchy)continue;
            if(action>0 && action==actor.SlamPillarAction && kind==HitKind.UnboundedCounter && (entity==0 || entity==actor.Entity)
                && actor.SlamPillars.Any(d=>d!=null && d.isActiveAndEnabled && d.damageDealt>0 && d.GetComponent<Collider2D>()?.enabled==true)) {
                log?.Invoke("HK_WHITE_PILLAR_COUNTER entity="+actor.Entity+" action="+action+" state="+actor.Control.ActiveStateName.Replace(' ','_')+" nativeSequenceContinues=True frame="+Time.frameCount);
                return true;
            }
            var move=actor.Move;
            // Lancer can land into Slash before the peer's jump counter arrives.
            // Admit only the exact recent authored strike across its landing boundary.
            bool landingReceipt=!actor.Holding && (profile==EncounterProfile.GodTamer && actor.GetUp!=null
                || profile==EncounterProfile.GreyPrinceZote && actor.LastDefensibleMove==actor.Moves[1]
                    && actor.Control.ActiveStateName=="FT Recover");
            bool late=actor.LastDefensibleMove!=null && CounterReceiptWindow.Matches(action,actor.Action,actor.LastDefensibleAction,Time.time-actor.LastDefensibleAt,landingReceipt);
            if(late && (actor.Action!=action || !actor.Active || move==null))move=actor.LastDefensibleMove;
            if(move==null || !actor.Active&&!late || actor.Action!=action&&!late || actor.CounterRetiredAction==action || entity!=0&&entity!=actor.Health.GetInstanceID()) continue;
            if(landingReceipt && late && actor.Action!=action)
                log?.Invoke((profile==EncounterProfile.GodTamer?"HK_GOD_TAMER_COUNTER_RECEIPT entity=":"HK_ZOTE_COUNTER_RECEIPT entity=")+actor.Entity+" acknowledged="+action+" current="+actor.Action+" state="+actor.Control.ActiveStateName.Replace(' ','_')+" age="+(Time.time-actor.LastDefensibleAt)+" exactRecentJump=True frame="+Time.frameCount);
            if(profile==EncounterProfile.Sheo && (move==actor.Moves[0] && kind==HitKind.StrikeParried || move==actor.Moves[2] && kind==HitKind.UnboundedCounter)) {
                actor.SuppressedPaintAction=action;
                log?.Invoke("HK_SHEO_PAINT_COUNTER action="+action+" state="+actor.Control.ActiveStateName.Replace(' ','_')+" kind="+kind+" nativeSequenceContinues=True frame="+Time.frameCount);
                return true;
            }
            if((late?move.Traits:Traits(actor))==AttackTraits.Crimson?kind!=HitKind.UnboundedCounter:move.Traits!=AttackTraits.Kickable || kind!=HitKind.TaiChiKick&&kind!=HitKind.UnboundedCounter) return false;
            if(PairedMove(actor)) {
                var pair=actor.Pair!;
                var left=pair.First.Health.transform.position.x<=pair.Second.Health.transform.position.x?pair.First:pair.Second;
                foreach(var member in new[]{pair.First,pair.Second}) {
                    if(member.Health==null || member.Health.isDead || !member.Health.gameObject.activeInHierarchy)continue;
                    string exit="Combo Recover";
                    foreach(var name in new[]{"S1","S2","S3","S4"}) {
                        var weapon=member.Control.FsmVariables.GetFsmGameObject(name)?.Value;
                        if(weapon==null || weapon==member.Health.gameObject)throw new InvalidOperationException("Native paired weapon cleanup identity changed.");
                        weapon.SetActive(false);
                    }
                    member.Pair=null;member.Move=null;member.Active=member.Windup=false;
                    member.CounterRetiredAction=member.Action;
                    if(member.Body!=null) {
                        var before=member.Body.bodyType;
                        member.Body.bodyType=member.NailmasterLandingBodyType;member.Body.linearVelocity=Vector2.zero;
                        log?.Invoke("HK_NAILMASTER_COUNTER_PHYSICS group="+pair.Group+" entity="+member.Entity+" before="+before+" after="+member.Body.bodyType+" nativeDiveCleanup=True frame="+Time.frameCount);
                    }
                    member.Control.SetState(exit);
                    member.GettingUp=false;
                    Hold(member,3,member.GetDown!.name,"Idle",member.GetDown);
                    // Let the real HK rigidbody complete the knockdown before
                    // pinning the fallen pose; a counter can arrive in midair.
                    member.NailmasterGravity=member.Body!.gravityScale;
                    member.NailmasterKnockdown=member.NailmasterLaunching=true;member.NailmasterLaunchElapsed=0;
                    member.Body.simulated=member.Simulated;member.Body.gravityScale=member.LaunchGravity;
                    member.Body.linearVelocity=new Vector2(member==left?-member.LaunchX:member.LaunchX,member.LaunchY);
                    member.Animation!.Play(member.NailmasterAir);member.Animation.Resume();
                    log?.Invoke("HK_NAILMASTER_PAIRED_COUNTER group="+pair.Group+" entity="+member.Entity+" action="+action+" state="+exit.Replace(' ','_')+" frame="+Time.frameCount+" ownStrike="+member.Action+" stunSeconds=3");
                }
                return true;
            }
            if(move.Exit==null) {
                log?.Invoke("HK_AUTHORED_SEQUENCE_COUNTER entity="+actor.Entity+" action="+action+" state="+actor.Control.ActiveStateName.Replace(' ','_')+" nativeSequenceContinues=True frame="+Time.frameCount);
                return true;
            }
            if(move.RequiredCounters>1) {
                if(!actor.CounterStreak.Accept(action)) return false;
                log?.Invoke("HK_AUTHORED_COUNTER_STREAK entity="+actor.Entity+" action="+action+" count="+actor.CounterStreak.Count+" required="+move.RequiredCounters+" frame="+Time.frameCount);
                if(actor.CounterStreak.Count<move.RequiredCounters) return true;
                actor.CounterStreak.Reset();
                if(move.ResumeAfterStagger!=null) {
                    if(actor.FinalPhase?.Value!=true) return false;
                    actor.StaggerResume=move.ResumeAfterStagger;actor.StaggerDeadline=Time.time+12;
                    // Route the native stun's final landing directly back into
                    // its native upward relaunch. SwitchState restores flight
                    // physics before Fire D runs. Never rerun Begin Rage/HP.
                    var landing=actor.Control.FsmStates.Single(s=>s.Name=="Stun Land").Transitions.Single(t=>t.EventName=="FINISHED");
                    if(landing.ToState!="Idle")throw new InvalidOperationException("Native Sly stun landing contract changed.");
                    actor.StaggerLanding=landing;actor.StaggerOriginalLanding=landing.ToFsmState;
                    landing.ToState=move.ResumeAfterStagger;landing.ToFsmState=actor.Control.FsmStates.Single(s=>s.Name==move.ResumeAfterStagger);
                }
            }
            if(profile==EncounterProfile.OroMato && move.Exit=="D Slash End" && actor.Body!=null) actor.Body.linearVelocity=Vector2.zero;
            string exitState=move.Exit;
            if(profile==EncounterProfile.OroMato && move.Traits==AttackTraits.Kickable) {
                foreach(var name in new[]{"S1","S2","S3","S4","Cyclone Slash","Dash Slash"})actor.Control.FsmVariables.GetFsmGameObject(name)?.Value?.SetActive(false);
                if(actor.Body!=null) {
                    actor.Body.bodyType=actor.NailmasterLandingBodyType;actor.Body.linearVelocity=Vector2.zero;
                    actor.Body.gravityScale=actor.Control.FsmStates.Single(s=>s.Name=="Cyclone End").Actions.OfType<HutongGames.PlayMaker.Actions.SetGravity2dScale>().Single().gravityScale.Value;
                }
                // Keep only the native completion dispatch. Entering Cyclone
                // End also enters Dstab Antic, which teleports up for a new dive.
                foreach(var completion in actor.ArtCompletions[move.Exit])
                    actor.Control.Fsm.Event(completion.eventTarget,completion.sendEvent.Value);
                exitState="Combo Recover";
                log?.Invoke("HK_NAILMASTER_ART_COUNTER_RECOVERY entity="+actor.Entity+" action="+action+" exit="+exitState.Replace(' ','_')
                    +" brotherAndControllerCompletion=True frame="+Time.frameCount);
            }
            if(profile==EncounterProfile.SoulWarrior) {
                actor.WarriorSlash!.SetActive(false);
                if(actor.Body!=null)actor.Body.linearVelocity=Vector2.zero;
                log?.Invoke("HK_WARRIOR_COUNTER_RECOVERY entity="+actor.Entity+" action="+action+" slashActive="+actor.WarriorSlash.activeSelf+" exit=Idle skippedAnimationWait=True nativeNextChoice=True frame="+Time.frameCount);
            }
            if(actor.VesselDashHit!=null && move.Exit=="Dash Recover") {
                actor.VesselDashHit.SetActive(false);
                log?.Invoke("HK_VESSEL_COUNTER_CLEANUP entity="+actor.Entity+" action="+action+" source=Dash_Slash_Hit nativeCleanup=Dash_Attack_3 frame="+Time.frameCount);
            }
            actor.CounterRetiredAction=action;
            if(profile==EncounterProfile.GreyPrinceZote && move==actor.Moves[1]) {
                var landing=actor.Control.FsmStates.Single(s=>s.Name=="Stun Air").Transitions.Single(t=>t.EventName=="LAND");
                var slam=actor.Control.FsmStates.Single(s=>s.Name=="Charge Slam");
                if(landing.ToState!="Stun" || !slam.Actions
                    .OfType<HutongGames.PlayMaker.Actions.SpawnObjectFromGlobalPool>().Any(a=>a.gameObject.Value?.name=="Shockwave Wave Z"))
                    throw new InvalidOperationException("Native Zote shockwave landing contract changed.");
                actor.ZoteLanding=landing;actor.ZoteOriginalLanding=landing.ToFsmState;
                landing.ToState="Charge Slam";landing.ToFsmState=slam;actor.ZoteCounterLanding=true;
            }
            actor.Control.SetState(exitState);
            actor.SuppressedRecoverySources.Clear();
            if(actor.CounterEffect!=null) {
                actor.CounterEffect.SetActive(false);
                log?.Invoke("HK_GOD_TAMER_ROLL_COUNTER entity="+actor.Entity+" action="+action+" effectActive="+actor.CounterEffect.activeSelf+" audioPlaying="+(actor.Health.GetComponent<AudioSource>()?.isPlaying??false)+" state="+actor.Control.ActiveStateName.Replace(' ','_')+" nativeRecovery=True frame="+Time.frameCount);
            }
            if(move.Hold>0) {
                if(actor.GetUp!=null) {
                    var sound=actor.Control.FsmStates.Single(s=>s.Name=="Defeat").Actions.OfType<HutongGames.PlayMaker.Actions.AudioPlayerOneShot>().Single();
                    var spawnPoint=sound.spawnPoint.Value;
                    try {sound.spawnPoint.Value=actor.Health.gameObject;AccessTools.Method(typeof(HutongGames.PlayMaker.Actions.AudioPlayerOneShot),"DoPlayRandomClip").Invoke(sound,Array.Empty<object>());}
                    finally {sound.spawnPoint.Value=spawnPoint;}
                    actor.GettingUp=false;actor.StunElapsed=0;
                    // Match the authored defeat's ground placement, preserving X/Z.
                    var groundPosition=actor.Health.transform.position;groundPosition.y=actor.StunGroundY;
                    actor.Health.transform.position=groundPosition;
                }
                actor.Position=actor.Health.transform.position;actor.Velocity=actor.Body?.linearVelocity??Vector2.zero;
                // Do not disable PlayMaker: that calls Stop and exits its state.
                // Suspend stepping of this exact FSM while keeping native events
                // and the authored receiving/recovery state intact.
                actor.StepLease=new NativeFsmStepLease(actor.Control.Fsm,deferTransitions:actor.GuardianIdle!=null);
                if(actor.GuardianIdle!=null) {
                    foreach(var effect in actor.GuardianBeamCleanup)if(effect!=null)effect.enabled=false;
                    log?.Invoke("HK_GUARDIAN_BEAM_COUNTER_CLEANUP entity="+actor.Entity+" renderers="+actor.GuardianBeamCleanup.Length+" nativeBeamRecoverTargets=True frame="+Time.frameCount);
                }
                if(actor.GuardianIdle!=null)log?.Invoke("HK_GUARDIAN_STUN_LEASE entity="+actor.Entity+" seconds="+move.Hold+" nativeState="+actor.Control.ActiveStateName.Replace(' ','_')+" callbackTransitionsDeferred=True beamCleanupRetained=True frame="+Time.frameCount);
                if(actor.Body!=null) {actor.Simulated=actor.Body.simulated;actor.Body.linearVelocity=Vector2.zero;actor.Body.simulated=false;}
                if(actor.Animation!=null) {
                    actor.AnimationPaused=actor.Animation.Paused;
                    if(move.HoldAnimation==null) actor.Animation.Pause();
                    else {if(actor.GetDown!=null){actor.Animation.PlayFromFrame(actor.GetDown,0);actor.Animation.Pause();}else {if(actor.GuardianIdle!=null)actor.Animation.Play(actor.GuardianIdle);else actor.Animation.Play(move.HoldAnimation);actor.Animation.Resume();}}
                }
                actor.AfterHold=move.AfterHold;
                actor.HoldAnimation=move.HoldAnimation;actor.SpriteChanges=0;actor.LastSprite=actor.Health.GetComponent<tk2dBaseSprite>()?.spriteId??-1;
                actor.Holding=true;actor.Remaining=actor.HoldDuration=actor.GetDown==null?move.Hold:Math.Max(move.Hold,actor.GetDown.Duration);actor.MaximumDrift=0;actor.ClockFrame=Time.frameCount;
            }
            actor.Active=actor.Windup=false;
            actor.Move=!actor.Holding && move.IsRetained(move.Exit)?move:null;
            log?.Invoke("HK_AUTHORED_MOVE_COUNTER entity="+actor.Health.GetInstanceID()+" action="+action+" exit="+move.Exit.Replace(' ','_')+" hold="+move.Hold+" nativeFSM=True frame="+Time.frameCount);
            return true;
        }
        return false;
    }
    private void Hold(Actor actor,float seconds,string animation,string recovery,tk2dSpriteAnimationClip? clip=null) {
        actor.Position=actor.Health.transform.position;actor.Velocity=Vector2.zero;
        actor.StepLease=new NativeFsmStepLease(actor.Control.Fsm,deferTransitions:profile==EncounterProfile.OroMato);
        if(actor.Body!=null){actor.Simulated=actor.Body.simulated;actor.Body.linearVelocity=Vector2.zero;actor.Body.simulated=false;}
        if(actor.Animation!=null){actor.AnimationPaused=actor.Animation.Paused;if(clip==null)actor.Animation.Play(animation);else actor.Animation.PlayFromFrame(clip,0);actor.Animation.Resume();}
        actor.HoldAnimation=animation;actor.AfterHold=recovery;actor.Holding=true;actor.Remaining=actor.HoldDuration=seconds;actor.ClockFrame=Time.frameCount;
    }
    private void TickNailmasterLanding(Actor actor) {
        if(actor.ClockFrame==Time.frameCount)return;
        actor.ClockFrame=Time.frameCount;actor.NailmasterLaunchElapsed+=Time.deltaTime;
        var filter=new ContactFilter2D();filter.SetLayerMask(1<<8);filter.useTriggers=false;
        int count=actor.Body!.GetContacts(filter,actor.LandingContacts);
        bool landed=false;
        for(int i=0;i<count;i++)if(actor.LandingContacts[i].normal.y>.5f)landed=true;
        if(actor.NailmasterLaunchElapsed>.1f && actor.Body.linearVelocity.y<=.1f && landed) {
            actor.NailmasterLaunching=false;actor.Body.linearVelocity=Vector2.zero;actor.Body.simulated=false;
            actor.Position=actor.Health.transform.position;actor.Remaining=actor.HoldDuration;
            actor.Animation!.PlayFromFrame(actor.GetDown,0);actor.Animation.Pause();actor.Animation.SetFrame(0,false);
            log?.Invoke("HK_NAILMASTER_STUN_LANDED entity="+actor.Entity+" y="+actor.Position.y+" flightSeconds="+actor.NailmasterLaunchElapsed+" stunSeconds="+actor.HoldDuration+" nativeTerrainContact=True frame="+Time.frameCount);
        } else if(actor.NailmasterLaunchElapsed>8) {
            // A missing floor must not leave an immortal frozen boss in midair.
            log?.Invoke("HK_NAILMASTER_STUN_LANDING_TIMEOUT entity="+actor.Entity+" nativeRecovery=True frame="+Time.frameCount);
            Release(actor);
        }
    }
    private void Release(Actor actor,bool recover=true) {
        if(!actor.Holding)return;actor.Holding=false;
        if(actor.NailmasterKnockdown) {
            actor.NailmasterLaunching=actor.NailmasterKnockdown=false;
            if(actor.Body!=null && actor.Health!=null && !actor.Health.isDead && actor.Health.hp>0)actor.Body.gravityScale=actor.NailmasterGravity;
        }
        actor.StepLease?.Dispose();actor.StepLease=null;
        if(actor.Body!=null) {actor.Body.simulated=actor.Simulated;if(actor.Health!=null&&!actor.Health.isDead)actor.Body.linearVelocity=actor.AfterHold==null?actor.Velocity:Vector2.zero;}
        if(actor.Animation!=null&&!actor.AnimationPaused) actor.Animation.Resume();
        if(recover && actor.AfterHold!=null && actor.Control!=null && actor.Health!=null && !actor.Health.isDead && actor.Health.hp>0 && actor.Health.gameObject.activeInHierarchy) {
            actor.HarmlessRecovery=profile==EncounterProfile.MassiveMossCharger;
            actor.Control.SetState(actor.AfterHold);
            if(actor.HarmlessRecovery) log?.Invoke("HK_MOSS_COUNTER_BURROW_IMMUNITY entity="+actor.Entity+" action="+actor.Action+" active=True nativeState="+actor.Control.ActiveStateName.Replace(' ','_')+" frame="+Time.frameCount);
            log?.Invoke("HK_AUTHORED_MOVE_RECOVERY entity="+actor.Entity+" action="+actor.Action+" state="+actor.AfterHold.Replace(' ','_')+" nativeFSM=True frame="+Time.frameCount);
        }
        actor.AfterHold=null;
        log?.Invoke("HK_AUTHORED_MOVE_HOLD_END entity="+actor.Entity+" action="+actor.Action+" gameplaySeconds="+(actor.HoldDuration-actor.Remaining)+" maxDrift="+actor.MaximumDrift+" nativeStateRestored=True frame="+Time.frameCount+" stationaryClip="+(actor.HoldAnimation??"paused")+" spriteChanges="+actor.SpriteChanges);
    }
    private static void RestoreStaggerLanding(Actor actor) {
        if(actor.StaggerLanding==null)return;
        actor.StaggerLanding.ToState="Idle";actor.StaggerLanding.ToFsmState=actor.StaggerOriginalLanding;
        actor.StaggerLanding=null;actor.StaggerOriginalLanding=null;
    }
    public void Dispose() {if(defenderOwner==this)defenderOwner=null;if(pairedOwner==this)pairedOwner=null;if(choiceOwner==this)choiceOwner=null;if(slyOwner==this)slyOwner=null;pairHarmony.UnpatchSelf();foreach(var actor in actors.Values) {RestoreStaggerLanding(actor);if(actor.ZoteCounterLanding && actor.ZoteLanding!=null){actor.ZoteLanding.ToState="Stun";actor.ZoteLanding.ToFsmState=actor.ZoteOriginalLanding;}Release(actor,false);actor.RangeQuery?.Dispose();}foreach(var pair in repairedWaits)pair.Key.finishEvent=pair.Value;repairedWaits.Clear();actors.Clear();damageOwners.Clear();}
}
