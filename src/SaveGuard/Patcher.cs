using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Mono.Cecil;

namespace YiGodhome.SaveGuard;

public static class Patcher
{
    public static IEnumerable<string> TargetDLLs { get { yield return "Assembly-CSharp.dll"; } }
    public static readonly IReadOnlyDictionary<string, string> KnownHashes = new Dictionary<string, string>
    {
        ["HollowKnight"] = "E9048EF6A633970F735E01EC166D3959F610EAEA7A88D827D48D67B1E5FB87BD",
        ["NineSols"] = "51F2921C61E237D7FC6A38F31DE2C8BEA8DD5826CF381D2FB8033B3C66AFD743"
    };

    public static void Patch(AssemblyDefinition assembly)
    {
        try
        {
            bool menu = Environment.GetEnvironmentVariable("HOLLOWSOLS_MENU") == "1" && Environment.GetEnvironmentVariable("YIGODHOME_MODE") == "menu";
            if (menu) HollowSols.Foundation.NativeSaveContext.ReadOnlyVanilla = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HOLLOWSOLS_MENU_FIXTURE"));
            if (!menu && !Guid.TryParse(Environment.GetEnvironmentVariable("YIGODHOME_SESSION"), out _)) throw new InvalidOperationException("No protected launch session.");
            var path = assembly.MainModule.FileName;
            var game = Weaver.Identify(assembly);
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                if (BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "") != KnownHashes[game])
                    throw new InvalidOperationException("Game assembly changed; re-audit save boundaries.");
            bool inspection = Environment.GetEnvironmentVariable("YIGODHOME_MODE") == "inspect-hk";
            bool preview = Environment.GetEnvironmentVariable("YIGODHOME_MODE") == "preview-hk" || Environment.GetEnvironmentVariable("YIGODHOME_MODE") == "human-hk" || Environment.GetEnvironmentVariable("YIGODHOME_MODE") == "pose-hk" || Environment.GetEnvironmentVariable("YIGODHOME_MODE") == "input-hk";
            bool memory = Environment.GetEnvironmentVariable("YIGODHOME_MODE") == "memory-ns" || Environment.GetEnvironmentVariable("YIGODHOME_MODE") == "pose-ns" || Environment.GetEnvironmentVariable("YIGODHOME_MODE") == "input-ns";
            var report = menu ? PairedSaveGate.Apply(assembly) : Weaver.Apply(assembly, inspection, preview, memory);
            Console.WriteLine("[YiGodhome] SAVE_GUARD_APPLIED game=" + game + " methods=" + report.Count + " mode=" + (menu ? "scoped-paired-native" : memory ? "memory-resource-only" : preview ? "godhome-read-only" : inspection ? "hk-slot4-read-only" : "no-save-io"));
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("[YiGodhome] SAVE_GUARD_FATAL " + error);
            Environment.Exit(74); // BepInEx normally catches patcher errors and continues. We must not.
        }
    }
}
