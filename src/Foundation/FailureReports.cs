// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace HollowSols.Foundation;

public sealed class ReportContext
{
    public Guid Incident { get; }
    public Guid Session { get; }
    public Guid Connection { get; }
    public GameSide Side { get; }
    public string Build { get; }
    public string Scene { get; }
    public ReportContext(Guid incident, Guid session, Guid connection, GameSide side, string build, string scene)
    {
        if (incident == Guid.Empty || side != GameSide.HollowKnight && side != GameSide.NineSols) throw new ArgumentException("Invalid report identity.");
        if (session != Guid.Empty && incident != session || session == Guid.Empty && connection != Guid.Empty && incident != connection)
            throw new ArgumentException("Report scope does not match its session/connection.");
        Incident = incident; Session = session; Connection = connection; Side = side;
        Build = build ?? "unknown"; Scene = scene ?? "unknown";
    }
}

// Only call disk operations on a worker. The two processes share incident identity,
// but never exchange credentials, upload anything, or touch native save payloads.
public sealed class FailureReportStore
{
    public const int FileBytes = 1024 * 1024;
    public const int BundleBytes = 16 * 1024 * 1024;
    private readonly string root, runs;
    private readonly string[] privateValues;
    private static readonly string[] logNames = { "plugin.log.startup", "plugin.log.2", "plugin.log.1", "plugin.log", "bootstrap.log" };
    private static readonly UTF8Encoding utf8 = new UTF8Encoding(false);
    public string Root => root;
    public FailureReportStore(string reportRoot, string runtimeRuns, params string[] privateValues)
    {
        root = Path.GetFullPath(reportRoot); runs = Path.GetFullPath(runtimeRuns);
        if (root == runs || root.StartsWith(runs + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Reports must be outside runtime/save storage.");
        this.privateValues = privateValues.Where(v => !string.IsNullOrWhiteSpace(v) && v.Length >= 3).OrderByDescending(v => v.Length).ToArray();
    }
    private static void CheckPath(string path)
    {
        // Refuse redirected files and every existing ancestor, including roots.
        for (string? next = Path.GetFullPath(path); next != null; next = Path.GetDirectoryName(next))
            if ((File.Exists(next) || Directory.Exists(next)) && (File.GetAttributes(next) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Redirected diagnostic path refused.");
    }
    private string IncidentPath(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("Missing incident.");
        string path = Path.Combine(root, "incidents", id.ToString("N")); CheckPath(path); return path;
    }
    private string ActivePath(Guid instance)
    {
        if (instance == Guid.Empty) throw new ArgumentException("Missing process instance.");
        string path = Path.Combine(root, "active", instance.ToString("N") + ".active"); CheckPath(path); return path;
    }
    private static void Atomic(string path, Action<Stream> write)
    {
        CheckPath(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string pending = path + ".pending-" + Guid.NewGuid().ToString("N");
        try {
            using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { write(stream); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(pending, path, null); else File.Move(pending, path);
        } finally { if (File.Exists(pending)) File.Delete(pending); }
    }
    private static void Text(string path, string value) => Atomic(path, stream => { var bytes = utf8.GetBytes(value); stream.Write(bytes, 0, bytes.Length); });
    public void Arm(Guid instance, ReportContext context, int process, long started)
    {
        Atomic(ActivePath(instance), stream => {
            using var w = new BinaryWriter(stream, utf8, true);
            w.Write(0x31525348); w.Write(process); w.Write(started);
            w.Write(context.Incident.ToByteArray()); w.Write(context.Session.ToByteArray()); w.Write(context.Connection.ToByteArray());
            w.Write((byte)context.Side); w.Write(Clean(context.Build, 1024)); w.Write(Clean(context.Scene, 256));
        });
    }
    public void Disarm(Guid instance) { string path = ActivePath(instance); if (File.Exists(path)) File.Delete(path); }
    public void RecoverAbandoned(Func<int, long, bool> ownerAlive)
    {
        string directory = Path.Combine(root, "active"); CheckPath(directory);
        if (!Directory.Exists(directory)) return;
        foreach (string path in Directory.EnumerateFiles(directory, "*.active").Take(64)) {
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out var instance)) continue;
            try {
                CheckPath(path); using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length > 4096) continue;
                using var r = new BinaryReader(stream, utf8, true);
                if (r.ReadInt32() != 0x31525348) continue;
                int process = r.ReadInt32(); long started = r.ReadInt64();
                var context = new ReportContext(new Guid(r.ReadBytes(16)), new Guid(r.ReadBytes(16)), new Guid(r.ReadBytes(16)), (GameSide)r.ReadByte(), r.ReadString(), r.ReadString());
                if (stream.Position != stream.Length || ownerAlive(process, started)) continue;
                Capture(context, "Previous game process ended before orderly bridge shutdown.", "");
                Disarm(instance);
            } catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException || e is ArgumentException || e is FormatException) { /* Preserve corrupt/locked markers for inspection. */ }
        }
    }
    private T Locked<T>(Guid incident, Func<string, T> action)
    {
        string directory = IncidentPath(incident); Directory.CreateDirectory(directory);
        string lockPath = Path.Combine(directory, "capture.lock"); CheckPath(lockPath);
        for (int attempt = 0; ; attempt++) {
            FileStream handle;
            try { handle = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 20) { Thread.Sleep(25); continue; }
            using (handle) return action(directory);
        }
    }
    public void Capture(ReportContext context, string failure, string menuTail)
    {
        Locked(context.Incident, directory => {
            string info = Path.Combine(directory, context.Side + ".txt");
            if (!File.Exists(info)) Text(info, "Hollow Sols failure report\nincident=" + context.Incident + "\nsession=" + context.Session
                + "\nconnection=" + context.Connection + "\nrole=" + context.Side + "\nutc=" + DateTime.UtcNow.ToString("O")
                + "\nbuild=" + Clean(context.Build, 1024) + "\nscene=" + Clean(context.Scene, 256) + "\nfailure=" + Clean(failure, 8192) + "\n");
            string menu = Path.Combine(directory, context.Side + "-menu.log");
            if (!File.Exists(menu)) Text(menu, Clean(menuTail, FileBytes));
            var notes = new StringBuilder();
            foreach (var side in new[] { GameSide.HollowKnight, GameSide.NineSols }) {
                bool found = false;
                foreach (string name in logNames) {
                    string destination = Path.Combine(directory, side + "-" + name);
                    if (File.Exists(destination)) { CheckPath(destination); found = true; continue; }
                    if (context.Session == Guid.Empty) continue;
                    string source = Path.Combine(runs, side.ToString(), context.Session.ToString(), name);
                    try {
                        CheckPath(source); if (!File.Exists(source)) continue;
                        string content = ReadTail(source); Text(destination, content); found = true;
                    } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { notes.AppendLine(side + "/" + name + ": unavailable (" + e.GetType().Name + ")"); }
                }
                if (!found) notes.AppendLine(side + ": ordinary session logs unavailable; no unrelated session was substituted.");
            }
            Text(Path.Combine(directory, context.Side + "-availability.txt"), notes.ToString());
            if (!File.Exists(Path.Combine(directory, "acknowledged"))) Text(Path.Combine(root, "pending", context.Incident.ToString("N") + ".pending"), "incident\n");
            return true;
        });
    }
    private string ReadTail(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        bool tail = stream.Length > FileBytes;
        if (tail) stream.Seek(-FileBytes, SeekOrigin.End);
        var bytes = new byte[(int)Math.Min(FileBytes, stream.Length)]; int count = 0, read;
        while (count < bytes.Length && (read = stream.Read(bytes, count, bytes.Length - count)) > 0) count += read;
        string text = utf8.GetString(bytes, 0, count);
        // A truncated first line can lose its secret/path prefix. Never export it.
        if (tail) { int line = text.IndexOf('\n'); text = line < 0 ? "" : text.Substring(line + 1); }
        return (tail ? "[bounded recent log tail]\n" : "") + Clean(text, FileBytes);
    }
    public void Acknowledge(Guid incident) => Locked(incident, path => {
        Text(Path.Combine(path, "acknowledged"), "dismissed\n");
        string pending = Path.Combine(root, "pending", incident.ToString("N") + ".pending"); CheckPath(pending);
        if (File.Exists(pending)) File.Delete(pending);
        return true;
    });
    public Guid? FindPending()
    {
        string directory = Path.Combine(root, "pending"); CheckPath(directory); if (!Directory.Exists(directory)) return null;
        foreach (var marker in Directory.EnumerateFiles(directory, "*.pending").Take(64).OrderByDescending(File.GetLastWriteTimeUtc)) {
            CheckPath(marker);
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(marker), "N", out var id)) continue;
            string path = IncidentPath(id);
            CheckPath(path);
            if (!File.Exists(Path.Combine(path, "acknowledged"))
                && (File.Exists(Path.Combine(path, "HollowKnight.txt")) || File.Exists(Path.Combine(path, "NineSols.txt")))) return id;
        }
        return null;
    }
    public string Export(Guid incident)
    {
        return Locked(incident, directory => {
            if (!File.Exists(Path.Combine(directory, "HollowKnight.txt")) && !File.Exists(Path.Combine(directory, "NineSols.txt")))
                throw new InvalidDataException("Incident snapshot is not ready.");
            string zipPath = Path.Combine(directory, "HollowSols-report-" + incident.ToString("N") + ".zip"); CheckPath(zipPath);
            if (File.Exists(zipPath)) { using var check = ZipFile.OpenRead(zipPath); if (check.GetEntry("README.txt") == null) throw new InvalidDataException("Incomplete report ZIP."); return zipPath; }
            var names = new List<string>();
            foreach (var side in new[] { GameSide.HollowKnight, GameSide.NineSols }) {
                names.Add(side + ".txt"); names.Add(side + "-menu.log"); names.Add(side + "-availability.txt");
                names.AddRange(logNames.Select(name => side + "-" + name));
            }
            Atomic(zipPath, stream => {
                using var archive = new ZipArchive(stream, ZipArchiveMode.Create, true);
                WriteEntry(archive, "README.txt", "Attach this ZIP, describe what happened, then submit.\nNo files are uploaded automatically.\nOrdinary bounded logs only; inspect before sharing publicly.\nIncident: " + incident + "\n");
                WriteEntry(archive, "availability.txt", string.Join("\n", new[] { GameSide.HollowKnight, GameSide.NineSols }.Select(side => side
                    + ": failure metadata=" + File.Exists(Path.Combine(directory, side + ".txt"))
                    + ", menu tail=" + File.Exists(Path.Combine(directory, side + "-menu.log"))
                    + "; unavailable files were not substituted from another session.")) + "\n");
                long total = 0;
                foreach (string name in names) {
                    string source = Path.Combine(directory, name); CheckPath(source); if (!File.Exists(source)) continue;
                    long length = new FileInfo(source).Length;
                    if (length > FileBytes + 4096 || total + length > BundleBytes) { WriteEntry(archive, "LIMIT.txt", "Remaining files omitted at report size limit.\n"); break; }
                    // Re-sanitize persisted text too; never treat local files as trusted.
                    string content = Clean(ReadTail(source), FileBytes); total += utf8.GetByteCount(content);
                    WriteEntry(archive, name, content);
                }
            });
            return zipPath;
        });
    }
    private static void WriteEntry(ZipArchive zip, string name, string content)
    { using var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Fastest).Open(), utf8); writer.Write(content); }
    public string Clean(string text, int limit)
    {
        text = text.Length <= limit ? text : text.Substring(0, limit) + "\n[truncated]";
        text = Regex.Replace(text, @"(?im)^.*\b(?:authorization|password|passwd|(?:client[_-]?)?secret|(?:(?:access|refresh|auth)[_-]?)?token|api[_-]?key)[""']?\s*[=:].*$", "[credential line redacted]", RegexOptions.None, TimeSpan.FromSeconds(1));
        text = Regex.Replace(text, @"\b(?:gh[pousr]_[A-Za-z0-9_]{16,}|github_pat_[A-Za-z0-9_]{16,}|Bearer\s+\S+)", "[credential redacted]", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        text = Regex.Replace(text, @"https?://[^/\s@]+@", "https://[credentials redacted]@", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        text = Regex.Replace(text, @"(?:[A-Za-z]:[\\/]|\\\\|(?<![A-Za-z0-9:])/(?:home|Users|tmp|var|mnt)/)[^\r\n""<>]*", "[local path redacted]", RegexOptions.None, TimeSpan.FromSeconds(1));
        foreach (string value in privateValues) text = text.Replace(value, "[private]");
        return text;
    }
    public static Uri? IssueUrl(string repository, Guid incident, string build = "See attached report", string failure = "Unexpected bridge failure; see attached logs")
    {
        if (!Uri.TryCreate(repository.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com"
            || uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0) return null;
        string[] parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length != 2 || parts.Any(p => !Regex.IsMatch(p, @"^[A-Za-z0-9][A-Za-z0-9_.-]{0,99}$") || p == "." || p == "..")) return null;
        string id = incident.ToString("N");
        string Short(string value) => Regex.Replace(value.Length > 180 ? value.Substring(0, 180) : value, @"[^A-Za-z0-9 ._:+;=-]", "_");
        return new Uri("https://github.com/" + parts[0] + "/" + parts[1] + "/issues/new?template=bug-report.yml&title="
            + Uri.EscapeDataString("[Bridge failure] " + id.Substring(0, 8)) + "&incident=" + id
            + "&build=" + Uri.EscapeDataString(Short(build)) + "&failure=" + Uri.EscapeDataString(Short(failure))
            + "&diagnostics=" + Uri.EscapeDataString("Attach HollowSols-report-" + id + ".zip. Describe what happened before submitting."));
    }
    public static string OpenReport(string path, Uri? issue, Action<string> reveal, Action<Uri> browse)
    {
        var result = new StringBuilder("Attach this ZIP, describe what happened, then submit.\nReport: " + path);
        try { reveal(path); } catch (Exception) { result.Append("\nExplorer could not open; use the report path above."); }
        if (issue == null) result.Append("\nGitHub submission is unavailable until Support.RepositoryUrl is configured.");
        else {
            result.Append("\nIssue link: ").Append(issue.AbsoluteUri);
            try { browse(issue); result.Append("\nThe issue link was handed to your browser. Sign in if needed; attach the ZIP and submit."); }
            catch (Exception) { result.Append("\nBrowser could not open. Open this link manually: ").Append(issue.AbsoluteUri); }
        }
        return result.ToString();
    }
}
