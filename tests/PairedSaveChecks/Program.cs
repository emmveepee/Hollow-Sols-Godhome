using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.IO.Pipes;
using HollowSols.Foundation;

internal static class Program
{
    private static readonly byte[] HK = Encoding.UTF8.GetBytes("fixture hk state");
    private static readonly byte[] NS = Encoding.UTF8.GetBytes("fixture ns jade/bow/loadout state");
    private static readonly NewGameChoice Godhome = new NewGameChoice(GameMode.Godhome, false, true);
    private static string evidence = "";
    private static int assertions;
    private static int Main(string[] args)
    {
        try {
            if (args.Length > 1 && args[0] == "interrupted-child") {
                string root = Path.GetFullPath(args[1]);
                if (!root.Contains(Path.DirectorySeparatorChar + ".local" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Child requires a disposable .local root.");
                var boundary = Enum.Parse<CheckpointBoundary>(args[2]);
                var store = new PairedSaveStore(root, phase => { if (phase == boundary) Environment.Exit(91); });
                store.Save(new ModSlot(1), Guid.Parse(args[3]), 1, Guid.NewGuid(), NS, HK);
                throw new Exception("Interrupted child passed its exit boundary");
            }
            if (args.Length > 1 && args[0] == "locked-child") {
                try { new PairedSaveStore(args[1]); return 92; } catch (IOException) { return 93; }
            }
            Run(args); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void Run(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Pass an isolated fixture root inside .local.");
        evidence = Path.GetFullPath(args[0]);
        if (!evidence.Contains(Path.DirectorySeparatorChar + ".local" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Fixture root must be ignored development data.");
        Directory.CreateDirectory(evidence);
        FailureReportChecks.Run(evidence);
        DeferredReadChecks.Run();
        DefaultPaths(); Isolation(); Faults(); Corruption(); Lifecycle(); Coordinator(); Wire(); Bundles(); MenuControl(); ProcessInterruptions(); NativeContext(); NativeExchange(); ProductionTransport(); LeaseDiagnostics();
        Console.WriteLine("PASS: " + assertions + " assertions; disposable fixture data preserved at " + evidence);
    }
    private static string Root(string name) => Path.Combine(evidence, name + "-" + Guid.NewGuid().ToString("N"), "HollowSols");
    private static void DefaultPaths()
    {
        string appData = Path.Combine(evidence, "application-" + Guid.NewGuid().ToString("N"));
        string home = Path.Combine(appData, "HollowSols");
        string log = Path.Combine(home, "logs", "123", "bootstrap.log");
        Directory.CreateDirectory(Path.GetDirectoryName(log)!); File.WriteAllText(log, "preserved bootstrap evidence");
        File.WriteAllText(Path.Combine(home, "store.lock"), "");
        string root = SavePaths.DefaultRoot(appData);
        Require(root == Path.Combine(home, "paired", "HollowSols"), "Bootstrap logs collided with the default save namespace");
        Require(new PairedSaveStore(root).ListSlots().All(s => s.Condition == SlotCondition.Empty), "Default startup created a user-facing save");
        Require(SavePaths.DefaultRoot(appData) == root && File.ReadAllText(log) == "preserved bootstrap evidence", "Default root changed or rewrote startup logs");
        string legacyData = Path.Combine(evidence, "prior-application-" + Guid.NewGuid().ToString("N"));
        string legacy = Path.Combine(legacyData, "HollowSols"); var store = new PairedSaveStore(legacy);
        var pair = store.Create(new ModSlot(1), Godhome, Guid.NewGuid(), HK, NS);
        Require(SavePaths.DefaultRoot(legacyData) == legacy, "An existing paired namespace was silently relocated");
        Same(store.Load(pair.Pair.Slot, pair.Pair.Id), HK, NS);
        File.Delete(Path.Combine(legacy, "namespace"));
        Require(SavePaths.DefaultRoot(legacyData) == legacy, "Unrecognized existing data silently became a fresh namespace");
        Refuse(() => new PairedSaveStore(SavePaths.DefaultRoot(legacyData)), "Unrecognized legacy data was adopted");
        Require(File.Exists(Path.Combine(legacy, "slot-1", "pair")), "Failed namespace validation discarded the existing pair");
        Console.WriteLine("PASS: default bootstrap logs and strict save namespace coexist; existing paired data stays in place and unknown data refuses fallback.");
    }
    private static void Require(bool condition, string message)
    { assertions++; if (!condition) throw new Exception(message); }
    private static void Refuse(Action action, string message)
    {
        try { action(); } catch (Exception e) when (e is OperationCanceledException || e is IOException || e is InvalidDataException || e is ArgumentException || e is InvalidOperationException || e is SaveUnavailableException)
        { assertions++; return; }
        throw new Exception(message);
    }
    private static void Same(PairedCheckpoint checkpoint, byte[] hk, byte[] ns)
    { Require(checkpoint.HollowKnight.SequenceEqual(hk) && checkpoint.NineSols.SequenceEqual(ns), "Paired state mismatch"); }

    private static void Isolation()
    {
        string root = Root("isolation"); string adjacent = Path.GetDirectoryName(root)!;
        Directory.CreateDirectory(adjacent);
        // Sentinel names model vanilla saves. These are not user save files.
        File.WriteAllBytes(Path.Combine(adjacent, "user4.dat"), HK);
        File.WriteAllBytes(Path.Combine(adjacent, "saveslot3.fixture"), NS);
        Refuse(() => new PairedSaveStore(adjacent), "Accepted a vanilla-like root");
        var store = new PairedSaveStore(root);
        Require(store.ListSlots().Length == 4 && store.ListSlots().All(s => s.Condition == SlotCondition.Empty), "Initial slots are not exactly four empty slots");
        Require(!Directory.EnumerateDirectories(root).Any(), "Startup created a slot");
        foreach (int bad in new[] { -1, 0, 5, int.MaxValue }) Refuse(() => new ModSlot(bad), "Invalid slot accepted");
        Refuse(() => store.Create(default, Godhome, Guid.NewGuid(), HK, NS), "Default slot accepted");
        Refuse(() => new NewGameChoice(GameMode.Classic, false, false), "Classic warning bypassed");
        Refuse(() => new NewGameChoice(GameMode.SteelSoul, false, false), "Steel Soul warning bypassed");
        Refuse(() => new NewGameChoice(GameMode.Classic, true, true), "Campaign full access accepted");
        var created = new PairedCheckpoint[4];
        for (int i = 0; i < 4; i++)
        {
            var choice = i == 0 ? Godhome : i == 1 ? new NewGameChoice(GameMode.Godhome, false, false)
                : new NewGameChoice(i == 2 ? GameMode.Classic : GameMode.SteelSoul, true, false);
            created[i] = store.Create(new ModSlot(i + 1), choice, Guid.NewGuid(), HK, NS);
            Require(store.ListSlots().Count(s => s.Condition != SlotCondition.Empty) == i + 1, "Creation affected unrelated slots");
            Same(store.Load(created[i].Pair.Slot, created[i].Pair.Id), HK, NS);
            Require(created[i].Pair.Choice.Mode == choice.Mode && created[i].Pair.Choice.FullGodhomeAccess == choice.FullGodhomeAccess, "Mode choice did not persist");
        }
        Require(created.Select(c => c.Pair.Id).Distinct().Count() == 4, "Pairs share identity");
        Refuse(() => store.Create(new ModSlot(1), Godhome, Guid.NewGuid(), HK, NS), "Creation replaced an occupied slot");
        Refuse(() => store.Load(new ModSlot(1), created[1].Pair.Id), "Foreign pair loaded");
        Refuse(() => store.DeleteConfirmed(new ModSlot(1), created[1].Pair.Id), "Foreign pair deleted");
        var saved = store.Save(new ModSlot(1), created[0].Pair.Id, 1, Guid.NewGuid(), NS, HK);
        Refuse(() => store.Save(new ModSlot(1), created[0].Pair.Id, 1, Guid.NewGuid(), HK, NS), "Stale save overwrote a newer checkpoint");
        Same(store.Load(new ModSlot(2), created[1].Pair.Id), HK, NS);
        string archive = store.DeleteConfirmed(new ModSlot(1), saved.Pair.Id);
        Require(Directory.Exists(archive) && store.ListSlots()[0].Condition == SlotCondition.Empty, "Deletion split or removed the pair archive");
        var replacement = store.Create(new ModSlot(1), Godhome, Guid.NewGuid(), HK, NS);
        Require(replacement.Pair.Id != saved.Pair.Id, "Recreation reused old identity");
        Refuse(() => store.DeleteConfirmed(new ModSlot(1), saved.Pair.Id), "Old confirmation deleted replacement");
        Require(File.ReadAllBytes(Path.Combine(adjacent, "user4.dat")).SequenceEqual(HK)
            && File.ReadAllBytes(Path.Combine(adjacent, "saveslot3.fixture")).SequenceEqual(NS), "Vanilla sentinels changed");
        using (new FileStream(Path.Combine(root, "store.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Refuse(() => store.Load(new ModSlot(1), replacement.Pair.Id), "Concurrent process lease was ignored");
        string unknown = Root("unknown"); Directory.CreateDirectory(unknown); File.WriteAllText(Path.Combine(unknown, "existing"), "preserve");
        Refuse(() => new PairedSaveStore(unknown), "Adopted unknown data namespace");
        Console.WriteLine("PASS: four isolated pairs, creation modes/consent, stale generation/identity refusal, retained paired deletion and unchanged adjacent vanilla sentinels.");
    }

    private static void Faults()
    {
        foreach (var stop in Enum.GetValues<CheckpointBoundary>())
        {
            string root = Root("fault-" + stop); var store = new PairedSaveStore(root);
            var first = store.Create(new ModSlot(1), Godhome, Guid.NewGuid(), HK, NS);
            var faulty = new PairedSaveStore(root, boundary => { if (boundary == stop) throw new IOException("simulated process interruption"); });
            Refuse(() => faulty.Save(first.Pair.Slot, first.Pair.Id, 1, Guid.NewGuid(), NS, HK), "Fault injection did not run");
            var loaded = new PairedSaveStore(root).Load(first.Pair.Slot, first.Pair.Id);
            bool published = stop == CheckpointBoundary.Published;
            Require(loaded.Generation == (published ? 2 : 1) && loaded.Recovered == !published, "Interrupted save chose the wrong generation");
            Require(published || loaded.RecoveryNotice != null, "Recovery did not provide a visible warning contract");
            Same(loaded, published ? NS : HK, published ? HK : NS);
            Require(Directory.EnumerateDirectories(Path.Combine(root, "slot-1")).Count() == 2, "Recovery destroyed original evidence");
            var resumed = store.Save(first.Pair.Slot, first.Pair.Id, loaded.Generation, Guid.NewGuid(), HK, NS);
            Require(resumed.Generation == 3, "Recovery reused an interrupted generation");
        }
        foreach (var stop in Enum.GetValues<CheckpointBoundary>().Where(s => s != CheckpointBoundary.Published))
        {
            string root = Root("create-fault-" + stop);
            var store = new PairedSaveStore(root, boundary => { if (boundary == stop) throw new IOException("interrupted creation"); });
            Refuse(() => store.Create(new ModSlot(1), Godhome, Guid.NewGuid(), HK, NS), "Creation fault did not run");
            Require(store.ListSlots()[0].Condition == SlotCondition.Unrecoverable, "Incomplete creation silently became a fresh save");
            Refuse(() => store.Create(new ModSlot(1), Godhome, Guid.NewGuid(), HK, NS), "Creation silently replaced incomplete data");
        }
        Console.WriteLine("PASS: interruptions at all five publication boundaries, incomplete creation refusal, synchronized recovery warning and preserved originals.");
    }

    private static void Corruption()
    {
        foreach (string damaged in new[] { "hk.payload", "ns.payload", "commit", "missing-hk", "missing-ns", "missing-commit" })
        {
            string root = Root("corrupt-" + damaged); var store = new PairedSaveStore(root);
            var first = store.Create(new ModSlot(1), Godhome, Guid.NewGuid(), HK, NS);
            store.Save(first.Pair.Slot, first.Pair.Id, 1, Guid.NewGuid(), NS, HK);
            string latest = Directory.GetDirectories(Path.Combine(root, "slot-1")).OrderBy(p => p, StringComparer.Ordinal).Last();
            if (damaged.StartsWith("missing-", StringComparison.Ordinal)) File.Move(Path.Combine(latest, damaged.Substring(8) + (damaged == "missing-commit" ? "" : ".payload")), Path.Combine(latest, "preserved-missing-original"));
            else File.WriteAllBytes(Path.Combine(latest, damaged), new byte[] { 42 });
            var recovered = store.Load(first.Pair.Slot, first.Pair.Id);
            Require(recovered.Recovered && recovered.Generation == 1, "Corruption did not fall back to synchronized state"); Same(recovered, HK, NS);
        }
        string noRoot = Root("no-valid"); var noStore = new PairedSaveStore(noRoot);
        var invalid = noStore.Create(new ModSlot(1), Godhome, Guid.NewGuid(), HK, NS);
        string generation = Directory.GetDirectories(Path.Combine(noRoot, "slot-1")).Single();
        File.WriteAllBytes(Path.Combine(generation, "ns.payload"), HK);
        Refuse(() => noStore.Load(invalid.Pair.Slot, invalid.Pair.Id), "No-valid-checkpoint load invented data");
        Require(noStore.ListSlots()[0].Condition == SlotCondition.Unrecoverable, "No-valid pair appears loadable");
        string otherRoot = Root("foreign"); var other = new PairedSaveStore(otherRoot);
        var foreign = other.Create(new ModSlot(2), Godhome, Guid.NewGuid(), NS, HK);
        string foreignGeneration = Directory.GetDirectories(Path.Combine(otherRoot, "slot-2")).Single();
        foreach (string file in new[] { "commit", "hk.payload", "ns.payload" }) File.Copy(Path.Combine(foreignGeneration, file), Path.Combine(generation, file), true);
        Refuse(() => noStore.Load(invalid.Pair.Slot, invalid.Pair.Id), "Foreign slot checkpoint loaded");
        File.WriteAllBytes(Path.Combine(noRoot, "slot-1", "pair"), new byte[] { 42 });
        Require(noStore.ListSlots()[0].Pair == null, "Corrupt pair metadata was accepted");
        var confirmation = noStore.PrepareDeletion(new ModSlot(1));
        Refuse(() => other.DeleteConfirmed(confirmation), "Deletion confirmation crossed namespaces");
        File.WriteAllText(Path.Combine(noRoot, "slot-1", "preserved-extra"), "changed after confirmation");
        Refuse(() => noStore.DeleteConfirmed(confirmation), "Stale corrupt-slot deletion was accepted");
        string archived = noStore.DeleteConfirmed(noStore.PrepareDeletion(new ModSlot(1)));
        Require(Directory.Exists(archived) && File.Exists(Path.Combine(archived, "preserved-extra")) && noStore.ListSlots()[0].Condition == SlotCondition.Empty, "Corrupt slot was not conservatively archived by explicit confirmation");
        // No symlink privileges are needed to test a Windows junction (a reparse point).
        if (OperatingSystem.IsWindows())
        {
            string junction = Path.Combine(evidence, "junction-" + Guid.NewGuid().ToString("N"));
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c mklink /J \"" + junction + "\" \"" + Path.GetDirectoryName(noRoot) + "\"") { UseShellExecute = false, CreateNoWindow = true });
            process!.WaitForExit(); Require(process.ExitCode == 0, "Fixture junction could not be created");
            Refuse(() => new PairedSaveStore(Path.Combine(junction, "HollowSols")), "Linked root accepted");
        }
        Console.WriteLine("PASS: corrupt/missing halves and manifests, no-valid failure, foreign-slot payload refusal and linked-root isolation.");
    }

    private static (BridgeLifecycle HK, BridgeLifecycle NS) Connect(bool nsFirst)
    {
        var hk = new BridgeLifecycle(GameSide.HollowKnight); var ns = new BridgeLifecycle(GameSide.NineSols);
        BridgeHello h, n;
        if (nsFirst) { n = ns.RequestBridge(0); h = hk.RequestBridge(1); }
        else { h = hk.RequestBridge(0); n = ns.RequestBridge(1); }
        Require(hk.RequestBridge(1).Activation == h.Activation && ns.RequestBridge(1).Activation == n.Activation, "Repeated button created duplicate authorization");
        hk.ObserveHello(n, 1); ns.ObserveHello(h, 1);
        var offer = hk.OfferConnection(1);
        Require(hk.OfferConnection(1).Equals(offer), "Duplicate offer created new connection");
        ns.AcceptOffer(offer, 1); hk.ConfirmConnection(offer, 1); ns.ConfirmConnection(offer, 1);
        Require(hk.State == BridgeState.SaveSelection && ns.State == BridgeState.Connected && hk.Connection == ns.Connection
            && !hk.Session.HasValue && !ns.Session.HasValue, "Connected is not distinct from gameplay");
        return (hk, ns);
    }
    private static SessionToken Activate(BridgeLifecycle hk, BridgeLifecycle ns, SavePair pair, double now)
    {
        var token = hk.BeginLoad(pair, now); ns.AcceptLoad(token, now); ns.LocalLoaded(token, false, now);
        hk.LocalLoaded(token, false, now); hk.PeerLoaded(token, now); hk.StartSession(token, now); ns.StartSession(token, now);
        return token;
    }
    private static void Lifecycle()
    {
        var store = new PairedSaveStore(Root("lifecycle")); var pair = store.Create(new ModSlot(1), Godhome, Guid.NewGuid(), HK, NS).Pair;
        var second = store.Create(new ModSlot(2), Godhome, Guid.NewGuid(), NS, HK).Pair;
        foreach (bool nsFirst in new[] { false, true })
        {
            var (hk, ns) = Connect(nsFirst);
            Refuse(() => ns.BeginLoad(pair, 1), "NS independently selected a slot");
            var token = hk.BeginLoad(pair, 1); ns.AcceptLoad(token, 1); ns.AcceptLoad(token, 1);
            Refuse(() => hk.StartSession(token, 1), "Gameplay started before either restore");
            hk.LocalLoaded(token, true, 1); hk.PeerLoaded(token, 1); ns.LocalLoaded(token, true, 1);
            Refuse(() => hk.StartSession(token, 1), "Recovery notice bypassed");
            hk.AcknowledgeRecoveryNotice(token); hk.StartSession(token, 1); ns.StartSession(token, 1);
            Require(hk.AcceptsGameplay(token) && ns.AcceptsGameplay(token), "Both sessions did not activate");
            Refuse(() => hk.BeginLoad(second, 1), "Slot switched with prior session active");
            hk.EndSession(token, 2); ns.EndSession(token, 2);
            Require(!hk.AcceptsGameplay(token) && hk.AcceptsSave(token), "Ending still permits gameplay or blocks final save");
            hk.Drained(token, GameSide.HollowKnight, 2); ns.Drained(token, GameSide.NineSols, 2);
            Require(hk.State == BridgeState.Ending, "Single-sided drain finished session");
            hk.Drained(token, GameSide.NineSols, 2); ns.Drained(token, GameSide.HollowKnight, 2);
            Require(hk.Connection == ns.Connection && hk.State == BridgeState.SaveSelection && !hk.Session.HasValue, "End disconnected bridge");
            var next = Activate(hk, ns, second, 2);
            Require(next.Ordinal == 2 && next.Session != token.Session && !hk.AcceptsSave(token), "Old slot retains save authority");
            Refuse(() => ns.AcceptLoad(token, 2), "Stale load affected new slot");
            Refuse(() => hk.EndSession(token, 2), "Stale end affected new slot");
            Refuse(() => hk.Disconnect(3), "Vanilla selection bypassed final session drain");
            hk.EndSession(next, 3); ns.EndSession(next, 3);
            foreach (var side in new[] { GameSide.HollowKnight, GameSide.NineSols }) { hk.Drained(next, side, 3); ns.Drained(next, side, 3); }
            hk.Disconnect(3); ns.Disconnect(3);
            Require(!hk.CanSimulate && !ns.AcceptsSave(next) && hk.Connection == Guid.Empty, "Vanilla boundary retained bridge authority");
            hk.NativeVanillaRestored(3); ns.NativeVanillaRestored(3);
            Refuse(() => ns.ConfirmConnection(new ConnectionOffer(token.Connection, Guid.NewGuid(), Guid.NewGuid()), 3), "Vanilla silently reconnected");
        }
        var unrequested = new BridgeLifecycle(GameSide.NineSols);
        Refuse(() => unrequested.ObserveHello(new BridgeHello(GameSide.HollowKnight, Guid.NewGuid()), 0), "Process presence authorized bridge");
        var missing = new BridgeLifecycle(GameSide.HollowKnight); var firstHello = missing.RequestBridge(0); missing.Tick(30);
        Require(missing.State == BridgeState.WaitingForCounterpart && missing.Connection == Guid.Empty, "Waiting for a missing counterpart expired the player's activation");
        missing.Fail("cancelled");
        Require(missing.RequestBridge(31).Activation != firstHello.Activation, "Retry reused activation");
        missing.ObserveHello(new BridgeHello(GameSide.NineSols, Guid.NewGuid(), 99), 31);
        Require(missing.State == BridgeState.Failed, "Protocol mismatch accepted");
        var late = new BridgeLifecycle(GameSide.HollowKnight); late.RequestBridge(0);
        late.ObserveHello(new BridgeHello(GameSide.NineSols, Guid.NewGuid()), 31); late.OfferConnection(31); late.Tick(61);
        Require(late.State == BridgeState.Failed, "An incomplete actual handshake waits indefinitely");
        var (live, peer) = Connect(false); var liveToken = Activate(live, peer, pair, 1); live.Tick(7);
        Require(live.State == BridgeState.Failed && !live.AcceptsGameplay(liveToken) && !live.AcceptsSave(liveToken), "Peer loss did not invalidate gameplay/save operations");
        Refuse(() => live.RequestBridge(7), "Reconnect bypassed native cleanup");
        live.NativeVanillaRestored(7); live.RequestBridge(7);
        Refuse(() => live.Tick(6), "Regressed clock accepted");
        var (introHost, introPeer) = Connect(false);
        var introToken = introHost.BeginLoad(pair, 1); introPeer.AcceptLoad(introToken, 1);
        introHost.IntroductionWaiting(introToken, 150); introPeer.IntroductionWaiting(introToken, 150);
        introHost.PeerHeartbeat(introToken.Connection, 181); introPeer.PeerHeartbeat(introToken.Connection, 181);
        introHost.Tick(181); introPeer.Tick(181);
        Require(introHost.State == BridgeState.Loading && introPeer.State == BridgeState.Loading, "Native dialogue reading expired the load deadline");
        introHost.Tick(187);
        Require(introHost.State == BridgeState.Failed, "Native introduction wait masked counterpart loss");
        introPeer.Tick(331);
        Require(introPeer.State == BridgeState.Failed, "Abandoned introduction wait removed the bounded operation deadline");
        Console.WriteLine("PASS: both activation orders, repeated presses, role authority, matched load/notice/start/drain, connected switching, vanilla invalidation, retry/mismatch and peer loss.");
    }

    private static void Coordinator()
    {
        var store = new PairedSaveStore(Root("coordinator")); var first = store.Create(new ModSlot(1), Godhome, Guid.NewGuid(), HK, NS);
        var (hk, ns) = Connect(false); var token = Activate(hk, ns, first.Pair, 1);
        var coordinator = new PairedSaveCoordinator(hk, store);
        Refuse(() => new PairedSaveCoordinator(ns, store), "NS became save coordinator");
        var request = coordinator.Begin(token, 1, 1);
        Require(coordinator.Begin(token, 1, 1).Transaction == request.Transaction, "Duplicate save request created transaction");
        coordinator.Submit(request, GameSide.HollowKnight, HK, 1);
        Refuse(() => coordinator.Commit(request, 1), "Single-side checkpoint committed");
        var source = (byte[])NS.Clone(); coordinator.Submit(request, GameSide.NineSols, source, 1); source[0]++;
        coordinator.Submit(request, GameSide.NineSols, NS, 1);
        Refuse(() => coordinator.Submit(request, GameSide.NineSols, HK, 1), "Changed duplicate snapshot accepted");
        var committed = coordinator.Commit(request, 1);
        Require(committed.Transaction == request.Transaction && committed.Session == token.Session && committed.Generation == 2, "Checkpoint protocol identities mismatch"); Same(committed, HK, NS);
        Refuse(() => coordinator.Commit(request, 1), "Committed save replayed");
        var asyncRequest=coordinator.Begin(token,2,1.5);
        coordinator.Submit(asyncRequest,GameSide.HollowKnight,HK,1.5);coordinator.Submit(asyncRequest,GameSide.NineSols,NS,1.5);
        var work=coordinator.PreparePublication(asyncRequest,1.5);
        Require(coordinator.Busy,"Preparing publication released the save fence");
        Refuse(()=>coordinator.PreparePublication(asyncRequest,1.5),"Duplicate publication acquired snapshots");
        Refuse(()=>coordinator.Submit(asyncRequest,GameSide.NineSols,NS,1.5),"Publication snapshots changed during worker ownership");
        Refuse(()=>coordinator.Begin(new SessionToken(token.Connection,token.Session,token.Pair,new ModSlot(2),token.Ordinal),2,1.5),"Publication switched slots");
        var worker=System.Threading.Tasks.Task.Run(work);Require(worker.Wait(5000),"Isolated publication worker hung");
        var published=coordinator.CompletePublication(asyncRequest,worker.Result,1.6);
        Require(published.Generation==3 && published.Transaction==asyncRequest.Transaction && !coordinator.Busy,"Publication result lost identity or failed to release fence");Same(published,HK,NS);
        Refuse(()=>coordinator.CompletePublication(asyncRequest,published,1.6),"Completed publication replayed");
        var cancelled=coordinator.Begin(token,3,1.7);coordinator.Submit(cancelled,GameSide.HollowKnight,HK,1.7);coordinator.Submit(cancelled,GameSide.NineSols,NS,1.7);
        var cancelledWork=coordinator.PreparePublication(cancelled,1.7);coordinator.Cancel();
        var preserved=System.Threading.Tasks.Task.Run(cancelledWork).GetAwaiter().GetResult();
        Refuse(()=>coordinator.CompletePublication(cancelled,preserved,1.8),"Cancelled publication changed current lifecycle");
        Require(store.Load(first.Pair.Slot,first.Pair.Id).Generation==4,"Interrupted ownership destroyed committed immutable checkpoint");
        Refuse(() => coordinator.DeleteConfirmed(first.Pair.Slot, first.Pair.Id), "Deleted active slot");
        var stale = coordinator.Begin(token, 4, 2); coordinator.Submit(stale, GameSide.HollowKnight, HK, 2);
        hk.Fail("fixture peer loss"); coordinator.Tick(3);
        Refuse(() => coordinator.Submit(stale, GameSide.NineSols, NS, 3), "Stale asynchronous result after disconnect accepted");
        Require(!coordinator.Busy && store.Load(first.Pair.Slot, first.Pair.Id).Generation == 4, "Peer loss committed partial save");
        Console.WriteLine("PASS: HK-only coordinated snapshots, duplicate immutability, one-half commit refusal, save transaction identity and disconnect cancellation.");
    }

    private static void Wire()
    {
        ControlWireCompatibilityChecks.Run();
        var h = new BridgeHello(GameSide.HollowKnight, Guid.NewGuid()); var n = new BridgeHello(GameSide.NineSols, Guid.NewGuid());
        var offer = new ConnectionOffer(Guid.NewGuid(), h.Activation, n.Activation);
        var token = new SessionToken(offer.Connection, Guid.NewGuid(), Guid.NewGuid(), new ModSlot(4), 1);
        var messages = new[] {
            new ControlMessage(ControlKind.Hello,h,1), new ControlMessage(ControlKind.Hello,n,1),
            new ControlMessage(ControlKind.Offer,h,2,offer), new ControlMessage(ControlKind.Acknowledge,n,2,offer),
            new ControlMessage(ControlKind.Connected,h,3,offer), new ControlMessage(ControlKind.Heartbeat,n,3,offer),
            new ControlMessage(ControlKind.Load,h,4,session:token), new ControlMessage(ControlKind.Loaded,n,4,session:token,recovered:true),
            new ControlMessage(ControlKind.Start,h,5,session:token,generation:1), new ControlMessage(ControlKind.End,n,5,session:token),
            new ControlMessage(ControlKind.Drained,h,6,session:token), new ControlMessage(ControlKind.Disconnect,n,6),
            new ControlMessage(ControlKind.Checkpoint,h,7,session:token,transaction:Guid.NewGuid(),generation:1),
            new ControlMessage(ControlKind.CheckpointReady,n,7,session:token,transaction:Guid.NewGuid(),generation:1),
            new ControlMessage(ControlKind.CheckpointCommitted,h,8,session:token,transaction:Guid.NewGuid(),generation:2),
            new ControlMessage(ControlKind.PrepareNative,h,9,session:token),
            new ControlMessage(ControlKind.IntroductionWaiting,h,10,session:token),
            new ControlMessage(ControlKind.NativeDefeat,n,11,session:token),
            new ControlMessage(ControlKind.SaveNeeded,n,8,session:token),
            new ControlMessage(ControlKind.EndReady,n,9,session:token)
        };
        foreach(var message in messages)
        {
            using var stream = new MemoryStream(); ControlProtocol.Write(stream,message);
            byte[] bytes=stream.ToArray(); Require(bytes.Length==ControlProtocol.MessageBytes,"Control wire size differs"); stream.Position=0;
            var result=ControlProtocol.Read(stream);
            Require(result.Kind==message.Kind && result.Side==message.Side && result.Hello.Activation==message.Hello.Activation
                && result.Sequence==message.Sequence && Nullable.Equals(result.Offer,message.Offer) && Nullable.Equals(result.Session,message.Session)
                && result.Transaction==message.Transaction && result.Generation==message.Generation && result.Recovered==message.Recovered,"Control roundtrip differs");
            for(int length=0;length<bytes.Length;length++) Refuse(()=>ControlProtocol.Read(new MemoryStream(bytes,0,length)),"Truncated control message accepted");
            foreach(int offset in new[]{0,4,6,7,162,167}) {
                var malformed=(byte[])bytes.Clone(); malformed[offset]=255;
                Refuse(()=>ControlProtocol.Read(new MemoryStream(malformed)),"Malformed control header/extension accepted");
            }
        }
        Refuse(()=>new ControlMessage(ControlKind.Load,n,1,session:token),"Peer sent slot selection");
        Refuse(()=>new ControlMessage(ControlKind.PrepareNative,n,1,session:token),"Peer independently prepared native gameplay");
        Refuse(()=>new ControlMessage(ControlKind.PrepareNative,h,1),"Native preparation omitted the selected pair");
        Refuse(()=>new ControlMessage(ControlKind.PrepareNative,h,1,session:token,transaction:Guid.NewGuid()),"Native preparation carried a checkpoint transaction");
        Refuse(()=>new ControlMessage(ControlKind.IntroductionWaiting,n,1,session:token),"NS extended HK introduction input waiting");
        Refuse(()=>new ControlMessage(ControlKind.NativeDefeat,h,1,session:token),"HK impersonated an actual native Yi defeat");
        Refuse(()=>new ControlMessage(ControlKind.Hello,h,1,offer),"Hello carried unauthorized connection");
        Refuse(()=>new ControlMessage(ControlKind.Start,h,1),"Start missing session accepted");
        Refuse(()=>new ControlMessage(ControlKind.Start,h,1,session:token),"Start omitted its committed generation");
        Refuse(()=>new ControlMessage(ControlKind.Start,h,1,session:default(SessionToken)),"Default control session accepted");
        Refuse(()=>new ControlMessage(ControlKind.Offer,h,1,new ConnectionOffer(Guid.NewGuid(),Guid.NewGuid(),n.Activation)),"Offer sender activation mismatch accepted");
        Console.WriteLine("PASS: separate control schema/authority, complete identity roundtrips, every truncation and malformed versions/roles/reserved fields.");
    }
    private static void Bundles()
    {
        var files = new Dictionary<string,byte[]> { ["flags"] = NS, ["meta"] = HK, ["backup/flags"] = NS, ["thumbnail.png"] = Array.Empty<byte>() };
        var bytes = NativeSaveBundle.Encode(files); var result = NativeSaveBundle.Decode(bytes);
        Require(result.Count == files.Count && files.All(f=>result[f.Key].SequenceEqual(f.Value)), "Native file bundle lost state");
        for(int size=0;size<bytes.Length;size++) Refuse(()=>NativeSaveBundle.Decode(bytes.Take(size).ToArray()),"Truncated native bundle accepted");
        foreach(string unsafeName in new[]{"../user4.dat","/saveslot3","C:/saveslot3","backup/../../flags","flags:stream","flags\\backup","NUL","COM1.dat","flags.","a//b","a/./b"})
            Refuse(()=>NativeSaveBundle.Encode(new Dictionary<string,byte[]> { [unsafeName]=HK }),"Unsafe native bundle name accepted");
        Refuse(()=>NativeSaveBundle.Encode(new Dictionary<string,byte[]> { ["flags"]=HK,["FLAGS"]=NS }),"Case alias accepted");
        Refuse(()=>NativeSaveBundle.Decode(bytes.Concat(new byte[]{0}).ToArray()),"Native bundle trailing bytes accepted");
        var stored = new PairedSaveStore(Root("native-bundle")).Create(new ModSlot(1), Godhome, Guid.NewGuid(), HK, bytes);
        Require(NativeSaveBundle.Decode(stored.NineSols)["flags"].SequenceEqual(NS), "Native NS file set did not persist");
        Console.WriteLine("PASS: opaque native flag/meta/backup bundle persistence, truncation and unsafe/aliased path refusal; no filesystem extraction.");
    }
    private static void MenuControl()
    {
        var store = new PairedSaveStore(Root("menu-control")); var pair=store.Create(new ModSlot(1), Godhome, Guid.NewGuid(), HK, NS).Pair;
        foreach(bool nsFirst in new[]{false,true})
        {
            var hk=new MenuBridgeController(GameSide.HollowKnight); var ns=new MenuBridgeController(GameSide.NineSols);
            if(nsFirst){ ns.Activate(0); hk.Activate(1); }else{ hk.Activate(0); ns.Activate(1); }
            hk.Activate(1); ns.Activate(1);
            string name="HollowSols.fixture."+Guid.NewGuid().ToString("N");
            using var server=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous);
            using var client=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous);
            var wait=server.WaitForConnectionAsync(); client.Connect(2000);
            Require(wait.Wait(2000),"Fixture pipe did not connect");
            int nativeLoads=0, nativeDefeats=0;
            void Pump(double now) {
                for(int step=0;step<64;step++) {
                    bool any=false;
                    if(hk.TryTakeOutgoing(out var host)) {
                        var receive=System.Threading.Tasks.Task.Run(()=>ControlProtocol.Read(client));
                        ControlProtocol.Write(server,host!); server.Flush();
                        Require(receive.Wait(2000),"Host control read timed out"); var native=ns.Receive(receive.Result,now);
                        if(native?.Kind==ControlKind.Load)nativeLoads++; any=true;
                    }
                    if(ns.TryTakeOutgoing(out var peer)) {
                        var receive=System.Threading.Tasks.Task.Run(()=>ControlProtocol.Read(server));
                        ControlProtocol.Write(client,peer!); client.Flush();
                        Require(receive.Wait(2000),"Peer control read timed out"); if(hk.Receive(receive.Result,now)?.Kind==ControlKind.NativeDefeat)nativeDefeats++; any=true;
                    }
                    if(!any)return;
                }
                throw new Exception("Control pump repeated initialization indefinitely");
            }
            Pump(1);
            Require(hk.Lifecycle.State==BridgeState.SaveSelection && ns.Lifecycle.State==BridgeState.Connected && nativeLoads==0,"Handshake independently loaded a native save");
            hk.Heartbeat();ns.Heartbeat();Pump(1);
            var token=hk.Select(pair,1);Pump(1);
            Require(nativeLoads==1 && ns.Lifecycle.Session!.Value.Equals(token),"Real-pipe slot/session identity differs");
            Refuse(()=>ns.PrepareNative(token),"NS independently initiated native preparation");
            var foreign=new SessionToken(token.Connection,Guid.NewGuid(),token.Pair,token.Slot,token.Ordinal);
            Refuse(()=>hk.PrepareNative(foreign),"Another session initiated native preparation");
            hk.PrepareNative(token);Pump(1);hk.PrepareNative(token);Pump(1);
            hk.IntroductionWaiting(token,1);Pump(1);
            Refuse(()=>ns.IntroductionWaiting(token,1),"NS impersonated an introduction input wait");
            Require(ns.Lifecycle.State==BridgeState.Loading && !ns.Lifecycle.CanSimulate,"Native preparation bypassed paired restoration");
            hk.NativeLoaded(token,false,1);ns.NativeLoaded(token,true,1);Pump(1);
            Refuse(()=>hk.Start(token,1),"Peer recovery did not gate the host notice");
            hk.Lifecycle.AcknowledgeRecoveryNotice(token);hk.Start(token,1);Pump(1);
            Require(hk.Lifecycle.CanSimulate && ns.Lifecycle.CanSimulate,"Real-pipe session did not start");
            Refuse(()=>hk.PrepareNative(token),"Active session restarted native initialization");
            Refuse(()=>hk.IntroductionWaiting(token,1),"Introduction wait affected active gameplay");
            Refuse(()=>hk.NativeDefeat(token),"HK originated a native Yi defeat");
            Refuse(()=>ns.NativeDefeat(foreign),"Foreign session originated a native defeat");
            ns.NativeDefeat(token);Pump(1);
            Require(nativeDefeats==1 && hk.Lifecycle.State==BridgeState.Active,"Native defeat changed lifecycle before the native adapter handled it");
            // NS originates this exit; HK must end the same session.
            ns.End(token,2);Pump(2);
            hk.NativeDrained(token,2);ns.NativeDrained(token,2);Pump(2);
            Require(hk.Lifecycle.State==BridgeState.SaveSelection && ns.Lifecycle.State==BridgeState.Connected,"Peer exit did not retain the connection");
            // Test disconnection from both native selection origins.
            if(nsFirst)ns.Disconnect(2);else hk.Disconnect(2);Pump(2);
            Require(hk.Lifecycle.State==BridgeState.Disconnecting && ns.Lifecycle.State==BridgeState.Disconnecting,"Vanilla intent did not reach both sides");
            hk.Lifecycle.NativeVanillaRestored(2);ns.Lifecycle.NativeVanillaRestored(2);
            Require(nativeLoads==1 && !hk.Lifecycle.CanSimulate && !ns.Lifecycle.Session.HasValue,"Native state remains authorized after vanilla restore");
        }
        foreach (bool peerFirst in new[] { false, true }) {
            var host = new MenuBridgeController(GameSide.HollowKnight); var peer = new MenuBridgeController(GameSide.NineSols);
            Require(!host.TryTakeOutgoing(out _) && !peer.TryTakeOutgoing(out _), "Idle listeners emitted an activation");
            if (peerFirst) peer.Activate(0); else host.Activate(0);
            for (int step = 0; step < 16; step++) {
                bool moved = false;
                if (host.TryTakeOutgoing(out var h)) { peer.Receive(h!, 1); moved = true; }
                if (peer.TryTakeOutgoing(out var n)) { host.Receive(n!, 1); moved = true; }
                if (!moved) break;
            }
            Require(host.Lifecycle.State == BridgeState.SaveSelection && peer.Lifecycle.State == BridgeState.Connected
                && host.Lifecycle.Connection == peer.Lifecycle.Connection, "One button failed to activate both sides");
            void PumpReconnect(double now) {
                for(int step=0;step<32;step++) {
                    bool any=false;
                    if(host.TryTakeOutgoing(out var h)){peer.Receive(h!,now);any=true;}
                    if(peer.TryTakeOutgoing(out var n)){host.Receive(n!,now);any=true;}
                    if(!any)return;
                }
                throw new Exception("Reconnect repeated handshake indefinitely");
            }
            for(int cycle=0;cycle<6;cycle++) {
                double now=2+cycle*2;Guid old=host.Lifecycle.Connection;
                // Heartbeats were already in flight when vanilla was selected.
                host.Heartbeat();peer.Heartbeat();
                if(cycle%2==0)peer.Disconnect(now);else host.Disconnect(now);
                PumpReconnect(now);
                Require(host.Lifecycle.State==BridgeState.Disconnecting && peer.Lifecycle.State==BridgeState.Disconnecting,"Trailing heartbeat broke graceful disconnect");
                host.NativeVanillaRestored(now);peer.NativeVanillaRestored(now);
                Require(!host.TryTakeOutgoing(out _) && !peer.TryTakeOutgoing(out _) && host.Lifecycle.State==BridgeState.Vanilla,"Vanilla restoration retained stale activation frames");
                if(cycle%2==0)peer.Activate(now+1);else host.Activate(now+1);
                PumpReconnect(now+1);host.Heartbeat();peer.Heartbeat();PumpReconnect(now+1);
                Require(host.Lifecycle.State==BridgeState.SaveSelection && peer.Lifecycle.State==BridgeState.Connected
                    && host.Lifecycle.Connection==peer.Lifecycle.Connection && host.Lifecycle.Connection!=old
                    && !host.Lifecycle.Session.HasValue && !peer.Lifecycle.Session.HasValue,"Fresh one-button reconnect after vanilla failed or loaded a save");
            }
        }
        foreach (bool startQueued in new[] { false, true }) {
            var host = new MenuBridgeController(GameSide.HollowKnight);
            var peer = new MenuBridgeController(GameSide.NineSols);
            int ended = 0, restarted = 0;
            void PumpCancelled(double now) {
                for (int step=0;step<32;step++) {
                    bool any=false;
                    if(host.TryTakeOutgoing(out var h)) {
                        var operation=peer.Receive(h!,now);
                        if(operation?.Kind==ControlKind.PrepareNative || operation?.Kind==ControlKind.Start) restarted++;
                        any=true;
                    }
                    if(peer.TryTakeOutgoing(out var n)) {
                        if(host.Receive(n!,now)?.Kind==ControlKind.End) ended++;
                        any=true;
                    }
                    if(!any)return;
                }
                throw new Exception("Cancelled load did not drain its control queue");
            }
            host.Activate(0);PumpCancelled(1);
            var token=host.Select(pair,2);PumpCancelled(2);
            if(startQueued) {
                host.NativeLoaded(token,false,2);peer.NativeLoaded(token,false,2);PumpCancelled(2);
                host.Start(token,2);
            } else { host.PrepareNative(token);host.IntroductionWaiting(token,2); }
            peer.End(token,2);peer.End(token,2); // repeated Back must not queue another End
            PumpCancelled(2);
            Require(restarted==0 && ended==1 && host.Lifecycle.State==BridgeState.Ending
                && peer.Lifecycle.State==BridgeState.Ending,"Completion tail restarted a cancelled native load or repeated its End");
            host.NativeDrained(token,3);peer.NativeDrained(token,3);PumpCancelled(3);
            peer.Disconnect(3);PumpCancelled(3);
            host.NativeVanillaRestored(3);peer.NativeVanillaRestored(3);
            peer.Activate(4);PumpCancelled(4);
            Require(host.Lifecycle.State==BridgeState.SaveSelection && peer.Lifecycle.State==BridgeState.Connected
                && !host.Lifecycle.Session.HasValue && !peer.Lifecycle.Session.HasValue,"Cancelled load poisoned subsequent activation");
        }
        Console.WriteLine("PASS: menu handshake/reconnect, cancellation crossing native preparation/start, idempotent End and vanilla disconnect. Native UI/game adapters are not exercised.");
    }
    private static void ProcessInterruptions()
    {
        foreach(var boundary in Enum.GetValues<CheckpointBoundary>()) {
            string root = Root("process-interruption-"+boundary);var store = new PairedSaveStore(root);
            var first=store.Create(new ModSlot(1),Godhome,Guid.NewGuid(),HK,NS);
            int Child(params string[] arguments) {
                var start=new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) {UseShellExecute=false,CreateNoWindow=true};
                // dotnet <dll> and dotnet run's apphost have different argv.
                if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
                    start.ArgumentList.Add(typeof(Program).Assembly.Location);
                foreach(string arg in arguments)start.ArgumentList.Add(arg);
                using var process=System.Diagnostics.Process.Start(start)!;
                if(!process.WaitForExit(10000)){process.Kill();throw new Exception("Fixture subprocess exceeded its deadline");}
                return process.ExitCode;
            }
            Require(Child("interrupted-child",root,boundary.ToString(),first.Pair.Id.ToString())==91,"Child did not terminate at intended boundary");
            var restored=store.Load(first.Pair.Slot,first.Pair.Id);bool published=boundary==CheckpointBoundary.Published;
            Require(restored.Generation==(published?2:1) && restored.Recovered==!published,"Process termination lost synchronized checkpoint");
            Same(restored,published?NS:HK,published?HK:NS);
            using (new FileStream(Path.Combine(root,"store.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))
                Require(Child("locked-child",root)==93,"Separate process bypassed store lock");
        }
        Console.WriteLine("PASS: separate-process termination at every checkpoint boundary and cross-process exclusive store lease. Power-loss durability is not established.");
    }
    private static void NativeContext()
    {
        var token=new SessionToken(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new ModSlot(2),1);
        NativeSaveContext.Enter(GameSide.NineSols,token,null);
        string root=NativeSaveContext.SlotPath(3);
        Require(NativeSaveContext.RedirectNativeLoad("saveslot3", false) == root, "Hardcoded native load directory was not redirected");
        Refuse(() => NativeSaveContext.RedirectNativeLoad("saveslot4", false), "Fifth native slot entered the isolated load");
        Refuse(()=>NativeSaveContext.SlotPath(2),"Native index leaked from mod slot");
        Refuse(()=>NativeSaveContext.Write("saveslot3/flags",NS),"Virtual provider accepted vanilla write");
        NativeSaveContext.CreateDirectory(root);NativeSaveContext.Write(root+"/flags",NS);NativeSaveContext.Write(root+"/meta",HK);
        Require(NativeSaveContext.Read(root+"/flags").SequenceEqual(NS),"Virtual native bytes differ");
        NativeSaveContext.Rename(root+"/flags",root+"/flags.tmp");Require(!NativeSaveContext.FileExists(root+"/flags"),"Native rename copied instead of moving");
        NativeSaveContext.Rename(root+"/flags.tmp",root+"/flags");
        var bundle=NativeSaveContext.Snapshot(token); long capturedRevision = NativeSaveContext.SnapshotRevision;
        Require(capturedRevision == NativeSaveContext.ContentRevision && capturedRevision > 0, "Native snapshot did not stamp its exact write revision");
        NativeSaveContext.Write(root+"/meta",NS);
        Require(NativeSaveContext.ContentRevision > capturedRevision && NativeSaveContext.SnapshotRevision == capturedRevision, "A write after capture was silently included in its earlier revision");
        Require(NativeSaveBundle.Decode(bundle)["meta"].SequenceEqual(HK), "A post-capture write mutated the prior snapshot");
        NativeSaveContext.Leave(token);
        Require(NativeSaveContext.ContentRevision == 0 && NativeSaveContext.SnapshotRevision == 0, "Native revisions leaked into the next session");
        Require(NativeSaveContext.ShouldHandlePath(root+"/flags"),"Ended session path fell back to vanilla");
        Refuse(()=>NativeSaveContext.Write(root+"/flags",HK),"Ended native operation wrote state");
        Require(!NativeSaveContext.ShouldHandlePath("saveslot3/flags"),"Inactive vanilla path was intercepted");
        var next=new SessionToken(token.Connection,Guid.NewGuid(),Guid.NewGuid(),new ModSlot(3),2);
        NativeSaveContext.Enter(GameSide.NineSols,next,bundle);Refuse(()=>NativeSaveContext.Read(root+"/flags"),"Old native path read a new session");
        Refuse(() => NativeSaveContext.RedirectNativeLoad("PlayerAbilityUnlockAll", true), "Existing slot accepted a baseline reset resource");
        Require(NativeSaveContext.Read(NativeSaveContext.SlotPath(3)+"/flags").SequenceEqual(NS),"Native bundle restore lost loadout bytes");
        NativeSaveContext.Leave(next);NativeSaveContext.Enter(GameSide.HollowKnight,token,HK);
        bool callback=false;NativeSaveContext.HollowKnightSaveRequested=(id,bytes,done)=>{Require(id.Equals(token)&&bytes.SequenceEqual(NS),"HK save identity/payload differs");done(true);};
        NativeSaveContext.HKWrite(4,NS,success=>callback=success);Require(callback,"Native HK callback did not complete");
        Refuse(()=>NativeSaveContext.HKWrite(2,HK,null),"Mod slot number leaked into HK native serializer");
        NativeSaveContext.Leave(token);NativeSaveContext.HollowKnightSaveRequested=null;
        var vanillaOwner = NativeSaveContext.AsyncOwner();
        NativeSaveContext.Enter(GameSide.NineSols, next, bundle);
        Refuse(() => NativeSaveContext.ValidateAsyncOwner(vanillaOwner), "Vanilla asynchronous operation entered a paired context");
        NativeSaveContext.ValidateAsyncOwner(next.Session); NativeSaveContext.Leave(next);
        Refuse(() => NativeSaveContext.ValidateAsyncOwner(next.Session), "Ended session resumed inside vanilla");
        NativeSaveContext.ValidateAsyncOwner(vanillaOwner);
        Console.WriteLine("PASS: native virtual provider rejects vanilla/stale paths, preserves flags/meta/rename and session restores, and scopes HK callbacks independently of mod slots.");
    }
    private static void NativeExchange()
    {
        string root = Root("exchange"); var exchange = new NativeFileExchange(root);
        var token = new SessionToken(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new ModSlot(4), 2);
        var transaction = Guid.NewGuid(); exchange.Publish(token, transaction, NS);
        Require(exchange.Read(token, transaction).SequenceEqual(NS), "Snapshot exchange changed native bytes");
        Require(exchange.ReadAsync(token,transaction).GetAwaiter().GetResult().SequenceEqual(NS),"Worker snapshot read changed native bytes");
        exchange.Publish(token, transaction, NS);
        Refuse(() => exchange.Publish(token, transaction, HK), "Snapshot transaction overwrote its immutable bytes");
        var wrong = new SessionToken(token.Connection, token.Session, Guid.NewGuid(), new ModSlot(1), token.Ordinal);
        Refuse(() => exchange.Read(wrong, transaction), "Foreign pair read another session snapshot");
        Refuse(() => exchange.ReadAsync(wrong,transaction).GetAwaiter().GetResult(),"Worker read accepted a foreign pair");
        string file = Directory.GetFiles(root, "*.ns", SearchOption.AllDirectories).Single();
        byte[] original = File.ReadAllBytes(file);
        for (int length = 0; length < original.Length; length++) {
            File.WriteAllBytes(file, original.Take(length).ToArray());
            Refuse(() => exchange.Read(token, transaction), "Truncated snapshot accepted");
        }
        File.WriteAllBytes(file, original); original[original.Length - 1] ^= 1; File.WriteAllBytes(file, original);
        Refuse(() => exchange.Read(token, transaction), "Corrupt snapshot accepted");
        Refuse(() => exchange.ReadAsync(token,transaction).GetAwaiter().GetResult(),"Worker read accepted a corrupt snapshot");
        Require(File.ReadAllBytes(file).SequenceEqual(original),"Failed worker read modified the original snapshot");
        Console.WriteLine("PASS: synchronous/worker snapshot reads preserve bytes, reject foreign pairs/corruption without modification; changed transactions and every truncation rejected.");
    }
    private static void ProductionTransport()
    {
        foreach (bool peerFirst in new[] { false, true }) {
            string name = "HollowSols.transport.fixture." + Guid.NewGuid().ToString("N");
            ControlTransport? hk = null, ns = null;
            try {
                if (peerFirst) ns = new ControlTransport(GameSide.NineSols, name);
                hk = new ControlTransport(GameSide.HollowKnight, name);
                ns ??= new ControlTransport(GameSide.NineSols, name);
                var h = new BridgeHello(GameSide.HollowKnight, Guid.NewGuid()); var n = new BridgeHello(GameSide.NineSols, Guid.NewGuid());
                hk.Send(new ControlMessage(ControlKind.Hello, h, 1));
                ns.Send(new ControlMessage(ControlKind.Hello, n, 1));
                bool hostReceived = false, peerReceived = false;
                Require(System.Threading.SpinWait.SpinUntil(() => {
                    if (hk.TryReceive(out var peer)) { Require(peer!.Side == GameSide.NineSols, "Transport peer role changed"); hostReceived = true; }
                    if (ns.TryReceive(out var host)) { Require(host!.Side == GameSide.HollowKnight, "Transport host role changed"); peerReceived = true; }
                    return hostReceived && peerReceived;
                }, 5000), "Production asynchronous pipe did not exchange queued frames");
                Require(System.Threading.SpinWait.SpinUntil(() => !hk.WritesPending && !ns.WritesPending, 5000), "Transport did not confirm its queued frame flushes");
                ns.Send(new ControlMessage(ControlKind.Disconnect, n, 2));
                Require(System.Threading.SpinWait.SpinUntil(() => !ns.WritesPending, 5000), "Terminal frame did not flush before close");
                ns.Dispose();
                Require(System.Threading.SpinWait.SpinUntil(() => hk.Failure != null, 5000), "Production transport did not detect counterpart loss");
                Require(hk.TryReceive(out var terminal) && terminal!.Kind == ControlKind.Disconnect && terminal.Hello.Activation == n.Activation, "EOF discarded a queued authenticated terminal frame");
            } finally { ns?.Dispose(); hk?.Dispose(); }
        }
        using (var missing = new ControlTransport(GameSide.NineSols, "HollowSols.missing." + Guid.NewGuid().ToString("N"))) {
            var watch = System.Diagnostics.Stopwatch.StartNew(); missing.Dispose();
            Require(watch.ElapsedMilliseconds < 1000, "Transport disposal blocked the native thread");
        }
        Console.WriteLine("PASS: production asynchronous transport connects in both launch orders, exchanges queued frames, detects peer loss and disposes without a main-thread wait.");
    }
    private static void LeaseDiagnostics()
    {
        string root = Root("diagnostic-lease");
        Refuse(() => new PairedSaveStore(root, leaseTrace: message => { if (message.StartsWith("STORE_LEASE_OPEN", StringComparison.Ordinal)) throw new IOException("Fixture log failure"); }), "Diagnostic failure was unexpectedly accepted");
        var messages = new List<string>(); var store = new PairedSaveStore(root, leaseTrace: messages.Add);
        Require(store.ListSlots().All(s => s.Condition == SlotCondition.Empty), "Failed diagnostic created a user-facing save");
        Require(messages.Count(s => s.StartsWith("STORE_LEASE_OPEN", StringComparison.Ordinal)) == messages.Count(s => s.StartsWith("STORE_LEASE_CLOSED", StringComparison.Ordinal)), "Diagnostic leases were not closed");
        bool checkedLease = false;
        new PairedSaveStore(root, leaseTrace: message => {
            if (!message.StartsWith("STORE_LEASE_OPEN", StringComparison.Ordinal)) return;
            Refuse(() => new PairedSaveStore(root), "Same-thread recursive mutex entry bypassed the store lease");
            var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(typeof(Program).Assembly.Location);
            start.ArgumentList.Add("locked-child"); start.ArgumentList.Add(root);
            using var process = System.Diagnostics.Process.Start(start)!;
            if (!process.WaitForExit(10000)) { process.Kill(); throw new Exception("Mutex fixture child exceeded its deadline"); }
            Require(process.ExitCode == 93, "Independent process bypassed the kernel mutex while the marker file allowed shared reads");
            checkedLease = true;
        });
        Require(checkedLease, "Kernel lease exclusion fixture did not run");
        Require(new PairedSaveStore(root).ListSlots().All(s => s.Condition == SlotCondition.Empty), "Kernel lease did not release after exclusion checks");
        Console.WriteLine("PASS: diagnostic callback failure releases the exclusive store lease without creating saves.");
    }
}
