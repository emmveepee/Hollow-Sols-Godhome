using System;
using System.Linq;
using YiGodhome.Bridge;

namespace YiGodhome;

// Some native minions recycle through FSM states without disabling their
// GameObject. HP resets alone cannot reopen a retired target generation.
internal sealed class NativeMinionSpawnLease
{
    private readonly HealthManager health;
    private readonly PlayMakerFSM control;
    private readonly string[] activeStates;
    private readonly AttackGeneration phases = new();
    private readonly Action<string>? log;
    private int samples;
    private readonly int initialHealth;
    private bool phaseActive;
    private long sourceActivation;
    private int healthOffset;
    internal long Generation { get; private set; }
    internal bool StopNativeSpawn()
    {
        if (health == null || !health.gameObject.activeInHierarchy || control == null) return false;
        // The authored parasite contract routes active lives through Leave and
        // pending spawns through Recycle. Never send a global encounter event.
        var state = control.FsmStates.SingleOrDefault(s => s.Name == control.ActiveStateName);
        if (state == null || !state.Transitions.Any(t => t.EventName == "STOP SPAWN" && (t.ToState == "Leave" || t.ToState == "Recycle"))) return false;
        control.SendEvent("STOP SPAWN");
        log?.Invoke("HK_NATIVE_MINION_STOP entity=" + health.GetInstanceID() + " activation=" + Generation
            + " nativeEvent=STOP_SPAWN state=" + control.ActiveStateName.Replace(' ', '_') + " frame=" + UnityEngine.Time.frameCount);
        return true;
    }
    private bool NativeActive => health != null && health.gameObject.activeInHierarchy && !health.isDead
        && control != null && Array.IndexOf(activeStates, control.ActiveStateName) >= 0;
    internal int Health => NativeActive?Math.Max(0,health.hp+healthOffset):0;
    internal bool Active => NativeActive && Health>0;
    internal NativeMinionSpawnLease(HealthManager health, string fsm, string[] activeStates, string[] dormantStates, Action<string>? log)
    {
        this.health=health; this.activeStates=activeStates; this.log=log;
        initialHealth=Math.Max(1,health.hp);
        control=health.GetComponents<PlayMakerFSM>().Single(f=>f.FsmName==fsm);
        foreach(var state in activeStates.Concat(dormantStates))
            if(!control.FsmStates.Any(s=>s.Name==state)) throw new InvalidOperationException("Native minion phase contract changed: "+health.name+"/"+state);
    }
    internal void Observe(long nativeActivation)
    {
        bool active=NativeActive;
        if(active && (!phaseActive || nativeActivation!=sourceActivation)) {
            // Special-death pooled bodies keep their native HP below zero between
            // lives. Native ZERO HP still drives every subsequent death. Project
            // the new authored life without changing HK HP or replaying a hit.
            healthOffset=health.hasSpecialDeath && health.hp<=0?initialHealth-health.hp:0;
        }
        phaseActive=active;
        sourceActivation=nativeActivation;
        long next=phases.Observe(active,nativeActivation);
        if(next!=Generation && samples++<64)
            log?.Invoke("HK_NATIVE_MINION_SPAWN entity="+health.GetInstanceID()+" activation="+next+" nativeActivation="+nativeActivation+" state="+control.ActiveStateName.Replace(' ','_')+" health="+health.hp+" nativeFSM=True frame="+UnityEngine.Time.frameCount);
        Generation=next;
    }
}
