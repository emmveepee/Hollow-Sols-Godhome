using System;
using System.Collections.Generic;
namespace YiGodhome.Bridge;

// A volley shares injury retirement without sharing its defense activation.
public sealed class DamageGroupRetirement
{
    private readonly HashSet<long> groups = new();
    private readonly Dictionary<long,float> initialHolds = new();
    private readonly Dictionary<long,bool> holdModes=new();
    private readonly List<long> ended=new(AttackVolumes.MaxCount);
    private readonly Predicate<long> removeMissing;
    private AttackVolume[] observed=Array.Empty<AttackVolume>();
    public DamageGroupRetirement(){removeMissing=Missing;}
    private bool Missing(long group){foreach(var volume in observed)if(volume.DamageGroupId==group)return false;return true;}
    public float InitialHoldUntil(long group) => initialHolds.TryGetValue(group,out var until)?until:-1;
    public bool Contains(long group) => group > 0 && groups.Contains(group);
    public bool Contains(long group,float now) => Contains(group) || group>0 && initialHolds.TryGetValue(group,out var until) && now<until;
    public bool Resolve(long group,bool damage,float now,float initialHold,bool repeat=false) {
        if(initialHold==0) return Resolve(group,damage);
        if(!float.IsFinite(now) || !float.IsFinite(initialHold) || initialHold<=0 || initialHold>10)
            throw new InvalidOperationException("Invalid initial damage-group hold.");
        if(!damage || group<=0 || groups.Contains(group))return false;
        if(initialHolds.TryGetValue(group,out var previous)) {
            if(holdModes[group]!=repeat)throw new InvalidOperationException("Live injury group changed hold mode.");
            if(!repeat || now<previous)return false;
            initialHolds[group]=now+initialHold;return true;
        }
        if(groups.Count+initialHolds.Count>=AttackVolumes.MaxCount)throw new InvalidOperationException("Damage-group hold budget exceeded.");
        initialHolds.Add(group,now+initialHold);holdModes.Add(group,repeat);return true;
    }
    public bool Resolve(long group, bool damage)
    {
        if (!damage || group <= 0 || groups.Contains(group)) return false;
        if (groups.Count+initialHolds.Count >= AttackVolumes.MaxCount) throw new InvalidOperationException("Retired damage-group budget exceeded.");
        return groups.Add(group);
    }
    public void Retain(AttackVolume[] volumes,float now=float.PositiveInfinity) {
        observed=volumes;
        try {
            groups.RemoveWhere(removeMissing);ended.Clear();
            foreach(var hold in initialHolds)if(now>=hold.Value && Missing(hold.Key))ended.Add(hold.Key);
            foreach(var key in ended){initialHolds.Remove(key);holdModes.Remove(key);}
        } finally {observed=Array.Empty<AttackVolume>();}
    }
}
