using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using YiGodhome.Bridge;

namespace YiGodhome;

internal sealed class NativeSummonHealth : IDisposable
{
    private static NativeSummonHealth? reading;
    private static readonly List<NativeSummonHealth> instances = new List<NativeSummonHealth>();
    private static bool patched;
    private readonly MonsterBase owner;
    private readonly UIMonsterHP view;
    private readonly Action<string> log;
    private ContactFrame? frame;
    private float compactScale;
    private bool? presented;
    private bool activated, immunityVerified;
    private int entity;
    private long activation;
    private int previousHealth = -1, previousMaximum = -1;
    private float previousInjury = -1;
    private float valuesChangedAt;
    private bool trailSampled;
    private static readonly System.Reflection.FieldInfo hpDisc = AccessTools.Field(typeof(UIMonsterHP), "HPDisc");
    private static readonly System.Reflection.FieldInfo hurtDisc = AccessTools.Field(typeof(UIMonsterHP), "HurtDisc");
    private static readonly System.Reflection.FieldInfo injuryDisc = AccessTools.Field(typeof(UIMonsterHP), "InternalInjuryDisc");
    public NativeSummonHealth(MonsterBase owner, Action<string> log)
    {
        this.owner = owner; this.log = log;
        if (!patched) {
            var h = new Harmony("local.yigodhome.summon.health");
            h.Patch(AccessTools.Method(typeof(PostureSystem), "GetPostureValue"), postfix: new HarmonyMethod(typeof(NativeSummonHealth), nameof(Solid)));
            h.Patch(AccessTools.PropertyGetter(typeof(PostureSystem), "MaxPostureValue"), postfix: new HarmonyMethod(typeof(NativeSummonHealth), nameof(Max)));
            h.Patch(AccessTools.PropertyGetter(typeof(PostureSystem), "InternalInjury"), postfix: new HarmonyMethod(typeof(NativeSummonHealth), nameof(Injury)));
            h.Patch(AccessTools.Method(typeof(UIMonsterHP), "UpdateUI"), prefix: new HarmonyMethod(typeof(NativeSummonHealth), nameof(Enter)),
                finalizer: new HarmonyMethod(typeof(NativeSummonHealth), nameof(Leave)));
            h.Patch(AccessTools.Method(typeof(UIMonsterHP), "ShowUI"), prefix: new HarmonyMethod(typeof(NativeSummonHealth), nameof(MayShow)));
            patched = true;
        }
        view = GameCore.Instance.monsterHpUI.BindMonsterHP(owner.postureSystem);
        instances.Add(this);
        view.gameObject.SetActive(false);
    }
    private static bool Enter(UIMonsterHP __instance, out NativeSummonHealth? __state)
    {
        __state = reading;
        var context = instances.FirstOrDefault(i => i.view == __instance);
        if (context == null) return true;
        reading = context;
        return context.frame != null && context.activated && context.frame.Health > 0;
    }
    private static void Leave(NativeSummonHealth? __state) => reading = __state;
    private static bool MayShow(UIMonsterHP __instance)
    {
        var context = instances.FirstOrDefault(i => i.view == __instance);
        return context == null || context.frame != null && context.activated && context.frame.Health > 0;
    }
    private BossHealthProjection Projection {
        get {
            // Read the gameplay injury before applying the UI-only range.
            var prior = reading; reading = null;
            try { return new BossHealthProjection(frame!.Health, frame.MaxHealth, owner.postureSystem.InternalInjury); }
            finally { reading = prior; }
        }
    }
    // UI's solid value is an integer. Use a presentation-only range large
    // enough to represent one-HP minions without truncating full health.
    private float DisplayMaximum => Math.Max(40, (float)(frame!.MaxHealth / BossHealthProjection.NativeToHost));
    private static void Solid(PostureSystem __instance, ref int __result)
    { if (reading?.frame != null && __instance == reading.owner.postureSystem) __result = Mathf.RoundToInt(reading.Projection.Solid * reading.DisplayMaximum); }
    private static void Max(PostureSystem __instance, ref float __result)
    { if (reading?.frame != null && __instance == reading.owner.postureSystem) __result = reading.DisplayMaximum; }
    private static void Injury(PostureSystem __instance, ref float __result)
    { if (reading?.frame != null && __instance == reading.owner.postureSystem) __result = (float)Math.Min(reading.frame.Health, __result * BossHealthProjection.NativeToHost) / reading.frame.MaxHealth * reading.DisplayMaximum; }
    public void Update(ContactFrame frame)
    {
        if (entity != frame.Entity || activation != frame.TargetGeneration) {
            entity = frame.Entity; activation = frame.TargetGeneration; activated = false; immunityVerified = false; presented = null;
            previousHealth = previousMaximum = -1; previousInjury = -1;
            trailSampled = false;
        }
        activated |= frame.HurtActive;
        this.frame = frame;
        bool visible = activated && frame.Health > 0;
        view.gameObject.SetActive(visible);
        float injury = owner.postureSystem.InternalInjury;
        // UpdateUI restarts the native damage-trail tween. Refresh values, not
        // every capture, so its authored delay and decay can finish normally.
        if (visible && (previousHealth != frame.Health || previousMaximum != frame.MaxHealth || previousInjury != injury)) {
            view.UpdateUI(owner.postureSystem);
            previousHealth = frame.Health; previousMaximum = frame.MaxHealth; previousInjury = injury;
            valuesChangedAt = Time.time; trailSampled = false;
            log("NS_SUMMON_HUD_VALUES encounter=" + frame.Encounter + " target=" + frame.Entity + " activation=" + frame.TargetGeneration
                + " health=" + frame.Health + " max=" + frame.MaxHealth + " injury=" + injury + " solid=" + Projection.Solid + " total=" + Projection.Total
                + " frame=" + Time.frameCount);
        }
        if (SessionConfiguration.Current.RunFixtures && visible && !trailSampled && !NativeInputBridge.HostPaused && Time.time - valuesChangedAt >= 1.25f) {
            trailSampled = true;
            var hp = (Shapes.Disc)hpDisc.GetValue(view);
            var trail = (Shapes.Disc)hurtDisc.GetValue(view);
            var internalHp = (Shapes.Disc)injuryDisc.GetValue(view);
            log("NS_SUMMON_HUD_TRAIL encounter=" + frame.Encounter + " target=" + frame.Entity + " activation=" + frame.TargetGeneration
                + " health=" + frame.Health + " injury=" + injury + " stableSeconds=" + (Time.time - valuesChangedAt)
                + " solidAngle=" + hp.AngRadiansStart + " trailAngle=" + trail.AngRadiansStart + " injuryAngle=" + internalHp.AngRadiansStart
                + " settled=" + (Math.Abs(hp.AngRadiansStart - trail.AngRadiansStart) < 0.001f) + " frame=" + Time.frameCount);
        }
    }
    public IEnumerable<Renderer> Renderers()
    {
        // Dormancy ends at the first native damageable activation. Temporary
        // immunity/rolling later in that same target lease must not blink its bar.
        bool active = frame != null && view != null && activated && frame.Health > 0;
        if (frame != null && presented != active) {
            presented = active;
            log("NS_SUMMON_HUD_VISIBILITY encounter=" + frame.Encounter + " geometry=" + frame.GeometryFrame
                + " target=" + frame.Entity + " activation=" + frame.TargetGeneration + " visible=" + active
                + " hurtActive=" + frame.HurtActive + " hp=" + frame.Health + " frame=" + Time.frameCount);
        }
        if (!active || frame == null || view == null) return Array.Empty<Renderer>();
        if (!frame.HurtActive && !immunityVerified) {
            immunityVerified = true;
            log("NS_SUMMON_HUD_LEASE_IMMUNITY encounter=" + frame.Encounter + " target=" + frame.Entity
                + " activation=" + frame.TargetGeneration + " geometry=" + frame.GeometryFrame
                + " visible=True hurtActive=False hp=" + frame.Health + " frame=" + Time.frameCount);
        }
        var point = owner.transform.position; point.y += frame.HurtHeight * 10 + 4;
        view.transform.position = new Vector3(point.x, point.y, 0);
        var parent = view.transform.parent.lossyScale;
        // Native LateUpdate places this screen-space widget at local scale one.
        // The bridge captures it in world space; don't multiply by the unrelated
        // quarantined gameplay camera zoom. Keep the arc compact above its actor.
        view.transform.localScale = new Vector3(1 / parent.x, 1 / parent.y, 1);
        var renderers = view.GetComponentsInChildren<Renderer>(true);
        if (compactScale == 0 && renderers.Length > 0) {
            Bounds ActualBounds(Renderer r) => r.TryGetComponent<Shapes.ShapeRenderer>(out var shape) ? shape.GetWorldBounds() : r.bounds;
            var bounds = ActualBounds(renderers[0]); foreach (var r in renderers.Skip(1)) bounds.Encapsulate(ActualBounds(r));
            if (bounds.size.x > 0) {
                compactScale = 36 / bounds.size.x; // 1.8 host units at20:1; retain native arc shape.
                log("NS_SUMMON_HUD_SCALE owner=" + owner.GetInstanceID() + " nativeWidth=" + bounds.size.x + " worldWidth=36 scale=" + compactScale);
            }
        }
        if (compactScale > 0) view.transform.localScale *= compactScale;
        return renderers;
    }
    public void Dispose() { instances.Remove(this); if (view != null) view.ReturnToPool(); }
}
