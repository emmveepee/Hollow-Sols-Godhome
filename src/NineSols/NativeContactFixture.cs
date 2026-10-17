using System;
using System.Linq;
using UnityEngine;

namespace YiGodhome;

// Reuse an initialized native attack and its owner/parry references. Only collision
// placement is translated. The rain-only proximity query uses the native
// buffered counter verdict without entering the damage path.
internal sealed class NativeContactFixture : IDisposable
{
    private readonly DamageDealer damage;
    private readonly PolygonCollider2D collider;
    private readonly ParriableAttackEffect parry;
    private readonly Vector2[][] paths;
    private readonly Vector3 position;
    private readonly bool originalActive, originalParryActive;
    private readonly Action<string> log;
    private bool contact;
    private readonly DamageRatioType originalRatioType;
    private readonly AttackDamageRatioData originalRatio;
    private AttackDamageRatioData? runtimeRatio;
    private bool disposed;
    private float damageScalar;
    private bool difficultyRegistered;
    private readonly bool originalParriable;
    private readonly DamageType originalDamageType;
    private readonly ParryParam originalParryParam;
    private readonly float originalAttackDir;
    private static readonly System.Reflection.FieldInfo AttackDirField = HarmonyLib.AccessTools.Field(typeof(DamageDealer), "attackDir");
    private int pushDirection;
    private Vector2 lastSize;
    internal static long PolygonWrites, PolygonReuses, SyncCalls, SyncTicks;
    private Vector2[] polygonPoints = Array.Empty<Vector2>();
    private Vector2[] installedPolygon = Array.Empty<Vector2>();
    private bool matrixVerified;
    private bool reuseVerified;
    private Vector2 projectileCenter;
    private int projectileDirection;
    private GameObject? cloneRoot;
    private readonly TriggerDetector? trigger;
    private CircleCollider2D? circle;
    private bool circleSelected, circleVerified;
    private Vector2 circleCenter;
    private float circleRadius;
    private float localCircleRadius;
    private Vector2 localCircleOffset;
    private static readonly System.Reflection.FieldInfo DealerCollider = HarmonyLib.AccessTools.Field(typeof(EffectDealer), "_myCollider");
    private static readonly System.Reflection.FieldInfo TriggerCollider = HarmonyLib.AccessTools.Field(typeof(TriggerDetector), "myCollider");
    public float NativeDelay => damage.DelayTime;
    public Collider2D Collider => circleSelected ? circle! : collider;
    public DamageDealer Dealer => damage;
    private EffectHitData? expandedCounterHit;
    internal void TryRainCounter(Player player)
    {
        // This extra proximity query never enters DamageReceiver or enlarges a
        // collider. The native buffered unbound window and native parry verdict
        // still decide success; normal/imperfect parries cannot use this path.
        if(!contact || parry.parried || damage.parriable || !player.counteringDefenseCondition.CustomInputBuffer || player.health.IsDead)return;
        if(expandedCounterHit==null) {
            var receiver=player.GetComponentsInChildren<DamageReceiver>(true)
                .First(r=>r.bindHealth==player.health && r.BindReceiver!=null);
            expandedCounterHit=new EffectHitData(damage.effectDealer,receiver.BindReceiver);
        }
        expandedCounterHit.Override(damage.effectDealer,expandedCounterHit.receiver,null);
        if(parry.PlayerParriedCheck(expandedCounterHit))
            log("NS_RADIANCE_RAIN_COUNTER_PROXIMITY nativeVerdict=True damageGeometryUnchanged=True dealer="+damage.GetInstanceID()+" frame="+Time.frameCount);
    }
    internal void SetCounterOwner(MonsterBase? injuryOwner) {
        if(cloneRoot!=null) cloneRoot.GetComponent<NativeAttackOwner>().InjuryOwner=injuryOwner;
        // Native AccurateParry intentionally skips internal injury for Ranged.
        // The imported template's detail tag cannot classify every host attack.
        damage.effectDealer.detailType=injuryOwner!=null?EffectDetailType.Undefined:EffectDetailType.Ranged;
    }
    public string Policy => "parriable=" + damage.parriable + " direction=" + damage.GlobalAttackDir + " knockback=" + parry.param.knockBackValue;
    public NativeContactFixture(MonsterBase owner, Action<string> log, float translatedDamage = 0, bool independent = false)
    {
        if (owner.enabled || !owner.fsm.isPaused) throw new InvalidOperationException("Contact fixture requires paused native owner.");
        this.log = log;
        damage = owner.GetComponentsInChildren<DamageDealer>(true).Single(d => d.name == "SlashDamage");
        parry = damage.bindParriable;
        if (independent)
        {
            try
            {
            // Clone only the two audited native siblings, with an inactive parent
            // so no collision callback can run before their cross-links are rebound.
            cloneRoot = new GameObject("YiGodhome Native Attack"); cloneRoot.SetActive(false);
            cloneRoot.transform.SetParent(damage.transform.parent, false);
            damage = UnityEngine.Object.Instantiate(damage.gameObject, cloneRoot.transform, false).GetComponent<DamageDealer>();
            parry = UnityEngine.Object.Instantiate(parry.gameObject, cloneRoot.transform, false).GetComponent<ParriableAttackEffect>();
            var context = cloneRoot.AddComponent<NativeAttackOwner>(); context.Monster = owner; context.InjuryOwner = owner; context.Parry = parry;
            AutoAttributeManager.AutoReferenceAllChildren(cloneRoot);
            damage.bindingParry = parry; parry.bindDamage = damage;
            damage.gameObject.SetActive(false); parry.gameObject.SetActive(false);
            cloneRoot.SetActive(true);
            if (damage.bindParriable != parry) throw new InvalidOperationException("Native attack clone retained a shared parry owner.");
            }
            catch { if (cloneRoot != null) { cloneRoot.SetActive(false); UnityEngine.Object.Destroy(cloneRoot); } throw; }
        }
        collider = damage.GetComponent<PolygonCollider2D>();
        trigger = damage.GetComponent<TriggerDetector>();
        if (NativeWorkCost.Enabled && trigger != null) NativeWorkCost.Detectors.Add(trigger);
        if (independent) collider.enabled = true;
        string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;
        log("NS_DEALER_GRAPH dealer=" + Path(damage.transform) + " parry=" + Path(parry.transform));
        if (collider == null || parry == null || !damage.parriable || damage.attacker == null)
        { if (cloneRoot != null) UnityEngine.Object.Destroy(cloneRoot); throw new InvalidOperationException("Native contact template dependencies missing."); }
        originalActive = damage.gameObject.activeSelf; originalParryActive = parry.gameObject.activeSelf;
        originalParriable = damage.parriable;
        originalDamageType = damage.type;
        originalParryParam = parry.param;
        originalAttackDir = (float)AttackDirField.GetValue(damage);
        log("NS_CONTACT_TYPE original=" + damage.type);
        position = damage.transform.localPosition;
        originalRatioType = damage.ratioType; originalRatio = damage.ratioData;
        paths = Enumerable.Range(0, collider.pathCount).Select(collider.GetPath).ToArray();
        try
        {
        damage.gameObject.SetActive(false);
        if (translatedDamage > 0)
        {
            runtimeRatio = ScriptableObject.CreateInstance<AttackDamageRatioData>();
            runtimeRatio.ratio = 1;
            damage.ratioType = (DamageRatioType)(-1); damage.ratioData = runtimeRatio;
            float scalar = damage.DamageAmount;
            if (scalar <= 0 || float.IsNaN(scalar)) throw new InvalidOperationException("Native damage scalar unavailable.");
            runtimeRatio.ratio = translatedDamage / scalar;
            damageScalar = scalar;
            log("NS_CONTACT_DAMAGE nativeAmount=" + damage.DamageAmount + " runtimeRatio=True");
        }
        log("NS_CONTACT_READY owner=" + owner.GetInstanceID() + " dealer=" + damage.GetInstanceID() + " parry=" + parry.GetInstanceID());
        log("NS_CONTACT_COMPONENTS " + string.Join(",", damage.GetComponents<Component>().Select(c => c.GetType().Name)));
        var rb = collider.attachedRigidbody;
        log("NS_CONTACT_PHYSICS dealer=" + damage.GetInstanceID() + " delay=" + damage.DelayTime + " interpolation=" + rb.interpolation + " body=" + rb.bodyType + " colliderLayer=" + collider.gameObject.layer + " effectType=" + damage.effectDealer.type);
        var state = (PlayerParryState)Player.i.FindState(PlayerStateType.Parry);
        var windows = (float[])typeof(PlayerParryState).GetField("spamDatas", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(state);
        log("NS_PARRY_TIMING duration=" + state.parryDuration.Value + " windows=" + string.Join(",", windows) + " spamLevel=" + state.spamLevel);
        NativeDifficultyDamage.Register(damage); difficultyRegistered = true;
        }
        catch { Dispose(); throw; }
    }
    public void SetDifficulty(int masks, int tier)
    {
        if (masks < 0 || masks > 100 || tier < 0 || tier > 2 || runtimeRatio == null || damageScalar <= 0)
            throw new InvalidOperationException("Invalid native incoming damage profile.");
        runtimeRatio.ratio = 27f * masks * (tier > 0 ? 2 : 1) / damageScalar;
        NativeDifficultyDamage.Set(damage, tier);
    }
    public void SetCrimson(bool crimson)
    {
        // Host contact is enabled only for authored attack windows, never idle touch.
        damage.type = DamageType.MonsterAttack;
        damage.parriable = crimson ? false : originalParriable;
    }
    public void SetLargeParryPush(bool large, bool initial = false, int direction = 0, bool gentle = false, bool nailmaster = false, bool swordCombo = false)
    {
        var param = originalParryParam;
        if (large || gentle) {
            param.knockBackType = KnockBackType.Custom;
            param.knockBackValue = GameCore.Instance.gameStat.GetPlayerKnockBackValue(gentle ? KnockBackType.Small : KnockBackType.Large) * (gentle ? .5f : initial ? 1.25f : 1.10f);
        }
        if(nailmaster) {
            large=YiGodhome.Bridge.NailmasterRecoil.Tumbles(large,initial);
            // Native OnTakeDamage maps Medium to a 200-unit recoil without
            // lying down; Large is its 320-unit tumbling/backflip reaction.
            // Set hurt type explicitly: Custom knockback otherwise inherits
            // the donor's hurt animation, independently of its push strength.
            param.knockBackType=large?KnockBackType.Large:KnockBackType.Medium;
            param.knockBackValue=GameCore.Instance.gameStat.GetPlayerKnockBackValue(param.knockBackType);
            param.hurtLiftType=large?HurtType.HurtLarge:HurtType.HurtMedium;
            param.LiftYForce=large?100:0;
            if(initial) {
                // Custom + DefaultAsParry retains the explicit230 recoil in
                // native OnTakeDamage; HurtMedium would hard-code it back to200.
                param.knockBackType=KnockBackType.Custom;param.knockBackValue=230;
                param.hurtLiftType=HurtType.DefaultAsParry;
            }
        }
        if(swordCombo) {
            param.knockBackType=KnockBackType.Medium;
            param.knockBackValue=GameCore.Instance.gameStat.GetPlayerKnockBackValue(KnockBackType.Medium);
            param.hurtLiftType=HurtType.HurtMedium;param.LiftYForce=0;
        }
        if (direction != 0 && damage.GlobalAttackDir != direction) damage.GlobalAttackDir = direction;
        else if (direction == 0 && pushDirection != 0) AttackDirField.SetValue(damage, originalAttackDir);
        pushDirection = direction;
        parry.param = param;
    }
    public void SetContact(Player player, bool active)
    {
        if (contact == active) return;
        SetWorldBounds(player.transform.position + new Vector3(0, 15, 0), new Vector2(16, 28), active);
        log("NS_CONTACT_PLACEMENT active=" + active + " frame=" + Time.frameCount + " health=" + player.health.currentValue + " chi=" + player.chiContainer.Value);
    }
    public void SetWorldBounds(Vector3 center, Vector2 size, bool active)
    {
        if (!active) { contact = false; damage.gameObject.SetActive(false); return; }
        SelectCircle(false);
        damage.transform.position = center;
        Vector2 Local(float x, float y) => (Vector2)damage.transform.InverseTransformPoint(center + new Vector3(x, y, 0)) - collider.offset;
        if (lastSize != size)
        {
            Vector2 h = size * 0.5f;
            collider.pathCount = 1; collider.SetPath(0, new[] { Local(-h.x, -h.y), Local(h.x, -h.y), Local(h.x, h.y), Local(-h.x, h.y) });
            installedPolygon = Array.Empty<Vector2>();
            lastSize = size;
        }
        if (!contact) { parry.gameObject.SetActive(false); parry.gameObject.SetActive(true); damage.gameObject.SetActive(true); contact = true; }
        long syncStart = System.Diagnostics.Stopwatch.GetTimestamp();
        Physics2D.SyncTransforms();
        SyncCalls++; SyncTicks += System.Diagnostics.Stopwatch.GetTimestamp() - syncStart;
    }
    public void SetWorldPolygon(Vector2[] points, bool active, Vector2? origin = null, bool synchronize = true, bool projectile = false, Vector2? nativeCircleCenter = null, float nativeCircleRadius = 0)
    {
        if (!active) { contact = false; damage.gameObject.SetActive(false); return; }
        Vector2 center = Vector2.zero; foreach (var point in points) center += point;
        center /= points.Length;
        if (nativeCircleRadius > 0) center = nativeCircleCenter ?? throw new InvalidOperationException("Native circle center missing.");
        SelectCircle(nativeCircleRadius > 0);
        if (projectile) {
            if (!contact) projectileDirection = 0;
            else if (Mathf.Abs(center.x - projectileCenter.x) > .002f) projectileDirection = center.x > projectileCenter.x ? 1 : -1;
            projectileCenter = center;
        }
        damage.transform.position = origin ?? center;
        if (circleSelected) {
            var scale = damage.transform.lossyScale;
            float sx = Mathf.Abs(scale.x), sy = Mathf.Abs(scale.y);
            if (sx <= 0 || Mathf.Abs(sx-sy) > sx*.00001f) throw new InvalidOperationException("Native circle requires uniform receiving scale.");
            circleCenter = center; circleRadius = nativeCircleRadius;
            float localRadius = nativeCircleRadius / sx;
            Vector2 localOffset = origin.HasValue ? (Vector2)damage.transform.InverseTransformPoint(center) : Vector2.zero;
            if (localCircleRadius != localRadius) { circle!.radius = localRadius; localCircleRadius = localRadius; }
            if (localCircleOffset != localOffset) { circle!.offset = localOffset; localCircleOffset = localOffset; }
        } else {
            if (collider.pathCount != 1) collider.pathCount = 1;
            // One native matrix read instead of a native Transform call per vertex.
            // The same matrix computes the same world-to-local collision polygon.
            var toLocal = damage.transform.worldToLocalMatrix;
            if (polygonPoints.Length != points.Length) polygonPoints = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++) polygonPoints[i] = (Vector2)toLocal.MultiplyPoint3x4(points[i]) - collider.offset;
            if (!matrixVerified && YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures) {
                float error=0;
                for(int i=0;i<points.Length;i++) error=Mathf.Max(error,Vector2.Distance(polygonPoints[i],(Vector2)damage.transform.InverseTransformPoint(points[i])-collider.offset));
                if(error>.002f || float.IsNaN(error)) throw new InvalidOperationException("Native world-to-local polygon matrix differs from Transform contract.");
                matrixVerified=true;
                log("NS_POLYGON_MATRIX_VERIFIED dealer="+damage.GetInstanceID()+" vertices="+points.Length+" maxError="+error+" nativeFrame="+Time.frameCount);
            }
            bool changed = installedPolygon.Length != polygonPoints.Length;
            if (!changed) for (int i = 0; i < polygonPoints.Length; i++)
                if ((installedPolygon[i] - polygonPoints[i]).sqrMagnitude > .002f * .002f) { changed = true; break; }
            if (changed) {
                collider.SetPath(0, polygonPoints);
                if (installedPolygon.Length != polygonPoints.Length) installedPolygon = new Vector2[polygonPoints.Length];
                Array.Copy(polygonPoints, installedPolygon, polygonPoints.Length);
                PolygonWrites++;
            } else {
                PolygonReuses++;
                if (!reuseVerified && YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures) {
                    float error = 0;
                    var actual = collider.GetPath(0);
                    for (int i = 0; i < points.Length; i++) error = Mathf.Max(error, Vector2.Distance(points[i], collider.transform.TransformPoint(actual[i] + collider.offset)));
                    if (error > .003f || float.IsNaN(error)) throw new InvalidOperationException("Retained native polygon differs from mapped host geometry.");
                    reuseVerified = true;
                    log("NS_POLYGON_REUSE_VERIFIED dealer=" + damage.GetInstanceID() + " vertices=" + points.Length + " maxWorldError=" + error + " nativeFrame=" + Time.frameCount);
                }
            }
        }
        if (!contact) { parry.gameObject.SetActive(false); parry.gameObject.SetActive(true); damage.gameObject.SetActive(true); contact = true; }
        // Native KnockBack faces against its incoming direction. A projectile
        // uses its own flight, never the paused slash owner's facing. Zero
        // asks the native receiver to derive the side from this dealer's center.
        if (projectile) {
            if (projectileDirection == 0) AttackDirField.SetValue(damage, 0f);
            else damage.GlobalAttackDir = projectileDirection;
        }
        SetCrimson(false);
        if (cloneRoot != null) NativeWorkCost.InspectAttackGraph(cloneRoot, points.Length, log);
        if (!synchronize) return;
        long syncStart = System.Diagnostics.Stopwatch.GetTimestamp();
        Physics2D.SyncTransforms();
        SyncCalls++; SyncTicks += System.Diagnostics.Stopwatch.GetTimestamp() - syncStart;
        VerifyCircle();
    }
    private void SelectCircle(bool selected)
    {
        if (circleSelected == selected) return;
        if (contact || cloneRoot == null || trigger == null)
            throw new InvalidOperationException("Collider primitive can change only on an inactive independent lease.");
        if (selected && circle == null) {
            circle = damage.gameObject.AddComponent<CircleCollider2D>(); circle.enabled = false;
            circle.isTrigger = collider.isTrigger; circle.sharedMaterial = collider.sharedMaterial;
        }
        circleSelected = selected;
        var shape = Collider;
        collider.enabled = !selected;
        if (circle != null) circle.enabled = selected;
        DealerCollider.SetValue(damage.effectDealer, shape); TriggerCollider.SetValue(trigger, shape);
        if (!ReferenceEquals(DealerCollider.GetValue(damage.effectDealer), shape) || !ReferenceEquals(TriggerCollider.GetValue(trigger), shape))
            throw new InvalidOperationException("Native collider primitive binding failed.");
    }
    internal void VerifyCircle()
    {
        if (!NativeWorkCost.Enabled || !circleSelected || !contact || circleVerified) return;
        var bounds = circle!.bounds;
        float error = Mathf.Max(Vector2.Distance(bounds.center,circleCenter),Mathf.Abs(bounds.extents.x-circleRadius),Mathf.Abs(bounds.extents.y-circleRadius));
        if (error > .003f || float.IsNaN(error) || circle.shapeCount != 1 || collider.enabled
            || !ReferenceEquals(DealerCollider.GetValue(damage.effectDealer),circle) || !ReferenceEquals(TriggerCollider.GetValue(trigger),circle))
            throw new InvalidOperationException("Native circle bounds or cached bindings differ from host geometry.");
        circleVerified = true;
        log("NS_NATIVE_CIRCLE_VERIFIED dealer=" + damage.GetInstanceID() + " shapes=" + circle.shapeCount + " maxWorldError=" + error
            + " radius=" + circleRadius + " nativeCallbacks=True separateParryReceiver=True frame=" + Time.frameCount);
    }
    public void SetHazard()
    {
        damage.type = DamageType.TrapDamage; damage.parriable = false;
        damage.effectDealer.type |= EffectType.Trap;
        if (!damage.ShouldTeleport) throw new InvalidOperationException("Native trap recovery unavailable.");
    }
    public void Dispose()
    {
        if (disposed || damage == null) return;
        disposed = true;
        if (!ReferenceEquals(trigger, null)) NativeWorkCost.Detectors.Remove(trigger!);
        if (difficultyRegistered) NativeDifficultyDamage.Remove(damage);
        damage.gameObject.SetActive(false);
        if (cloneRoot != null) { cloneRoot.SetActive(false); UnityEngine.Object.Destroy(cloneRoot); if (runtimeRatio != null) UnityEngine.Object.Destroy(runtimeRatio); return; }
        damage.transform.localPosition = position;
        damage.ratioType = originalRatioType; damage.ratioData = originalRatio;
        damage.parriable = originalParriable;
        damage.type = originalDamageType;
        parry.param = originalParryParam;
        AttackDirField.SetValue(damage, originalAttackDir);
        if (runtimeRatio != null) UnityEngine.Object.Destroy(runtimeRatio);
        collider.pathCount = paths.Length;
        for (int i = 0; i < paths.Length; i++) collider.SetPath(i, paths[i]);
        parry.gameObject.SetActive(originalParryActive); damage.gameObject.SetActive(originalActive);
    }
}
