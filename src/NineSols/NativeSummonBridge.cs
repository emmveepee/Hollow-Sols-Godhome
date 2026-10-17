using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YiGodhome.Bridge;

namespace YiGodhome;

internal sealed class NativeSummonBridge : IDisposable
{
    private sealed class Entry : IDisposable
    {
        public long Generation;
        public int OwnerId;
        public int Entity;
        public Guid Encounter;
        public bool BossBar;
        public bool HideHealthBar;
        public int HostHealth;
        public NativeMinionOwner Native = null!;
        public NativeTargetProbe Target = null!;
        public NativeAttackVolumes Attacks = null!;
        public NativeSummonHealth? Hud;
        public NativeBossHealth? BossHud;
        public void Dispose() { Hud?.Dispose(); BossHud?.Dispose(); Attacks?.Dispose(); Target?.Dispose(); }
    }
    private readonly ReceiverPool<NativeMinionOwner> pool;
    private readonly MonsterBase template;
    private readonly GeometryLandingProbe geometry;
    private readonly Action<string> log;
    private readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>();
    private readonly Dictionary<HitOutcome, Entry> emitted = new Dictionary<HitOutcome, Entry>();
    private readonly PendingTargetAcks<Entry> pending = new PendingTargetAcks<Entry>();
    private bool disposed;
    private readonly NativeDepositArtwork depositArtwork=new();
    private readonly HashSet<int> talismanVisualEvidence=new();
    public ContactFrame? CheatTarget { get; private set; }
    public IEnumerable<Renderer> Renderers {
        get {
            foreach (var entry in entries.Values) foreach (var renderer in entry.Hud?.Renderers() ?? Array.Empty<Renderer>()) yield return renderer;
            foreach(var entry in entries.Values) foreach(var attachment in entry.Native.Owner.GetComponentsInChildren<FooAttachable>(true)) {
                var deposit=attachment.bindingFoo;
                if(deposit==null || !deposit.HasFoo()) continue;
                var renderers=depositArtwork.Get(deposit);
                if(SessionConfiguration.Current.RunFixtures && talismanVisualEvidence.Count<32 && talismanVisualEvidence.Add(deposit.GetInstanceID()))
                    log("NS_TALISMAN_DEPOSIT_ART owner="+entry.OwnerId+" deposit="+deposit.GetInstanceID()+" nativeAnimator="+(deposit.GetComponentInChildren<Animator>(true)!=null)+" renderers="+string.Join(",",renderers.Select(r=>r.name))+" directNativeArt=True");
                foreach(var renderer in renderers) yield return renderer;
            }
            var sources = entries.Values.SelectMany(e => e.Native.Owner.GetComponentsInChildren<FooAttachable>(true)).Select(f => f.fooFXPlayer).Where(f => f != null).Distinct().ToArray();
            var seen = new HashSet<PoolObject>();
            for (int depth = 0; depth < 3 && sources.Length > 0; depth++) {
                var next = new List<FxPlayer>();
                foreach (var source in sources) foreach (var obj in source.AllObjects) {
                    if (obj == null || !obj.isOnScene || !obj.gameObject.activeInHierarchy || obj.lastPlayerComponent != source || !seen.Add(obj)) continue;
                    if (seen.Count > 128) throw new InvalidOperationException("Attached talisman effect closure exceeded its owner budget.");
                    foreach (var renderer in obj.GetComponentsInChildren<Renderer>(true)) yield return renderer;
                    next.AddRange(obj.GetComponentsInChildren<FxPlayer>(true));
                }
                sources = next.Distinct().ToArray();
            }
        }
    }
    public NativeSummonBridge(MonsterBase template, GeometryLandingProbe geometry, Action<string> log)
    { this.template = template; this.geometry = geometry; this.log = log;
      pool = new ReceiverPool<NativeMinionOwner>(SummonedTargets.Maximum * 2, () => new NativeMinionOwner(template, log), n => n.Reset(), n => n.VerifyReset()); }
    public void Update(ContactFrame? parent)
    {
        CheatTarget = null;
        if (parent == null) { foreach (var e in entries.Values) { e.Target.Update(null); e.Attacks.Update(null); } return; }
        foreach (var id in entries.Keys.Where(id => !parent.Summons.Any(t => t.Entity == id && t.Generation == entries[id].Generation)).ToArray())
        {
            var retired = entries[id]; retired.Dispose(); pool.Retire(retired.Native, Time.realtimeSinceStartup); entries.Remove(id);
            pending.Retire(retired);
            foreach (var outcome in emitted.Where(p => p.Value == retired).Select(p => p.Key).ToArray()) emitted.Remove(outcome);
            log("NS_SUMMON_RETIRED encounter=" + parent.Encounter + " entity=" + id + " generation=" + retired.Generation + " retained=" + pool.Retained + " quarantined=" + pool.Quarantined);
        }
        var ordered = SessionConfiguration.Current.RunFixtures && SessionConfiguration.Current.Has("kick-probe")
            ? parent.Summons.OrderByDescending(t => t.Kickable).ToArray() : parent.Summons;
        foreach (var target in ordered)
        {
            bool born=false;
            if (!entries.TryGetValue(target.Entity, out var e))
            {
                var native = pool.Acquire(Time.realtimeSinceStartup, out bool reused);
                if (reused) log("NS_SUMMON_OWNER_REUSED owner=" + native.Owner.GetInstanceID() + " resetVerified=True");
                e = new Entry { Native = native, OwnerId = native.Owner.GetInstanceID(), Generation = target.Generation, BossBar = target.BossBar, HideHealthBar = target.HideHealthBar };
                try {
                    native.Classify(target.BossBar || target.SharedHealth, log);
                    e.Target = new NativeTargetProbe(native.Owner, geometry, log, true, true);
                    e.Attacks = new NativeAttackVolumes(Player.i, native.Owner, geometry, log, projectileBlockers: false);
                    if (target.BossBar) {
                        e.BossHud = new NativeBossHealth(native.Owner, log);
                        if (target.SharedHealth)
                            e.BossHud.PresentationInjury = () => entries.Values.Where(other => other.HostHealth > 0)
                                .Sum(other => other.Native.Owner.postureSystem.InternalInjury);
                    }
                    else if (!target.SharedHealth && !target.HideHealthBar) e.Hud = new NativeSummonHealth(native.Owner, log);
                    entries.Add(target.Entity, e);
                    born=true;
                } catch { e.Dispose(); native.Dispose(); throw; }
                log("NS_SUMMON_BOUND entity=" + target.Entity + " generation=" + target.Generation + " owner=" + native.Owner.GetInstanceID()
                    + " encounter=" + parent.Encounter + " frame=" + parent.NativeFrame + " damage=" + target.Damage + " tier=" + parent.Difficulty
                    + " retained=" + pool.Retained + " health=" + target.Health + " maxHealth=" + target.MaxHealth + " hiddenHealthBar=" + target.HideHealthBar + " nativeHealthWidget=" + (e.Hud != null));
            }
            if (e.BossBar != target.BossBar || !e.BossBar && e.HideHealthBar != target.HideHealthBar) throw new InvalidOperationException("Target presentation changed within a native lease.");
            var frame = target.Project(parent);
            e.HostHealth = target.Health;e.Entity=target.Entity;e.Encounter=parent.Encounter;
            var pos = geometry.Map.ToTarget(new YiGodhome.Geometry.Point(target.X, target.Y));
            e.Native.Owner.transform.position = new Vector3(pos.X, pos.Y, e.Native.Owner.transform.position.z);
            frame.Volumes = new[] { target.Attack() };
            e.Target.Update(frame,blockTalisman:target.PassiveRepel); e.Attacks.Update(frame); e.Hud?.Update(frame); e.BossHud?.Update(frame, geometry.SourceScene,target.HideHealthBar);
            if(born && EncounterProfile.ForScene(geometry.SourceScene)?.Family==EncounterProfile.Collector)e.Target.CheckNativeBirthMelee();
            if ((frame.HurtActive || SessionConfiguration.Current.RunFixtures && SessionConfiguration.Current.Has("kick-probe") && frame.Kickable) && CheatTarget == null && (EncounterProfile.ForScene(geometry.SourceScene)?.Key != "Uumuu" || !target.BossBar || !target.PassiveRepel)) {
                CheatTarget = target.Project(parent);
                CheatTarget.Volumes = EncounterProfile.ForScene(geometry.SourceScene)?.Roster == true ? parent.Volumes : frame.Volumes;
            }
        }
    }
    public bool TryGetOutcome(out HitOutcome outcome)
    {
        foreach (var e in entries.Values) if (e.Target.TryGetOutcome(out outcome)) { emitted.Add(outcome, e); return true; }
        outcome = null!; return false;
    }
    public void Submitted(long sequence, HitOutcome outcome)
    { if (emitted.TryGetValue(outcome, out var e)) { emitted.Remove(outcome); pending.Add(sequence, e, e.OwnerId, e.Generation); e.Target.Submitted(sequence, outcome); } }
    public bool Acknowledged(HitAck ack)
    { if (pending.TryTake(ack.Sequence, out var settled)) {
        if(ack.TargetKilled && ack.Disposition==HitDisposition.Applied && settled.LiveTarget is Entry killed)
            NativeHostContact.RetireContactOwner(killed.Entity,settled.Activation,killed.Encounter);
        settled.LiveTarget?.Target.Acknowledged(ack);
        log("NS_SUMMON_SETTLED sequence=" + ack.Sequence + " generation=" + settled.Activation + " owner=" + settled.Owner + " result=" + ack.Disposition + " health=" + ack.Health + " retired=" + (settled.LiveTarget == null));
        if (disposed && pending.Count == 0) log("NS_SUMMON_SETTLEMENT_DRAINED pendingMetadata=0");
        return true;
    } return false; }
    public void Dispose() { if (disposed) return; disposed = true; foreach (var e in entries.Values) { pending.Retire(e); e.Dispose(); } pool.Dispose(); depositArtwork.Clear(); entries.Clear(); emitted.Clear(); log("NS_SUMMON_DISPOSE retained=" + NativeMinionOwner.Retained + " active=0 pending=0 settlementMetadata=" + pending.Count); }
}
