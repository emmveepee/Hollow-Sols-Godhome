using System;
using System.Collections.Generic;
namespace YiGodhome.Bridge;

// Concurrent native bodies may resolve in any order. Never use a highest-ID
// watermark to retire another body's still-live action.
public sealed class ActionRetirement
{
    private readonly HashSet<long> actions = new HashSet<long>();
    private readonly Predicate<long> removeMissing;
    private long observedPrimary;
    private AttackVolume[] observedVolumes=Array.Empty<AttackVolume>();
    private SummonedTarget[] observedTargets=Array.Empty<SummonedTarget>();
    private bool counterGroups;
    public ActionRetirement() {removeMissing=Missing;}
    public bool Contains(long action) => actions.Contains(action);
    public bool Retire(long action)
    {
        if (action <= 0 || actions.Contains(action)) return false;
        if (actions.Count >= AttackVolumes.MaxCount + 1) throw new InvalidOperationException("Concurrent retired-action budget exceeded.");
        return actions.Add(action);
    }
    public void Retain(long primary, AttackVolume[] volumes) => Retain(primary,volumes,false);
    public void Retain(long primary, AttackVolume[] volumes, SummonedTarget[] targets) {
        observedTargets=targets;
        try {Retain(primary,volumes,false);} finally {observedTargets=Array.Empty<SummonedTarget>();}
    }
    public void RetainCounterGroups(long sequence,AttackVolume[] volumes) => Retain(sequence,volumes,true);
    private void Retain(long primary,AttackVolume[] volumes,bool groups) {
        if(actions.Count==0) return;
        observedPrimary=primary;observedVolumes=volumes;counterGroups=groups;
        try {actions.RemoveWhere(removeMissing);} finally {observedVolumes=Array.Empty<AttackVolume>();}
    }
    private bool Missing(long action) {
        if(action==observedPrimary) return false;
        foreach(var volume in observedVolumes) if((counterGroups?volume.CounterGroupId:volume.ActionId)==action) return false;
        if(!counterGroups) foreach(var target in observedTargets) if(target.AttackId==action) return false;
        return true;
    }
}
