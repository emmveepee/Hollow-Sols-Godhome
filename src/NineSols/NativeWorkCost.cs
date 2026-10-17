using System;
using System.Diagnostics;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using YiGodhome.Bridge;

namespace YiGodhome;

// Known native owners only, fixed counters and bounded summary cadence.
internal static class NativeWorkCost
{
    internal static bool Enabled => SessionConfiguration.Current.RunFixtures
        && SessionConfiguration.Current.Has("trace-probe") && EncounterProfile.Selected == EncounterProfile.NightmareGrimm;
    internal sealed class Meter
    {
        private long window;
        private double total, maximum;
        private int samples;
        internal void Record(long start, string stage, Action<string> log)
        {
            if (!Enabled || start == 0) return;
            long now = Stopwatch.GetTimestamp();
            double ms = (now - start) * 1000d / Stopwatch.Frequency;
            total += ms; maximum = Math.Max(maximum, ms); samples++;
            if (window == 0) window = now;
            if ((now - window) / (double)Stopwatch.Frequency < 2) return;
            log("NS_NATIVE_WORK stage=" + stage + " samples=" + samples + " meanMs=" + total / samples
                + " maxMs=" + maximum + " totalMs=" + total + " frame=" + Time.frameCount);
            window = now; total = maximum = 0; samples = 0;
        }
    }
    internal readonly struct Scope : IDisposable
    {
        private readonly Meter meter;
        private readonly string stage;
        private readonly Action<string> log;
        private readonly long start;
        internal Scope(Meter meter, string stage, Action<string> log)
        { this.meter = meter; this.stage = stage; this.log = log; start = Enabled ? Stopwatch.GetTimestamp() : 0; }
        public void Dispose() => meter.Record(start, stage, log);
    }
    internal static readonly Meter Player = new Meter(), Movement = new Meter(), PlayerMotion = new Meter(), Hud = new Meter(), BossHud = new Meter();
    internal static readonly Meter Update = new Meter(), LateUpdate = new Meter(), Memory = new Meter(), Target = new Meter();
    internal static readonly Meter ActorCapture = new Meter(), SummonCapture = new Meter(), RestCapture = new Meter(), MenuCapture = new Meter();
    internal static readonly HashSet<TriggerDetector> Detectors = new HashSet<TriggerDetector>();
    internal static readonly Meter Trigger = new Meter();
    internal static readonly Meter TriggerEnter = new Meter(), TriggerExit = new Meter();
    private static readonly System.Reflection.FieldInfo Entered = AccessTools.Field(typeof(TriggerDetector), "enteredColliders");
    private static int triggerSamples, triggerMaximum, triggerContacts;
    private static float triggerAt;
    private static readonly HashSet<int> graphShapes = new HashSet<int>();
    internal static void InspectAttackGraph(GameObject root, int vertices, Action<string> log)
    {
        if (!Enabled || (vertices != 4 && vertices != 64) || !graphShapes.Add(vertices)) return;
        var colliders = root.GetComponentsInChildren<Collider2D>(true);
        var receivers = root.GetComponentsInChildren<EffectReceiver>(true);
        if (colliders.Length > 16 || receivers.Length > 16)
            throw new InvalidOperationException("Known attack graph exceeds bounded diagnostic scope.");
        log("NS_ATTACK_GRAPH vertices=" + vertices + " colliders=" + colliders.Length + " receivers=" + receivers.Length);
        foreach (var c in colliders) {
            var rb = c.attachedRigidbody;
            log("NS_ATTACK_GRAPH_COLLIDER vertices=" + vertices + " name=" + c.name + " type=" + c.GetType().Name
                + " layer=" + c.gameObject.layer + " sameLayerIgnored=" + Physics2D.GetIgnoreLayerCollision(c.gameObject.layer,c.gameObject.layer)
                + " trigger=" + c.isTrigger + " enabled=" + c.enabled
                + " active=" + c.gameObject.activeInHierarchy + " shapes=" + c.shapeCount + " position=" + c.transform.position
                + " bounds=" + c.bounds + " body=" + (rb == null ? "none" : rb.bodyType.ToString())
                + " interpolation=" + (rb == null ? "none" : rb.interpolation.ToString())
                + " components=" + string.Join(",", Array.ConvertAll(c.GetComponents<Component>(), component => component.GetType().Name)));
        }
        var receiverCollider = AccessTools.Field(typeof(EffectReceiver), "myCollider2D");
        foreach (var r in receivers) {
            var c = (Collider2D)receiverCollider.GetValue(r);
            log("NS_ATTACK_GRAPH_RECEIVER vertices=" + vertices + " name=" + r.name + " effectType=" + r.effectType
                + " collider=" + (c == null ? "none" : c.name) + " sharedDamageCollider=" + (c != null && c.GetComponent<DamageDealer>() != null));
        }
    }
    internal static long BeginTrigger(TriggerDetector detector)
    { return Enabled && Detectors.Contains(detector) ? Stopwatch.GetTimestamp() : 0; }
    internal static void EndTrigger(TriggerDetector detector, long start, Action<string> log)
    {
        if (start == 0) return;
        Trigger.Record(start, "native_trigger", log);
        int contacts = ((System.Collections.ICollection)Entered.GetValue(detector)).Count;
        triggerSamples++; triggerContacts += contacts; triggerMaximum = Math.Max(triggerMaximum, contacts);
        if (Time.realtimeSinceStartup < triggerAt) return;
        triggerAt = Time.realtimeSinceStartup + 2;
        log("NS_NATIVE_TRIGGER_CONTACTS samples=" + triggerSamples + " contacts=" + triggerContacts + " maximum=" + triggerMaximum
            + " owned=" + Detectors.Count + " frame=" + Time.frameCount);
        triggerSamples = triggerContacts = triggerMaximum = 0;
    }
}
