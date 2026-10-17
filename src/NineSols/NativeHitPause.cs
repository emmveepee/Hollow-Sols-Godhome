using System;
using HarmonyLib;
using UnityEngine;
using YiGodhome.Bridge;

namespace YiGodhome;

internal static class NativeHitPause
{
    private static Guid session, encounter;
    private static long last;
    public static void Accept(HitAck ack, Guid lifetime, MonsterBase owner, Action<string> log)
    {
        if (session != ack.Session || encounter != lifetime) { session = ack.Session; encounter = lifetime; last = 0; }
        if (ack.Sequence <= last) return;
        last = ack.Sequence;
        if (!ack.ParryPause) return;
        TimePauseManager.GamePlayTimeScaleModifier.TimePause(owner, 7, 0f, null);
        var player = Player.i;
        (player.GetComponent<NativeHitPauseObserver>() ?? player.gameObject.AddComponent<NativeHitPauseObserver>()).Begin(ack.Sequence, owner, lifetime, log);
    }
    public static void Cancel(MonsterBase owner) => Player.i?.GetComponent<NativeHitPauseObserver>()?.Cancel(owner);
}

// Movement remains a native vote. Animator speed is independently written by
// Player's native synchronizer, so that writer is gated only for this short lease.
internal sealed class NativeHitPauseObserver : MonoBehaviour
{
    private const float HoldSeconds = .18f;
    private static NativeHitPauseObserver? active;
    internal static bool Held => active?.voted == true;
    private readonly Harmony harmony = new Harmony("local.yigodhome.counter-frame-hold");
    private Action<string>? log;
    private long sequence;
    private Guid encounter;
    private float started, held, maximum, animationProgress, speed, unityScale;
    private Vector3 position;
    private AnimatorStateInfo[] states = Array.Empty<AnimatorStateInfo>();
    private Player? player;
    private MonsterBase? source;
    private Sprite? sprite;
    private Vector2 velocity;
    private bool animatorEnabled;
    private bool voted, spriteChanged, stateChanged;
    private void Awake()
    {
        harmony.Patch(AccessTools.Method(typeof(Player), "AnimatorSpeedSyncSlowDownRatio"), prefix: new HarmonyMethod(typeof(NativeHitPauseObserver), nameof(BeforeSpeedSync)));
        harmony.Patch(AccessTools.Method(typeof(Player), "Update"), prefix: new HarmonyMethod(typeof(NativeHitPauseObserver), nameof(BeforePlayerStep)));
        harmony.Patch(AccessTools.Method(typeof(Actor), "Move"), prefix: new HarmonyMethod(typeof(NativeHitPauseObserver), nameof(BeforeMove)));
    }
    private static bool BeforePlayerStep(Player __instance) => active == null || !active.voted || __instance != active.player;
    private static bool BeforeMove(Actor __instance) => active == null || !active.voted || __instance != active.player;
    private static bool BeforeSpeedSync(Player __instance) => active == null || !active.voted || __instance != active.player;
    public void Begin(long id, MonsterBase owner, Guid lifetime, Action<string> logger)
    {
        Release(); player = Player.i; source = owner; encounter = lifetime; sequence = id; log = logger;
        started = Time.realtimeSinceStartup; held = maximum = animationProgress = 0; spriteChanged = stateChanged = false;
        position = player.transform.position; sprite = player.PlayerSprite.sprite;
        states = new AnimatorStateInfo[player.animator.layerCount];
        for (int i = 0; i < states.Length; i++) states[i] = player.animator.GetCurrentAnimatorStateInfo(i);
        speed = player.animator.speed;
        unityScale = Time.timeScale;
        velocity = player.Velocity; animatorEnabled = player.animator.enabled;
        player.FreezeWithVote(true, this); voted = true; active = this; player.animator.speed = 0;
        player.animator.enabled = false;
        Time.timeScale = 0;
        log("NS_HOST_PARRY_STOP sequence=" + sequence + " encounter=" + encounter + " frame=" + Time.frameCount + " seconds=" + HoldSeconds + " animationSpeed=0 position=" + position);
    }
    private void Release()
    {
        bool releasing = voted;
        if (voted && player != null) {
            player.FreezeWithVote(false, this);
            player.Velocity = velocity;
            player.animator.enabled = animatorEnabled;
            if (player.animator != null && player.animator.speed == 0) player.animator.speed = speed;
        }
        voted = false; if (active == this) active = null;
        if (releasing && !NativeInputBridge.HostPaused && Time.timeScale == 0) Time.timeScale = unityScale > 0 ? unityScale : 1;
    }
    public void Cancel() { Release(); log = null; }
    public void Cancel(MonsterBase owner) { if (source == owner) Cancel(); }
    private void Update() { if (voted) Time.timeScale = 0; }
    private void LateUpdate()
    {
        if (log == null) return;
        if (player == null || source == null || !player.gameObject.activeInHierarchy) { Cancel(); return; }
        Time.timeScale = 0;
        maximum = Mathf.Max(maximum, Vector3.Distance(position, player.transform.position));
        player.transform.position = position; player.Velocity = Vector2.zero;
        spriteChanged |= sprite != player.PlayerSprite.sprite;
        for (int i = 0; i < states.Length; i++) {
            var now = player.animator.GetCurrentAnimatorStateInfo(i);
            stateChanged |= now.fullPathHash != states[i].fullPathHash;
            animationProgress = Mathf.Max(animationProgress, Mathf.Abs(now.normalizedTime - states[i].normalizedTime));
        }
        float elapsed = Time.realtimeSinceStartup - started;
        if (!NativeInputBridge.HostPaused) held += Time.unscaledDeltaTime;
        if (held < HoldSeconds) return;
        Release();
        log("NS_HOST_PARRY_RESUME sequence=" + sequence + " encounter=" + encounter + " pausedDisplacement=" + maximum + " animationProgress=" + animationProgress
            + " spriteChanged=" + spriteChanged + " stateChanged=" + stateChanged + " elapsed=" + elapsed + " held=" + held + " frame=" + Time.frameCount);
        log = null;
    }
    private void OnDisable() => Cancel();
    private void OnDestroy() { Cancel(); harmony.UnpatchSelf(); }
}
