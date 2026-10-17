// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HollowSols.Foundation;

internal static class FailureReportChecks
{
    private static void Need(bool condition, string why) { if (!condition) throw new Exception(why); }
    private static void Refuse(Action action)
    {
        try { action(); } catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException || e is ArgumentException) { return; }
        throw new Exception("Unsafe report operation was accepted");
    }
    private static string Entry(ZipArchive zip, string name) { using var r = new StreamReader(zip.GetEntry(name)!.Open()); return r.ReadToEnd(); }
    public static void Run(string fixture)
    {
        string root = Path.Combine(fixture, "report-" + Guid.NewGuid().ToString("N"));
        string storage = Path.Combine(root, "logs", "reports"), runs = Path.Combine(root, "paired", "runtime", "runs");
        var store = new FailureReportStore(storage, runs, "FakeUser", "FakeMachine");
        Guid session = Guid.NewGuid(), connection = Guid.NewGuid(), instance = Guid.NewGuid();
        var hk = new ReportContext(session, session, connection, GameSide.HollowKnight, "mod=0.1.0 module=abcd", "GG_Workshop");
        var ns = new ReportContext(session, session, connection, GameSide.NineSols, "mod=0.1.0 module=ef01", "YiGodhomeCanvas");
        string hkRun = Path.Combine(runs, "HollowKnight", session.ToString()), nsRun = Path.Combine(runs, "NineSols", session.ToString());
        Directory.CreateDirectory(hkRun); Directory.CreateDirectory(nsRun);
        File.WriteAllText(Path.Combine(hkRun, "plugin.log"), "FRAME_PERF mean=6.9\nIOException disconnect\nuser=FakeUser machine=FakeMachine\n at C:\\Users\\FakeUser\\private folder\\game.cs:42\nAuthorization: Bearer VerySecretCredential\npassword=SecretPassword\napi_key=SecretApiKey\nghp_0123456789abcdefghijklmnop\n");
        File.WriteAllText(Path.Combine(nsRun, "plugin.log.startup"), "native build verified\n");
        File.WriteAllText(Path.Combine(nsRun, "plugin.log"), "NS combat log\n/home/FakeUser/private file\n");
        File.WriteAllText(Path.Combine(hkRun, "detail-private.log"), "EXCLUDED_DETAIL");
        File.WriteAllText(Path.Combine(hkRun, "user4.dat"), "EXCLUDED_SAVE");
        File.WriteAllText(Path.Combine(hkRun, "deployment.json"), "EXCLUDED_DEPLOYMENT");
        string foreign = Path.Combine(runs, "HollowKnight", Guid.NewGuid().ToString()); Directory.CreateDirectory(foreign);
        File.WriteAllText(Path.Combine(foreign, "plugin.log"), "FOREIGN_SESSION");
        string original = File.ReadAllText(Path.Combine(hkRun, "plugin.log"));
        string credentialSample = store.Clean("{\"password\":\"JsonSecret\"}\nclient_secret=ClientSecretValue\nrefresh_token=RefreshSecret\nhttps://privateUser:PrivatePassword@proxy.example/path\n", 8192);
        foreach (string value in new[] { "JsonSecret", "ClientSecretValue", "RefreshSecret", "privateUser", "PrivatePassword" })
            Need(!credentialSample.Contains(value), "Structured credential was not redacted");
        store.Capture(hk, "Unexpected connection timeout", "HK menu before disconnect");
        store.Capture(ns, "Peer EOF", "NS menu before disconnect");
        Need(store.FindPending() == session, "Incident was not retained");
        string report = store.Export(session); byte[] first = File.ReadAllBytes(report);
        using (var zip = ZipFile.OpenRead(report)) {
            string all = string.Join("\n", zip.Entries.Select(e => Entry(zip, e.FullName)));
            Need(all.Contains("FRAME_PERF") && all.Contains("NS combat log") && all.Contains("HK menu") && all.Contains("NS menu"), "Useful two-role ordinary logs missing");
            Need(all.Contains(session.ToString()) && all.Contains("module=abcd") && all.Contains("Peer EOF"), "Session/build/failure identity missing");
            foreach (string forbidden in new[] { "FakeUser", "FakeMachine", "SecretPassword", "SecretApiKey", "VerySecretCredential", "ghp_0123456789", "EXCLUDED_", "FOREIGN_SESSION", "private folder", "/home/" })
                Need(!all.Contains(forbidden), "Private/unrelated material leaked: " + forbidden);
            Need(zip.Entries.All(e => !e.FullName.Contains("..") && !e.FullName.Contains("/") && e.Length <= FailureReportStore.FileBytes + 4096), "Unbounded or unsafe ZIP entry");
            Need(Entry(zip, "README.txt").Contains("Attach this ZIP") && Entry(zip, "README.txt").Contains("No files are uploaded automatically"), "Upload guidance missing");
        }
        Need(File.ReadAllText(Path.Combine(hkRun, "plugin.log")) == original, "Source log changed during sanitization");
        Need(File.ReadAllText(Path.Combine(hkRun, "user4.dat")) == "EXCLUDED_SAVE", "Synthetic save sentinel changed");
        File.WriteAllText(Path.Combine(hkRun, "plugin.log"), "later source rotation must not replace frozen snapshot");
        Need(store.Export(session) == report && File.ReadAllBytes(report).SequenceEqual(first), "Repeated export changed or duplicated report");
        store.Acknowledge(session); Need(store.FindPending() == null, "Continue/dismissal was not persisted");

        // Missing peer and large-tail redaction, with no cross-session substitution.
        Guid large = Guid.NewGuid(); string largeRun = Path.Combine(runs, "HollowKnight", large.ToString()); Directory.CreateDirectory(largeRun);
        File.WriteAllText(Path.Combine(largeRun, "plugin.log"), "password=" + new string('x', FailureReportStore.FileBytes + 400) + "\nSAFE_RECENT_TAIL\n");
        var missing = new ReportContext(large, large, connection, GameSide.HollowKnight, "fixture", "GG_Atrium");
        store.Capture(missing, "stale peer", "");
        using (var zip = ZipFile.OpenRead(store.Export(large))) {
            Need(Entry(zip, "HollowKnight-plugin.log").Contains("SAFE_RECENT_TAIL"), "Recent log tail lost");
            Need(!Entry(zip, "HollowKnight-plugin.log").Contains(new string('x', 64)), "Truncated credential line leaked");
            Need(Entry(zip, "HollowKnight-availability.txt").Contains("NineSols: ordinary session logs unavailable"), "Missing peer was not explicit");
            Need(Entry(zip, "availability.txt").Contains("menu tail=False"), "Missing peer menu evidence not explicit");
        }
        store.Acknowledge(large);

        // Active markers survive a simulated process loss; orderly exit does not prompt.
        Guid abandoned = Guid.NewGuid(); var previous = new ReportContext(abandoned, abandoned, connection, GameSide.NineSols, "old-module", "old-room");
        store.Arm(instance, previous, 123, 456);
        store.RecoverAbandoned((pid, start) => { Need(pid == 123 && start == 456, "Owner identity changed"); return true; });
        Need(store.FindPending() == null, "Live peer was declared crashed");
        var restarted = new FailureReportStore(storage, runs); restarted.RecoverAbandoned((_, _) => false);
        Need(restarted.FindPending() == abandoned, "Restart lost abandoned incident");
        restarted.Acknowledge(abandoned); restarted.RecoverAbandoned((_, _) => false); Need(restarted.FindPending() == null, "Recovered incident duplicated");
        store.Arm(instance, previous, 123, 456); store.Disarm(instance); store.RecoverAbandoned((_, _) => false);
        Need(store.FindPending() == null, "Normal exit/cancel generated a crash report");

        // A window-close callback must finish removal after queued marker writes,
        // rather than scheduling it and immediately allowing the process to exit.
        foreach (var side in new[] { GameSide.HollowKnight, GameSide.NineSols }) {
            string closeRoot = Path.Combine(root, "window-close-" + side);
            var closing = new FailureReportStore(closeRoot, runs);
            Guid closeInstance = Guid.NewGuid(), closeIncident = Guid.NewGuid();
            var closeContext = new ReportContext(closeIncident, closeIncident, connection, side, "window-close", "room");
            using var releaseArm = new ManualResetEventSlim();
            using var armStarted = new ManualResetEventSlim();
            Task queuedArm = Task.Run(() => { armStarted.Set(); releaseArm.Wait(); closing.Arm(closeInstance, closeContext, 123, 456); });
            Need(armStarted.Wait(TimeSpan.FromSeconds(2)), "Queued arm did not start");
            try {
                Need(!closing.DisarmBeforeExit(closeInstance, queuedArm, TimeSpan.Zero), "Shutdown ignored outstanding report work");
            } finally { releaseArm.Set(); }
            Need(closing.DisarmBeforeExit(closeInstance, queuedArm, TimeSpan.FromSeconds(2)), "Orderly shutdown did not drain queued arm");
            var afterClose = new FailureReportStore(closeRoot, runs); afterClose.RecoverAbandoned((_, _) => false);
            Need(afterClose.FindPending() == null, "Graceful window close prompted on restart");
            closing.Arm(closeInstance, closeContext, 123, 456);
            Need(closing.DisarmBeforeExit(closeInstance, Task.FromException(new IOException("prior report work failed")), TimeSpan.FromSeconds(2)), "Earlier worker failure prevented orderly disarm");
            Need(closing.DisarmBeforeExit(closeInstance, Task.CompletedTask, TimeSpan.FromSeconds(2)), "Repeated orderly disarm failed");
            afterClose.RecoverAbandoned((_, _) => false);
            Need(afterClose.FindPending() == null, "Repeated/faulted-worker shutdown prompted on restart");
            // Explicit shutdown does not erase a genuine incident or its evidence.
            closing.Capture(closeContext, "actual unexpected failure before closing", "preserved");
            Need(closing.DisarmBeforeExit(closeInstance, Task.CompletedTask, TimeSpan.FromSeconds(2)), "Failure-followed-by-close did not finish");
            Need(afterClose.FindPending() == closeIncident, "Orderly close erased a previously captured incident");
        }

        // Capture/export failures are bounded, preserve originals, and leave no partial published ZIP.
        Guid locked = Guid.NewGuid(); string incident = Path.Combine(storage, "incidents", locked.ToString("N")); Directory.CreateDirectory(incident);
        using (var gate = new FileStream(Path.Combine(incident, "capture.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) {
            var timer = Stopwatch.StartNew(); Refuse(() => store.Capture(new ReportContext(locked, locked, connection, GameSide.HollowKnight, "test", "room"), "failure", ""));
            Need(timer.ElapsedMilliseconds < 3000, "Report lock failure was unbounded");
        }
        Refuse(() => store.Export(Guid.NewGuid()));
        Guid interrupted = Guid.NewGuid(); store.Capture(new ReportContext(interrupted, interrupted, connection, GameSide.HollowKnight, "fixture", "room"), "failure", "");
        string blockedZip = Path.Combine(storage, "incidents", interrupted.ToString("N"), "HollowSols-report-" + interrupted.ToString("N") + ".zip");
        Directory.CreateDirectory(blockedZip); Refuse(() => store.Export(interrupted));
        Need(!Directory.EnumerateFiles(Path.GetDirectoryName(blockedZip)!, "*.pending-*").Any(), "Interrupted export left a partial ZIP");
        Need(Directory.Exists(blockedZip), "Exporter deleted pre-existing obstruction");
        Guid fileLocked = Guid.NewGuid(); string fileLockedRun = Path.Combine(runs, "NineSols", fileLocked.ToString()); Directory.CreateDirectory(fileLockedRun);
        string lockedLog = Path.Combine(fileLockedRun, "plugin.log"); File.WriteAllText(lockedLog, "preserved locked source");
        using (var sourceLock = new FileStream(lockedLog, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
            store.Capture(new ReportContext(fileLocked, fileLocked, connection, GameSide.NineSols, "fixture", "room"), "failure", "");
            using var lockedZip = ZipFile.OpenRead(store.Export(fileLocked));
            Need(Entry(lockedZip, "NineSols-availability.txt").Contains("IOException"), "Locked source omission was not explicit");
        }
        Need(File.ReadAllText(lockedLog) == "preserved locked source", "Locked source modified");
        string corruptMarker = Path.Combine(storage, "active", Guid.NewGuid().ToString("N") + ".active");
        File.WriteAllBytes(corruptMarker, new byte[] { 1, 2, 3 });
        store.RecoverAbandoned((_, _) => false);
        Need(File.ReadAllBytes(corruptMarker).SequenceEqual(new byte[] { 1, 2, 3 }), "Corrupt marker was destroyed");

        foreach (string bad in new[] { "", "http://github.com/a/b", "https://github.com.evil/a/b", "https://user:secret@github.com/a/b", "https://github.com/a/b?token=x", "https://github.com/a/b/c", "https://evil.invalid/a/b" })
            Need(FailureReportStore.IssueUrl(bad, session) == null, "Unsafe issue target accepted");
        var url = FailureReportStore.IssueUrl("https://github.com/example/private-report-tests", session, "build52", "timeout")!;
        Need(url.AbsoluteUri.Contains("template=bug-report.yml") && url.AbsoluteUri.Contains("build=build52") && url.AbsoluteUri.Contains("failure=timeout") && url.AbsoluteUri.Length < 1200, "Issue form metadata missing or oversized");
        int browser = 0, explorer = 0;
        string offline = FailureReportStore.OpenReport(report, url, _ => { explorer++; throw new IOException(); }, _ => { browser++; throw new IOException(); });
        Need(explorer == 1 && browser == 1 && offline.Contains(report) && offline.Contains(url.AbsoluteUri), "Offline/browser failure lost manual fallback");
        string local = FailureReportStore.OpenReport(report, null, _ => explorer++, _ => browser++);
        Need(browser == 1 && local.Contains("unavailable"), "Unconfigured repository invoked browser");
        Refuse(() => new ReportContext(Guid.NewGuid(), session, connection, GameSide.HollowKnight, "foreign", "room"));
        Console.WriteLine("PASS: failure reports isolate two-role ordinary logs, redact/bound ZIPs, reuse snapshots, recover abandoned owners, drain queued window-close markers for both roles without erasing actual incidents, reject locked/interrupted exports, and provide offline browser fallbacks; no native saves/network/browser used.");
    }
}
