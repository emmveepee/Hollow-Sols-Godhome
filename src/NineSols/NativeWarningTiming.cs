using System;
using HarmonyLib;
using UnityEngine;

namespace YiGodhome;

// Speed belongs to this pooled effect instance, never its shared animation asset.
internal sealed class NativeWarningTiming : MonoBehaviour
{
    private static int instances;
    private static readonly Harmony harmony = new Harmony("local.yigodhome.warning-completion");
    private Animator[] animators = Array.Empty<Animator>();
    private float[] speeds = Array.Empty<float>();
    private float[] cueSpeeds = Array.Empty<float>();
    private AnimatorUpdateMode[] modes = Array.Empty<AnimatorUpdateMode>();
    private bool hostClock;
    private PoolObject? pool;
    private Action<string>? log;
    private long action;
    private Guid encounter;
    internal bool Owns(long id, Guid lifetime) => pool!=null && pool.isOnScene && action==id && encounter==lifetime;
    private void Awake()
    {
        if (++instances == 1) harmony.Patch(AccessTools.Method(typeof(PoolObject), "BeforeObjectReturnToPool"),
            prefix: new HarmonyMethod(typeof(NativeWarningTiming), nameof(BeforePoolReset)));
    }
    private static void BeforePoolReset(PoolObject __instance) => __instance.GetComponent<NativeWarningTiming>()?.ObserveEnd(true);
    public void Begin(PoolObject owner, Action<string> logger, long id, Guid lifetime, bool unscaled = false)
    {
        Restore(); pool = owner; log = logger; action = id; encounter = lifetime;
        hostClock = unscaled;
        animators = GetComponentsInChildren<Animator>(true); speeds = new float[animators.Length]; cueSpeeds = new float[animators.Length]; modes = new AnimatorUpdateMode[animators.Length];
        if (animators.Length == 0) throw new InvalidOperationException("Native danger animation owner missing.");
        for (int i = 0; i < animators.Length; i++) {
            speeds[i] = animators[i].speed;
            modes[i] = animators[i].updateMode;
            if (hostClock) animators[i].updateMode = AnimatorUpdateMode.UnscaledTime;
            // Finish the enclosing clip within 120 ms. Native pooling and artwork
            // remain unchanged; the original speed is restored before reuse.
            float length = 0;
            foreach (var clip in animators[i].runtimeAnimatorController.animationClips) length = Mathf.Max(length, clip.length);
            animators[i].speed = Mathf.Max(speeds[i], length / 0.12f);
            cueSpeeds[i] = animators[i].speed;
            logger("NS_DASH_CUE_TIMING action=" + id + " encounter=" + encounter + " clipSeconds=" + length + " speed=" + animators[i].speed + " completionBudget=0.12 frame=" + Time.frameCount + " hostClock=" + hostClock);
        }
    }
    private void Update()
    {
        if (pool == null) return;
        if (!pool.isOnScene) { Restore(); return; }
        if (hostClock) for (int i = 0; i < animators.Length; i++) if (animators[i] != null) animators[i].speed = NativeInputBridge.HostPaused ? 0 : cueSpeeds[i];
        ObserveEnd(false);
    }
    private void ObserveEnd(bool nativeReturn)
    {
        bool complete = animators.Length > 0;
        float progress = float.MaxValue;
        foreach (var animator in animators) {
            float time = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            progress = Mathf.Min(progress, time); complete &= time >= 1f;
        }
        if (complete && log != null) {
            log("NS_DASH_CUE_COMPLETE action=" + action + " encounter=" + encounter + " animationProgress=" + progress + " nativeReturn=" + nativeReturn + " frame=" + Time.frameCount); log = null;
        }
    }
    private void Restore()
    {
        for (int i = 0; i < animators.Length; i++) if (animators[i] != null) { animators[i].speed = speeds[i]; animators[i].updateMode = modes[i]; }
        animators = Array.Empty<Animator>(); speeds = cueSpeeds = Array.Empty<float>(); modes = Array.Empty<AnimatorUpdateMode>(); hostClock = false; pool = null; log = null;
    }
    private void OnDisable() => Restore();
    private void OnDestroy() { Restore(); if (--instances == 0) harmony.UnpatchSelf(); }
}
