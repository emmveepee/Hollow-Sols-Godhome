using System;
using System.Linq;
using HutongGames.PlayMaker.Actions;
using UnityEngine;
using YiGodhome.Bridge;
namespace YiGodhome;
// Exact Markoth controller adaptation, retaining native shield identities and tweens.
internal sealed class MarkothShieldPolicy : IDisposable {
 private readonly HealthManager boss;
 private readonly PlayMakerFSM attack,movement,nails;
 private readonly CreateObject spawn;
 private readonly Action<string>? log;
 private PlayMakerFSM? control;
 private GameObject? root;
 private HutongGames.PlayMaker.FsmGameObject? nativeParent;
 private GameObject? parentOwner;
 private MeshRenderer[] artwork=Array.Empty<MeshRenderer>();
 private bool[] visible=Array.Empty<bool>();
 private bool[] renderEnabled=Array.Empty<bool>();
 private ParticleSystem[] particles=Array.Empty<ParticleSystem>();
 private bool[] particlePlaying=Array.Empty<bool>();
 private tk2dSpriteAnimator? animator;
 private readonly tk2dSpriteAnimationClip staggerEnd,staggerIdle;
 private NativeFsmStepLease? attackHold;
 private float staggerBegan;
 private bool movementEnabled,nailsEnabled,animatorPaused;
 private Vector3 holdPosition;
 private float stunnedUntil,telegraphUntil;
 private bool suppressed,dispelled;
 private long action;
 private string state="";
 internal MarkothShieldPolicy(HealthManager boss,Action<string>? log) {
  this.boss=boss;this.log=log;
  var fsms=boss.GetComponents<PlayMakerFSM>();
  attack=fsms.Single(f=>f.FsmName=="Shield Attack");movement=fsms.Single(f=>f.FsmName=="Movement");nails=fsms.Single(f=>f.FsmName=="Attacking");
  spawn=attack.FsmStates.Single(s=>s.Name=="Init").Actions.OfType<CreateObject>().Single(a=>a.gameObject.Value?.name=="Markoth Shield");
  if(!new[]{"Ready","Send Attack","Reset"}.All(n=>attack.FsmStates.Any(s=>s.Name==n)))throw new InvalidOperationException("Markoth shield attack graph changed.");
  animator=boss.GetComponent<tk2dSpriteAnimator>()??throw new InvalidOperationException("Markoth animator missing.");
  staggerEnd=animator.GetClipByName("Attack End");var idle=animator.GetClipByName("Idle");
  if(staggerEnd==null || staggerEnd.frames.Length<2 || staggerEnd.fps<=0 || idle==null || idle.frames.Length<2 || idle.fps<=0)
   throw new InvalidOperationException("Markoth native stagger animation contract changed.");
  staggerIdle=new tk2dSpriteAnimationClip(idle){name="YiGodhome Markoth Stagger Idle",wrapMode=tk2dSpriteAnimationClip.WrapMode.Loop};
  NativeFsmStepLease.Prepare();
 }
 internal static bool IsShield(Collider2D shape)=>shape is PolygonCollider2D&&shape.enabled&&shape.gameObject.activeInHierarchy&&shape.GetComponent<Renderer>()?.enabled==true&&shape.GetComponent<TinkEffect>()!=null&&(shape.name=="Shield"||shape.name=="Shield 2")&&shape.GetComponentsInParent<PlayMakerFSM>(true).Any(f=>f.name.StartsWith("Markoth Shield",StringComparison.Ordinal)&&f.FsmName=="Control");
 private void Bind() {
  if(control!=null)return;
  root=spawn.storeObject.Value;if(root==null)return;
  control=root.GetComponents<PlayMakerFSM>().Single(f=>f.FsmName=="Control");
  if(!new[]{"Tween Out Init","At Start CW","At Start CCW","At Pause","Tween Out","Tween In","Stop","Choose"}.All(n=>control.FsmStates.Any(s=>s.Name==n)))throw new InvalidOperationException("Markoth native shield recovery graph changed.");
  artwork=root.GetComponentsInChildren<MeshRenderer>(true);if(artwork.Length!=2)throw new InvalidOperationException("Markoth shield artwork ownership changed.");
  visible=new bool[artwork.Length];renderEnabled=new bool[artwork.Length];
  particles=root.GetComponentsInChildren<ParticleSystem>(true);
  if(particles.Length>16)throw new InvalidOperationException("Markoth shield particle ownership budget exceeded.");
  particlePlaying=new bool[particles.Length];
  nativeParent=control.FsmStates.Single(s=>s.Name=="Init").Actions.OfType<GetParent>().Single().storeResult;
  parentOwner=nativeParent.Value;
  if(parentOwner!=boss.gameObject)throw new InvalidOperationException("Native shield follow owner is not Markoth.");
  log?.Invoke("HK_MARKOTH_SHIELDS_BOUND owner="+root.GetInstanceID()+" shields=2 particles="+particles.Length+" nativeTween=True frame="+Time.frameCount);
 }
 internal void Update() {
  Bind();if(control==null||boss==null||boss.isDead)return;
  if(dispelled&&Time.time<stunnedUntil){boss.transform.position=holdPosition;var rb=boss.GetComponent<Rigidbody2D>();if(rb!=null)rb.linearVelocity=Vector2.zero;
   var clip=Time.time-staggerBegan<staggerEnd.frames.Length/staggerEnd.fps?staggerEnd:staggerIdle;
   if(animator!=null){if(animator.CurrentClip!=clip)animator.Play(clip);if(GameManager.instance?.IsGamePaused()!=true)animator.Resume();else animator.Pause();}return;}
  if(dispelled) {
   dispelled=false;
   ReleaseHold();
   // Replay the exact native initial outward tween from the center, preserving
   // which of the two authored shield bodies was active before the counter.
   var tween=control.FsmStates.Single(s=>s.Name=="Tween Out Init").Actions.OfType<iTweenMoveTo>().Single();
   var moving=FSMUtility.GetSafe(tween.gameObject,tween);
   if(moving==null)throw new InvalidOperationException("Native shield respawn tween owner missing.");
   var origin=moving.transform.localPosition;origin.x=0;origin.y=0;moving.transform.localPosition=origin;
   for(int i=0;i<artwork.Length;i++)artwork[i].enabled=renderEnabled[i];
   RestoreParticles();
   // Resume this initialized controller without replaying its Init graph.
   // Init loses its detached parent and resets later shield progression.
   bool restart=control.Fsm.RestartOnEnable; control.Fsm.RestartOnEnable=false;
   control.enabled=true; control.Fsm.RestartOnEnable=restart;
   // OnEnable replays Init on this already-detached native shield root. Its
   // GetParent then loses the cached boss and the every-frame follow target.
   nativeParent!.Value=parentOwner;
   control.SetState("Tween Out Init");
   attack.SetState("Reset");telegraphUntil=Time.time+1;
   log?.Invoke("HK_MARKOTH_SHIELD_RESPAWN action="+action+" shields="+visible.Count(v=>v)+" nativeState=Tween_Out_Init telegraphSeconds=1 damaging=False frame="+Time.frameCount);
  }
  if(suppressed&&telegraphUntil>0&&Time.time>=telegraphUntil&&control.ActiveStateName!="Tween Out Init") {
   suppressed=false;telegraphUntil=0;
   log?.Invoke("HK_MARKOTH_SHIELD_READY action="+action+" damaging=True nativeState="+control.ActiveStateName.Replace(' ','_')+" frame="+Time.frameCount);
  }
  string next=attack.ActiveStateName+":"+control.ActiveStateName;
  if(next!=state){state=next;log?.Invoke("HK_MARKOTH_SHIELD_WINDOW state="+next.Replace(' ','_')+" action="+action+" suppressed="+suppressed+" frame="+Time.frameCount);}
 }
 private bool Windup=>!suppressed&&attack.ActiveStateName=="Ready";
 private bool Crimson=>!suppressed&&attack.ActiveStateName=="Send Attack"&&control!=null&&new[]{"At Start CW","At Start CCW","At Pause","Tween Out","Tween In"}.Contains(control.ActiveStateName);
 internal void Action(ref long sequence) { if((Windup||Crimson)&&action==0)action=++sequence;if(!Windup&&!Crimson&&!suppressed)action=0; }
 internal void Decorate(AttackVolume v,DamageHero source) {
  if(control==null||source.GetComponentsInParent<PlayMakerFSM>(true).All(f=>f!=control))return;
  v.BlocksProjectiles=!dispelled&&source.GetComponent<PolygonCollider2D>()?.GetInstanceID()==v.Id;
  v.ParryCooldown=1;v.CooldownOnDamage=true;
  v.Active&=!suppressed;
  if(Crimson){v.ActionId=action;v.Traits|=AttackTraits.Crimson;}
 }
 internal MeshRenderer[] VisualParts=>Windup||Crimson ? artwork.Where(r=>r!=null&&r.enabled&&r.gameObject.activeInHierarchy).ToArray():Array.Empty<MeshRenderer>();
 internal bool Present(ContactFrame frame) {
  if(!Windup&&!Crimson)return false;
  frame.AttackId=frame.CrimsonSequence=action;frame.Crimson=true;frame.Windup=Windup;frame.CrimsonCharge=1;
  var b=boss.GetComponent<Collider2D>().bounds;frame.X=b.center.x;frame.Y=b.center.y;frame.Width=Math.Max(1,b.size.x);frame.Height=Math.Max(1,b.size.y);return true;
 }
 internal bool IsPresent=>Windup||Crimson;
 internal bool Counter(long id,HitKind kind) {
  if(id<=0||id!=action||kind!=HitKind.UnboundedCounter||!Crimson||control==null)return false;
  suppressed=dispelled=true;stunnedUntil=Time.time+3;
  // The dormant second shield has an enabled renderer on an inactive object.
  // Restore that renderer flag independently of visibility so HK's later
  // native activation can reveal it without a second mod-owned spawn.
  for(int i=0;i<artwork.Length;i++){renderEnabled[i]=artwork[i].enabled;visible[i]=renderEnabled[i]&&artwork[i].gameObject.activeInHierarchy;artwork[i].enabled=false;}
  for(int i=0;i<particles.Length;i++){particlePlaying[i]=particles[i].isPlaying;particles[i].Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);}
  control.enabled=false;iTween.Stop(root!);foreach(var r in artwork)iTween.Stop(r.gameObject);
  movementEnabled=movement.enabled;nailsEnabled=nails.enabled;movement.enabled=nails.enabled=false;
  attackHold=new NativeFsmStepLease(attack.Fsm,true);
  holdPosition=boss.transform.position;var rb=boss.GetComponent<Rigidbody2D>();if(rb!=null)rb.linearVelocity=Vector2.zero;
  staggerBegan=Time.time;
  if(animator!=null){animatorPaused=animator.Paused;animator.Play(staggerEnd);animator.Resume();}
  log?.Invoke("HK_MARKOTH_SHIELD_DISPEL action="+id+" shields="+visible.Count(v=>v)+" stunSeconds=3 damaging=False frame="+Time.frameCount);
  return true;
 }
 private static void Resume(PlayMakerFSM fsm,bool enabled) {
  if(fsm==null)return;
  bool restart=fsm.Fsm.RestartOnEnable;fsm.Fsm.RestartOnEnable=false;
  fsm.enabled=enabled;fsm.Fsm.RestartOnEnable=restart;
 }
 private void ReleaseHold(){attackHold?.Dispose();attackHold=null;Resume(movement,movementEnabled);Resume(nails,nailsEnabled);if(animator!=null){if(animatorPaused)animator.Pause();else animator.Resume();}}
 private void RestoreParticles(){for(int i=0;i<particles.Length;i++)if(particles[i]!=null&&particlePlaying[i]&&particles[i].gameObject.activeInHierarchy)particles[i].Play(false);}
 public void Dispose(){if(dispelled){ReleaseHold();if(control!=null)Resume(control,true);for(int i=0;i<artwork.Length;i++)if(artwork[i]!=null)artwork[i].enabled=renderEnabled[i];RestoreParticles();}suppressed=dispelled=false;}
}
