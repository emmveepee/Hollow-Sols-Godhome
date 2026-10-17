using System;
using System.Collections.Generic;
using System.Linq;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;

namespace YiGodhome;

// Read the target's authored HP comparisons, never invent percentage gates or
// write phase variables. Only direct self HP-to-IntCompare/IntTestToBool lineage qualifies.
internal sealed class NativeHealthNotches
{
    private readonly HealthManager health;
    private readonly List<(FsmInt Value, string State)> thresholds = new List<(FsmInt, string)>();
    private int[]? pinned;
    private int pinnedMaximum;
    private bool discovered;
    private float nextDiscovery;
    private readonly Action<string>? log;
    internal NativeHealthNotches(HealthManager health, Action<string>? log)
    {
        this.health = health; this.log = log;
    }
    private void Discover()
    {
        bool initializedHp=false;
        thresholds.Clear();
        foreach (var fsm in health.GetComponents<PlayMakerFSM>()) {
            var self = new HashSet<string>(fsm.FsmStates.SelectMany(s => s.Actions).OfType<GetOwner>()
                .Select(a => a.storeGameObject.Name).Where(n => !string.IsNullOrEmpty(n)));
            bool Owns(FsmOwnerDefault target, FsmStateAction action) => FSMUtility.GetSafe(target, action)?.GetComponent<HealthManager>() == health
                || target.GameObject != null && target.GameObject.UseVariable && self.Contains(target.GameObject.Name);
            var variables = new HashSet<string>();
            foreach (var action in fsm.FsmStates.SelectMany(s => s.Actions)) {
                FsmInt? variable = action is GetHP get && Owns(get.target, get) ? get.storeValue
                    : action is GetHPEveryFrame every && Owns(every.target, every) ? every.storeValue : null;
                if (variable != null && variable.UseVariable && !string.IsNullOrEmpty(variable.Name)) variables.Add(variable.Name);
            }
            initializedHp |= variables.Count > 0;
            foreach (var state in fsm.FsmStates) foreach (var compare in state.Actions.OfType<IntCompare>()) {
                bool branches = !string.IsNullOrEmpty(compare.lessThan?.Name) || !string.IsNullOrEmpty(compare.greaterThan?.Name);
                if (compare.integer1.UseVariable && variables.Contains(compare.integer1.Name) && branches)
                    thresholds.Add((compare.integer2, fsm.FsmName + "/" + state.Name));
                if (compare.integer2.UseVariable && variables.Contains(compare.integer2.Name) && branches)
                    thresholds.Add((compare.integer1, fsm.FsmName + "/" + state.Name));
                if (Bridge.SessionConfiguration.Current.RunFixtures && (compare.integer1.UseVariable && variables.Contains(compare.integer1.Name)
                    || compare.integer2.UseVariable && variables.Contains(compare.integer2.Name)))
                    log?.Invoke("HK_HEALTH_COMPARISON state=" + state.Name.Replace(' ', '_') + " first=" + compare.integer1.Name + " second=" + compare.integer2.Name
                        + " less=" + compare.lessThan?.Name + " greater=" + compare.greaterThan?.Name);
            }
            foreach (var state in fsm.FsmStates) foreach (var compare in state.Actions.OfType<IntTestToBool>()) {
                bool branches = !string.IsNullOrEmpty(compare.lessThanBool?.Name) || !string.IsNullOrEmpty(compare.greaterThanBool?.Name);
                if (!branches) continue;
                if (compare.int1.UseVariable && variables.Contains(compare.int1.Name))
                    thresholds.Add((compare.int2, fsm.FsmName + "/" + state.Name));
                if (compare.int2.UseVariable && variables.Contains(compare.int2.Name))
                    thresholds.Add((compare.int1, fsm.FsmName + "/" + state.Name));
            }
        }
        discovered=initializedHp || !health.name.Contains("Grimm");
    }
    internal int[] Capture(int maximum)
    {
        var family = Bridge.EncounterProfile.ForScene(health.gameObject.scene.name)?.Family;
        if (family != null && !family.UsesHealthNotches) return Array.Empty<int>();
        // A native phase reset may replace the target maximum. Never export a
        // cached threshold outside the current target lease's HP range.
        if (pinned != null && pinnedMaximum == maximum) return pinned;
        pinned = null; pinnedMaximum = maximum;
        // A dormant native FSM has not rebound serialized action variables yet.
        // Delay discovery until its real HP reads are initialized; never pin
        // a preparing snapshot as proof that this boss has no thresholds.
        if(!discovered) {
            if(UnityEngine.Time.realtimeSinceStartup < nextDiscovery) return Array.Empty<int>();
            nextDiscovery=UnityEngine.Time.realtimeSinceStartup+.25f;
            Discover();
            if(!discovered) return Array.Empty<int>();
        }
        // Called at native target activation after authored difficulty/init HP.
        var values = thresholds.Select(t => t.Value.Value).Where(t => t > 0 && t < maximum).Distinct().OrderByDescending(t => t).ToArray();
        // A Self variable/threshold may be assigned by native Init after the
        // first preparing snapshot. Do not permanently pin that empty state.
        if (values.Length == 0 && thresholds.Count > 0) return Array.Empty<int>();
        pinned = values;
        Bridge.HealthNotches.Validate(pinned, maximum);
        log?.Invoke("HK_HEALTH_NOTCHES entity=" + health.GetInstanceID() + " name=" + health.name + " max=" + maximum
            + " thresholds=" + string.Join(",", pinned) + " lineage=" + string.Join(",", thresholds.Select(t => t.State)) + " nativeComparisons=True");
        return pinned;
    }
}
