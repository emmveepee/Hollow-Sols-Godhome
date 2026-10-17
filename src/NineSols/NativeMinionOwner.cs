using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace YiGodhome;

internal sealed class NativeMinionRoot : MonoBehaviour { }

// Native combat graph with host-owned lifetime. No level entry/reset or AI runs.
internal sealed class NativeMinionOwner : IDisposable
{
    private static readonly Harmony isolation = new Harmony("local.yigodhome.minion.isolation");
    private static readonly HashSet<MethodBase> patched = new HashSet<MethodBase>();
    private static readonly HashSet<string> awake = new HashSet<string> {
        "StealthGameBoss", "BossBase", "MonsterBase", "Actor", "PhysicsMover", "MonsterCore", "Health", "PostureSystem",
        "InternalDamageUpdater", "FooAttachable", "MonsterDecreasePostureReceiver", "EffectReceiver", "EffectDealer",
        "FxPlayer", "VelocityModifierManager", "AnimationEvents", "DamageDealer", "ParriableAttackEffect", "TriggerDetector", "AbstractEmitter"
    };
    private static bool Owns(Component c) => c != null && c.GetComponentInParent<NativeMinionRoot>(true) != null;
    private static bool Gate(Component __instance, MethodBase __originalMethod)
    {
        if (!Owns(__instance)) return true;
        if (__originalMethod.Name == "OnDisable" && (__instance is TriggerDetector || __instance is EffectReceiver)) return true;
        if (__originalMethod.Name == "OnDestroy" && (__instance is EffectReceiver || __instance is FooAttachable)) return true;
        return (__originalMethod.Name == "Awake" || __originalMethod.Name == "OnEnable"
            && (__instance is EffectReceiver || __instance is EffectDealer || __instance is DamageDealer || __instance is ParriableAttackEffect))
            && awake.Contains(__originalMethod.DeclaringType!.Name);
    }
    private readonly GameObject root;
    private readonly Action<string> log;
    private bool disposed;
    private MonsterStat? originalStat, minionStat;
    private HurtInterruptData? minionInterrupt;
    private static readonly FieldInfo StatField = AccessTools.Field(typeof(MonsterBase), "_monsterStat");
    internal void Classify(bool boss, Action<string> log) {
        originalStat ??= Owner.monsterStat;
        if (!boss && minionStat == null) {
            minionStat = UnityEngine.Object.Instantiate(originalStat);
            minionInterrupt = UnityEngine.Object.Instantiate(originalStat.hurtInterruptData);
            minionInterrupt.monsterLevel = MonsterLevel.Minion;
            minionStat.hurtInterruptData = minionInterrupt;
        }
        StatField.SetValue(Owner, boss ? originalStat : minionStat);
        if ((Owner.monsterStat.hurtInterruptData.monsterLevel >= MonsterLevel.MiniBoss) != boss) throw new InvalidOperationException("Native target classification lease failed.");
        log("NS_TARGET_CLASSIFICATION owner=" + Owner.GetInstanceID() + " boss=" + boss + " ownedStat=" + !boss + " sharedTemplateUnchanged=True");
    }
    public static int Retained { get; private set; }
    public MonsterBase Owner { get; }
    internal static void PrepareTemplate(MonsterBase template, Action<string> log)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        int previous = patched.Count;
        foreach (var component in template.GetComponentsInChildren<MonoBehaviour>(true).Where(c => c != null))
            for (Type? type = component.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
                foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (!method.IsAbstract && method.ReturnType == typeof(void) && method.GetParameters().Length == 0
                        && new[] { "Awake", "OnEnable", "Start", "OnDisable", "OnDestroy", "EnterLevelAwake", "EnterLevelReset", "OnStateEnter", "OnStateExit", "OnStateUpdate", "OnSpriteUpdate" }.Contains(method.Name)
                        && patched.Add(method))
                        isolation.Patch(method, prefix: new HarmonyMethod(typeof(NativeMinionOwner), nameof(Gate)));
        if (patched.Count != previous) log("NS_MINION_CALLBACKS_READY template=" + template.GetInstanceID()
            + " methods=" + (patched.Count - previous) + " milliseconds="
            + ((System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency)
            + " ownersCreated=0 scopedTemplate=True");
    }
    public NativeMinionOwner(MonsterBase template, Action<string> log)
    {
        this.log = log;
        PrepareTemplate(template, log);
        root = new GameObject("YiGodhome Native Minion Owner"); root.SetActive(false); root.AddComponent<NativeMinionRoot>();
        try
        {
            var clone = UnityEngine.Object.Instantiate(template.gameObject, root.transform, false);
            Owner = clone.GetComponent<MonsterBase>();
            clone.name = "YiGodhome Minion Combat";
            AutoAttributeManager.AutoReferenceAllChildren(clone);
            foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
            foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            foreach (var collider in clone.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
            foreach (var animator in clone.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            foreach (var state in clone.GetComponentsInChildren<MonsterState>(true))
                foreach (var field in state.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                    if (field.GetValue(state) is Component dependency && !dependency.transform.IsChildOf(clone.transform)) field.SetValue(state, null);
            if (Owner.monsterCore == template.monsterCore || Owner.health == template.health || Owner.postureSystem == template.postureSystem
                || Owner.monsterCore.fooAttachable == template.monsterCore.fooAttachable) throw new InvalidOperationException("Minion graph retained shared combat state.");
            clone.SetActive(true); root.SetActive(true);
            AccessTools.Method(typeof(MonsterBase), "CheckInit").Invoke(Owner, null);
            Owner.Pause();
            foreach (var component in clone.GetComponentsInChildren<MonoBehaviour>(true))
                if (component is EffectReceiver || component is MonsterDecreasePostureReceiver || component is FooAttachable
                    || component is InternalDamageUpdater || component is PostureSystem || component is FxPlayer
                    || component is DamageDealer || component is EffectDealer || component is ParriableAttackEffect) component.enabled = true;
            foreach (var collider in clone.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
            clone.AddComponent<PausedTargetMaintenance>().Initialize(Owner);
            if (!Owner.Inited || Owner.fsm == template.fsm || !Owner.fsm.isPaused) throw new InvalidOperationException("Independent native initialization failed.");
            log("NS_MINION_OWNER_READY owner=" + Owner.GetInstanceID() + " health=" + Owner.health.GetInstanceID()
                + " posture=" + Owner.postureSystem.GetInstanceID() + " foo=" + Owner.monsterCore.fooAttachable.GetInstanceID() + " independent=True paused=True");
            Retained++;
        }
        catch { root.SetActive(false); UnityEngine.Object.Destroy(root); throw; }
    }
    public void Reset()
    {
        if (originalStat != null) StatField.SetValue(Owner, originalStat);
        foreach (var foo in Owner.GetComponentsInChildren<FooAttachable>(true)) if (foo.bindingFoo != null) foo.bindingFoo.FooExpired();
        foreach (var collider in Owner.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
        Owner.postureSystem.CurrentInternalInjury = 0;
        Owner.postureSystem.PostureValue = Owner.postureSystem.MaxPostureValue;
        Owner.health.GainFull(); Owner.Pause();
    }
    public void VerifyReset()
    {
        int attachments = Owner.GetComponentsInChildren<FooAttachable>(true).Count(f => f.bindingFoo != null);
        int colliders = Owner.GetComponentsInChildren<Collider2D>(true).Count(c => c.enabled);
        if (Owner.postureSystem.InternalInjury != 0 || Owner.postureSystem.PostureValue != Owner.postureSystem.MaxPostureValue
            || !Owner.fsm.isPaused || attachments != 0 || colliders != 0) {
            log("NS_MINION_RESET_REJECTED owner=" + Owner.GetInstanceID() + " injury=" + Owner.postureSystem.InternalInjury
                + " posture=" + Owner.postureSystem.PostureValue + " maximum=" + Owner.postureSystem.MaxPostureValue
                + " paused=" + Owner.fsm.isPaused + " attachments=" + attachments + " enabledColliders=" + colliders + " frame=" + Time.frameCount);
            throw new InvalidOperationException("Native receiver is not clean for a new activation.");
        }
    }
    public void Dispose() { if (disposed) return; disposed = true; Retained--; if (originalStat != null && Owner != null) StatField.SetValue(Owner, originalStat);
        UnityEngine.Object.Destroy(minionInterrupt); UnityEngine.Object.Destroy(minionStat); if (root != null) { root.SetActive(false); UnityEngine.Object.Destroy(root); } }
}
