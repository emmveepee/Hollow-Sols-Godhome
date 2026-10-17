using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YiGodhome.Bridge;

namespace YiGodhome;

internal sealed class NativeRingAttackPolicy : IDisposable
{
    private readonly Func<long> allocate;
    private readonly Action<string>? log;
    private HealthManager? boss;
    private PlayMakerFSM? attacker;
    private long volley;
    private bool volleyActive;
    private HutongGames.PlayMaker.Actions.FindChild[] references=Array.Empty<HutongGames.PlayMaker.Actions.FindChild>();
    private readonly Dictionary<int,NativeFiniteWeaponStrike> sources=new();
    private readonly List<NativeFiniteWeaponStrike> owners=new();
    private bool bound;
    internal NativeRingAttackPolicy(Func<long> allocate,Action<string>? log) {this.allocate=allocate;this.log=log;}
    internal void Register(HealthManager health)
    {
        if(boss==health) return;
        if(boss!=null) throw new InvalidOperationException("Duplicate native Hu ring owner.");
        boss=health;
        var control=health.GetComponents<PlayMakerFSM>().Single(f=>f.FsmName=="Attacking");
        attacker=control;
        references=control.FsmStates.Single(s=>s.Name=="Init").Actions.OfType<HutongGames.PlayMaker.Actions.FindChild>().ToArray();
        if(references.Length!=17 || references.Select(f=>f.childName.Value).Distinct().Count()!=17
            || references.Any(f=>!int.TryParse(f.childName.Value,out int n) || n<1 || n>17)) throw new InvalidOperationException("Native Hu ring manifest changed.");
    }
    internal void Update()
    {
        if(attacker!=null) {
            string state=attacker.ActiveStateName;
            bool active=state=="Ring Antic" || state=="Attack" || state=="Mega Warp Out" || state.StartsWith("M ",StringComparison.Ordinal);
            if(active && !volleyActive)volley=allocate();
            volleyActive=active;
        }
        if(bound || boss==null || references.Any(f=>f.storeResult.Value==null)) return;
        foreach(var reference in references) {
            var root=reference.storeResult.Value;
            if(root.scene!=boss.gameObject.scene || root.GetComponent<HealthManager>()!=null) throw new InvalidOperationException("Native finite ring ownership changed.");
            var control=root.GetComponents<PlayMakerFSM>().Single(f=>f.FsmName=="Control");
            var reset=control.FsmStates.Single(s=>s.Name=="Reset").Actions.OfType<HutongGames.PlayMaker.Actions.ActivateGameObject>().Single();
            // UseOwner is the direct native ring FSM's own GameObject even
            // before its inactive state actions have received Owner context.
            bool self=reset.gameObject.OwnerOption==HutongGames.PlayMaker.OwnerDefaultOption.UseOwner
                || FSMUtility.GetSafe(reset.gameObject,reset)==root;
            if(reset.activate.Value || !self) throw new InvalidOperationException("Native ring lifetime no longer ends in root deactivation.");
            var damage=root.GetComponentsInChildren<DamageHero>(true);
            if(damage.Length!=4 || damage.Any(d=>d.GetComponent<Collider2D>()==null)) throw new InvalidOperationException("Native ring strike shape closure changed.");
            var lease=root.AddComponent<NativeFiniteWeaponStrike>();owners.Add(lease);lease.Bind(allocate,log);
            foreach(var source in damage) sources.Add(source.GetInstanceID(),lease);
            log?.Invoke("HK_RING_STRIKE_BOUND index="+reference.childName.Value+" owner="+root.GetInstanceID()+" colliders="+string.Join(",",damage.Select(d=>d.GetComponent<Collider2D>().GetInstanceID()))+" shapes=4 nativeReset=True");
        }
        bound=true;references=Array.Empty<HutongGames.PlayMaker.Actions.FindChild>();
    }
    internal void Decorate(AttackVolume volume,DamageHero source)
    {
        Update();
        if(!sources.TryGetValue(source.GetInstanceID(),out var lease)) return;
        if(volley==0)volley=allocate();
        volume.ActionId=volume.DamageGroupId=volley;
        volume.Traits|=AttackTraits.RetireOnParry;
        volume.ParryCooldown=volume.InitialDamageGroupHold=1;volume.CooldownOnDamage=volume.RepeatDamageGroupHold=true;
        volume.Active &= boss!=null && !boss.isDead && boss.hp>0 && lease.gameObject.activeInHierarchy;
    }
    public void Dispose() {foreach(var owner in owners) if(owner!=null) UnityEngine.Object.Destroy(owner);owners.Clear();sources.Clear();}
}
