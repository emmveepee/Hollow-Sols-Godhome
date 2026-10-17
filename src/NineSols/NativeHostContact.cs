using System;
using System.Linq;
using UnityEngine;
using YiGodhome.Bridge;
using YiGodhome.Geometry;

namespace YiGodhome;

internal sealed class NativeHostContact : IDisposable
{
    private static NativeHostContact? active;
    internal static void RetireContactOwner(int entity,long generation,Guid encounter)=>active?.volumes.RetireContactOwner(entity,generation,encounter);
    internal static bool DiagnosticVolumeActive(AttackVolume volume)=>active?.volumes.DiagnosticVolumeActive(volume)==true;
    private long retiredAttack;
    public static void EndNativeAttack(ContactFrame source)
    {
        var adapter = active;
        if (adapter == null || source.Encounter != adapter.encounter || source.AttackId <= 0) return;
        adapter.retiredAttack = source.AttackId;
        adapter.volumes.RetireAction(source.AttackId);
        if (source.Entity == adapter.entity) adapter.native.SetWorldBounds(default, default, false);
        adapter.verdict.Suspend(); adapter.admitted = false; adapter.overlapping = false;
        adapter.log("NS_ATTACK_RETIRED attack=" + source.AttackId + " damaging=False sameNativeFrame=" + Time.frameCount);
    }
    private readonly Player player;
    private readonly MonsterBase owner;
    private readonly GeometryLandingProbe geometry;
    private readonly NativeContactFixture native;
    private readonly NativeContactVerdict verdict;
    private readonly NativeAttackVolumes volumes;
    private readonly Action<string> log;
    private Guid encounter;
    private int entity, collider;
    private bool overlapping, stale = true;
    private long lastSequence;
    private float nextSample;
    private float separatedAt = -1;
    private float resumeAt;
    private bool admitted;
    private readonly bool isolateTargetDiagnostic;
    private bool damagedSeen, targetIsolated;
    private readonly bool hitDiagnostic;
    public bool OutgoingValidated { get; set; }
    private readonly Collider2D[] receivers;
    private ContactFrame? currentFrame;
    private long attackId, warnedAttack;
    private readonly System.Collections.Generic.Queue<HitOutcome> counters = new System.Collections.Generic.Queue<HitOutcome>();
    private PoolObject? warning;
    private readonly NativeMantisWarnings? mantisWarnings;
    private readonly NativeRagePresentation ragePresentation;
    private readonly NativeUumuuRepulsion? uumuuRepulsion;
    private readonly FxPlayer dangerEffect;
    private readonly PoolObject dangerPrefab;
    private readonly Vector3 dangerPosition;
    private readonly Transform dangerConstraint;
    private readonly SoundPlayer dangerSound;
    public System.Collections.Generic.IEnumerable<Renderer> WarningRenderers => (warning != null && warning.isOnScene ? warning.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>()).Concat(ragePresentation.Renderers).Concat(mantisWarnings?.Renderers ?? Array.Empty<Renderer>());
    internal long RageVisualSequence => ragePresentation.CapturedSequence;
    internal long RageHostFrame => ragePresentation.HostFrame;
    public bool TryGetCounter(out HitOutcome outcome) { if (volumes.TryGetOutcome(out outcome)) return true; if (counters.Count == 0) { outcome = null!; return false; } outcome = counters.Dequeue(); return true; }
    public NativeHostContact(Player player, MonsterBase owner, GeometryLandingProbe geometry, Action<string> log)
    {
        this.player = player; this.owner = owner; this.geometry = geometry; this.log = log;
        if (EncounterProfile.ForScene(geometry.SourceScene)?.Key == "Uumuu") uumuuRepulsion = new NativeUumuuRepulsion(player, geometry, log);
        string run = System.IO.Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!;
        isolateTargetDiagnostic = YiGodhome.Bridge.SessionConfiguration.Current.Has("target-probe") && YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures;
        isolateTargetDiagnostic &= !YiGodhome.Bridge.SessionConfiguration.Current.Has("lifecycle-probe");
        isolateTargetDiagnostic &= !YiGodhome.Bridge.SessionConfiguration.Current.Has("combat-probe");
        hitDiagnostic = YiGodhome.Bridge.SessionConfiguration.Current.Has("hit-probe");
        var colliderField = typeof(EffectReceiver).GetField("myCollider2D", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!;
        var receiverField = typeof(DamageReceiver).GetField("bindReceiver", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!;
        receivers = player.GetComponentsInChildren<DamageReceiver>(true).Where(d => d.bindHealth == player.health)
            .Select(d => (EffectReceiver)receiverField.GetValue(d)).Where(r => r != null && r.effectType == EffectType.EnemyAttack)
            .Select(r => (Collider2D)colliderField.GetValue(r)).Where(c => c != null).Distinct().ToArray();
        if (receivers.Length == 0) throw new InvalidOperationException("Native health receiver collider unavailable.");
        dangerEffect = owner.GetComponentsInChildren<FxPlayer>(true).Single(f => f.name == "DangerHintEffect" && f.EmitPoolObject != null && f.EmitPoolObject.name == "Effect_TaiDanger_Big");
        dangerPrefab = dangerEffect.EmitPoolObject;
        if (EncounterProfile.ForScene(geometry.SourceScene)?.ArmorAndHead == true) {
            // Installed Jiequan level41's warning players all reference this
            // sharedassets5 native prefab. Never load his story/AI for artwork.
            var maceCue = Resources.FindObjectsOfTypeAll<FxPlayer>().Select(f => f.EmitPoolObject)
                .Where(p => p != null && p.name == "Effect_TaiDanger").Distinct().ToArray();
            if (maceCue.Length == 1) dangerEffect.EmitPoolObject = maceCue[0];
            log("NS_MACE_WARNING_STYLE prefab=" + dangerEffect.EmitPoolObject.name + " native=True jiequan=" + (maceCue.Length == 1) + " candidates=" + maceCue.Length);
        }
        dangerPosition = dangerEffect.transform.localPosition;
        dangerConstraint = dangerEffect.setPositionConstraint;
        dangerSound = owner.GetComponentsInChildren<SoundPlayer>(true).Single(s => s.name == "DangerHint_SoundPlayer" && s.soundName == "UISFX_DangerHint_Boss");
        var profile=EncounterProfile.ForScene(geometry.SourceScene);
        if(profile==EncounterProfile.Mantis || profile==EncounterProfile.Sisters || profile==EncounterProfile.Watchers)
            mantisWarnings=new NativeMantisWarnings(dangerEffect,dangerSound,geometry,log,profile==EncounterProfile.Watchers);
        ragePresentation = new NativeRagePresentation(owner, geometry, log, () => dangerSound.Play(null));
        // Keep the initialized template immutable. Later volume leases must not
        // inherit this primary dealer's crimson/parriable or difficulty state.
        native = new NativeContactFixture(owner, log, 27, independent: true);
        try { verdict = new NativeContactVerdict(native.Dealer, player, log,
            YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures); }
        catch { native.Dispose(); throw; }
        volumes = new NativeAttackVolumes(player, owner, geometry, log);
        dangerEffect.setPositionConstraint = owner.transform;
        verdict.Countered = () => {
            var f = currentFrame;
            if (f == null || !f.Active || !(f.Crimson || f.Kickable)) return;
            counters.Enqueue(new HitOutcome { Session = f.Session, Encounter = f.Encounter, GeometryFrame = f.GeometryFrame, HostTick = f.CaptureTick,
                NativeFrame = Time.frameCount, Entity = f.Entity, TargetActivation = f.TargetGeneration, Dealer = native.Dealer.GetInstanceID(), Kind = HitKind.UnboundedCounter, AttackId = f.AttackId });
            log("NS_CRIMSON_COUNTER attack=" + f.AttackId + " native=True");
            EndNativeAttack(f);
        };
        foreach (var c in receivers) log("NS_HOST_RECEIVER name=" + c.name + " bounds=" + c.bounds);
        active = this;
    }
    public void Update(ContactFrame? frame)
    {
        verdict.Check();
        currentFrame = null;
        if (targetIsolated && hitDiagnostic && !OutgoingValidated && player.chiContainer.Value < 1
            && player.CurrentStateType != PlayerStateType.FooAttack && player.CurrentStateType != PlayerStateType.FooExplode)
        {
            targetIsolated = false; stale = true; overlapping = false;
            log("NS_TARGET_BODY_RESUMED reason=native-qi-spent-without-host-talisman-ack");
        }
        damagedSeen |= player.health.currentValue < 240;
        if (isolateTargetDiagnostic && !targetIsolated && damagedSeen && player.chiContainer.Value >= 1)
        {
            targetIsolated = true;
            log("NS_TARGET_BODY_ISOLATED nativeDamageAndQi=True health=" + player.health.currentValue + " chi=" + player.chiContainer.Value);
        }
        if (owner == null || owner.enabled || !owner.fsm.isPaused || owner.animator.enabled)
            throw new InvalidOperationException("Native contact owner left its paused contract.");
        if (frame == null || player.health.IsDead)
        {
            ragePresentation.Update(null, default, 0);
            mantisWarnings?.Update(null);
            volumes.Update(null);
            native.SetWorldBounds(default, default, false);
            verdict.Suspend(); admitted = false;
            if (!stale) log("NS_HOST_CONTACT_DISABLED reason=stale-or-dead");
            stale = true; overlapping = false; return;
        }
        if (frame.Session != Guid.Parse(Environment.GetEnvironmentVariable("YIGODHOME_SESSION")) || frame.GeometryFrame != geometry.SourceFrame)
            throw new InvalidOperationException("Contact identity, geometry or damage profile mismatch.");
        if (encounter == Guid.Empty) { encounter = frame.Encounter; entity = frame.Entity; collider = frame.Collider; }
        if (frame.Encounter != encounter || frame.Entity != entity || frame.Collider != collider || frame.Sequence < lastSequence)
            throw new InvalidOperationException("Contact encounter/entity identity changed.");
        if (stale) { resumeAt = Time.time + native.NativeDelay + Time.fixedDeltaTime; stale = false; }
        lastSequence = frame.Sequence;
        native.SetDifficulty(frame.Damage, frame.Difficulty);
        if (frame.AttackId != attackId)
        {
            native.SetWorldBounds(default, default, false); verdict.Suspend(); admitted = false; overlapping = false; separatedAt = -1;
            attackId = frame.AttackId; resumeAt = Time.time + native.NativeDelay + Time.fixedDeltaTime;
        }
        Point p = geometry.Map.ToTarget(new Point(frame.X, frame.Y));
        Vector2 center = new Vector2(p.X, p.Y), size = new Vector2(frame.Width, frame.Height) * geometry.Map.Scale;
        // Native warning effects are parented to this receiver owner.
        owner.SetPhysicPosition(center);
        ragePresentation.Update(frame, center, geometry.Map.Scale * Math.Max(frame.Width, frame.Height));
        mantisWarnings?.Update(frame);
        var warningFamily=EncounterProfile.ForScene(geometry.SourceScene)?.Family;
        long warningIdentity=(warningFamily==EncounterProfile.AbsoluteRadiance || warningFamily==EncounterProfile.WhiteDefender) && frame.CrimsonSequence>0
            ?frame.CrimsonSequence:frame.AttackId;
        if (mantisWarnings==null && EncounterProfile.ForScene(geometry.SourceScene)?.ArmorAndHead != true && (frame.KickWindup || frame.Windup || frame.Kickable || frame.Crimson) && warningIdentity != warnedAttack)
        {
            warnedAttack = warningIdentity;
            if (warning != null && warning.isOnScene) warning.ReturnToPool();
            dangerEffect.transform.position = center;
            var effect = dangerEffect.PlayCustomAt(center, Vector3.zero, owner.transform);
            warning = effect == null ? null : effect.GetComponent<PoolObject>();
            if (warning == null) throw new InvalidOperationException("Native crimson warning did not spawn.");
            if (frame.KickWindup || frame.Kickable || frame.QuickWarning)
                (warning.GetComponent<NativeWarningTiming>() ?? warning.gameObject.AddComponent<NativeWarningTiming>()).Begin(warning, log, frame.AttackId, frame.Encounter, frame.QuickWarning);
            bool sounded = dangerSound.Play(null);
            log("NS_CRIMSON_SOUND attack=" + frame.AttackId + " event=" + dangerSound.soundName + " accepted=" + sounded + " playingId=" + dangerSound.PlayID);
            log("NS_CRIMSON_WARNING attack=" + frame.AttackId + " encounter=" + frame.Encounter + " target=" + frame.Entity + " targetActivation=" + frame.TargetGeneration
                + " hostFrame=" + frame.NativeFrame + " nativeFrame=" + Time.frameCount + " admissionDelayed=" + (Time.time < resumeAt)
                + " native=" + (warning != null) + " renderers=" + string.Join(",", WarningRenderers.Select(r => r.name + ":" + r.GetType().Name + ":" + r.bounds)));
        }
        // The warning belongs to windup, not the dealer's activation delay.
        // Collision admission retains the audited native settling interval.
        // The settling gate belongs to this body. Independently spawned host
        // projectile activations remain live across boss windup/attack changes.
        if (Time.time < resumeAt) { volumes.Update(frame); return; }
        volumes.Update(frame);
        uumuuRepulsion?.Update(frame);
        native.SetCrimson(frame.Crimson || frame.Kickable);
        native.SetCounterOwner(frame.Active ? NativeTargetProbe.CounterOwner(frame.Entity) : null);
        currentFrame = frame;
        if (!frame.Active || frame.AttackId > 0 && frame.AttackId == retiredAttack) {
            native.SetWorldBounds(default, default, false); verdict.Suspend(); admitted = false;
            overlapping = false; separatedAt = -1; return;
        }
        // Talisman callbacks use the native owner's transform as well as its receiver.
        // The outgoing-only fixture suppresses contact damage, not target motion.
        if (targetIsolated) { native.SetWorldBounds(default, default, false); verdict.Suspend(); admitted = false; return; }
        if (!admitted) { verdict.Begin(); admitted = true; }
        native.SetWorldBounds(center, size, frame.Active);
        if (frame.Active && (Vector2.Distance(native.Collider.bounds.center, center) > 0.1f || Vector2.Distance(native.Collider.bounds.size, size) > 0.1f))
            throw new InvalidOperationException("Native contact collider differs from mapped host bounds.");
        bool touching = frame.Active && receivers.Any(c => c.enabled && c.gameObject.activeInHierarchy && Physics2D.Distance(native.Collider, c).isOverlapped);
        if (touching) { overlapping = true; separatedAt = -1; }
        if (overlapping && !touching && separatedAt < 0) separatedAt = Time.time;
        if (overlapping && !touching && Time.time - separatedAt > native.NativeDelay + Time.fixedDeltaTime)
        {
            // A continuous body contact becomes a new contact only after separation.
            // Native parry and per-health cooldowns still decide every hit verdict.
            native.SetWorldBounds(center, size, false);
            verdict.Begin();
            native.SetWorldBounds(center, size, frame.Active);
            log("NS_HOST_CONTACT_SEPARATED frame=" + Time.frameCount);
            overlapping = false; separatedAt = -1;
        }
        if (Time.realtimeSinceStartup >= nextSample)
        {
            nextSample = Time.realtimeSinceStartup + 1;
            log("NS_HOST_CONTACT_SAMPLE sequence=" + frame.Sequence + " entity=" + entity + " active=" + frame.Active + " overlap=" + touching
                + " center=" + center + " size=" + size + " health=" + player.health.currentValue + " chi=" + player.chiContainer.Value);
        }
    }
    public void Dispose() { if (active == this) active = null; currentFrame = null; counters.Clear(); uumuuRepulsion?.Dispose(); mantisWarnings?.Dispose(); ragePresentation.Dispose(); if (warning != null && warning.isOnScene) warning.ReturnToPool(); warning = null; dangerEffect.EmitPoolObject = dangerPrefab; dangerEffect.setPositionConstraint = dangerConstraint; dangerEffect.transform.localPosition = dangerPosition; volumes.Dispose(); verdict.Dispose(); native.Dispose(); }
}
