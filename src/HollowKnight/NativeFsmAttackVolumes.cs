using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YiGodhome.Bridge;

namespace YiGodhome;

// HeroBox's authored damages_hero path, separate from DamageHero components.
internal sealed class NativeFsmAttackVolumes : IDisposable
{
    private sealed class Entry {
        internal PlayMakerFSM Fsm = null!;
        internal Collider2D Shape = null!;
        internal NativeFsmAttackLease Lease = null!;
        internal PlayMakerFSM? Control;
        internal readonly AttackGeneration Generation = new();
        internal long Activation, Action;
        internal bool Crimson, Active, Retired;
        internal HutongGames.PlayMaker.FsmInt[] Variables = null!;
        internal HutongGames.PlayMaker.FsmInt Damage = null!, Hazard = null!;
        internal int DamageIndex, HazardIndex;
        internal float[]? LastPoints;
        internal readonly ImmutableContourSnapshot<(Matrix4x4,Vector2,float,Vector2)> Geometry = new();
    }
    private readonly Dictionary<int, Entry> entries = new();
    private readonly NativeEncounterPools pools;
    private readonly Func<long> action;
    private readonly Action<string>? log;
    private readonly List<int> destroyed = new();
    private readonly List<AttackVolume> result = new();
    private static readonly Vector2[] UnitCircle = Enumerable.Range(0,64)
        .Select(i=>new Vector2(Mathf.Cos(i*Mathf.PI/32),Mathf.Sin(i*Mathf.PI/32))).ToArray();
    private static readonly Vector2[] UnitBox = { new Vector2(-1,-1),new Vector2(1,-1),new Vector2(1,1),new Vector2(-1,1) };
    private static readonly float[] InactiveShape = { 0,0,1,0,0,1 };
    private int geometryChecks;
    private int variableChecks;
    private static void BindVariables(Entry e)
    {
        var variables = e.Fsm.FsmVariables.IntVariables;
        if (ReferenceEquals(e.Variables,variables) && ReferenceEquals(variables[e.DamageIndex],e.Damage)
            && ReferenceEquals(variables[e.HazardIndex],e.Hazard)) return;
        e.Damage = variables.Single(v=>v.Name=="damageDealt");
        e.Hazard = variables.Single(v=>v.Name=="hazardType");
        e.DamageIndex = Array.IndexOf(variables,e.Damage); e.HazardIndex = Array.IndexOf(variables,e.Hazard);
        if (e.Damage.Value != FSMUtility.GetInt(e.Fsm,"damageDealt") || e.Hazard.Value != FSMUtility.GetInt(e.Fsm,"hazardType"))
            throw new InvalidOperationException("Native flame variable binding differs from FSMUtility.");
        e.Variables = variables;
    }
    internal NativeFsmAttackVolumes(NativeEncounterPools pools, Func<long> action, Action<string>? log)
    {
        this.pools = pools; this.action = action; this.log = log;
        HostDamageRegistry.PoolSpawned += Register;
        try { foreach (var root in pools.OwnedInstances()) Register(root); }
        catch { Dispose(); throw; }
    }
    private void Register(GameObject root)
    {
        if (root == null || !(root.name.StartsWith("Grimm_flare_pillar", StringComparison.Ordinal) || root.name.StartsWith("Flame Trail", StringComparison.Ordinal)) || !pools.Owns(root)) return;
        foreach (var fsm in root.GetComponentsInChildren<PlayMakerFSM>(true)) {
            if (fsm.FsmName != "damages_hero") continue;
            if (fsm.GetComponent<DamageHero>() != null) throw new InvalidOperationException("Native FSM flame has two damage authorities.");
            foreach (var shape in fsm.GetComponents<Collider2D>()) {
                int id = shape.GetInstanceID();
                if (entries.ContainsKey(id)) continue;
                if (entries.Count >= AttackVolumes.MaxCount) throw new InvalidOperationException("Native FSM flame capacity exceeded.");
                var lease = root.GetComponent<NativeFsmAttackLease>() ?? root.AddComponent<NativeFsmAttackLease>();
                var control = root.name.StartsWith("Grimm_flare_pillar", StringComparison.Ordinal) ? root.GetComponents<PlayMakerFSM>().Single(f => f.FsmName == "Control") : null;
                if (control != null && !control.FsmStates.Any(s => s.Name == "Recycle" && s.Actions.Any(a => a.GetType().Name == "RecycleSelf"))) throw new InvalidOperationException("Native pillar recycle contract changed.");
                var entry = new Entry { Fsm = fsm, Shape = shape, Lease = lease, Control = control, Crimson = control != null };
                BindVariables(entry); entries.Add(id,entry);
                log?.Invoke("HK_FSM_ATTACK_BOUND id=" + id + " root=" + root.name + " shape=" + shape.name + " fsm=" + fsm.FsmName + " nativePool=True");
            }
        }
    }
    internal AttackVolume[] Capture()
    {
        destroyed.Clear();
        foreach (var pair in entries) if (pair.Value.Shape == null || pair.Value.Fsm == null || pair.Value.Lease == null) destroyed.Add(pair.Key);
        foreach (var id in destroyed) entries.Remove(id);
        destroyed.Clear(); result.Clear();
        foreach (var pair in entries) {
            var e = pair.Value;
            BindVariables(e);
            int damage = e.Damage.Value, hazard = e.Hazard.Value;
            if (damage < 0 || damage > 2 || hazard != 1) throw new InvalidOperationException("Unaudited native FSM flame damage semantics.");
            bool enabled = damage > 0 && e.Shape.enabled && e.Shape.gameObject.activeInHierarchy;
            long generation = e.Generation.Observe(enabled, e.Lease.Generation);
            if (e.Lease.ActionGeneration != e.Lease.Generation) {
                if(damage!=FSMUtility.GetInt(e.Fsm,"damageDealt") || hazard!=FSMUtility.GetInt(e.Fsm,"hazardType"))
                    throw new InvalidOperationException("Native flame variable values changed across pooled activation.");
                if(HostFrameProbe.Enabled && variableChecks++<16) log?.Invoke("HK_FSM_VARIABLES_VERIFIED id="+pair.Key+" generation="+e.Lease.Generation+" sameNativeValues=True frame="+Time.frameCount);
                e.Lease.ActionGeneration = e.Lease.Generation; e.Lease.Action = action(); e.Lease.Retired = false;
            }
            if (generation != e.Activation) { e.Activation = generation; e.Retired = e.Lease.Retired; }
            e.Action = e.Lease.Action;
            if (enabled != e.Active) log?.Invoke("HK_FSM_ATTACK_WINDOW id=" + pair.Key + " name=" + e.Shape.name + " activation=" + e.Activation + " action=" + e.Action + " active=" + enabled + " damage=" + damage + " frame=" + Time.frameCount);
            e.Active = enabled;
            if (e.Retired || e.Lease.Retired) continue;
            if (!enabled) {
                // Base and column can have a collider-free gap. Keep their
                // shared action retired on NS until this exact pooled root
                // ends; another pillar/new pool generation remains independent.
                if(e.Crimson && e.Lease.gameObject.activeInHierarchy)
                    result.Add(new AttackVolume { Id=pair.Key, Activation=e.Activation, ActionId=e.Action,
                        Active=false, Damage=damage, Category=AttackCategory.Melee, Traits=AttackTraits.Crimson,
                        Points=e.LastPoints??InactiveShape });
                continue;
            }
            Vector2[] local;
            float radius = 1;
            Vector2 dimensions = Vector2.one;
            if (e.Shape is BoxCollider2D box && box.edgeRadius == 0) { dimensions = box.size*.5f; local = UnitBox; }
            else if (e.Shape is CircleCollider2D circle) { local = UnitCircle; radius = circle.radius; }
            else throw new InvalidOperationException("Unaudited native FSM flame collider.");
            var matrix = e.Shape.transform.localToWorldMatrix;
            var offset = e.Shape.offset;
            var key = (matrix,offset,radius,dimensions);
            bool reused = e.Geometry.TryGet(key, out var points);
            if (!reused) points = new float[local.Length*2];
            float error = 0;
            bool verify = HostFrameProbe.Enabled && geometryChecks<16;
            if (!reused || verify) for(int i=0;i<local.Length;i++) {
                var p = Vector2.Scale(local[i],dimensions)*radius+offset; var w = matrix.MultiplyPoint3x4(p);
                if (!reused) { points[i*2]=w.x; points[i*2+1]=w.y; }
                if(verify) error=Mathf.Max(error,Vector2.Distance(e.Shape.transform.TransformPoint(p),new Vector2(points[i*2],points[i*2+1])));
            }
            if (!reused) e.Geometry.Publish(key,points);
            if(verify) {
                geometryChecks++;
                if(float.IsNaN(error)||error>.0001f)throw new InvalidOperationException("Native FSM collider matrix differs from Transform.");
                log?.Invoke("HK_FSM_GEOMETRY_MATRIX_VERIFIED id="+pair.Key+" vertices="+local.Length+" maxError="+error+" frame="+Time.frameCount);
            }
            double area = 0;
            for (int i = 0; i < points.Length; i += 2) { int n = (i + 2) % points.Length; area += (double)points[i] * points[n + 1] - (double)points[i + 1] * points[n]; }
            if (Math.Abs(area) < .00001) {
                if(e.Crimson && e.Lease.gameObject.activeInHierarchy)
                    result.Add(new AttackVolume { Id=pair.Key, Activation=e.Activation, ActionId=e.Action,
                        Active=false, Damage=damage, Category=AttackCategory.Melee, Traits=AttackTraits.Crimson,
                        Points=e.LastPoints??InactiveShape });
                continue;
            }
            e.LastPoints=points;
            result.Add(new AttackVolume { Id = pair.Key, Activation = e.Activation, ActionId = e.Action, Active = true, Damage = e.Crimson ? damage : 0, Category = e.Crimson ? AttackCategory.Melee : AttackCategory.Burn,
                Traits = e.Crimson ? AttackTraits.Crimson : AttackTraits.None, Points = points });
        }
        var captured=result.ToArray(); result.Clear(); return captured;
    }
    internal bool Counter(long actionId, HitKind kind)
    {
        var e = entries.Values.FirstOrDefault(e => e.Action == actionId && e.Active && !e.Lease.Retired);
        if (e == null || !e.Crimson || kind != HitKind.UnboundedCounter) return false;
        e.Lease.Retired = true;
        int shapes = 0;
        foreach (var sibling in entries.Values.Where(s => s.Lease == e.Lease)) { sibling.Retired = true; shapes++; }
        e.Control!.SetState("Recycle");
        log?.Invoke("HK_FSM_FLAME_COUNTER id=" + e.Shape.GetInstanceID() + " action=" + actionId + " nativeSequenceContinues=True wholePillar=True shapes=" + shapes + " nativeRecycle=True rootActive=" + e.Lease.gameObject.activeInHierarchy + " frame=" + Time.frameCount);
        return true;
    }
    public void Dispose() { HostDamageRegistry.PoolSpawned -= Register; entries.Clear(); result.Clear(); destroyed.Clear(); }
}

internal sealed class NativeFsmAttackLease : MonoBehaviour
{
    internal long Generation { get; private set; }
    internal long ActionGeneration = -1, Action;
    internal bool Retired;
    private void OnEnable() => Generation++;
}
