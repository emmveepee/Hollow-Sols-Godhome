using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace YiGodhome.Bridge;

// Compatibility boundary for existing launch tickets. Files are read once;
// gameplay code consumes validated immutable capabilities, never live flags.
public sealed class SessionConfiguration
{
    private static readonly string[] names = {
        "visual", "proxy", "arena", "hub-probe", "hub-combat", "contact-bridge", "target-probe", "hit-probe",
        "lifecycle-probe", "retry-probe", "scripted-input", "unlimited-human", "cheat-probe", "invulnerable-probe", "kick-probe", "talisman-traversal-probe", "wall-probe",
        "rest-probe", "lever-probe", "travel-probe", "pause-probe", "pantheon-probe", "injury-refusal", "crimson-probe", "combat-probe", "effects-probe", "await-geometry", "export-geometry",
        "enemy-probe", "contact-probe", "death-probe", "defeat-probe", "encounter-audit", "combat-audit", "content-audit", "canvas-service-audit", "jade-probe", "performance-probe", "canvas-baseline", "extended-diagnostic", "failure-probe", "switch-probe", "pv-tuning-probe", "reflection-finish-probe", "markoth-shield-probe", "markoth-projectile-probe", "homing-probe", "stagger-probe", "configuration-probe", "trace-probe", "hud-reference-probe", "void-probe", "engine-perf", "native-perf"
    };
    private readonly HashSet<string> enabled;
    private static readonly Lazy<SessionConfiguration> current = new Lazy<SessionConfiguration>(Load);
    private static SessionConfiguration? paired;
    public static SessionConfiguration Current => paired ?? current.Value;
    public static void EnterPaired(string mode)
    {
        if (paired != null) throw new InvalidOperationException("A paired gameplay configuration is already active.");
        paired = new SessionConfiguration(new[] { "visual", "effects-probe", "proxy", "arena", "hub-probe", "hub-combat", "contact-bridge", "target-probe", "hit-probe", "combat-probe", "await-geometry", "export-geometry", "unlimited-human" }, mode, 0);
    }
    public static void LeavePaired() => paired = null;
    public bool Scripted => Has("scripted-input");
    public bool Performance => Has("performance-probe");
    public bool RunFixtures => BuildFeatures.DevelopmentTools && Scripted && !Performance;
    public bool Cheats => Has("cheat-probe");
    public bool Visual => Has("visual");
    public bool Proxy => Has("proxy");
    public bool Arena => Has("arena");
    public bool Hub => Has("hub-probe");
    public bool HubCombat => Has("hub-combat");
    public bool ManualTools => BuildFeatures.DevelopmentTools && HubCombat && !Scripted;
    public bool Contacts => Has("contact-bridge");
    public bool Targets => Has("target-probe");
    public bool Hits => Has("hit-probe");
    public string FailureBoundary { get; }
    public int Pantheon { get; }
    public int PantheonStart { get; }
    public bool Has(string name) => enabled.Contains(name);
    public SessionConfiguration(IEnumerable<string> flags, string mode, int seconds, string failureBoundary = "", int pantheon = 0, int pantheonStart = 0)
    {
        enabled = new HashSet<string>(flags, StringComparer.Ordinal);
        FailureBoundary = failureBoundary;
        Pantheon = pantheon; PantheonStart = pantheonStart;
        if (Has("pantheon-probe") != (pantheon != 0) || pantheon < 0 || pantheon > 4 || pantheonStart < 0 || pantheonStart > 20
            || pantheon == 0 && pantheonStart != 0 || pantheon != 0 && (!Scripted || !HubCombat || Performance || seconds <= 0 || seconds > 600))
            throw new InvalidDataException("Pantheon fixture requires P1..4 and a bounded scripted combat session.");
        if (pantheon != 0 && new[] { "kick-probe", "talisman-traversal-probe", "wall-probe", "rest-probe", "lever-probe", "travel-probe", "pause-probe", "void-probe", "reflection-finish-probe", "markoth-shield-probe", "markoth-projectile-probe", "homing-probe", "stagger-probe", "pv-tuning-probe", "switch-probe", "failure-probe" }.Any(Has))
            throw new InvalidDataException("Pantheon fixture cannot combine unrelated automated scenarios.");
        foreach (string flag in enabled) if (!names.Contains(flag)) throw new InvalidDataException("Unknown session capability: " + flag);
        bool input = mode == "input-hk" || mode == "input-ns";
        if (Has("engine-perf") && (seconds <= 0 || seconds > 120 || mode != "baseline" && mode != "bridge" && mode != "preview-hk"))
            throw new InvalidDataException("Standalone engine timing requires a bounded baseline/native preview.");
        if (Has("native-perf") && (mode != "preview-hk" || !Has("engine-perf") || !Has("encounter-audit") || enabled.Count != 3))
            throw new InvalidDataException("Native benchmark requires only bounded preview, engine timing and encounter selection.");
        if (Has("canvas-baseline") && (!Performance || !Scripted || !HubCombat || seconds <= 0))
            throw new InvalidDataException("Canvas comparison baseline requires bounded fixed-idle combat services.");
        if (Has("content-audit") && (mode != "memory-ns" || seconds != 120 || enabled.Count != 1))
            throw new InvalidDataException("Content acquisition proof requires an isolated bounded native memory session.");
        if (Has("canvas-service-audit") && (mode != "memory-ns" || seconds != 120 || enabled.Count != 2 || !Has("enemy-probe")))
            throw new InvalidDataException("Canvas service inventory requires isolated bounded native bootstrap preparation.");
        if (Has("jade-probe") && (mode != "memory-ns" || seconds != 120 || enabled.Count != 2 || !Has("enemy-probe")))
            throw new InvalidDataException("Jade comparison requires isolated bounded native contact preparation.");
        if (Scripted && (!input || seconds <= 0 || seconds > 600)) throw new InvalidDataException("Scripted gameplay requires a bounded input session.");
        foreach (string diagnostic in new[] { "configuration-probe", "cheat-probe", "invulnerable-probe", "kick-probe", "talisman-traversal-probe", "wall-probe", "rest-probe", "lever-probe", "injury-refusal", "crimson-probe", "retry-probe", "performance-probe", "canvas-baseline", "extended-diagnostic", "switch-probe", "pv-tuning-probe", "reflection-finish-probe", "markoth-shield-probe", "markoth-projectile-probe", "homing-probe", "stagger-probe", "void-probe" })
            if (Has(diagnostic) && !Scripted) throw new InvalidDataException("Diagnostic requires scripted authorization: " + diagnostic);
        if (seconds == 0 && (!input || !Has("unlimited-human") || Scripted)) throw new InvalidDataException("Unlimited runtime requires explicit human input.");
        Require("proxy", "visual"); Require("arena", "proxy"); Require("hub-probe", "proxy");
        Require("contact-bridge", "arena"); Require("target-probe", "contact-bridge"); Require("hit-probe", "target-probe");
        Require("hub-combat", "hub-probe"); Require("hub-combat", "hit-probe"); Require("retry-probe", "hit-probe"); Require("invulnerable-probe", "cheat-probe"); Require("kick-probe", "cheat-probe"); Require("talisman-traversal-probe", "cheat-probe");
        if (Has("invulnerable-probe") && (Performance || Has("failure-probe") || Pantheon != 0 || new[] { "kick-probe", "crimson-probe", "pv-tuning-probe", "reflection-finish-probe", "markoth-shield-probe", "markoth-projectile-probe", "stagger-probe", "talisman-traversal-probe" }.Any(Has)))
            throw new InvalidDataException("Invulnerable fixture is victory-only; cannot certify defense, damage or defeat.");
        if (Has("trace-probe") && (!Scripted || !HubCombat || Performance || Pantheon != 0)) throw new InvalidDataException("Trace fixture requires bounded scripted Godhome gameplay.");
        Require("hud-reference-probe","trace-probe");
        Require("switch-probe", "hub-combat"); Require("void-probe", "hub-combat");
        Require("lever-probe", "hub-combat");
        if (Has("travel-probe") && (!Scripted || !HubCombat || Cheats)) throw new InvalidDataException("Travel fixture requires bounded scripted combat without grants.");
        Require("homing-probe", "cheat-probe"); Require("stagger-probe", "cheat-probe"); Require("markoth-projectile-probe", "cheat-probe"); Require("markoth-shield-probe", "cheat-probe"); Require("pv-tuning-probe", "cheat-probe"); Require("reflection-finish-probe", "cheat-probe");
        if (Has("pause-probe") && (!Scripted || !Cheats || Performance || seconds <= 0)) throw new InvalidDataException("Pause fixture requires bounded scripted combat grants.");
        Require("configuration-probe", "cheat-probe"); Require("cheat-probe", "hub-combat"); Require("rest-probe", "hub-combat"); Require("wall-probe", "hub-combat");
        Require("injury-refusal", "hit-probe"); Require("crimson-probe", "combat-probe"); Require("extended-diagnostic", "hit-probe");
        Require("contact-probe", "enemy-probe"); Require("death-probe", "enemy-probe"); Require("defeat-probe", "encounter-audit");
        foreach (string flag in new[] { "enemy-probe", "contact-probe", "death-probe", "combat-audit" })
            if (Has(flag) && (mode != "memory-ns" || seconds <= 0)) throw new InvalidDataException(flag + " requires bounded native-only Yi preview.");
        foreach (string flag in new[] { "encounter-audit", "defeat-probe" })
            if (Has(flag) && (mode != "preview-hk" || seconds <= 0)) throw new InvalidDataException(flag + " requires bounded native-only HK preview.");
        if (Visual && !input) throw new InvalidDataException("Visual gameplay requires input mode.");
        if (Performance && (!Hub || Hits && !HubCombat || Cheats || Has("rest-probe") || Has("wall-probe") || Has("lever-probe") || Has("travel-probe") || Has("void-probe")))
            throw new InvalidDataException("Fixed idle benchmark requires hub gameplay without fixtures or modifiers.");
        if (Performance && new[] { "retry-probe", "lifecycle-probe", "injury-refusal", "crimson-probe", "failure-probe", "switch-probe", "extended-diagnostic" }.Any(Has))
            throw new InvalidDataException("Fixed idle benchmark cannot acquire encounter fixtures.");
        if (Has("failure-probe") != (failureBoundary.Length != 0)
            || failureBoundary.Length != 0 && (!Scripted || !HubCombat || Performance || !new[] { "Preparing", "Active", "Victory", "Defeated", "Returned" }.Contains(failureBoundary)))
            throw new InvalidDataException("Failure injection requires one bounded scripted combat boundary.");
    }
    private void Require(string flag, string dependency)
    { if (Has(flag) && !Has(dependency)) throw new InvalidDataException(flag + " requires " + dependency); }
    private static SessionConfiguration Load()
    {
        string? log = Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG");
        string mode = Environment.GetEnvironmentVariable("YIGODHOME_MODE") ?? "baseline";
        int.TryParse(Environment.GetEnvironmentVariable("YIGODHOME_SMOKE_SECONDS"), out int seconds);
        if (string.IsNullOrEmpty(log)) return new SessionConfiguration(Array.Empty<string>(), "baseline", 30);
        string root = Path.GetDirectoryName(log)!;
        string fault = Path.Combine(root, "failure-probe.request");
        string sequence = Path.Combine(root, "pantheon-probe.request");
        int pantheon = 0, start = 0;
        if (File.Exists(sequence)) {
            var fields = File.ReadAllText(sequence).Trim().Split(':');
            if (fields.Length != 2 || !int.TryParse(fields[0], out pantheon) || !int.TryParse(fields[1], out start))
                throw new InvalidDataException("Invalid Pantheon fixture selection.");
        }
        return new SessionConfiguration(names.Where(n => File.Exists(Path.Combine(root, n + ".request"))), mode, seconds,
            File.Exists(fault) ? File.ReadAllText(fault).Trim() : "", pantheon, start);
    }
    public override string ToString() => string.Join(",", enabled.OrderBy(n => n));
}
