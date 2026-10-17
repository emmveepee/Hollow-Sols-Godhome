using System;
using System.Linq;
using YiGodhome.Bridge;
namespace YiGodhome;
internal static class SoulNativeEvidence {
 internal static void Inspect(HealthManager health,Action<string>? log) {
  if(!SessionConfiguration.Current.RunFixtures)return;
  if(health.name.Contains("Grimm")) foreach(var bat in UnityEngine.Resources.FindObjectsOfTypeAll<HealthManager>().Where(h=>h.name=="Real Bat"&&h.gameObject.scene==health.gameObject.scene)) Inspect(bat,log);
  foreach(var fsm in health.GetComponents<PlayMakerFSM>())
   foreach(var state in fsm.FsmStates.Where(s=>s.Name.IndexOf("Stun",StringComparison.OrdinalIgnoreCase)>=0 || s.Name=="Explode" || s.Name.Contains("Reform") || s.Name=="Exploded" || s.Name=="Adjust HP" || health.name.StartsWith("Black Knight",StringComparison.Ordinal) || health.name=="Real Bat"
    || (health.name.Contains("Mage Lord") && (health.name.EndsWith("Phase2",StringComparison.Ordinal) || s.Name.Contains("Quake") || s.Name.Contains("Dash") || s.Name.Contains("Charge") || s.Name=="Attack Choice" || s.Name=="Reactivate" || s.Name=="Idle" || s.Name=="Re Pos"))
    || (health.name.Contains("Grimm") && (s.Name.Contains("Dash") || s.Name.Contains("Slash") || s.Name.Contains("Uppercut") || s.Name.Contains("Bow") || s.Name.Contains("Puffer") || s.Name.StartsWith("AD ",StringComparison.Ordinal) || s.Name.Contains("Balloon") || s.Name.Contains("Firebat") || s.Name.Contains("Pillar"))))) {
    log?.Invoke("HK_SOUL_STAGGER_GRAPH entity="+health.GetInstanceID()+" fsm="+fsm.FsmName+" state="+state.Name+" transitions="+string.Join(",",state.Transitions.Select(t=>t.EventName+"->"+t.ToState)));
    foreach(var action in state.Actions)log?.Invoke("HK_SOUL_STAGGER_ACTION state="+state.Name+" type="+action.GetType().Name+" fields="+string.Join(";",action.GetType().GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public).Select(f=>f.Name+"="+Describe(f.GetValue(action)))));
   }
  foreach(var c in health.GetComponentsInChildren<UnityEngine.Collider2D>(true))log?.Invoke("HK_SOUL_NATIVE_RECEIVER entity="+health.GetInstanceID()+" boss="+health.name.Replace(' ','_')+" collider="+c.name+" type="+c.GetType().Name+" active="+(c.enabled&&c.gameObject.activeInHierarchy)+" healthOwner="+(c.GetComponentInParent<HealthManager>()?.GetInstanceID()??0)+" scripts="+string.Join(",",c.GetComponents<UnityEngine.MonoBehaviour>().Select(b=>b==null?"missing":b.GetType().Name)));
 }
 private static string Describe(object? v)=>v is HutongGames.PlayMaker.FsmOwnerDefault o?o.OwnerOption+":"+o.GameObject?.Name+"="+o.GameObject?.Value?.name:v is HutongGames.PlayMaker.FsmGameObject g?g.Value?.name??"null":v?.ToString()??"null";
}
