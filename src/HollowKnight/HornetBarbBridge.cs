using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YiGodhome.Bridge;
using Pose = YiGodhome.Bridge.Pose;

namespace YiGodhome;

// Environmental native hit receiver, using the same fresh native melee windows
// as Hall levers. No synthetic HealthManager, damage pool or target widget.
internal sealed class HornetBarbBridge : IDisposable
{
    private readonly HostDamageRegistry registry;
    private readonly Guid session, encounter;
    private readonly Action<string>? log;
    private readonly Func<long> allocate;
    private readonly Func<(HealthManager Health, long Generation)?> liveTarget;
    private readonly Dictionary<DamageHero, Entry> owners = new();
    private sealed class Entry {
        internal long Generation;
        internal long Action;
        internal bool ParryConsumed;
        internal HostReflectedProjectile? Reflection;
        internal HealthManager? PendingBoss;
        internal long PendingTargetGeneration;
        internal float PendingDeadline;
        internal PlayMakerFSM Control = null!;
        internal Collider2D Shape = null!;
        internal readonly Dictionary<int,long> Consumed = new();
    }
    internal HornetBarbBridge(Guid session, Guid encounter, string scene, NativeEncounterPools pools, Action<string>? log,
        Func<long> allocate, Func<(HealthManager Health, long Generation)?> liveTarget)
    {
        this.session = session; this.encounter = encounter; this.log = log;
        this.allocate=allocate;this.liveTarget=liveTarget;
        registry = new HostDamageRegistry(scene, d => d.name.StartsWith("Hornet Barb", StringComparison.Ordinal) && pools.Owns(d.gameObject), nativePools:true);
    }
    internal void Decorate(AttackVolume volume, DamageHero damage)
    {
        if (!damage.name.StartsWith("Hornet Barb", StringComparison.Ordinal) || !owners.TryGetValue(damage, out var entry)) return;
        volume.Active &= !entry.ParryConsumed && entry.Control.ActiveStateName=="Barb" && entry.Reflection?.IsReflected!=true && entry.PendingBoss==null;
        volume.ActionId=entry.Action;volume.ConsumeOnContact=false;volume.NotifyParry=true;
        volume.ParryCooldown=1;volume.CooldownOnDamage=true;
    }
    internal bool Parry(long action, HitKind kind)
    {
        if(kind!=HitKind.StrikeDeflected || action<=0)return false;
        var target=liveTarget();if(!target.HasValue)return false;
        foreach(var pair in owners) {
            var damage=pair.Key;var entry=pair.Value;
            if(entry.Action!=action || damage==null || !damage.gameObject.activeInHierarchy
                || entry.Generation!=registry.Activation(damage) || entry.ParryConsumed || entry.Reflection?.IsReflected==true || entry.PendingBoss!=null || entry.Control.ActiveStateName!="Barb")continue;
            var boss=target.Value.Health;long generation=target.Value.Generation;
            // Native events may be consumed by the next FSM/physics tick.
            // Acknowledge this one accepted parry, then lease only its real
            // launch; never resend or fabricate a replacement velocity.
            entry.ParryConsumed=true;
            entry.PendingBoss=boss;entry.PendingTargetGeneration=generation;entry.PendingDeadline=Time.time+1;
            entry.Control.SendEvent("BLOCKED HIT");
            log?.Invoke("HK_HORNET_BARB_LAUNCH_PENDING action="+action+" generation="+entry.Generation+" state="+entry.Control.ActiveStateName+" frame="+Time.frameCount);
            CompleteLaunch(damage,entry);
            return true;
        }
        return false;
    }
    private void CompleteLaunch(DamageHero damage,Entry entry) {
        var boss=entry.PendingBoss;if(boss==null)return;
        var current=liveTarget();long generation=entry.PendingTargetGeneration;
        if(!damage.gameObject.activeInHierarchy || registry.Activation(damage)!=entry.Generation
            || !current.HasValue || current.Value.Health!=boss || current.Value.Generation!=generation || boss.isDead) {
            entry.PendingBoss=null;log?.Invoke("HK_HORNET_BARB_LAUNCH_CANCELLED action="+entry.Action+" activationOrTargetEnded=True noReplay=True");return;
        }
        var body=damage.GetComponent<Rigidbody2D>();
        string state=entry.Control.ActiveStateName;
        if(state!="Break" || body==null || body.linearVelocity.sqrMagnitude<=0) {
            if(state=="Land" || state=="Recycle Pause" || state=="Recycle" || Time.time>=entry.PendingDeadline) {
                entry.PendingBoss=null;
                log?.Invoke("HK_HORNET_BARB_LAUNCH_CANCELLED action="+entry.Action+" state="+state.Replace(' ','_')+" nativeCleanupOrDeadline=True noReplay=True bridgeRetained=True");
            }
            return;
        }
        entry.Reflection??=damage.gameObject.AddComponent<HostReflectedProjectile>();
        entry.Reflection.Begin(entry.Control,damage,entry.Shape,boss,new TargetHealthState(boss.GetInstanceID(),generation,boss.hp),entry.Generation,log,
            validTargetLease:()=> {var target=liveTarget();return target.HasValue && target.Value.Health==boss && target.Value.Generation==generation;});
        entry.PendingBoss=null;
        log?.Invoke("HK_HORNET_BARB_PARRIED action="+entry.Action+" generation="+entry.Generation+" nativeLaunch=True nativeCollisionDamage=True frame="+Time.frameCount);
    }
    internal void Update(Pose? pose, long geometry)
    {
        if (pose == null || pose.GeometrySession != session || pose.GeometryFrame != geometry
            || GameManager.instance.gameState.ToString() == "PAUSED") return;
        foreach (var dead in owners.Keys.Where(d => d == null).ToArray()) owners.Remove(dead);
        foreach (var damage in registry.Snapshot()) {
            if (!damage.name.StartsWith("Hornet Barb", StringComparison.Ordinal)) continue;
            if (!damage.gameObject.activeInHierarchy) continue;
            if (!owners.TryGetValue(damage, out var entry)) {
                if (owners.Count >= 16) throw new InvalidOperationException("Hornet barb owner capacity exceeded.");
                var fsm = damage.GetComponents<PlayMakerFSM>().Single(f => f.FsmName == "Control");
                if (fsm.ActiveStateName != "Barb") continue; // Await native initialization before leasing.
                var tink = damage.GetComponent<TinkEffect>();
                if (tink == null || !tink.sendFSMEvent || tink.FSMEvent != "BLOCKED HIT" || tink.fsm != fsm
                    || !fsm.FsmStates.Any(s => s.Name == "Barb" && s.Transitions.Any(t => t.EventName == "BLOCKED HIT" && t.ToState == "Hit")))
                    throw new InvalidOperationException("Hornet native barb hit contract changed.");
                entry = new Entry { Control = fsm, Shape = damage.GetComponent<BoxCollider2D>() };
                if (entry.Shape == null) throw new InvalidOperationException("Hornet barb hit geometry missing.");
                owners.Add(damage, entry);
                log?.Invoke("HK_HORNET_BARB_BOUND entity=" + damage.GetInstanceID() + " nativeEvent=BLOCKED_HIT healthManager=False frame=" + Time.frameCount);
            }
            long generation = registry.Activation(damage);
            if (generation != entry.Generation) { entry.Generation = generation; entry.Action=allocate(); entry.ParryConsumed=false;entry.PendingBoss=null;entry.Consumed.Clear(); }
            CompleteLaunch(damage,entry);
            if (entry.ParryConsumed || entry.PendingBoss!=null || !entry.Shape.enabled || !damage.gameObject.activeInHierarchy || entry.Control.ActiveStateName != "Barb") continue;
            foreach (var window in pose.Melee) {
                if (entry.Consumed.TryGetValue(window.Collider, out var prior) && prior >= window.Activation) continue;
                var b = new Bounds(new Vector3(window.X, window.Y, entry.Shape.bounds.center.z), new Vector3(window.Width, window.Height, 1));
                if (!entry.Shape.bounds.Intersects(b)) continue;
                if (entry.Consumed.Count >= 64 && !entry.Consumed.ContainsKey(window.Collider)) throw new InvalidOperationException("Hornet barb melee identity capacity exceeded.");
                entry.Consumed[window.Collider] = window.Activation;
                entry.Control.SendEvent("BLOCKED HIT");
                log?.Invoke("HK_HORNET_BARB_MELEE encounter=" + encounter + " entity=" + damage.GetInstanceID() + " generation=" + generation
                    + " collider=" + window.Collider + " activation=" + window.Activation + " state=" + entry.Control.ActiveStateName.Replace(' ','_') + " geometry=" + geometry + " frame=" + Time.frameCount);
                break;
            }
        }
    }
    public void Dispose() { foreach(var entry in owners.Values)if(entry.Reflection!=null){entry.Reflection.Retire();UnityEngine.Object.Destroy(entry.Reflection);}registry.Dispose(); owners.Clear(); log?.Invoke("HK_HORNET_BARB_DISPOSE retained=0"); }
}
