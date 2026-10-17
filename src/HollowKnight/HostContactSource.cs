using System;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using YiGodhome.Bridge;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

namespace YiGodhome;

internal sealed class HostContactSource : IDisposable
{
    private readonly Guid session, encounter;
    private HealthManager? boss;
    private TargetHealthState? targetHealth;
    private NativeHealthNotches? notches;
    private DamageHero? damage;
    private BoxCollider2D? collider;
    private BoxCollider2D? hurt;
    private BoxCollider2D[] hurtShapes = Array.Empty<BoxCollider2D>();
    private readonly Action<string>? log;
    private PlayMakerFSM[] fsms = Array.Empty<PlayMakerFSM>();
    private string[] bodyStateNames = Array.Empty<string>(), bodyStateValues = Array.Empty<string>();
    private PlayMakerFSM? attackFsm;
    private long attackId, counteredAttack;
    private int forcedSummons;
    private float nextForcedSummon;
    private bool wasCrimson;
    private bool wasKickable;
    private float kickRecoveryUntil;
    private HostAttackVolumes? volumes;
    private HostSummons? summons;
    private PureVesselCombat? pureCombat;
    private long removedAt;
    private bool wasDescending;
    private RosterContactSource? roster;
    internal bool HidePlayer=>roster?.HidePlayer==true;
    internal bool AwaitingNativeBossCompletion => roster?.AwaitingNativeBossCompletion==true;
    public void ObserveNativeHealth(HitOutcome hit, int health) { if (targetHealth != null) targetHealth.ObserveNative(hit.Entity, hit.TargetActivation, health); }
    public bool KnowsSummon(HitOutcome h) => roster?.Knows(h) == true || summons?.Knows(h) == true;
    internal bool RecognizesSummon(HitOutcome h) => roster?.Recognizes(h) == true || summons?.Knows(h) == true;
    public HealthManager? ResolveSummon(HitOutcome h) => roster != null ? roster.Resolve(h) : summons?.Resolve(h);
    internal int KnownSummonHealth(HitOutcome hit) => roster?.KnownHealth(hit) ?? (summons?.Resolve(hit)?.hp ?? 0);
    internal bool CounterSummon(HitOutcome hit) => roster?.CounterTarget(hit) == true;
    public int PresentedHealth(HealthManager h) => roster?.PresentedHealth(h) ?? Math.Max(0, h.hp);
    internal void ObservedMelee(YiGodhome.Bridge.Pose pose,HostHitReceiver receiver) {
        if(Latest!=null && pose.GeometrySession==session && pose.GeometryFrame==Latest.GeometryFrame && GameManager.instance.gameState.ToString()!="PAUSED")
            roster?.ObservedMelee(pose,receiver);
    }
    internal void ObservedDefeatedHead(YiGodhome.Bridge.Pose pose) {
        if(Latest!=null && pose.GeometrySession==session && pose.GeometryFrame==Latest.GeometryFrame
            && GameManager.instance.gameState.ToString()!="PAUSED")roster?.ObservedDefeatedHead(pose);
    }
    internal void TraceNative(Action<string> trace) { if (roster != null) roster.TraceNative(trace); else if (boss != null) TraceBody(boss, trace); }
    internal static void TraceBody(HealthManager body, Action<string> trace) {
        trace("DETAIL_NATIVE_TARGET entity=" + body.GetInstanceID() + " name=" + body.name.Replace(' ','_') + " active=" + body.gameObject.activeInHierarchy + " health=" + body.hp + " invincible=" + body.IsInvincible
            + " position=" + body.transform.position + " states=" + string.Join(";",body.GetComponents<PlayMakerFSM>().Take(8).Select(f => f.FsmName + ":" + f.ActiveStateName)));
    }
    public ContactFrame? Latest { get; private set; }
    internal bool ScriptedSkipCounter=>roster?.ScriptedSkipCounter==true;
    internal bool ScriptedCounterPrecharge=>roster?.ScriptedCounterPrecharge==true;
    public HealthManager? Boss => boss;
    internal bool NativeCounterWindow(HealthManager target) => target == boss && attackFsm?.ActiveStateName == "Counter Stance"
        && attackFsm.FsmStates.Single(s => s.Name == "Counter Stance").Transitions.Any(t => t.EventName == "BLOCKED HIT");
    // Synchronously consumed by BindBodies; never published to a worker/frame.
    private readonly System.Collections.Generic.List<HealthManager> presentationBodies = new(32);
    internal System.Collections.Generic.IReadOnlyList<HealthManager> PresentationBodies {
        get {
            presentationBodies.Clear();
            if (roster != null) roster.CopyPresentationBodies(presentationBodies);
            else {
                if (boss != null) presentationBodies.Add(boss);
                summons?.CopyPresentationBodies(presentationBodies);
            }
            return presentationBodies;
        }
    }
    internal bool DiagnosticEligible(SummonedTarget target) => roster?.DiagnosticEligible(target) ?? true;
    internal MeshRenderer[] RageParts => roster?.RageParts ?? Array.Empty<MeshRenderer>();
    internal HealthManager[] RageBodies => roster?.RageBodies ?? (boss == null ? Array.Empty<HealthManager>() : new[] { boss });
    internal AttackTraits? AuraTraits(HealthManager body) => roster?.AuraTraits(body);
    public bool MatchesPrimary(HitOutcome hit) => roster != null ? roster.MatchesAnchor(hit) : targetHealth?.Matches(hit) == true;
    public HostContactSource(Guid session, Guid encounter = default, Action<string>? log = null) { this.session = session; this.encounter = encounter == Guid.Empty ? Guid.NewGuid() : encounter; this.log = log; }
    public ContactFrame Capture(long geometryFrame, YiGodhome.Bridge.Pose? pose = null)
    {
        if (EncounterProfile.Selected.Roster) return Latest = (roster ??= new RosterContactSource(session, encounter, log)).Capture(geometryFrame, pose);
        bool xero = EncounterProfile.Selected.Family == EncounterProfile.Xero;
        bool gruz = EncounterProfile.Selected.Family == EncounterProfile.Gruz;
        bool mawlek = EncounterProfile.Selected.Family == EncounterProfile.Mawlek;
        bool pure = EncounterProfile.Selected.Family == EncounterProfile.PureVessel;
        if (UnitySceneManager.GetActiveScene().name != EncounterProfile.Selected.Scene || GameManager.instance.profileID != 4)
            throw new InvalidOperationException("Contact source left audited encounter.");
        if (boss == null && targetHealth != null) {
            if (!pure || targetHealth.Health != 0 || Latest == null) throw new InvalidOperationException("Unadapted primary target removal.");
            long now = Stopwatch.GetTimestamp();
            if (removedAt == 0) { removedAt = now; log?.Invoke("HK_TERMINAL_BODY_REMOVED entity=" + targetHealth.Entity + " activation=" + targetHealth.Activation + " encounter=" + encounter + " awaitingNativeCompletion=True frame=" + Time.frameCount); }
            if ((now - removedAt) / (double)Stopwatch.Frequency > 30) throw new InvalidOperationException("Native encounter completion did not follow terminal body removal within 30 seconds.");
            return Latest = targetHealth.RemovedSnapshot(Latest, Time.frameCount, now);
        }
        if (boss == null)
        {
            boss = Resources.FindObjectsOfTypeAll<HealthManager>().Single(h => h.name == EncounterProfile.Selected.Boss && h.gameObject.scene.name == EncounterProfile.Selected.Scene);
            if (targetHealth != null) throw new InvalidOperationException("Primary body disappeared; this profile needs a native phase adapter.");
            notches = new NativeHealthNotches(boss, log);
            targetHealth = new TargetHealthState(boss.GetInstanceID(), 1, Math.Max(0, boss.hp));
            damage = gruz ? boss.GetComponentsInChildren<DamageHero>(true).Single(d => d.name == "Hero Damager") : boss.GetComponent<DamageHero>();
            collider = damage.GetComponent<BoxCollider2D>();
            hurt = boss.GetComponent<BoxCollider2D>();
            log?.Invoke("HK_COMBAT_DIFFICULTY profile=" + EncounterProfile.Selected.Key + " tier=" + (BossSceneController.Instance == null ? 0 : BossSceneController.Instance.BossLevel) + " health=" + boss.hp + " baseDamage=" + damage.damageDealt);
            if (pure) hurtShapes = boss.GetComponentsInChildren<BoxCollider2D>(true).Where(c => c.GetComponent<ExtraDamageableProxy>() != null).ToArray();
            fsms = boss.GetComponents<PlayMakerFSM>();
            bodyStateNames = new string[fsms.Length]; bodyStateValues = new string[fsms.Length];
            attackFsm = xero ? null : fsms.Single(f => f.FsmName == (gruz ? "Big Fly Control" : mawlek ? "Mawlek Control" : pure ? "Control" : "Big Buzzer"));
            if (pure) pureCombat = new PureVesselCombat(boss, attackFsm!, log, encounter);
            foreach (var fsm in fsms) foreach (var state in fsm.FsmStates)
                log?.Invoke("HK_BODY_STATE_AUDIT fsm=" + fsm.FsmName + " state=" + state.Name + " actions=" + string.Join(",", state.Actions.Select(a => a.GetType().Name))
                    + " transitions=" + string.Join(",", state.Transitions.Select(t => t.EventName + ":" + t.ToState)));
        }
        if (forcedSummons < 4 && Time.time >= nextForcedSummon && (Latest?.Summons.Length ?? 0) == 0 && EncounterProfile.Selected.Family == EncounterProfile.Vengefly && attackFsm?.ActiveStateName == "Idle"
            && YiGodhome.Bridge.SessionConfiguration.Current.Has("cheat-probe"))
        { forcedSummons++; nextForcedSummon = Time.time + 8; attackFsm.SetState("Summon Antic"); log?.Invoke("HK_SUMMON_FIXTURE nativeSummonState=True"); }
        if (kickRecoveryUntil > 0 && Time.time >= kickRecoveryUntil)
        {
            EndKickStun();
        }
        pureCombat?.Update();
        bool bodyStateChanged = false;
        for (int i = 0; i < fsms.Length; i++) {
            string name = fsms[i].FsmName, state = fsms[i].ActiveStateName;
            if (bodyStateNames[i] != name || bodyStateValues[i] != state) bodyStateChanged = true;
            bodyStateNames[i] = name; bodyStateValues[i] = state;
        }
        if(xero && SessionConfiguration.Current.RunFixtures && SessionConfiguration.Current.Has("trace-probe") && Time.frameCount%120==0)
            log?.Invoke("HK_XERO_OWNER_STATE states="+string.Join(";",fsms.Select(f=>f.FsmName+":"+f.ActiveStateName+":"+f.enabled))+" timeScale="+Time.timeScale+" frame="+Time.frameCount);
        targetHealth!.Observe(Math.Max(0, boss.hp));
        bool attacking = pure ? pureCombat!.Descending : !xero && (gruz ? IsBodyAttack(attackFsm!.ActiveStateName) : mawlek ? JumpAttack(attackFsm!.ActiveStateName) : SwoopAttack(attackFsm!.ActiveStateName));
        bool crimson = false;
        bool bounce = gruz && IsBounce(attackFsm!.ActiveStateName);
        bool kickable = bounce && attacking || EncounterProfile.Selected.Family == EncounterProfile.Vengefly && kickRecoveryUntil == 0
            && (attackFsm!.ActiveStateName == "Swoop L" || attackFsm.ActiveStateName == "Swoop R");
        if (crimson && !wasCrimson) attackId++;
        if ((gruz ? bounce : kickable) && !wasKickable) attackId++;
        wasKickable = gruz ? bounce : kickable;
        attacking &= kickRecoveryUntil == 0;
        // A counter ends the entire swoop, including its retreat, not just the stun.
        attacking &= !(EncounterProfile.Selected.Family == EncounterProfile.Vengefly && counteredAttack > 0
            && counteredAttack == attackId && SwoopAttack(attackFsm!.ActiveStateName));
        wasCrimson = crimson;
        if (bodyStateChanged) log?.Invoke("HK_BODY_STATE " + string.Join(";", fsms.Select(f => f.FsmName + ":" + f.ActiveStateName)) + " attackWindow=" + attacking);
        if (collider == null || damage == null || damage.hazardType != 1 || collider.edgeRadius != 0
            || Math.Abs(Mathf.DeltaAngle(collider.transform.eulerAngles.z, 0)) > 0.01f)
            throw new InvalidOperationException("Unsupported host contact geometry or hazard semantics.");
        // Transform authored corners even while disabled (Unity returns empty bounds then).
        Vector3 center = collider.transform.TransformPoint(collider.offset);
        Vector3 scale = collider.transform.lossyScale;
        if (hurt == null || hurt.edgeRadius != 0 || Math.Abs(Mathf.DeltaAngle(hurt.transform.eulerAngles.z, 0)) > 0.01f)
            throw new InvalidOperationException("Unsupported host health collider.");
        Vector3 hurtCenter = hurt.transform.TransformPoint(hurt.offset), hurtScale = hurt.transform.lossyScale;
        var hurtBounds = new Bounds(hurtCenter, new Vector3(Math.Abs(hurt.size.x * hurtScale.x), Math.Abs(hurt.size.y * hurtScale.y), 0));
        bool hurtEnabled = hurt.enabled && hurt.gameObject.activeInHierarchy;
        hurtEnabled &= !boss.isDead && boss.hp > 0;
        if(EncounterProfile.Selected.Family == EncounterProfile.Vengefly) hurtEnabled &= !boss.IsInvincible;
        if (pure) {
            hurtEnabled = false;
            foreach (var c in hurtShapes) if (c != null && c.enabled && c.gameObject.activeInHierarchy) {
                if (!hurtEnabled) hurtBounds = c.bounds; else hurtBounds.Encapsulate(c.bounds);
                hurtEnabled = true;
            }
            // Native counter stance receives blocked nail hits on the body,
            // even while ordinary damage proxies are disabled.
            if (attackFsm?.ActiveStateName == "Counter Stance" && !hurtEnabled) hurtEnabled = hurt.enabled && hurt.gameObject.activeInHierarchy;
        }
        hurtEnabled &= !boss.isDead && boss.hp > 0;
        var attackVolumes = (volumes ??= new HostAttackVolumes(log)).Capture(!boss.isDead, pureCombat == null ? null : pureCombat.Decorate, attacking);
        bool pureKickable = pureCombat?.Kickable == true && attackVolumes.Any(v => v.Active && (v.Traits & YiGodhome.Bridge.AttackTraits.Kickable) != 0);
        float width = Math.Abs(collider.size.x * scale.x), height = Math.Abs(collider.size.y * scale.y);
        if (pureCombat?.Descending == true) {
            var body = new Bounds(center, new Vector3(width, height, 0));
            foreach (var c in hurtShapes) if (c != null && c.enabled && c.gameObject.activeInHierarchy) body.Encapsulate(c.bounds);
            center = body.center; width = body.size.x; height = body.size.y;
            if (!wasDescending) log?.Invoke("HK_PV_SLAM_BODY encounter=" + encounter + " frame=" + Time.frameCount + " width=" + width + " height=" + height + " damaging=" + (damage.isActiveAndEnabled && collider.enabled) + " nativeRoot=" + collider.size);
        }
        wasDescending = pureCombat?.Descending == true;
        var frame = new ContactFrame { Session = session, Encounter = encounter, GeometryFrame = geometryFrame, NativeFrame = Time.frameCount,
            CaptureTick = Stopwatch.GetTimestamp(), Entity = targetHealth.Entity, TargetGeneration = targetHealth.Activation, Collider = collider.GetInstanceID(), Health = targetHealth.Health, MaxHealth = targetHealth.Maximum, Damage = damage.damageDealt,
            Difficulty = BossSceneController.Instance != null ? BossSceneController.Instance.BossLevel : 0,
            Notches = notches?.Capture(targetHealth!.Maximum) ?? Array.Empty<int>(),
            AttackId = pure ? pureCombat!.ActionId : crimson || kickable || bounce ? attackId : 0, Crimson = pure ? pureCombat!.Crimson : crimson, KickWindup = pureCombat?.KickWindup == true || bounce && !attacking, Windup = pure && pureCombat!.Windup, Kickable = pure ? pureKickable : kickable && !boss.isDead && damage.isActiveAndEnabled && collider.enabled && collider.gameObject.activeInHierarchy,
            // Mawlek's native body is exported once as contact below. A second
            // primary jump dealer would bypass that body's one-second retirement.
            Active = !mawlek && attacking && !boss.isDead && damage.isActiveAndEnabled && collider.enabled && collider.gameObject.activeInHierarchy,
            X = center.x, Y = center.y, Width = width, Height = height,
            HurtCollider = hurt.GetInstanceID(), HurtActive = !boss.isDead && hurtEnabled,
            HurtX = hurtBounds.center.x, HurtY = hurtBounds.center.y, HurtWidth = hurtBounds.size.x, HurtHeight = hurtBounds.size.y,
            Summons = EncounterProfile.Selected.Family == EncounterProfile.Vengefly
                ? (summons ??= new HostSummons(EncounterProfile.Selected.Scene, d => d.name == "Buzzer(Clone)" || d.name == "Buzzer", log)).Capture(!boss.isDead) : Array.Empty<SummonedTarget>(),
            Volumes = attackVolumes };
        if (frame.AttackId > 0 && (frame.Crimson || frame.Kickable || frame.KickWindup)) {
            frame.CrimsonSequence = frame.AttackId; frame.CrimsonCharge = 1;
        }
        frame.BossBarsReady=NativeBossTitles.ReadyFor(frame.BossTitle);
        return Latest = frame;
    }
    internal static bool IsBounce(string state) => state == "Slam Antic" || state == "Check Direction" || state == "Go Left" || state == "Go Right"
        || state == "Launch Up" || state == "Launch Down" || state == "Flying" || state == "Turn Left" || state == "Turn Right" || state == "Slam Up" || state == "Slam Down";
    private static bool SwoopAttack(string state) => state == "Swoop L" || state == "Swoop R" || state == "Swoop Rise";
    private void EndKickStun()
    {
        if (kickRecoveryUntil <= 0) return;
        kickRecoveryUntil = 0;
        if (attackFsm == null) return;
        attackFsm.enabled = true;
        if (boss == null || boss.isDead || boss.hp <= 0 || !boss.gameObject.activeInHierarchy) return;
        attackFsm.SetState("Swoop Rise");
        // Rise has movement actions only, so it otherwise retains the interrupted
        // swoop clip. Use this encounter's own ordinary flying clip during retreat.
        var flight = attackFsm.FsmStates.Single(s => s.Name == "Idle").Actions
            .OfType<HutongGames.PlayMaker.Actions.Tk2dPlayAnimation>().Single().clipName.Value;
        boss.GetComponent<tk2dSpriteAnimator>().Play(flight);
        log?.Invoke("HK_TAICHI_RETREAT_ART clip=" + flight + " source=Idle");
        log?.Invoke("HK_TAICHI_RECOVERED attack=" + attackId + " state=" + attackFsm.ActiveStateName);
    }
    private static bool JumpAttack(string state) => state == "Jump" || state == "In Air" || state == "Land" || state == "Jump 2" || state == "In Air 2" || state == "Land 2";
    public bool ResolveProjectile(int id, long activation, bool reflected = false) => roster != null ? roster.ResolveProjectile(id, activation, reflected) : targetHealth != null && volumes?.Resolve(id, activation, reflected, boss, targetHealth) == true;
    public void Dispose() { EndKickStun(); pureCombat?.Dispose(); volumes?.Dispose(); summons?.Dispose(); roster?.Dispose(); }
    public bool InterruptSwoop(long id)
    {
        if (roster != null) return roster.Counter(id, HitKind.TaiChiKick);
        if (pureCombat != null) return pureCombat.Interrupt(id, YiGodhome.Bridge.HitKind.TaiChiKick);
        if (EncounterProfile.Selected.Family == EncounterProfile.Gruz) return Latest?.Kickable == true && EndBounce(id);
        if (id <= 0 || id != attackId || id == counteredAttack || Latest?.Kickable != true || boss == null || boss.isDead || attackFsm == null) return false;
        if (attackFsm.ActiveStateName != "Swoop L" && attackFsm.ActiveStateName != "Swoop R") return false;
        counteredAttack = id;
        iTween.Stop(boss.gameObject);
        attackFsm.enabled = false;
        boss.GetComponent<Rigidbody2D>().linearVelocity = Vector2.zero;
        kickRecoveryUntil = Time.time + 0.65f;
        log?.Invoke("HK_TAICHI_STUN attack=" + id + " seconds=0.65 nativeRise=True");
        return true;
    }
    public bool InterruptBounce(long id)
    {
        if (roster != null) return roster.Counter(id, HitKind.UnboundedCounter);
        if (pureCombat != null) return pureCombat.Interrupt(id, YiGodhome.Bridge.HitKind.UnboundedCounter);
        return EncounterProfile.Selected.Family == EncounterProfile.Gruz && Latest?.Kickable == true && EndBounce(id);
    }
    internal bool ParriedStrike(long action,HitKind kind)=>roster?.Counter(action,kind)==true;
    internal HealthManager? ParriedMinion(long action)=>roster?.ParriedMinion(action);
    private bool EndBounce(long id)
    {
        if (id <= 0 || id != attackId || id == counteredAttack || boss == null || boss.isDead || attackFsm == null
            || !IsBounce(attackFsm.ActiveStateName) || !IsBodyAttack(attackFsm.ActiveStateName)) return false;
        var recovery = attackFsm.FsmStates.Single(s => s.Name == "Slam End");
        if (!recovery.Transitions.Any(t => t.EventName == "FINISHED" && t.ToState == "Super End")) throw new InvalidOperationException("Native bounce recovery changed.");
        string before = attackFsm.ActiveStateName;
        counteredAttack = id;
        attackFsm.SetState("Slam End");
        log?.Invoke("HK_TAICHI_BOUNCE_INTERRUPTED attack=" + id + " from=" + before + " to=" + attackFsm.ActiveStateName);
        return true;
    }
    internal static bool IsBodyAttack(string state)
    {
        // Audited Attuned Gruz Mother: the existing body collider is the attack
        // during charging and slamming. Hover, windup and recovery are harmless.
        switch (state) {
            case "Charge": case "Launch Up": case "Launch Down": case "Flying":
            case "Turn Left": case "Turn Right": case "Slam Up": case "Slam Down": return true;
            case "Init": case "Sleep": case "Wake": case "Fly": case "Buzz": case "Super Choose":
            case "Charge Antic": case "Charge Recover L": case "Charge Recover R": case "Charge Recover U": case "Charge Recover D":
            case "Super End": case "Recover End": case "Slam Antic": case "Check Direction": case "Go Left": case "Go Right":
            case "Slam End": case "Invincible": case "GG Boss Wake": case "Wake Sound": case "GG Extra Pause": return false;
            default: throw new InvalidOperationException("Unaudited Gruz Mother attack state: " + state);
        }
    }
}
