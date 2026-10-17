using System;
using System.IO;
using System.Reflection;

namespace Doorstop;

// No reference to game assemblies or BepInEx: this must run before either is loaded.
public static class Entrypoint
{
    public const string Marker = "YiGodhome.GuardedBaselineV1";
    private static bool guarded;

    public static void Start()
    {
        try
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("YIGODHOME_SESSION")))
            {
                var ticket = SessionTicket.ConsumeIfPresent(Path.Combine(Path.GetDirectoryName(typeof(Entrypoint).Assembly.Location)!, "YiGodhome.session"), DateTime.UtcNow);
                // Ordinary Steam launch: no BepInEx, patches, hooks or save access.
                if (ticket == null)
                {
                    string menuMarker = Path.Combine(Path.GetDirectoryName(typeof(Entrypoint).Assembly.Location)!, "HollowSols.menu");
                    if (!File.Exists(menuMarker)) return;
                    if (File.ReadAllText(menuMarker) != "HollowSols native menu schema 1\n") throw new InvalidOperationException("Invalid native-menu installation marker.");
                    Environment.SetEnvironmentVariable("HOLLOWSOLS_MENU", "1");
                    Environment.SetEnvironmentVariable("YIGODHOME_MODE", "menu");
                    string log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HollowSols", "logs", System.Diagnostics.Process.GetCurrentProcess().Id.ToString(), "bootstrap.log");
                    Directory.CreateDirectory(Path.GetDirectoryName(log)!);
                    Environment.SetEnvironmentVariable("YIGODHOME_BOOT_LOG", log);
                }
                else {
                Environment.SetEnvironmentVariable("YIGODHOME_SESSION", ticket.Session);
                Environment.SetEnvironmentVariable("YIGODHOME_SMOKE_SECONDS", ticket.Seconds.ToString());
                Environment.SetEnvironmentVariable("YIGODHOME_BOOT_LOG", ticket.LogPath);
                Environment.SetEnvironmentVariable("YIGODHOME_BRIDGE", ticket.Mode == "bridge" ? "1" : "0");
                Environment.SetEnvironmentVariable("YIGODHOME_MODE", ticket.Mode);
                if (ticket.Mode == "menu-test")
                {
                    var fixture = File.ReadAllLines(Path.Combine(Path.GetDirectoryName(ticket.LogPath)!, "native-menu.fixture"));
                    if (fixture.Length != 5) throw new InvalidOperationException("Invalid native menu fixture descriptor.");
                    string fixtureRoot = Path.GetFullPath(fixture[1]);
                    string permitted = Path.GetFullPath(Path.GetDirectoryName(Path.GetDirectoryName(ticket.LogPath)!)!) + Path.DirectorySeparatorChar;
                    if (fixture.Length != 5 || fixture[0] != ticket.Session || !fixtureRoot.StartsWith(permitted, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(fixtureRoot) != "HollowSols") throw new InvalidOperationException("Unsafe native menu fixture namespace.");
                    Environment.SetEnvironmentVariable("HOLLOWSOLS_MENU", "1"); Environment.SetEnvironmentVariable("HOLLOWSOLS_MENU_FIXTURE", ticket.Session);
                    Environment.SetEnvironmentVariable("HOLLOWSOLS_SAVE_ROOT", fixtureRoot); Environment.SetEnvironmentVariable("YIGODHOME_SESSION", null);
                    Environment.SetEnvironmentVariable("YIGODHOME_MODE", "menu");
                }
                }
            }
            if (Environment.GetEnvironmentVariable("HOLLOWSOLS_MENU") != "1" && !Guid.TryParse(Environment.GetEnvironmentVariable("YIGODHOME_SESSION"), out _))
                throw new InvalidOperationException("Invalid protected YiGodhome session.");
            AppDomain.CurrentDomain.AssemblyLoad += InspectAssembly;
            foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies()) Inspect(loaded);
            var ownPath = Environment.GetEnvironmentVariable("DOORSTOP_INVOKE_DLL_PATH")
                ?? throw new InvalidOperationException("Doorstop did not supply the bootstrap location.");
            var loader = Assembly.LoadFile(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(ownPath))!, "BepInEx.Preloader.dll"));
            var entry = loader.GetType("Doorstop.Entrypoint", true)!.GetMethod("Start", BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMethodException("BepInEx entrypoint missing.");
            entry.Invoke(null, null);
            if (!guarded) throw new InvalidOperationException("Save protection did not load; refusing to start Unity gameplay.");
            var message = "[YiGodhome] BOOTSTRAP_GUARD_VERIFIED session=" + Environment.GetEnvironmentVariable("YIGODHOME_SESSION");
            File.AppendAllText(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG")!, message + Environment.NewLine);
        }
        catch (Exception error) { Fatal(error.ToString()); }
    }

    private static void InspectAssembly(object? sender, AssemblyLoadEventArgs args) => Inspect(args.LoadedAssembly);

    private static void Inspect(Assembly assembly)
    {
        if (assembly.GetName().Name != "Assembly-CSharp") return;
        string mode = Environment.GetEnvironmentVariable("YIGODHOME_MODE") ?? "baseline";
        string expected = mode == "menu" ? "YiGodhome.GuardedPairedSavesV1" : mode == "memory-ns" || mode == "pose-ns" || mode == "input-ns" ? "YiGodhome.GuardedBattleMemoriesV1" : mode == "preview-hk" || mode == "human-hk" || mode == "pose-hk" || mode == "input-hk" ? "YiGodhome.GuardedGodhomePreviewV1" : mode == "inspect-hk" ? "YiGodhome.GuardedHollowKnightInspectionV1" : Marker;
        if (assembly.GetType(expected, false) == null) Fatal("An unguarded or wrong-mode game assembly was loaded.");
        guarded = true;
    }

    private static void Fatal(string message)
    {
        try
        {
            var log = Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG");
            if (!string.IsNullOrEmpty(log)) File.AppendAllText(log, DateTime.UtcNow.ToString("O") + " FATAL " + message + Environment.NewLine);
        }
        finally { Environment.Exit(73); }
    }
}
