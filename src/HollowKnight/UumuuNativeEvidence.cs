using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YiGodhome.Bridge;
using HutongGames.PlayMaker;

namespace YiGodhome;

// Bounded, exact Ooma dependency evidence. No discovery of unrelated objects,
// artificial exposure, damage replay or normal-session reflection inventory.
internal sealed class UumuuNativeEvidence
{
    private readonly HashSet<int> inspected = new HashSet<int>();
    private readonly Action<string>? log;
    private PlayMakerFSM? bossControl;
    private HealthManager? boss;
    private string lastState = "";
    internal UumuuNativeEvidence(Action<string>? log) { this.log = log; }
    internal void Register(HealthManager health)
    {
        if (health != null && health.name == "Mega Jellyfish GG" && boss != health) {
            boss = health;
            bossControl = health.GetComponents<PlayMakerFSM>().Single(f => f.FsmStates.Any(s => s.Name == "Wounded") && f.FsmStates.Any(s => s.Name == "Explode"));
        }
        if (!SessionConfiguration.Current.RunFixtures || health == null || !health.name.StartsWith("Jellyfish GG", StringComparison.Ordinal)) return;
        var effects = health.GetComponent<EnemyDeathEffects>();
        var corpse = effects == null ? null : (GameObject)HarmonyLib.AccessTools.Field(typeof(EnemyDeathEffects), "corpsePrefab").GetValue(effects);
        if (corpse == null) return;
        var pending = new Queue<GameObject>(); pending.Enqueue(corpse);
        while (pending.Count > 0) {
            var prefab = pending.Dequeue();
            if (!inspected.Add(prefab.GetInstanceID())) continue;
            if (inspected.Count > 8) throw new InvalidOperationException("Ooma evidence dependency budget exceeded.");
            foreach (var fsm in prefab.GetComponentsInChildren<PlayMakerFSM>(true)) {
                log?.Invoke("HK_OOMA_CORE_GRAPH object=" + fsm.name + " fsm=" + fsm.FsmName + " prefab=" + prefab.GetInstanceID());
                foreach (var state in fsm.FsmStates) {
                    log?.Invoke("HK_OOMA_CORE_STATE name=" + state.Name + " transitions=" + string.Join(",", state.Transitions.Select(t => t.EventName + "->" + t.ToState)));
                    foreach (var action in state.Actions) {
                        var fields = action.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                        log?.Invoke("HK_OOMA_CORE_ACTION state=" + state.Name + " type=" + action.GetType().Name + " fields="
                            + string.Join(";", fields.Select(f => f.Name + "=" + Describe(f.GetValue(action)))));
                        if (action.GetType().Name == "CreateObject" || action.GetType().Name == "SpawnObjectFromGlobalPool")
                            foreach (var reference in fields.Select(f => f.GetValue(action)).OfType<FsmGameObject>())
                                if (reference.Value != null && !reference.UseVariable && (reference.Value.name == "Lil Jellyfish" || reference.Value.name == "Gas Explosion Uumuu")) pending.Enqueue(reference.Value);
                    }
                }
            }
        }
    }
    internal void Observe(HealthManager health, Guid encounter, long activation) {
        if (health == null || health.name != "Mega Jellyfish GG") return;
        if (boss != health || bossControl == null) throw new InvalidOperationException("Uumuu native state owner was replaced without registration.");
        var control = bossControl;
        string snapshot = control.ActiveStateName + ":" + health.IsInvincible;
        if (lastState == snapshot) return;
        lastState = snapshot;
        log?.Invoke("HK_UUMUU_NATIVE_STATE encounter=" + encounter + " entity=" + health.GetInstanceID() + " activation=" + activation
            + " state=" + control.ActiveStateName.Replace(' ', '_') + " invincible=" + health.IsInvincible + " health=" + health.hp + " frame=" + Time.frameCount);
    }
    private static string Describe(object? value) => value is FsmEvent e ? e.Name : value is FsmGameObject g ? g.Value?.name ?? "null" : value?.ToString() ?? "null";
}
