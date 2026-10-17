using System;
using System.Linq;
using HutongGames.PlayMaker.Actions;
using UnityEngine;
namespace YiGodhome;
// Authored stagger receiver, distinct geometry on the same health/activation lease.
internal sealed class NativeStaggerGeometry {
 private readonly Collider2D body,wound;
 internal NativeStaggerGeometry(HealthManager health,Action<string>? log,string? activationState="Stun Init",string childName="Wound Box") {
  body=health.GetComponent<BoxCollider2D>();
  var native=activationState==null ? health.GetComponentsInChildren<BoxCollider2D>(true).Single(c=>c.name==childName&&c.GetComponentInParent<HealthManager>()==health).gameObject
   : health.GetComponents<PlayMakerFSM>().SelectMany(f=>f.FsmStates).Where(s=>s.Name==activationState).SelectMany(s=>s.Actions).OfType<ActivateGameObject>().Single(a=>a.activate.Value&&a.gameObject.GameObject.Value?.name==childName).gameObject.GameObject.Value;
  if(native.GetComponentInParent<HealthManager>()!=health)throw new InvalidOperationException("Native stagger receiver ownership changed.");
  wound=native.GetComponent<BoxCollider2D>();if(body==null||wound==null)throw new InvalidOperationException("Native stagger geometry missing.");
  log?.Invoke("HK_SOUL_WOUND_BOUND entity="+health.GetInstanceID()+" body="+body.GetInstanceID()+" wound="+wound.GetInstanceID()+" nativeGates=True");
 }
 internal Collider2D Shape=>wound.enabled&&wound.gameObject.activeInHierarchy?wound:body;
}
