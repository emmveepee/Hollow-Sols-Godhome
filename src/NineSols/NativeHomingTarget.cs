using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using RCGFSM.Projectiles;
using YiGodhome.Bridge;

namespace YiGodhome;

// Native sensor adapter only. The native follower still chooses and steers.
internal sealed class NativeHomingTarget : IDisposable
{
    private static readonly Dictionary<MonsterBase, NativeHomingTarget> targets = new Dictionary<MonsterBase, NativeHomingTarget>();
    private static readonly Harmony harmony = new Harmony("local.yigodhome.homing.targets");
    private static readonly HashSet<PlayerArrowProjectileFollower> followers = new HashSet<PlayerArrowProjectileFollower>();
    private static readonly FieldInfo setField = AccessTools.Field(typeof(PlayerArrowProjectileFollower), "followingSet");
    private static readonly FieldInfo queueField = AccessTools.Field(typeof(PlayerArrowProjectileFollower), "chasingQueue");
    private readonly MonsterBase owner;
    private readonly GameObject query;
    private readonly BoxCollider2D shape;
    private readonly Action<string> log;
    private ContactFrame? current;
    private readonly HashSet<int> selectedArrows = new HashSet<int>();
    private bool disposed;
    internal NativeHomingTarget(MonsterBase owner, Action<string> log)
    {
        this.owner = owner; this.log = log;
        query = new GameObject("YiGodhome Native Homing Sensor Target");
        try {
        if (targets.Count >= SummonedTargets.Maximum + 1 || setField == null || queueField == null
            || setField.FieldType != typeof(HashSet<MonsterBase>) || queueField.FieldType != typeof(Queue<MonsterBase>))
            throw new InvalidOperationException("Native homing contract/capacity changed.");
        query.transform.SetParent(owner.transform, false);
        query.layer = LayerMask.NameToLayer("PhysicsBody");
        int sensor = LayerMask.NameToLayer("ActorSensor");
        if (query.layer < 0 || sensor < 0) throw new InvalidOperationException("Native homing layers missing.");
        shape = query.AddComponent<BoxCollider2D>(); shape.isTrigger = true;
        shape.includeLayers = 1 << sensor; shape.excludeLayers = ~(1 << sensor);
        var body = query.AddComponent<ActorBody>();
        body.IsFreeToSetOffset = true;
        AccessTools.Method(typeof(ActorBody), "AlignPos").Invoke(body, null);
        body.enabled = false;
        if (body.actor != owner) throw new InvalidOperationException("Native homing ActorBody ownership changed.");
        shape.enabled = false;
        if (targets.Count == 0) {
            harmony.Patch(AccessTools.PropertyGetter(typeof(MonsterBase), "CenterTransform"), postfix: new HarmonyMethod(typeof(NativeHomingTarget), nameof(Center)));
            harmony.Patch(AccessTools.Method(typeof(PlayerArrowProjectileFollower), "ChaseMonsterTarget"), prefix: new HarmonyMethod(typeof(NativeHomingTarget), nameof(Admit)));
            harmony.Patch(AccessTools.Method(typeof(PlayerArrowProjectileFollower), "Update"), prefix: new HarmonyMethod(typeof(NativeHomingTarget), nameof(Prune)));
            harmony.Patch(AccessTools.Method(typeof(PlayerArrowProjectileFollower), "GetNextFollowTarget"), postfix: new HarmonyMethod(typeof(NativeHomingTarget), nameof(Selected)));
        }
        targets.Add(owner, this);
        } catch {
            query.SetActive(false); UnityEngine.Object.Destroy(query);
            if (targets.Count == 0) harmony.UnpatchSelf();
            throw;
        }
    }
    private static void Center(MonsterBase __instance, ref Transform __result)
    { if (targets.TryGetValue(__instance, out var target)) __result = target.query.transform; }
    private static bool Admit(PlayerArrowProjectileFollower __instance, Actor __0)
    {
        if (NativePlayerArrow.Owner(__instance, Player.i) != null) {
            followers.RemoveWhere(f => f == null || !f.isActiveAndEnabled);
            if (followers.Count >= 128 && !followers.Contains(__instance)) throw new InvalidOperationException("Native homing follower capacity exceeded.");
            followers.Add(__instance);
        }
        return !(__0 is MonsterBase boss) || !targets.TryGetValue(boss, out var target) || target.shape.enabled;
    }
    private static void Prune(PlayerArrowProjectileFollower __instance)
    {
        if (NativePlayerArrow.Owner(__instance, Player.i) == null) return;
        var queue = (Queue<MonsterBase>)queueField.GetValue(__instance);
        var set = (HashSet<MonsterBase>)setField.GetValue(__instance);
        int count = queue.Count;
        for (int i = 0; i < count; i++) {
            var boss = queue.Dequeue();
            bool available = boss != null && (!targets.TryGetValue(boss, out var target) || target.shape.enabled);
            if (available) queue.Enqueue(boss!);
            else {
                set.Remove(boss!);
                if (boss != null && targets.TryGetValue(boss, out var inactive) && __instance.FollowTarget == inactive.query.transform) __instance.FollowTarget = null;
            }
        }
    }
    private static void Selected(PlayerArrowProjectileFollower __instance)
    {
        foreach (var target in targets.Values) if (__instance.FollowTarget == target.query.transform && target.current != null && target.selectedArrows.Add(__instance.GetInstanceID())) {
            if (target.selectedArrows.Count > 128) target.selectedArrows.Clear();
            var f = target.current;
            target.log("NS_HOMING_TARGET encounter=" + f.Encounter + " entity=" + f.Entity + " activation=" + f.TargetGeneration + " available=" + target.shape.enabled + " arrow=" + __instance.GetInstanceID() + " nativeFrame=" + Time.frameCount);
            break;
        }
    }
    internal void Update(ContactFrame? frame, Vector2 center, Vector2 size)
    {
        if (frame == null || current != null && frame != null && !new TargetIdentity(current).Equals(new TargetIdentity(frame))) selectedArrows.Clear();
        current = frame;
        query.transform.position = new Vector3(center.x, center.y, owner.transform.position.z);
        var scale = query.transform.lossyScale;
        shape.offset = Vector2.zero;
        shape.size = new Vector2(size.x / Math.Abs(scale.x), size.y / Math.Abs(scale.y));
        shape.enabled = frame != null && frame.HurtActive && frame.Health > 0;
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        shape.enabled = false;
        foreach (var follower in followers) if (follower != null && follower.isActiveAndEnabled) {
            var queue = (Queue<MonsterBase>)queueField.GetValue(follower);
            var set = (HashSet<MonsterBase>)setField.GetValue(follower);
            int count = queue.Count;
            for (int i = 0; i < count; i++) { var queued = queue.Dequeue(); if (!ReferenceEquals(queued, owner)) queue.Enqueue(queued); }
            set.Remove(owner);
            if (follower.FollowTarget == query.transform) follower.FollowTarget = null;
        }
        targets.Remove(owner); current = null;
        UnityEngine.Object.Destroy(query);
        if (targets.Count == 0) { harmony.UnpatchSelf(); followers.Clear(); }
        log("NS_HOMING_TARGET_DISPOSE retained=" + targets.Count);
    }
}
