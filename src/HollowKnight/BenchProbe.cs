using System;
using System.Linq;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;

namespace YiGodhome;

internal static class BenchProbe
{
    public static void Audit(Action<string> log)
    {
        foreach (var fsm in Resources.FindObjectsOfTypeAll<PlayMakerFSM>().Where(f => f.gameObject.activeInHierarchy
            && (f.name.IndexOf("bench", StringComparison.OrdinalIgnoreCase) >= 0 || f.FsmName.IndexOf("bench", StringComparison.OrdinalIgnoreCase) >= 0)))
        {
            log("HK_BENCH_FSM object=" + fsm.name + " fsm=" + fsm.FsmName + " state=" + fsm.ActiveStateName + " position=" + fsm.transform.position);
            foreach (var state in fsm.FsmStates)
            {
                log("HK_BENCH_STATE name=" + state.Name + " transitions=" + string.Join(",", state.Transitions.Select(t => t.EventName + "->" + t.ToState)));
                foreach (var action in state.Actions)
                {
                    string fields = string.Join(";", action.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)
                        .Where(f => !f.IsStatic).Select(f => f.Name + "=" + Describe(f.GetValue(action))));
                    log("HK_BENCH_ACTION state=" + state.Name + " type=" + action.GetType().Name + " fields=" + fields);
                }
            }
        }
    }
    private static string Describe(object? value)
    {
        if (value == null) return "null";
        if (value is FsmString s) return s.Value ?? "null";
        if (value is FsmBool b) return b.Value.ToString();
        if (value is FsmEvent e) return e.Name;
        if (value is FsmGameObject g) return g.Value == null ? "null" : g.Value.name;
        if (value is UnityEngine.Object o) return o.name;
        if (value is Array a) return "array[" + a.Length + "]";
        return value.ToString() ?? "null";
    }
}
