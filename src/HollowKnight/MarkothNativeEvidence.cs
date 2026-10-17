using System;
using System.Linq;
using HutongGames.PlayMaker;
using YiGodhome.Bridge;
namespace YiGodhome;
internal static class MarkothNativeEvidence {
 internal static void Inspect(HealthManager health, Action<string>? log) {
  if (!SessionConfiguration.Current.RunFixtures) return;
  var prefab = health.GetComponents<PlayMakerFSM>().SelectMany(f => f.FsmStates).SelectMany(s => s.Actions)
   .OfType<HutongGames.PlayMaker.Actions.CreateObject>().Select(a => a.gameObject.Value).FirstOrDefault(g => g != null && g.name == "Markoth Shield");
  var fsms = health.GetComponents<PlayMakerFSM>().Concat(prefab == null ? Array.Empty<PlayMakerFSM>() : prefab.GetComponentsInChildren<PlayMakerFSM>(true));
  foreach (var fsm in fsms) foreach(var state in fsm.FsmStates) {
   log?.Invoke("HK_MARKOTH_GRAPH object=" + fsm.name + " fsm=" + fsm.FsmName + " state=" + state.Name + " transitions=" + string.Join(",",state.Transitions.Select(t=>t.EventName+"->"+t.ToState)));
   foreach(var action in state.Actions) log?.Invoke("HK_MARKOTH_ACTION fsm=" + fsm.FsmName + " state=" + state.Name + " type=" + action.GetType().Name + " fields=" + string.Join(";",action.GetType().GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public).Select(f=>f.Name+"="+Describe(f.GetValue(action)))));
  }
  if(prefab!=null) foreach(var c in prefab.GetComponentsInChildren<UnityEngine.Collider2D>(true)) log?.Invoke("HK_MARKOTH_SHIELD_SHAPE name="+c.name+" type="+c.GetType().Name+" trigger="+c.isTrigger+" scripts="+string.Join(",",c.GetComponents<UnityEngine.MonoBehaviour>().Select(b=>b==null?"missing":b.GetType().Name)));
 }
 private static string Describe(object? v) => v is FsmOwnerDefault o ? o.OwnerOption + ":" + o.GameObject?.Name + "=" + o.GameObject?.Value?.name : v is FsmEvent e ? e.Name : v is FsmGameObject g ? g.Value?.name ?? "null" : v?.ToString() ?? "null";
}
