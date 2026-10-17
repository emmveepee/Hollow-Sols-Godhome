using System;
using System.Linq;
using UnityEngine;

namespace YiGodhome;

// A shared lease acquired only after a real native counter blocked a hit.
// It leaves the authored FSM enabled, so releasing never restarts that graph.
internal sealed class HostCounterHold : MonoBehaviour
{
    private HealthManager? target;
    private tk2dSpriteAnimator[] animations = Array.Empty<tk2dSpriteAnimator>();
    private bool[] paused = Array.Empty<bool>();
    private PlayMakerFSM[] controls = Array.Empty<PlayMakerFSM>();
    private string[] controlStates = Array.Empty<string>();
    private Rigidbody2D? body;
    private Vector2 velocity;
    private Vector3 position;
    private float held, scale;
    private bool active;
    private bool simulated;
    private NativeFsmStepLease[] leases=Array.Empty<NativeFsmStepLease>();
    private Action<string>? log;
    private long sequence;
    private int sprite;
    private bool changed, stateChanged;
    internal void Begin(HealthManager owner, long id, Action<string> logger)
    {
        Release(); target = owner; sequence = id; log = logger;
        animations = owner.GetComponentsInChildren<tk2dSpriteAnimator>(true);
        controls = owner.GetComponents<PlayMakerFSM>();
        if (animations.Length > 32 || controls.Length > 16) throw new InvalidOperationException("Counter hold owner budget exceeded.");
        controlStates = controls.Select(f => f.ActiveStateName).ToArray();
        paused = animations.Select(a => a.Paused).ToArray();
        position = owner.transform.position; body = owner.GetComponent<Rigidbody2D>();
        velocity = body == null ? Vector2.zero : body.linearVelocity;
        sprite = owner.GetComponent<tk2dBaseSprite>()?.spriteId ?? -1;
        scale = Time.timeScale; held = 0; changed = stateChanged = false; active = true;
        foreach (var a in animations) a.Pause();
        // The zero gameplay clock and paused native animator hold the authored
        // counter. Disabling PlayMaker stops its graph and loses the receiving
        // state even when RestartOnEnable is false.
        leases=controls.Select(f=>new NativeFsmStepLease(f.Fsm)).ToArray();
        if (body != null) {simulated=body.simulated;body.linearVelocity=Vector2.zero;body.simulated=false;}
        Time.timeScale = 0;
        log("HK_COUNTER_HOLD_BEGIN sequence=" + id + " entity=" + owner.GetInstanceID() + " seconds=0.18 position=" + position + " frame=" + Time.frameCount);
    }
    private void Update() { if (active) Time.timeScale = 0; }
    private void LateUpdate()
    {
        if (!active) return;
        if (target == null || target.isDead || !target.gameObject.activeInHierarchy) { Release(); return; }
        changed |= (target.GetComponent<tk2dBaseSprite>()?.spriteId ?? -1) != sprite;
        for (int i=0;i<controls.Length;i++) if(controls[i] != null) stateChanged |= controls[i].ActiveStateName != controlStates[i];
        target.transform.position = position;
        if (body != null) body.linearVelocity = Vector2.zero;
        foreach (var a in animations) if (a != null) a.Pause();
        Time.timeScale = 0;
        if (GameManager.instance?.IsGamePaused() != true) held += Time.unscaledDeltaTime;
        if (held < .18f) return;
        log?.Invoke("HK_COUNTER_HOLD_END sequence=" + sequence + " held=" + held + " spriteChanged=" + changed + " stateChanged=" + stateChanged + " frame=" + Time.frameCount);
        Release();
    }
    internal void Release()
    {
        if (!active) return;
        active = false;
        foreach(var lease in leases)lease.Dispose();
        leases=Array.Empty<NativeFsmStepLease>();
        for (int i = 0; i < animations.Length; i++) if (animations[i] != null && !paused[i]) animations[i].Resume();
        if (body != null) {body.simulated=simulated;body.linearVelocity = velocity;}
        // Native FreezeMoment has already entered its slowdown when we bind.
        // Resume the normal gameplay clock instead of retaining that slowdown.
        if (GameManager.instance?.IsGamePaused() != true) Time.timeScale = scale < 1 ? 1 : scale;
    }
    private void OnDisable() => Release();
    private void OnDestroy() => Release();
}
