using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using YiGodhome.Bridge;

static class Program
{
    static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Reject(Action action) { try { action(); } catch (InvalidOperationException) { return; } catch (InvalidDataException) { return; } throw new Exception("Invalid operation accepted"); }
    static EncounterStatus State(Guid id, EncounterPhase phase) => new EncounterStatus(id, 10, phase);
    static void Main()
    {
        DetailedCaptureChecks.Run();
        RetiredAcknowledgements(); RosterTargets(); ReflectedHealth(); EarlyReturn(); SequenceHandoff(); Lifetimes(); Configuration(); Pools(); Summons(); Logs();
        Console.WriteLine("PASS: lifecycle deadlines/lost outcomes, immutable capabilities, receiver quarantine/capacity/reset, summon projection and target identities.");
    }
    static void SequenceHandoff()
    {
        var session = Guid.NewGuid(); var id = Guid.NewGuid(); var life = new EncounterLifetime();
        life.Observe(State(id, EncounterPhase.Active), 0);
        life.Observe(State(id, EncounterPhase.Advancing), 1);
        Assert(!ReturnPreparation.MayReset(true, 0, EncounterPhase.Advancing), "Sequence win restored player resources");
        Assert(!ReturnPreparation.MayAdvance(1, session, session, "GG_Gruz_Mother", 11, 10), "Lost acknowledgement permitted advancement");
        Assert(!ReturnPreparation.MayAdvance(0, session, session, "GG_Gruz_Mother", 10, 10), "Old geometry admitted new target");
        Assert(!ReturnPreparation.MayAdvance(0, session, Guid.NewGuid(), "GG_Gruz_Mother", 11, 10), "Foreign session admitted");
        Assert(!ReturnPreparation.MayAdvance(0, session, session, "Town", 11, 10), "Campaign room admitted");
        Assert(ReturnPreparation.MayAdvance(0, session, session, "GG_Spa", 11, 10), "Fresh native interlude rejected");
        Reject(() => life.Observe(State(Guid.NewGuid(), EncounterPhase.Preparing), 2));
        life.Observe(State(id, EncounterPhase.Ready), 3); life.CompleteReturn();
        life.Observe(State(Guid.NewGuid(), EncounterPhase.Preparing), 4);
        var stalled = new EncounterLifetime(); stalled.Observe(State(id, EncounterPhase.Active), 0);
        var paused = new EncounterLifetime(); paused.Observe(State(id, EncounterPhase.Active), 0);
        paused.Observe(State(id, EncounterPhase.Returned), 1);
        Assert(paused.Check(1, false, 0, true) == null, "Return failed before pause");
        Assert(paused.Check(2, false, 0, true, true) == null && paused.Check(202, false, 0, true, true) == null,
            "Player pause consumed native return deadline");
        Assert(paused.Check(203, false, 0, true) == null && paused.Check(232, false, 0, true) == null,
            "Resumed native return lost its remaining deadline");
        Assert(paused.Check(233, false, 0, true) == "return-deadline", "Unpaused return can hang indefinitely");
        var pausedLost = new EncounterLifetime(); pausedLost.Observe(State(id, EncounterPhase.Active), 0);
        Assert(pausedLost.Check(1, true, 1, false, true)!.StartsWith("hit-channel-stopped"), "Pause concealed lost native outcomes");
        stalled.Observe(State(id, EncounterPhase.Advancing), 1);
        Assert(stalled.Check(31, false, 0, true) == "return-deadline", "Stalled handoff has no finite deadline");
        var lost = new EncounterLifetime(); lost.Observe(State(id, EncounterPhase.Active), 0);
        lost.Observe(State(id, EncounterPhase.Advancing), 1);
        Assert(lost.Check(2, true, 1, true)!.StartsWith("hit-channel-stopped"), "Uncertain sequence damage was discarded/replayed");
        Reject(() => new SessionConfiguration(new[] { "pantheon-probe", "unlimited-human" }, "input-hk", 0, pantheon: 1));
        Assert(ReturnPreparation.FreshHall(session, session, "GG_Atrium", 11, 10), "Native Pantheon return rejected");
        Reject(() => new SessionConfiguration(new[] { "pantheon-probe", "scripted-input", "hub-probe", "hub-combat", "travel-probe" }, "input-hk", 100, pantheon: 1));
        var premature = new EncounterLifetime(); premature.Observe(State(id, EncounterPhase.Active), 0);
        Reject(() => premature.Observe(State(id, EncounterPhase.SequenceResults), 1));
        var results = new EncounterLifetime(); results.Observe(State(id, EncounterPhase.Active), 0);
        results.Observe(State(id, EncounterPhase.Victory), 1); results.Observe(State(id, EncounterPhase.SequenceResults), 10);
        Assert(results.Check(1000, false, 0, true) == null, "Authored human results wait timed out");
        results.Observe(State(id, EncounterPhase.Returned), 1001);
        Assert(results.Check(1031, false, 0, true) == "return-deadline", "Post-results restoration lost its deadline");
    }
    static void RetiredAcknowledgements()
    {
        var pending = new PendingTargetAcks<object>();
        var firstLease = new object(); var reusedLease = new object();
        pending.Add(1, firstLease, -25, 1);
        pending.Retire(firstLease);
        pending.Add(2, reusedLease, -25, 2); // Same native object, independent activation.
        Assert(pending.TryTake(2, out var live) && ReferenceEquals(live.LiveTarget, reusedLease) && live.Activation == 2,
            "Current owner's acknowledgement lost its lease");
        Assert(pending.TryTake(1, out var old) && old.LiveTarget == null && old.Owner == -25 && old.Activation == 1,
            "Late retired acknowledgement acquired the reused owner");
        Assert(!pending.TryTake(1, out _) && pending.Count == 0, "Duplicate acknowledgement settled twice");
        for (int i = 1; i <= PendingTargetAcks<object>.Maximum; i++) pending.Add(i, firstLease, -25, 3);
        Reject(() => pending.Add(65, reusedLease, -25, 4));
        Reject(() => pending.Add(1, firstLease, -25, 3));
        pending.Retire(firstLease);
        Assert(pending.Count == 64, "Retirement silently discarded uncertain acknowledgements");
        for (int i = 64; i >= 1; i--) Assert(pending.TryTake(i, out var retired) && retired.LiveTarget == null, "Retired capacity retained a live owner");
        Assert(pending.Count == 0, "Retired acknowledgements did not drain");
        Console.WriteLine("PASS: retired acknowledgements after owner reuse, independent activation, duplicate rejection and bounded metadata drain.");
    }
    static void RosterTargets()
    {
        var parent = new ContactFrame { Session = Guid.NewGuid(), Encounter = Guid.NewGuid(), GeometryFrame = 10,
            NativeFrame = 1, Sequence = 1, CaptureTick = 1, SentTick = 1, Entity = 999, TargetGeneration = 1,
            Collider = 999, HurtCollider = 999, Health = 0, MaxHealth = 1, Width = 1, Height = 1, HurtWidth = 1, HurtHeight = 1,
            Summons = new[] { new SummonedTarget { Entity = 10, Collider = 11, Generation = 1, Health = 100, MaxHealth = 200,
                Active = false, Width = 2, Height = 3 }, new SummonedTarget { Entity = 20, Collider = 21, Generation = 1,
                Health = 60, MaxHealth = 100, Active = true, Width = 2, Height = 3 } } };
        using var bytes = new MemoryStream(); ContactCodec.Write(bytes, parent); bytes.Position = 0;
        var read = ContactCodec.Read(bytes);
        Assert(read.Health == 0 && read.Summons.Length == 2 && read.Summons[0].Health == 100,
            "Inactive encounter anchor lost independent living targets");
        var hidden = read.Summons[0].Project(read); var visible = read.Summons[1].Project(read);
        Assert(!hidden.HurtActive && hidden.TargetGeneration == 1 && hidden.Health == 100 && visible.HurtActive,
            "Teleport/invulnerability lost target health or admitted a hidden receiver");
        Assert(!new TargetIdentity(hidden).Equals(new TargetIdentity(visible)), "Independent roster receivers share identity");
        Assert(visible.HurtActive && !read.Summons[1].Attack().Active, "Zero-damage body admitted a native contact/parry attack");
        // Cold initialization is outside the warm-path budget. This specifically
        // catches reintroducing a per-read predicate/closure in projectile work.
        var familyProfiles = new[] { EncounterProfile.NightmareGrimm, EncounterProfile.ForScene("GG_Ghost_Markoth_V")!, EncounterProfile.FalseKnightAudit };
        foreach (var profile in familyProfiles) { var warm = profile.Family; }
        long familyBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 8192; i++) foreach (var profile in familyProfiles) {
            var expected = profile == EncounterProfile.FalseKnightAudit ? profile : profile.Key == "Markoth" ? EncounterProfile.Markoth : EncounterProfile.NightmareGrimm;
            if (profile.Family != expected) throw new Exception("Boss family identity changed.");
        }
        Assert(GC.GetAllocatedBytesForCurrentThread() == familyBefore, "Warm boss-family reads allocate managed garbage");
        foreach (var p in new[] { EncounterProfile.Mantis, EncounterProfile.Sisters, EncounterProfile.Grimm, EncounterProfile.NightmareGrimm, EncounterProfile.Sly, EncounterProfile.Watchers })
            Assert(EncounterProfile.ForScene(p.Scene) == p && p.Roster, "Requested native scene is not selectable");
        Assert(!EncounterProfile.Watchers.IndividualBossBars && EncounterProfile.Mantis.IndividualBossBars,
            "Watcher presentation acquired global boss bars");
        foreach (var p in new[] { EncounterProfile.Markoth, EncounterProfile.Gruz, EncounterProfile.Uumuu, EncounterProfile.Nosk, EncounterProfile.Collector }) {
            var variant = EncounterProfile.ForScene(p.Scene + "_V");
            Assert(variant != null && variant.Family == p && variant.Scene != p.Scene && variant.OwnNativePools,
                "Alternate arena lost shared boss rules or exact pool ownership");
        }
    }
    static void ReflectedHealth()
    {
        var body = new TargetHealthState(101, 7, 1600); body.Observe(4);
        Assert(!body.ObserveNative(102, 7, -17) && body.Health == 4, "Wrong reflected body changed health");
        Assert(!body.ObserveNative(101, 6, -17) && body.Health == 4, "Retired reflected activation changed health");
        Assert(body.ObserveNative(101, 7, -17) && body.Health == 0 && body.Maximum == 1600, "Lethal native reflection did not update snapshot");
        var prior = new ContactFrame { Session = Guid.NewGuid(), Encounter = Guid.NewGuid(), Entity = 101, TargetGeneration = 7,
            Active = true, HurtActive = true, Health = 4, Volumes = new[] { new AttackVolume { Active = true } } };
        var removed = body.RemovedSnapshot(prior, 123, 456);
        Assert(removed.Encounter == prior.Encounter && removed.Health == 0 && !removed.Active && !removed.HurtActive && removed.Volumes.Length == 0, "Reflection removal retained combat or inferred another encounter");
        var replacement = new TargetHealthState(101, 8, 1600);
        Assert(!replacement.ObserveNative(101, 7, -17) && replacement.Health == 1600, "Old projectile killed replacement body");
        Console.WriteLine("PASS: lethal reflected health clamps before removal, stale identities refused and replacement health independent.");
    }
    static void EarlyReturn()
    {
        var session = Guid.NewGuid();
        Assert(!ReturnPreparation.MayReset(true, 0, EncounterPhase.Victory), "Victory presentation reset Yi before native arena departure");
        Assert(ReturnPreparation.MayReset(true, 0, EncounterPhase.Returned), "Actual native return did not permit drained restoration");
        Assert(!ReturnPreparation.MayReset(true, 1, EncounterPhase.Victory), "Pending outcome owner reset early");
        Assert(ReturnPreparation.MayReset(true, 0, EncounterPhase.Defeated) && !ReturnPreparation.MayReset(true, 1, EncounterPhase.Defeated), "Explicit alive cancellation bypassed outcome drain");
        Assert(!ReturnPreparation.MayReset(true, 0, EncounterPhase.Active), "Active fight admitted return reset");
        Assert(!ReturnPreparation.MayReset(false, 0, EncounterPhase.Returned), "Duplicate return reset");
        Assert(ReturnPreparation.FreshHall(session, session, "GG_Workshop", 11, 10), "Fresh Hall not admitted for early lying presentation");
        Assert(!ReturnPreparation.FreshHall(session, session, "GG_Hollow_Knight", 11, 10), "Old arena admitted after warm reset");
        Assert(!ReturnPreparation.FreshHall(session, session, "GG_Workshop", 10, 10), "Stale return geometry admitted");
        Assert(!ReturnPreparation.FreshHall(session, Guid.NewGuid(), "GG_Workshop", 11, 10), "Foreign return session admitted");
        Console.WriteLine("PASS: early return restoration drains outcomes and imports only fresh same-session Hall geometry.");
    }
    static void Logs()
    {
        using(var blocked=new System.Threading.ManualResetEventSlim())
        using(var entered=new System.Threading.ManualResetEventSlim()) {
            var lines=new System.Collections.Generic.List<string>();
            bool closed=false;
            var writer=new AsyncLogWriter(line=>{if(line=="first"){entered.Set();blocked.Wait();}lines.Add(line);},()=>closed=true,256);
            writer.WriteLine("first");Assert(entered.Wait(2000),"File worker did not start");
            var clock=System.Diagnostics.Stopwatch.StartNew();
            for(int i=0;i<10000;i++)writer.WriteLine("bounded-evidence-"+i);
            Assert(clock.ElapsedMilliseconds<1000 && writer.QueuedBytes<=256,"Blocked file IO stalled producer or exceeded byte budget");
            blocked.Set();writer.Dispose();
            Assert(closed && lines[0]=="first" && lines.Exists(l=>l.Contains("LOG_QUEUE_OVERFLOW")),"Worker lost order/overflow evidence or failed to drain/close");
        }
        Console.WriteLine("PASS: blocked log sink leaves producers responsive, bounds queued memory, reports dropped records and drains in order on shutdown.");
        string directory = Path.Combine(".local", "foundation-checks", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "plugin.log");
        using (var log = new BoundedEventLog(path, 1024)) for (int i = 0; i < 1000; i++) log.WriteLine(i.ToString("D4") + " " + new string('x', 90));
        long bytes = 0; foreach (var file in Directory.GetFiles(directory)) { long length = new FileInfo(file).Length; Assert(length <= 1024, "Log segment overflow"); bytes += length; }
        Assert(bytes <= 4096 && File.ReadAllText(path).Contains("0999") && File.ReadAllText(path + ".startup").StartsWith("0000"), "Log rotation lost startup/tail or is unbounded");
        string retained=Path.Combine(directory,"retained.log");
        using(var log=new BoundedEventLog(retained,1024,retainPerformance:true)) {
            log.WriteLine("2026-10-14T00:00:00Z FRAME_PERF scene=test frame=1");
            for(int i=0;i<1000;i++)log.WriteLine("ordinary "+i+new string('x',90));
        }
        Assert(File.ReadAllText(retained+".perf").Contains("frame=1") && !File.ReadAllText(retained+".perf").Contains("ordinary"),"Sparse performance evidence lost during general log rotation");

    }
    static void Lifetimes()
    {
        foreach (var ending in new[] { EncounterPhase.Victory, EncounterPhase.Defeated, EncounterPhase.Restoring }) {
            var id = Guid.NewGuid(); var life = new EncounterLifetime();
            life.Observe(State(id, EncounterPhase.Preparing), 0);
            Assert(life.Check(59, false, 0, false) == null, "Preparation expired early");
            life.Observe(State(id, EncounterPhase.Active), 59);
            Assert(life.Check(100000, false, 0, false) == null, "Active gameplay was time limited");
            life.Observe(State(id, ending), 100000);
            Assert(life.Check(100001, false, 1, true) == null, "Delayed acknowledgement failed early");
            Assert(life.Check(100002, true, 1, true) != null, "Lost ack hung after terminal combat");
            Assert(life.Check(100003, false, 0, true) != null, "Terminal failure resurrected");
        }
        var encounter = Guid.NewGuid(); var timed = new EncounterLifetime();
        timed.Observe(State(encounter, EncounterPhase.Preparing), 0);
        Assert(timed.Check(60, false, 0, false) == "preparation-deadline", "Preparation can hang");
        var returns = new EncounterLifetime(); returns.Observe(State(encounter, EncounterPhase.Active), 0);
        returns.Observe(State(encounter, EncounterPhase.Defeated), 1);
        returns.Observe(State(encounter, EncounterPhase.Restoring), 20);
        Assert(returns.Check(31, false, 0, true) == "return-deadline", "Phase change extended return deadline");
        var repeat = new EncounterLifetime(); repeat.Observe(State(encounter, EncounterPhase.Active), 0);
        Reject(() => repeat.Observe(State(Guid.NewGuid(), EncounterPhase.Preparing), 1));
        repeat.Observe(State(encounter, EncounterPhase.Victory), 2);
        Assert(repeat.Check(3, true, 0, true) == null, "Clean drained teardown rejected");
        Reject(() => repeat.CompleteReturn());
        repeat.Observe(State(encounter, EncounterPhase.Returned), 3);
        repeat.CompleteReturn(); repeat.Observe(State(Guid.NewGuid(), EncounterPhase.Preparing), 4);
        repeat.Cancel(); Assert(repeat.Check(100, true, 1, false) == null, "Cancellation failed");
        var contact = new EncounterLifetime(); contact.Observe(State(encounter, EncounterPhase.Active), 0);
        Assert(contact.CheckContact(10, true) == null && contact.CheckContact(10.5, true) == null, "Independent contact/state pipes require bounded shutdown ordering allowance");
        contact.Observe(State(encounter, EncounterPhase.Victory), 10.6);
        Assert(contact.CheckContact(11, false) == null, "Normal victory contact retirement became failure");
        var orphan = new EncounterLifetime(); orphan.Observe(State(encounter, EncounterPhase.Active), 0);
        orphan.CheckContact(1, true); Assert(orphan.CheckContact(2, true) == "contact-channel-stopped", "Orphaned contact pipe can hang active combat");
    }
    static void Configuration()
    {
        // The old human HubCombat launcher expands EffectsProbe implicitly.
        // Paired startup must keep its complete gameplay/rendering contract.
        var humanGameplay = new HashSet<string>(new[] { "visual", "effects-probe", "proxy", "arena", "hub-probe", "hub-combat", "contact-bridge", "target-probe", "hit-probe", "combat-probe", "await-geometry", "export-geometry", "unlimited-human" });
        foreach (var mode in new[] { "input-hk", "input-ns" }) {
            try {
                SessionConfiguration.EnterPaired(mode);
                var paired = SessionConfiguration.Current;
                Assert(humanGameplay.SetEquals(paired.ToString().Split(',')), "Paired startup lost an existing human gameplay capability");
                Assert(paired.Visual && paired.HubCombat && paired.Has("effects-probe") && !paired.RunFixtures && !paired.Cheats,
                    "Paired gameplay selected body-only capture or acquired automated diagnostics");
            } finally { SessionConfiguration.LeavePaired(); }
        }
        var normal = new[] { "visual", "proxy", "arena", "hub-probe", "hub-combat", "contact-bridge", "target-probe", "hit-probe", "unlimited-human" };
        var config = new SessionConfiguration(normal, "input-hk", 0);
        Assert(config.HubCombat && config.ManualTools && !config.Scripted && !config.Cheats, "Normal gameplay acquired diagnostics");
        var invulnerableFlags = new List<string>(normal) { "invulnerable-probe" };
        Reject(() => new SessionConfiguration(invulnerableFlags, "input-ns", 180));
        invulnerableFlags.Add("scripted-input"); Reject(() => new SessionConfiguration(invulnerableFlags, "input-ns", 180));
        invulnerableFlags.Add("cheat-probe");
        Assert(new SessionConfiguration(invulnerableFlags, "input-ns", 180).Has("invulnerable-probe"), "Bounded victory immunity rejected");
        Reject(() => new SessionConfiguration(invulnerableFlags, "input-ns", 0));
        foreach (string incompatible in new[] { "kick-probe", "crimson-probe", "performance-probe", "stagger-probe" })
            Reject(() => new SessionConfiguration(invulnerableFlags.Concat(new[] { incompatible }), "input-ns", 180));
        Reject(() => new SessionConfiguration(invulnerableFlags.Concat(new[] { "failure-probe" }), "input-ns", 180, "Defeated"));
        var voidFlags = new List<string>(normal) { "void-probe" }; Reject(() => new SessionConfiguration(voidFlags, "input-hk", 0));
        var traceFlags = new List<string>(normal) { "trace-probe" }; Reject(() => new SessionConfiguration(traceFlags, "input-hk", 90));
        traceFlags.Add("scripted-input"); var trace = new SessionConfiguration(traceFlags, "input-hk", 90); Assert(trace.RunFixtures && !trace.Cheats, "Trace acquired cheats or rejected bounded fixture");
        var hudFlags=new List<string>(normal) {"hud-reference-probe"};
        Reject(()=>new SessionConfiguration(hudFlags,"input-ns",90));
        hudFlags.Add("scripted-input");Reject(()=>new SessionConfiguration(hudFlags,"input-ns",90));
        hudFlags.Add("trace-probe");Assert(new SessionConfiguration(hudFlags,"input-ns",90).RunFixtures,"Bounded native HUD reference capture refused");
        Reject(()=>new SessionConfiguration(hudFlags,"input-ns",0));
        Reject(()=>new SessionConfiguration(hudFlags.Concat(new[]{"performance-probe"}),"input-ns",90));
        var staggerFlags = new List<string>(normal) { "stagger-probe" }; Reject(() => new SessionConfiguration(staggerFlags, "input-hk", 0));
        var homingFlags = new List<string>(normal) { "homing-probe" }; Reject(() => new SessionConfiguration(homingFlags, "input-hk", 0));
        var talismanFlags = new List<string>(normal) { "talisman-traversal-probe" };
        Reject(() => new SessionConfiguration(talismanFlags, "input-hk", 200));
        talismanFlags.Add("scripted-input"); Reject(() => new SessionConfiguration(talismanFlags, "input-hk", 200));
        talismanFlags.Add("cheat-probe"); Assert(new SessionConfiguration(talismanFlags, "input-hk", 200).RunFixtures, "Bounded grounded talisman fixture refused");
        Reject(() => new SessionConfiguration(talismanFlags, "input-hk", 0));
        talismanFlags.Add("pantheon-probe"); Reject(() => new SessionConfiguration(talismanFlags, "input-hk", 200, pantheon: 1));
        var arrowFlags = new List<string>(normal) { "markoth-projectile-probe" }; Reject(() => new SessionConfiguration(arrowFlags, "input-hk", 0));
        var shieldFlags = new List<string>(normal) { "markoth-shield-probe" }; Reject(() => new SessionConfiguration(shieldFlags, "input-hk", 0));
        shieldFlags.Add("scripted-input"); Reject(() => new SessionConfiguration(shieldFlags, "input-hk", 200));
        shieldFlags.Add("cheat-probe"); Assert(new SessionConfiguration(shieldFlags, "input-hk", 200).RunFixtures, "Bounded native shield fixture refused");
        var leverFlags = new List<string>(normal) { "lever-probe" }; Reject(() => new SessionConfiguration(leverFlags, "input-hk", 0));
        leverFlags.Add("scripted-input");
        Assert(new SessionConfiguration(leverFlags, "input-hk", 90).RunFixtures, "Bounded native lever fixture refused");
        var travelFlags = new List<string>(normal) { "travel-probe" }; Reject(() => new SessionConfiguration(travelFlags, "input-hk", 0));
        travelFlags.Add("scripted-input");
        Assert(new SessionConfiguration(travelFlags, "input-hk", 180).RunFixtures, "Bounded native travel fixture refused");
        travelFlags.Add("cheat-probe"); Reject(() => new SessionConfiguration(travelFlags, "input-hk", 180));
        var pauseFlags = new List<string>(normal) { "pause-probe" }; Reject(() => new SessionConfiguration(pauseFlags, "input-hk", 0));
        pauseFlags.Add("scripted-input"); Reject(() => new SessionConfiguration(pauseFlags, "input-hk", 180));
        pauseFlags.Add("cheat-probe"); Assert(new SessionConfiguration(pauseFlags, "input-hk", 180).RunFixtures, "Bounded combat pause refused");
        pauseFlags.Add("performance-probe"); Reject(() => new SessionConfiguration(pauseFlags, "input-hk", 180));
        var gameplayIdle = new List<string>(normal) { "scripted-input", "performance-probe" };
        var baseline = new List<string>(gameplayIdle) { "canvas-baseline" };
        Assert(new SessionConfiguration(baseline, "input-ns", 180).Performance, "Bounded canvas comparison refused");
        Reject(() => new SessionConfiguration(new List<string>(normal) { "canvas-baseline" }, "input-ns", 0));
        Reject(() => new SessionConfiguration(new List<string>(normal) { "scripted-input", "canvas-baseline" }, "input-ns", 180));
        Assert(new SessionConfiguration(gameplayIdle, "input-hk", 180).Performance && !new SessionConfiguration(gameplayIdle, "input-hk", 180).RunFixtures,
            "Normal gameplay idle benchmark acquired fixtures");
        gameplayIdle.Add("travel-probe"); Reject(() => new SessionConfiguration(gameplayIdle, "input-hk", 180));
        var flags = new List<string>(normal); flags.Add("cheat-probe"); Reject(() => new SessionConfiguration(flags, "input-hk", 0));
        flags.Add("scripted-input"); Reject(() => new SessionConfiguration(flags, "input-hk", 0));
        var diagnostic = new SessionConfiguration(flags, "input-ns", 180);
        flags.Clear(); Assert(diagnostic.Cheats && diagnostic.Scripted && !diagnostic.ManualTools, "Configuration changed after validation");
        Reject(() => new SessionConfiguration(new[] { "target-probe" }, "input-hk", 180));
        Reject(() => new SessionConfiguration(new[] { "unknown" }, "baseline", 30));
        Assert(new SessionConfiguration(new[] { "engine-perf" }, "baseline", 60).Has("engine-perf"), "Bounded native timing rejected");
        Reject(() => new SessionConfiguration(new[] { "engine-perf" }, "baseline", 0));
        Reject(() => new SessionConfiguration(new[] { "engine-perf" }, "input-hk", 120));
        Reject(() => new SessionConfiguration(new[] { "engine-perf" }, "baseline", 121));
        Assert(new SessionConfiguration(new[] { "engine-perf", "native-perf", "encounter-audit" }, "preview-hk", 120).Has("native-perf"), "Narrow native benchmark rejected");
        Reject(() => new SessionConfiguration(new[] { "engine-perf", "native-perf" }, "preview-hk", 120));
        Reject(() => new SessionConfiguration(new[] { "engine-perf", "native-perf", "encounter-audit", "defeat-probe" }, "preview-hk", 120));
        Reject(() => new SessionConfiguration(normal, "input-hk", 0, "Active"));
        var fault = new List<string>(normal) { "scripted-input", "failure-probe" };
        Assert(new SessionConfiguration(fault, "input-hk", 180, "Returned").FailureBoundary == "Returned", "Guarded fault unavailable");
        Reject(() => new SessionConfiguration(fault, "input-hk", 180, "Ready"));
        foreach (string flag in new[] { "kick-probe", "wall-probe", "rest-probe", "injury-refusal", "retry-probe", "pv-tuning-probe", "reflection-finish-probe" })
            Reject(() => new SessionConfiguration(new[] { flag }, "input-hk", 100));
        foreach (string flag in new[] { "enemy-probe", "contact-probe", "death-probe", "combat-audit", "content-audit", "canvas-service-audit", "encounter-audit", "defeat-probe" }) {
            var polluted = new List<string>(normal) { flag };
            Reject(() => new SessionConfiguration(polluted, "input-hk", 0));
        }
        Assert(new SessionConfiguration(new[] { "enemy-probe", "contact-probe", "death-probe" }, "memory-ns", 100).Has("death-probe"), "Authorized native-only audit blocked");
        Assert(new SessionConfiguration(new[] { "content-audit" }, "memory-ns", 120).Has("content-audit"), "Bounded content audit blocked");
        Assert(new SessionConfiguration(new[] { "canvas-service-audit", "enemy-probe" }, "memory-ns", 120).Has("canvas-service-audit"), "Bounded service audit blocked");
        Assert(new SessionConfiguration(new[] { "jade-probe", "enemy-probe" }, "memory-ns", 120).Has("jade-probe"), "Bounded native Jade comparison blocked");
        Reject(() => new SessionConfiguration(new[] { "jade-probe", "enemy-probe" }, "memory-ns", 119));
        Reject(() => new SessionConfiguration(new[] { "jade-probe", "enemy-probe", "contact-probe" }, "memory-ns", 120));
        Reject(() => new SessionConfiguration(new List<string>(normal) { "jade-probe" }, "input-ns", 0));
        Reject(() => new SessionConfiguration(new[] { "canvas-service-audit" }, "memory-ns", 120));
        Reject(() => new SessionConfiguration(new[] { "canvas-service-audit", "enemy-probe" }, "memory-ns", 119));
        Reject(() => new SessionConfiguration(new[] { "canvas-service-audit", "enemy-probe", "contact-probe" }, "memory-ns", 120));
        Reject(() => new SessionConfiguration(new[] { "content-audit" }, "memory-ns", 119));
        Reject(() => new SessionConfiguration(new[] { "content-audit", "enemy-probe" }, "memory-ns", 180));
        Assert(new SessionConfiguration(new[] { "encounter-audit", "defeat-probe" }, "preview-hk", 100).Has("defeat-probe"), "Authorized native HK audit blocked");
    }
    sealed class Owner : IDisposable { public int Injury, Talismans, Disposals; public bool Dirty; public void Dispose() { Disposals++; } }
    static void Pools()
    {
        var all = new List<Owner>();
        var pool = new ReceiverPool<Owner>(2, () => { var o = new Owner(); all.Add(o); return o; }, o => { o.Injury = o.Talismans = 0; },
            o => { Assert(o.Injury == 0 && o.Talismans == 0, "Cross-target state"); if (o.Dirty) throw new InvalidOperationException("Dirty attachment"); });
        var a = pool.Acquire(0, out _); a.Injury = 20; a.Talismans = 2;
        pool.Retire(a, 0); var b = pool.Acquire(0.5, out bool reused);
        Assert(!reused && !ReferenceEquals(a, b), "Quarantine reused early");
        Reject(() => pool.Acquire(0.5, out _)); Assert(pool.Retained == 2, "Quarantine omitted from budget");
        var clean = pool.Acquire(1, out reused); Assert(reused && ReferenceEquals(a, clean) && clean.Injury == 0 && clean.Talismans == 0, "Reset/reuse failed");
        Reject(() => pool.Retire(new Owner(), 2)); pool.Retire(a, 2); a.Dirty = true;
        Reject(() => pool.Acquire(3, out _)); Assert(a.Disposals == 1 && pool.Retained == 1, "Dirty owner retained");
        pool.Dispose(); pool.Dispose(); Assert(b.Disposals == 1 && pool.Retained == 0, "Pool disposal leaked/doubled");
        // Sustained alternating leases must plateau, not retain every activation.
        using var endurance = new ReceiverPool<Owner>(64, () => new Owner(), o => o.Injury = 0, o => Assert(o.Injury == 0, "Dirty"));
        for (int cycle = 0; cycle < 1000; cycle++) { var wave = new List<Owner>(); for (int n = 0; n < 32; n++) wave.Add(endurance.Acquire(cycle * 2, out _)); foreach (var owner in wave) endurance.Retire(owner, cycle * 2); Assert(endurance.Retained == 32, "Retained growth"); }
    }
    static void Summons()
    {
        var scenes = new HashSet<string>(); var keys = new HashSet<string>();
        foreach (var profile in EncounterProfile.All) {
            Assert(scenes.Add(profile.Scene) && keys.Add(profile.Key), "Ambiguous native profile");
            Assert(ReferenceEquals(EncounterProfile.ForScene(profile.Scene), profile) && profile.IsBossName(profile.Boss), "Native profile selector mismatch");
            Assert(!(profile.OwnNativePools || profile.NativeHitAuthority) || profile.Roster, "Native pool capability escaped roster path");
        }
        Assert(EncounterProfile.ForScene(EncounterProfile.FalseKnightAudit.Scene) == EncounterProfile.FalseKnight && EncounterProfile.FalseKnight.ArmorAndHead && EncounterProfile.FailedChampion.ArmorAndHead && EncounterProfile.ForScene("GG_Radiance") == EncounterProfile.AbsoluteRadiance, "Requested native encounter admission mismatch");
        Assert(EncounterProfile.AbsoluteRadiance.OwnNativePools && EncounterProfile.AbsoluteRadiance.NativeHitAuthority
            && EncounterProfile.AbsoluteRadiance.IsBossName("Absolute Radiance") && !EncounterProfile.AbsoluteRadiance.IsTargetName("Radiant Orb(Clone)")
            && EncounterProfile.ForScene("Dream_Final_Boss")==null && EncounterProfile.ForScene("GG_Radiance_V")==null,
            "Radiance admission broadened to campaign, speculative alternate arena, or projectile boss authority");
        Assert(EncounterProfile.Flukemarm.IsTargetName("Fluke Fly Spawner (5)") && !EncounterProfile.Flukemarm.IsBossName("Fluke Fly Spawner (5)"), "Native minion acquired boss authority");
        foreach(var warrior in new[]{EncounterProfile.SoulWarrior,EncounterProfile.ForScene("GG_Mage_Knight_V")!}) {
            Assert(warrior.IsTargetName("Mage Balloon Spawner") && warrior.IsTargetName("Mage Balloon Spawner (35)")
                && !warrior.IsBossName("Mage Balloon Spawner (35)"),"Alternate warrior minion lost target admission or gained boss authority");
            Assert(!warrior.IsTargetName("Mage Balloon Spawner (36)") && !warrior.IsTargetName("Mage Balloon Spawner(Clone)")
                && !warrior.IsTargetName("Mage Balloon Spawner (0)"),"Unpinned warrior receiver admitted");
        }
        var alternate = EncounterProfile.ForScene("GG_Ghost_Markoth_V");
        Assert(alternate != null && alternate.Key == "Markoth" && alternate.Scene.EndsWith("_V") && alternate.NativeHitAuthority && alternate.OwnNativePools,
            "Native alternate arena lost exact scene ownership");
        Assert(ReferenceEquals(alternate, EncounterProfile.ForScene("GG_Ghost_Markoth_V")) && EncounterProfile.ForScene("GG_Ghost_Markoth_FAKE") == null,
            "Alternate profile allocates repeatedly or admits guessed scenes");
        var xeroAlternate=EncounterProfile.ForScene("GG_Ghost_Xero_V");
        Assert(xeroAlternate!=null && xeroAlternate.Family==EncounterProfile.Xero && !xeroAlternate.Roster
            && xeroAlternate.EntryFsm==EncounterProfile.Xero.EntryFsm && xeroAlternate.EntryState==EncounterProfile.Xero.EntryState
            && !xeroAlternate.UsesHealthNotches && xeroAlternate.Scene!=EncounterProfile.Xero.Scene,
            "Alternate Xero bypassed its native sword/entry adapter or lost arena ownership");
        var vengeflyAlternate=EncounterProfile.ForScene("GG_Vengefly_V");
        Assert(vengeflyAlternate!=null && vengeflyAlternate.Roster && vengeflyAlternate.IsBossName("Giant Buzzer Col (1)"),
            "Multi-body Vengefly exception lost independent native boss ownership");
        Assert(EncounterProfile.OroMato.IsBossName("Oro") && EncounterProfile.OroMato.IsBossName("Mato") && !EncounterProfile.OroMato.IsBossName("Oro range"), "Multi-body selector is too broad");
        Assert(EncounterProfile.SoulMaster.IsBossName("Mage Lord Phase2") && !EncounterProfile.Watchers.IndividualBossBars, "Replacement body or summon presentation lost");
        var parent = new ContactFrame { Session = Guid.NewGuid(), Encounter = Guid.NewGuid(), GeometryFrame = 10, Entity = 1 };
        foreach (int damage in new[] { 0, 1, 2, 8, 100 }) foreach (int tier in new[] { 0, 1, 2 }) {
            parent.Difficulty = tier;
            var target = new SummonedTarget { Entity = 2, Collider = 3, Generation = 4, Health = 20, MaxHealth = 30, Damage = damage, Active = true, Width = 2, Height = 4 };
            var projected = target.Project(parent); var attack = target.Attack();
            Assert(projected.SecondaryTarget && !parent.SecondaryTarget && projected.Difficulty == tier && attack.Damage == damage && attack.Activation == projected.TargetGeneration && attack.Id == projected.Collider, "Native attack projection lost role/damage/tier/activation");
            var first = new TargetIdentity(projected); projected.HurtCollider++; Assert(first.Equals(new TargetIdentity(projected)), "Native collider replacement changed health/injury identity"); target.Generation++; var next = new TargetIdentity(target.Project(parent)); Assert(!first.Equals(next), "Activation aliases prior injury owner");
            var one = new InjurySettlement(); var two = new InjurySettlement(); one.Track(1, 12); two.Track(1, 7);
            Assert(one.Resolve(1, HitDisposition.Refused) == 12 && one.Resolve(1, HitDisposition.Refused) == 0 && two.Resolve(1, HitDisposition.Stale) == 7, "Independent/duplicate settlement failed");
        }
        Assert(EncounterProfile.FalseKnight.IsTargetName("Head") && !EncounterProfile.FalseKnight.IsBossName("Head") && !EncounterProfile.FailedChampion.IsTargetName("Tinger"), "Armor/head widget authority or excluded Tinger changed");
        Assert(EncounterProfile.FalseKnight.AuditOnly == false && EncounterProfile.FailedChampionAudit.AuditOnly
            && !Array.Exists(EncounterProfile.All, p => p.AuditOnly), "Native-only phase audits admitted to ordinary roster");
        var nativePhase = new ArmorHeadPhase();
        nativePhase.Observe("Idle", false);
        nativePhase.Observe("Stun Start", false);
        Assert(nativePhase.Broken && nativePhase.ArmorHealth(260) == 0, "Early native refill made depleted armor visible");
        nativePhase.Observe("Opened", true); long exposedLease = nativePhase.HeadLease;
        nativePhase.Observe("Hit", true);
        Assert(nativePhase.Exposed && !nativePhase.HeadDamageState && nativePhase.HeadLease == exposedLease, "Per-hit reaction discarded head injury/maximum");
        nativePhase.Observe("Opened", true);
        Assert(nativePhase.HeadLease == exposedLease && nativePhase.HeadDamageState, "Head reaction manufactured another phase");
        nativePhase.Observe("Stun Recover", true);
        Assert(!nativePhase.Exposed && nativePhase.ArmorHealth(260) == 0, "Recovery restored armor early");
        nativePhase.Observe("Idle Pause", false);
        Assert(nativePhase.ArmorLease == 2 && nativePhase.ArmorHealth(260) == 260, "Native armor recovery failed to renew injury lease");
        nativePhase.Observe("Death Open", false); nativePhase.Observe("Opened 2", true);
        Assert(nativePhase.HeadLease == exposedLease + 1 && nativePhase.Broken, "Final native exposure lost independent ownership");
        var rage = new RageStrike(); rage.Observe("Rage"); long slam = rage.Action;
        Assert(rage.Striking && !rage.Windup && !rage.Counter(slam + 1) && rage.Counter(slam) && !rage.Counter(slam), "Counter identity/replay admission failed");
        Assert(rage.Striking && rage.InSequence && rage.Action == slam, "Defense interrupted native rage");
        rage.Observe("Particle Pause"); rage.Observe("Anim End"); Assert(rage.InSequence && !rage.Counter(slam), "Native recovery lost the retired crimson action"); rage.Observe("Turn"); rage.Observe("Rage");
        Assert(rage.Action == slam + 1 && !rage.Counter(slam) && rage.Counter(slam + 1), "Next mace strike retained retired defense");
        rage.Observe("Rage End"); Assert(!rage.InSequence && !rage.Counter(rage.Action), "Counter admitted outside rage");
        var visual = new RageStrike(); visual.Observe("R Attack Antic");
        long sequence = visual.Sequence, firstStrike = visual.Action;
        Assert(visual.InitialCharge && visual.Windup, "Initial native anticipation omitted");
        visual.Observe("Rage Begin"); Assert(visual.Action == firstStrike && visual.Sequence == sequence && visual.InitialCharge, "Native intermediate state ended anticipation");
        visual.Observe("Rage"); Assert(visual.Action == firstStrike && visual.Sequence == sequence && !visual.InitialCharge && visual.Striking && !visual.Windup, "Damaging native Rage frames treated as anticipation");
        visual.Observe("Rage Slam"); Assert(!visual.InitialCharge && visual.Counter(firstStrike), "Released first strike missing");
        visual.Observe("Particle Pause"); Assert(!visual.Striking && visual.InSequence, "Native turn/body collider admitted as mace strike");
        visual.Observe("Turn"); Assert(!visual.Striking, "Turn animation admitted as damaging swing"); visual.Observe("Rage");
        Assert(visual.Sequence == sequence && visual.Action == firstStrike + 1 && !visual.InitialCharge && !visual.Counter(firstStrike), "Counter or next swing restarted visual sequence");
        visual.Observe("Rage End"); visual.Observe("R Attack Antic");
        Assert(visual.Sequence == sequence + 1 && visual.InitialCharge, "New rage retained released visual state");
        var armorParent = new ContactFrame { Session = Guid.NewGuid(), Encounter = Guid.NewGuid(), GeometryFrame = 7, Entity = 90, Collider = 90, HurtCollider = 90, Sequence = 1, Frequency = 1000, MaxHealth = 1, Width = 1, Height = 1, HurtWidth = 1, HurtHeight = 1 };
        var emptyArmor = new SummonedTarget { Entity = 91, Collider = 93, Generation = 1, Health = 0, MaxHealth = 260, BossBar = true, Width = 3, Height = 4 };
        var exposedHead = new SummonedTarget { Entity = 92, Collider = 94, Generation = 2, Health = 40, MaxHealth = 40, Active = true, Width = 1, Height = 2 };
        armorParent.Summons = new[] { emptyArmor, exposedHead };
        armorParent.AttackId = 8; armorParent.Crimson = armorParent.Windup = armorParent.QuickWarning = true;
        using var phaseWire = new MemoryStream(); ContactCodec.Write(phaseWire, armorParent); phaseWire.Position = 0;
        var phaseFrame = ContactCodec.Read(phaseWire);
        Assert(phaseFrame.QuickWarning && phaseFrame.Windup && phaseFrame.Encounter == armorParent.Encounter && phaseFrame.Summons[0].BossBar && !phaseFrame.Summons[0].Project(armorParent).HurtActive
            && !phaseFrame.Summons[1].BossBar && phaseFrame.Summons[1].Project(armorParent).HurtActive, "Armor depletion lost encounter or enabled wrong receiver/widget");
        // False Knight native audit: armor reset 65 -> 260 and separate 40 HP head.
        var armor = new BossHealthProjection(260, 260, 0); var head = new BossHealthProjection(40, 40, 0);
        Assert(armor.Total == 1 && head.Total == 1, "Per-body maximum health combined");
        var armorState = new TargetHealthState(10, 1, 260); var headState = new TargetHealthState(11, 1, 40);
        armorState.Observe(65); armorState.Observe(260); headState.Observe(0);
        Assert(armorState.Maximum == 260 && armorState.Health == 260 && headState.Maximum == 40, "Armor reset contaminated head health");
        var replacement = new TargetHealthState(10, 2, 260);
        Assert(!replacement.Matches(new HitOutcome { Entity = 10, TargetActivation = 1 }), "Replacement accepts retired activation");
        var prior = new ContactFrame { Session = Guid.NewGuid(), Encounter = Guid.NewGuid(), GeometryFrame = 1,
            Entity = 11, TargetGeneration = 1, Collider = 20, HurtCollider = 21, Width = 2, Height = 3, HurtWidth = 2, HurtHeight = 3,
            Active = true, HurtActive = true, Kickable = true, AttackId = 7 };
        var terminal = headState.RemovedSnapshot(prior, 40, 100);
        Assert(terminal.Encounter == prior.Encounter && terminal.Entity == 11 && terminal.TargetGeneration == 1
            && terminal.Health == 0 && !terminal.Active && !terminal.HurtActive && !terminal.Kickable && terminal.AttackId == 0
            && terminal.Volumes.Length == 0 && prior.Active, "Retired body retained attacks, aliased prior snapshot or changed encounter identity");
        Reject(() => armorState.RemovedSnapshot(prior, 40, 100));
        terminal.Sequence = 1; terminal.SentTick = terminal.CaptureTick;
        using var removedWire = new MemoryStream(); ContactCodec.Write(removedWire, terminal); removedWire.Position = 0;
        Assert(ContactCodec.Read(removedWire).Health == 0, "Terminal body snapshot cannot cross the real contact codec");
    }
}
