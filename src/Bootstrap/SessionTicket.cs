using System;
using System.Globalization;
using System.IO;

namespace Doorstop;

// Steam launches from its existing client, so per-parent environment variables do not survive.
// A single-use, expiring local ticket carries only launcher diagnostics, never save data.
public sealed class SessionTicket
{
    public string Session { get; }
    public int Seconds { get; }
    public string LogPath { get; }
    public string Mode { get; }

    private SessionTicket(string session, int seconds, string logPath, string mode)
    { Session = session; Seconds = seconds; LogPath = logPath; Mode = mode; }

    public static SessionTicket Consume(string path, DateTime utcNow)
        => ConsumeIfPresent(path, utcNow) ?? throw new FileNotFoundException("No protected launch ticket.", path);

    // Only absence at the atomic claim is vanilla. Invalid/unreadable tickets
    // remain errors; do not downgrade a requested protected launch.
    public static SessionTicket? ConsumeIfPresent(string path, DateTime utcNow)
    {
        var claim = path + ".claim-" + Guid.NewGuid().ToString("N");
        try { File.Move(path, claim); } // Another process cannot consume the same ticket.
        catch (FileNotFoundException) { return null; }
        try
        {
            var lines = File.ReadAllLines(claim);
            if ((lines.Length != 4 && lines.Length != 5) || !Guid.TryParse(lines[0], out var session)
                || !DateTime.TryParseExact(lines[1], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiry)
                || expiry.Kind != DateTimeKind.Utc || expiry <= utcNow || expiry > utcNow.AddMinutes(3)
                || !int.TryParse(lines[2], out int seconds) || (seconds != 0 && seconds < 10) || seconds > 10980
                || !Path.IsPathRooted(lines[3]) || !Directory.Exists(Path.GetDirectoryName(lines[3])))
                throw new InvalidOperationException("Invalid or expired protected launch ticket.");
            string mode = lines.Length == 5 ? lines[4] : "baseline";
            if (mode != "baseline" && mode != "bridge" && mode != "inspect-hk" && mode != "preview-hk" && mode != "human-hk" && mode != "inspect-ns-template" && mode != "memory-ns" && mode != "pose-hk" && mode != "pose-ns" && mode != "input-hk" && mode != "input-ns" && mode != "menu-test") throw new InvalidOperationException("Unknown launch mode.");
            if (mode == "menu-test" && (seconds < 10 || !File.Exists(Path.Combine(Path.GetDirectoryName(lines[3])!, "native-menu.fixture")))) throw new InvalidOperationException("Native menu tests require explicit bounded fixture authorization.");
            if(seconds>600 && !ValidEndurancePlan(lines,mode,seconds,session))throw new InvalidOperationException("Extended session requires an authenticated finite endurance plan.");
            bool unlimitedHuman = (mode == "input-hk" || mode == "input-ns")
                && File.Exists(Path.Combine(Path.GetDirectoryName(lines[3])!, "unlimited-human.request"))
                && !File.Exists(Path.Combine(Path.GetDirectoryName(lines[3])!, "scripted-input.request"));
            if (seconds == 0 && !unlimitedHuman) throw new InvalidOperationException("Unlimited sessions require explicit human input mode.");
            bool extendedDiagnostic = File.Exists(Path.Combine(Path.GetDirectoryName(lines[3])!, "extended-diagnostic.request"))
                && File.Exists(Path.Combine(Path.GetDirectoryName(lines[3])!, "scripted-input.request"));
            bool boundedHitProbe = (seconds <= 200 || extendedDiagnostic) && (mode == "input-hk" || mode == "input-ns")
                && File.Exists(Path.Combine(Path.GetDirectoryName(lines[3])!, "hit-probe.request"));
            bool humanHubProbe = (mode == "input-hk" || mode == "input-ns")
                && File.Exists(Path.Combine(Path.GetDirectoryName(lines[3])!, "hub-probe.request"))
                && !File.Exists(Path.Combine(Path.GetDirectoryName(lines[3])!, "scripted-input.request"));
            if (seconds > 120 && mode != "human-hk" && mode != "menu-test" && !boundedHitProbe && !humanHubProbe) throw new InvalidOperationException("Long sessions require a human test or explicit bounded hit probe.");
            return new SessionTicket(session.ToString(), seconds, Path.GetFullPath(lines[3]), mode);
        }
        finally { File.Delete(claim); }
    }
    private static bool ValidEndurancePlan(string[] ticket,string mode,int seconds,Guid session) {
        if(mode!="menu-test")return false;
        string folder=Path.GetDirectoryName(ticket[3])!;
        var descriptor=File.ReadAllLines(Path.Combine(folder,"native-menu.fixture"));
        if(descriptor.Length!=5 || descriptor[0]!=session.ToString() || descriptor[2]!="combat-Endurance-Sweep")return false;
        var plan=File.ReadAllLines(Path.Combine(folder,"endurance-plan.request"));
        if(plan.Length<2 || plan.Length>46 || plan[0]!=session.ToString() || seconds<(plan.Length-1)*180+120 || seconds>(plan.Length-1)*240+180)return false;
        var seen=new System.Collections.Generic.HashSet<string>();
        for(int i=1;i<plan.Length;i++)if(!seen.Add(plan[i]) || !System.Text.RegularExpressions.Regex.IsMatch(plan[i],"^[A-Za-z]{1,40}$"))return false;
        return true;
    }

}
