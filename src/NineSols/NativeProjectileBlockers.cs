using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using YiGodhome.Bridge;
using YiGodhome.Geometry;
namespace YiGodhome;
// Query-only shield copies: never terrain, damage receivers or Yi movement solids.
internal sealed class NativeProjectileBlockers : IDisposable {
 private sealed class Entry { internal PolygonCollider2D Shape = null!; internal long Activation; internal Vector2[] Points=Array.Empty<Vector2>(); }
 private readonly Dictionary<int,Entry> entries = new();
 private readonly RaycastHit2D[] hits = new RaycastHit2D[256];
 private readonly Player player;
 private readonly GeometryLandingProbe geometry;
 private readonly Action<string> log;
 private readonly Harmony harmony = new("local.yigodhome.projectile-blockers");
 private int auditSamples;
 private static readonly Dictionary<string,float> diagnosticOffsets=new();
 internal static float? DiagnosticOffset(Player player)=>SessionConfiguration.Current.Has("markoth-projectile-probe")&&diagnosticOffsets.TryGetValue(player.weaponDataCollection.currentWeapon.name,out var offset)?offset:(float?)null;
 private float nextPrune;
 private readonly List<int> removed=new();

 private sealed class Arrow { internal Projectile Projectile=null!; internal PoolObject Pool=null!; internal Collider2D[] Shapes=Array.Empty<Collider2D>(); }
 private readonly Dictionary<int,Arrow> arrows=new();
 private ContactFrame? frame;
 private Exception? failure;
 private static NativeProjectileBlockers? current;
 private static readonly FieldInfo Moving = AccessTools.Field(typeof(Projectile),"moving");
 private static readonly FieldInfo FlightShape=AccessTools.Field(typeof(Projectile),"myCollider");
 private static readonly FieldInfo EffectShape=AccessTools.Field(typeof(EffectDealer),"_myCollider");
 private static readonly MethodInfo Finish = AccessTools.Method(typeof(Projectile),"HitTargetDone");
 internal NativeProjectileBlockers(Player player,GeometryLandingProbe geometry,Action<string> log) {
  this.player=player;this.geometry=geometry;this.log=log;current=this;
  try { harmony.Patch(AccessTools.Method(typeof(Projectile),"ApplyPhysicsMove"),prefix:new HarmonyMethod(typeof(NativeProjectileBlockers),nameof(BeforeMove))); harmony.Patch(AccessTools.Method(typeof(EffectDealer),"HitEffectReceiverCheck"),prefix:new HarmonyMethod(typeof(NativeProjectileBlockers),nameof(BeforeDealer))); harmony.Patch(AccessTools.Method(typeof(PoolObject),"OnReturnToPool"),prefix:new HarmonyMethod(typeof(NativeProjectileBlockers),nameof(BeforeReturn))); harmony.Patch(AccessTools.Method(typeof(RaycastDetector),"AttackableAndSolidRaycastTest"),prefix:new HarmonyMethod(typeof(NativeProjectileBlockers),nameof(BeforeRaycast))); } catch { Dispose();throw; }
 }
 internal void Update(ContactFrame? next) {
  if(failure!=null)throw new InvalidOperationException("Native projectile blocker failed without replay.",failure);
  if(frame != null && next != null && frame.Encounter == next.Encounter && frame.GeometryFrame == next.GeometryFrame && frame.Sequence == next.Sequence) return;
  frame=next;
  if(Time.realtimeSinceStartup>=nextPrune){nextPrune=Time.realtimeSinceStartup+1;PruneArrows();}
  foreach(var e in entries.Values)e.Shape.enabled=false;
  if(next==null)return;
  foreach(var v in next.Volumes) {
   if(!v.BlocksProjectiles)continue;
   if(!entries.TryGetValue(v.Id,out var e)) {
    if(entries.Count>=AttackVolumes.MaxCount)throw new InvalidOperationException("Projectile blocker capacity exceeded.");
    var obj=new GameObject("YiGodhome Projectile Blocker"){layer=2};
    e=new Entry{Shape=obj.AddComponent<PolygonCollider2D>()};e.Shape.isTrigger=true;entries.Add(v.Id,e);
   }
   e.Activation=v.Activation;
   if(e.Points.Length!=v.Points.Length/2)e.Points=new Vector2[v.Points.Length/2];
   var points=e.Points;
   for(int i=0;i<points.Length;i++){var p=geometry.Map.ToTarget(new Point(v.Points[2*i],v.Points[2*i+1]));points[i]=new Vector2(p.X,p.Y);}
   e.Shape.points=points;e.Shape.enabled=true;
  }
 }
 private static void BeforeReturn(PoolObject __instance) {
  NativePlayerArrow.Retire(__instance);
  var projectile=__instance.GetComponent<Projectile>();if(projectile!=null)current?.arrows.Remove(projectile.GetInstanceID());
 }
 private static bool BeforeMove(Projectile __instance) {
  var service=current;if(service==null)return true;if(service.failure!=null)return false;
  try{return service.Move(__instance);}catch(Exception ex){service.failure=ex;service.log("NS_PROJECTILE_BLOCKER_FAILED replay=False reason="+ex.Message);return false;}
 }
 private bool Move(Projectile arrow) {
  if(frame==null||entries.Count==0||RCGTime.deltaTime<=0||!(bool)Moving.GetValue(arrow))return true;
  var pool=arrow.GetComponentInParent<PoolObject>();
  if(pool==null||!Owned(pool.lastPlayerComponent)||NativePlayerArrow.IsPiercer(arrow,player)||NativePlayerArrow.IsThunder(arrow,player))return true;
  if(!arrows.TryGetValue(arrow.GetInstanceID(),out var native)) {
   if(arrows.Count>=128)PruneArrows();
   if(arrows.Count>=128)throw new InvalidOperationException("Native arrow blocker cache capacity exceeded.");
   var effects=System.Linq.Enumerable.Where(arrow.GetComponentsInChildren<EffectDealer>(true),d=>NativePlayerArrow.Owner(d,player)!=null);
   var shapes=System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Distinct(System.Linq.Enumerable.OfType<Collider2D>(System.Linq.Enumerable.Concat(System.Linq.Enumerable.Select(effects,d=>EffectShape.GetValue(d)),new object[]{FlightShape.GetValue(arrow)}))));
   native=new Arrow{Projectile=arrow,Pool=pool,Shapes=shapes};arrows.Add(arrow.GetInstanceID(),native);
   if(SessionConfiguration.Current.Has("markoth-projectile-probe")) {
    string weapon=NativePlayerArrow.WeaponName(arrow,player);
    var flight=FlightShape.GetValue(arrow) as Collider2D;
    if(flight!=null&&flight.enabled&&!diagnosticOffsets.ContainsKey(player.weaponDataCollection.currentWeapon.name)) {
     float offset=flight.bounds.center.y-player.transform.position.y;
     if(Math.Abs(offset)>200)throw new InvalidOperationException("Native arrow nozzle offset out of fixture bounds.");
     diagnosticOffsets.Add(player.weaponDataCollection.currentWeapon.name,offset);
     log("NS_ARROW_NATIVE_NOZZLE weapon="+weapon+" offsetY="+offset+" collider="+flight.name+" nativeFrame="+Time.frameCount);
    }
   }
   if((SessionConfiguration.Current.Has("markoth-shield-probe")||SessionConfiguration.Current.Has("markoth-projectile-probe"))&&auditSamples++<8)log("NS_SHIELD_ARROW_GRAPH projectile="+arrow.name+" nativePool="+pool.name+" playerOwned=True shapes="+shapes.Length);
  }
  if(native.Pool!=pool)throw new InvalidOperationException("Native arrow pool owner changed.");
  foreach(var shape in native.Shapes)if(shape.enabled&&shape.gameObject.activeInHierarchy&&!MoveShape(arrow,shape))return false;
  return true;
 }
 private static bool BeforeDealer(EffectDealer __instance,Collider2D __0,ref bool __result) {
  var service=current;if(service==null||service.frame==null||service.entries.Count==0||RCGTime.deltaTime<=0)return true;
  try {
   var pool=NativePlayerArrow.Owner(__instance,service.player);if(pool==null||NativePlayerArrow.IsPiercer(__instance,service.player)||NativePlayerArrow.IsThunder(__instance,service.player))return true;
   var shape=EffectShape.GetValue(__instance) as Collider2D;if(shape==null||!shape.enabled)return true;
   var start=(Vector2)service.player.transform.position;
   var target=__0.ClosestPoint(start);float targetDistance=Vector2.Distance(start,target);
   foreach(var pair in service.entries) {
    var shield=pair.Value.Shape;if(!shield.enabled||!Physics2D.Distance(shape,shield).isOverlapped)continue;
    // A piercing beam can overlap shield and victim in the same physics step.
    // Resolve the intervening shield before permitting a downstream native hit.
    if(Vector2.Distance(start,shield.ClosestPoint(start))>targetDistance)continue;
    int id=pool.GetInstanceID();string weapon=NativePlayerArrow.WeaponName(__instance,service.player);string prefab=pool.OriginalPrefab.name.Replace(" ","_");pool.ReturnToPool();
    service.log("NS_PROJECTILE_BLOCKED encounter="+service.frame.Encounter+" shield="+pair.Key+" activation="+pair.Value.Activation+" projectile="+id+" prefab="+prefab+" weapon="+weapon+" hostFrame="+service.frame.NativeFrame+" nativeFrame="+Time.frameCount+" nativePoolReturn=True active="+pool.gameObject.activeInHierarchy+" route=nativeDealer");
    __result=false;return false;
   }
   return true;
  }catch(Exception ex){service.failure=ex;service.log("NS_PROJECTILE_BLOCKER_FAILED replay=False reason="+ex.Message);__result=false;return false;}
 }
 private static readonly FieldInfo DetectorShape=AccessTools.Field(typeof(RaycastDetector),"myCollider2D");
 private static readonly MethodInfo SolidEvent=AccessTools.Method(typeof(RaycastDetector),"InvokeRayCastSolidEvent");
 private static bool BeforeRaycast(RaycastDetector __instance,Vector2 __0,ref int __result) {
  var service=current;if(service==null||service.frame==null||RCGTime.deltaTime<=0||!__instance.isActiveAndEnabled)return true;
  try {
   if(!NativePlayerArrow.IsThunder(__instance,service.player))return true;
   var shape=DetectorShape.GetValue(__instance) as Collider2D;
   if(shape==null)throw new InvalidOperationException("Native Thunder raycast geometry missing.");
   var b=shape.bounds;var filter=new ContactFilter2D();filter.SetLayerMask(1<<2);filter.useTriggers=true;
   float distance=__0.magnitude*RCGTime.deltaTime;int count;
   if(shape is CircleCollider2D circle)count=Physics2D.CircleCast(b.center,circle.radius,__0.normalized,filter,service.hits,distance);
   else if(shape is BoxCollider2D)count=Physics2D.BoxCast(b.center,b.size,shape.transform.eulerAngles.z,__0.normalized,filter,service.hits,distance);
   else throw new InvalidOperationException("Unsupported native Thunder raycast geometry: "+shape.GetType().Name);
   if(count==service.hits.Length)throw new InvalidOperationException("Native Thunder query capacity exceeded.");
   int best=-1,shield=0;float nearest=float.PositiveInfinity;long activation=0;
   for(int i=0;i<count;i++)foreach(var e in service.entries)if(e.Value.Shape.enabled&&service.hits[i].collider==e.Value.Shape&&service.hits[i].distance<nearest){best=i;shield=e.Key;activation=e.Value.Activation;nearest=service.hits[i].distance;}
   if(best<0)return true;
   var pool=NativePlayerArrow.Owner(__instance,service.player)!;int id=pool.GetInstanceID();string weapon=NativePlayerArrow.WeaponName(__instance,service.player);
   // Same native emitter event as real solid collision. The authored Thunder
   // FSM owns its impact, explosion, damage and retirement.
   SolidEvent.Invoke(__instance,new object[]{service.hits[best]});
   service.log("NS_PROJECTILE_SHIELD_IMPACT encounter="+service.frame.Encounter+" shield="+shield+" activation="+activation+" projectile="+id+" weapon="+weapon+" nativeSolidEvent=True nativeEmitter=RaycastDetector nativeFrame="+Time.frameCount+" hostFrame="+service.frame.NativeFrame);
   __result=0;return false;
  }catch(Exception ex){service.failure=ex;service.log("NS_PROJECTILE_BLOCKER_FAILED replay=False reason="+ex.Message);__result=0;return false;}
 }
 private void PruneArrows(){removed.Clear();foreach(var p in arrows)if(p.Value.Projectile==null||p.Value.Pool==null||!p.Value.Pool.isOnScene)removed.Add(p.Key);foreach(var id in removed)arrows.Remove(id);}
 private bool Owned(Component source) {
  for(int i=0;i<8&&source!=null;i++) {if(source.transform.IsChildOf(player.transform))return true;var pool=source.GetComponentInParent<PoolObject>();if(pool==null||pool.lastPlayerComponent==source)return false;source=pool.lastPlayerComponent;}
  return false;
 }
 private bool MoveShape(Projectile arrow,Collider2D shape) {
  var motion=arrow.Velocity*RCGTime.deltaTime;
  var filter=new ContactFilter2D();filter.SetLayerMask(1<<2);filter.useTriggers=true;
  int count=shape.Cast(motion.normalized,filter,hits,motion.magnitude);
  if(count==hits.Length)throw new InvalidOperationException("Projectile blocker sweep capacity exceeded.");
  Entry? blocked=null;int id=0;float distance=float.PositiveInfinity;
  foreach(var pair in entries) {
   var e=pair.Value;if(!e.Shape.enabled)continue;
   if(Physics2D.Distance(shape,e.Shape).isOverlapped){blocked=e;id=pair.Key;distance=0;break;}
   for(int i=0;i<count;i++)if(hits[i].collider==e.Shape&&hits[i].distance<distance){blocked=e;id=pair.Key;distance=hits[i].distance;}
  }
  if(blocked==null)return true;
  int arrowId=arrow.GetInstanceID();string weapon=NativePlayerArrow.WeaponName(arrow,player);
  Finish.Invoke(arrow,new object[]{true,arrow.transform.position,false});
  log("NS_PROJECTILE_BLOCKED encounter="+frame!.Encounter+" shield="+id+" activation="+blocked.Activation+" projectile="+arrowId+" prefab="+arrow.name.Replace(" ","_")+" weapon="+weapon+" hostFrame="+frame.NativeFrame+" nativeFrame="+Time.frameCount+" nativePoolReturn=True active="+arrow.gameObject.activeInHierarchy);
  return false;
 }
 public void Dispose(){int released=entries.Count;harmony.UnpatchSelf();foreach(var e in entries.Values)if(e.Shape!=null)UnityEngine.Object.Destroy(e.Shape.gameObject);entries.Clear();arrows.Clear();NativePlayerArrow.Clear();diagnosticOffsets.Clear();frame=null;if(current==this)current=null;if(released>0)log("NS_PROJECTILE_BLOCKERS_DISPOSE released="+released+" retained=0");}
}
