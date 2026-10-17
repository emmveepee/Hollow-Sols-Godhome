using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;
using YiGodhome.SaveGuard;
using MA = Mono.Cecil.MethodAttributes;
using TA = Mono.Cecil.TypeAttributes;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "public-build") return PublicBuildChecks.Run(args.Skip(1).ToArray());
            if (args.Length > 0) return BootstrapChild(args[0]);
            SyntheticCallbacks();
            SyntheticReadGate();
            GodhomePolicy();
            BootstrapFailures();
            Tickets();
            CheckGame("C:/Steam/steamapps/common/Hollow Knight/hollow_knight_Data/Managed");
            CheckGame("C:/Steam/steamapps/common/Nine Sols/NineSols_Data/Managed");
            InspectionVariant();
            GodhomeVariant();
            MemoryChecks.Run();
            PairedGuardChecks.Run();
            Console.WriteLine("PASS: callback behavior, both installed assembly guards, in-memory serialization, unchanged source hashes.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    private static void GodhomePolicy()
    {
        using var a = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("GodhomePolicy" + Guid.NewGuid().ToString("N"), new Version(1, 0)), "fixture", ModuleKind.Dll);
        GodhomeGate.CreatePolicy(a.MainModule);
        using var bytes = new MemoryStream(); a.Write(bytes);
        var type = Assembly.Load(bytes.ToArray()).GetType("YiGodhome.GodhomePolicy")!;
        var continuation = type.GetMethod("CanContinue")!;
        var transition = type.GetMethod("CanTransition")!;
        foreach (int managerProfile in new[] { -1, 0, 1, 2, 3, 4, 5 })
        foreach (int dataProfile in new[] { 0, 1, 2, 3, 4, 5 })
        foreach (bool godseeker in new[] { false, true })
        foreach (string? scene in new[] { null, "", "GG_Atrium", "GG_Workshop", "gg_Atrium", "Town", "Menu_Title", "Quit_To_Menu" })
        {
            bool valid = managerProfile == 4 && dataProfile == 4 && godseeker && scene != null && scene.StartsWith("GG_", StringComparison.Ordinal);
            object?[] args = { managerProfile, dataProfile, godseeker, scene };
            Require((bool)continuation.Invoke(null, args)! == valid, "Godhome continuation policy failed");
            Require((bool)transition.Invoke(null, args)! == (valid || scene == "Menu_Title" || scene == "Quit_To_Menu"), "Godhome transition policy failed");
        }
        Console.WriteLine("PASS: injected Godhome policy executed against profile, mode and scene combinations");
    }

    private static void GodhomeVariant()
    {
        const string managed = "C:/Steam/steamapps/common/Hollow Knight/hollow_knight_Data/Managed";
        using var resolver = new DefaultAssemblyResolver(); resolver.AddSearchDirectory(managed);
        using var a = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "Assembly-CSharp.dll"), new ReaderParameters { AssemblyResolver = resolver });
        Weaver.Apply(a, false, true);
        var manager = a.MainModule.GetType("GameManager");
        Require(manager.Methods.Single(m => m.Name == "QueueAchievement").Body.Instructions.Select(i => i.Operand).OfType<MethodReference>()
            .All(m => m.DeclaringType.FullName == "System.Console"),
            "Protected native Pantheon completion can persist an achievement");
        var quit = manager.Methods.Single(m => m.Name == "ReturnToMainMenu");
        Require(quit.Body.Instructions[0].OpCode == OpCodes.Ldc_I4_2 && quit.Body.Instructions[1].OpCode == OpCodes.Starg,
            "Godhome quit does not select native DontSave mode");
        foreach (string name in new[] { "RunContinueGame", "BeginSceneTransition", "LoadScene", "LoadSceneAdditive" })
        {
            var method = manager.Methods.Single(m => m.Name == name);
            Require(method.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().Any(m => m.DeclaringType.FullName == "YiGodhome.GodhomePolicy"), "Missing scene gate: " + name);
        }
        foreach (var type in new[] { manager, a.MainModule.GetType("DesktopPlatform") })
        foreach (var method in type.Methods.Where(m => new[] { "SaveGame", "WriteSaveSlot", "ClearSaveSlot", "ClearSaveFile", "StartNewGame", "ContinueGame", "LoadGameFromUI" }.Contains(m.Name)))
            foreach (var call in method.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>())
                Require(call.DeclaringType.FullName == "System.Console" || call.DeclaringType.FullName.StartsWith("System.Action`1<", StringComparison.Ordinal), "Preview enabled native save/menu mutation: " + method.FullName);
        using var bytes = new MemoryStream(); a.Write(bytes); bytes.Position = 0;
        using var checkedAssembly = AssemblyDefinition.ReadAssembly(bytes);
        Require(checkedAssembly.MainModule.GetType(Weaver.GodhomeMarker) != null, "Godhome marker missing");
        using var ns = AssemblyDefinition.ReadAssembly("C:/Steam/steamapps/common/Nine Sols/NineSols_Data/Managed/Assembly-CSharp.dll");
        bool refused = false; try { Weaver.Apply(ns, false, true); } catch (InvalidOperationException) { refused = true; }
        Require(refused, "HK preview accepted in Nine Sols");
        Console.WriteLine("PASS: Godhome variant serialized with scene gates; saving/menu entry remain blocked");
    }

    private static void SyntheticReadGate()
    {
        using var a = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("ReadGate" + Guid.NewGuid().ToString("N"), new Version(1, 0)), "fixture", ModuleKind.Dll);
        var module = a.MainModule;
        var type = new TypeDefinition("Fixture", "ReadGate", TA.Public | TA.Abstract | TA.Sealed, module.TypeSystem.Object);
        module.Types.Add(type);
        var method = new MethodDefinition("Read", MA.Public | MA.Static, module.TypeSystem.Int32);
        method.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4, 42));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(method);
        Weaver.ReadOnlySlotFour(method, new List<string>());
        foreach (var instruction in method.Body.Instructions)
            if (instruction.Operand is MethodReference call && call.DeclaringType.FullName == "System.Console")
                instruction.Operand = module.ImportReference(typeof(Console).GetMethod("WriteLine", new[] { typeof(string) })!);
        using var bytes = new MemoryStream(); a.Write(bytes);
        var read = Assembly.Load(bytes.ToArray()).GetType("Fixture.ReadGate")!.GetMethod("Read")!;
        foreach (int value in new[] { int.MinValue, -1, 0, 1, 2, 3, 4, 5, 42, int.MaxValue })
            Require((int)read.Invoke(null, new object[] { value })! == (value == 4 ? 42 : 0), "Synthetic integer allowlist failed: " + value);
        Console.WriteLine("PASS: synthetic read gate executes original code only for integer 4 (no game/save execution)");
    }

    private static void InspectionVariant()
    {
        const string managed = "C:/Steam/steamapps/common/Hollow Knight/hollow_knight_Data/Managed";
        using var resolver = new DefaultAssemblyResolver(); resolver.AddSearchDirectory(managed);
        using var a = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "Assembly-CSharp.dll"), new ReaderParameters { AssemblyResolver = resolver });
        var nativeRead = a.MainModule.GetType("DesktopPlatform").Methods.Single(m => m.Name == "LocalReadSaveSlot");
        var fileCalls = nativeRead.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().Where(m => m.DeclaringType.FullName == "System.IO.File").Select(m => m.Name).ToArray();
        Require(fileCalls.Length == 2 && fileCalls.Contains("Exists") && fileCalls.Contains("ReadAllBytes"), "Native read now has unexpected file operations");
        var report = Weaver.Apply(a, true);
        var allowed = new[] { "IsSaveSlotInUse", "LocalIsSaveSlotInUse", "ReadSaveSlot", "LocalReadSaveSlot", "GetSaveSlotPath", "LoadGame" };
        foreach (var m in Weaver.AllTypes(a.MainModule.Types).SelectMany(t => t.Methods).Where(m => report.Contains(m.FullName)))
        {
            if (allowed.Contains(m.Name) && m.DeclaringType.Name != "GameCoreOnlineSubsystem")
                Require(m.Body.Instructions[0].OpCode == OpCodes.Ldarg && m.Body.Instructions[1].OpCode == OpCodes.Ldc_I4_4 && m.Body.Instructions[2].OpCode == OpCodes.Beq, "Missing read-slot prefix: " + m.FullName);
            else foreach (var call in m.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>())
                Require(call.DeclaringType.FullName == "System.Console" || call.DeclaringType.FullName.StartsWith("System.Action`1<", StringComparison.Ordinal), "Write/gameplay boundary was enabled: " + m.FullName);
        }
        using var bytes = new MemoryStream(); a.Write(bytes); bytes.Position = 0;
        using var checkedAssembly = AssemblyDefinition.ReadAssembly(bytes);
        Require(checkedAssembly.MainModule.GetType(Weaver.InspectionMarker) != null && checkedAssembly.MainModule.GetType(Weaver.Marker) == null, "Inspection marker incorrect");
        using var ns = AssemblyDefinition.ReadAssembly("C:/Steam/steamapps/common/Nine Sols/NineSols_Data/Managed/Assembly-CSharp.dll");
        bool refused = false; try { Weaver.Apply(ns, true); } catch (InvalidOperationException) { refused = true; }
        Require(refused, "HK inspection accepted in Nine Sols");
        Console.WriteLine("PASS: read-only HK variant serialized; writes/gameplay blocked; Nine Sols rejected");
    }

    private static void Tickets()
    {
        var directory = Path.Combine(Path.GetTempPath(), "YiGodhomeTicket-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ticket");
        try
        {
            var now = DateTime.UtcNow;
            var session = Guid.NewGuid().ToString();
            void Write(DateTime expiry) => File.WriteAllLines(path, new[] { session, expiry.ToString("O"), "20", Path.Combine(directory, "bootstrap.log") });
            Write(now.AddMinutes(2));
            var ticket = Doorstop.SessionTicket.Consume(path, now);
            Require(ticket.Session == session && ticket.Seconds == 20 && !File.Exists(path), "Ticket not consumed");
            bool replayDenied = false;
            try { Doorstop.SessionTicket.Consume(path, now); } catch (IOException) { replayDenied = true; }
            Require(replayDenied, "Ticket replay accepted");
            Write(now.AddSeconds(-1));
            bool expiredDenied = false;
            try { Doorstop.SessionTicket.Consume(path, now); } catch (InvalidOperationException) { expiredDenied = true; }
            Require(expiredDenied && !Directory.EnumerateFiles(directory).Any(), "Expired ticket accepted or claim leaked");
            Write(now.AddMinutes(2)); File.AppendAllText(path, "bridge" + Environment.NewLine);
            Require(Doorstop.SessionTicket.Consume(path, now).Mode == "bridge", "Bridge ticket not decoded");
            foreach (string mode in new[] { "input-hk", "input-ns" })
            {
                Write(now.AddMinutes(2)); File.AppendAllText(path, mode + Environment.NewLine);
                Require(Doorstop.SessionTicket.Consume(path, now).Mode == mode, "Input ticket not decoded");
            }
            Write(now.AddMinutes(2)); File.AppendAllText(path, "unknown-mode" + Environment.NewLine);
            bool modeDenied = false;
            try { Doorstop.SessionTicket.Consume(path, now); } catch (InvalidOperationException) { modeDenied = true; }
            Require(modeDenied, "Unknown ticket mode accepted");
            foreach (string mode in new[] { "human-hk", "preview-hk", "baseline", "input-hk", "input-ns" })
            {
                File.WriteAllLines(path, new[] { session, now.AddMinutes(2).ToString("O"), "300", Path.Combine(directory, "bootstrap.log"), mode });
                bool accepted = false;
                try { accepted = Doorstop.SessionTicket.Consume(path, now).Seconds == 300; } catch (InvalidOperationException) { }
                Require(accepted == (mode == "human-hk"), "Long-session mode boundary failed");
            }
            foreach (bool requested in new[] { false, true })
            {
                if (requested) File.WriteAllText(Path.Combine(directory, "hit-probe.request"), "bounded test");
                foreach (string mode in new[] { "input-hk", "input-ns", "baseline" })
                foreach (int seconds in new[] { 200, 201 })
                {
                    File.WriteAllLines(path, new[] { session, now.AddMinutes(2).ToString("O"), seconds.ToString(), Path.Combine(directory, "bootstrap.log"), mode });
                    bool accepted = false;
                    try { accepted = Doorstop.SessionTicket.Consume(path, now).Seconds == seconds; } catch (InvalidOperationException) { }
                    Require(accepted == (requested && seconds == 200 && mode != "baseline"), "Bounded hit-probe duration boundary failed");
                }
            }
            File.WriteAllText(Path.Combine(directory, "extended-diagnostic.request"), "bounded");
            foreach (bool scripted in new[] { false, true }) {
                if (scripted) File.WriteAllText(Path.Combine(directory, "scripted-input.request"), "explicit");
                foreach (int seconds in new[] { 300, 600, 601 }) {
                    File.WriteAllLines(path, new[] { session, now.AddMinutes(2).ToString("O"), seconds.ToString(), Path.Combine(directory, "bootstrap.log"), "input-hk" });
                    bool accepted = false;
                    try { accepted = Doorstop.SessionTicket.Consume(path, now).Seconds == seconds; } catch (InvalidOperationException) { }
                    Require(accepted == (scripted && seconds <= 600), "Extended diagnostic escaped bounded scripted authorization");
                }
            }
            File.Delete(Path.Combine(directory, "extended-diagnostic.request"));
            File.Delete(Path.Combine(directory, "scripted-input.request"));
            File.Delete(Path.Combine(directory, "hit-probe.request"));
            File.WriteAllText(Path.Combine(directory, "hub-probe.request"), "human traversal");
            foreach (bool scripted in new[] { false, true })
            {
                if (scripted) File.WriteAllText(Path.Combine(directory, "scripted-input.request"), "automated");
                foreach (string mode in new[] { "input-hk", "input-ns", "baseline", "preview-hk" })
                foreach (int seconds in new[] { 540, 600, 601 })
                {
                    File.WriteAllLines(path, new[] { session, now.AddMinutes(2).ToString("O"), seconds.ToString(), Path.Combine(directory, "bootstrap.log"), mode });
                    bool accepted = false;
                    try { accepted = Doorstop.SessionTicket.Consume(path, now).Seconds == seconds; } catch (InvalidOperationException) { }
                    Require(accepted == (!scripted && seconds <= 600 && (mode == "input-hk" || mode == "input-ns")), "Human hub duration boundary failed");
                }
            }
            File.Delete(Path.Combine(directory, "scripted-input.request"));
            foreach (bool requested in new[] { false, true })
            foreach (bool scripted in new[] { false, true })
            {
                var request = Path.Combine(directory, "unlimited-human.request");
                var script = Path.Combine(directory, "scripted-input.request");
                if (requested) File.WriteAllText(request, "manual exit"); else File.Delete(request);
                if (scripted) File.WriteAllText(script, "automated"); else File.Delete(script);
                foreach (string mode in new[] { "input-hk", "input-ns", "baseline", "human-hk", "preview-hk" })
                foreach (int seconds in new[] { -1, 0, 1, 9 })
                {
                    File.WriteAllLines(path, new[] { session, now.AddMinutes(2).ToString("O"), seconds.ToString(), Path.Combine(directory, "bootstrap.log"), mode });
                    bool accepted = false;
                    try { accepted = Doorstop.SessionTicket.Consume(path, now).Seconds == seconds; } catch (InvalidOperationException) { }
                    Require(accepted == (seconds == 0 && requested && !scripted && (mode == "input-hk" || mode == "input-ns")), "Unlimited human duration boundary failed");
                }
            }
            // Long automated sessions are admitted only by the exact finite
            // continuous-menu plan; ordinary menu/human caps stay unchanged.
            foreach(bool matching in new[]{false,true})
            foreach(bool duplicate in new[]{false,true})
            foreach(string mode in new[]{"menu-test","baseline","input-hk"})
            foreach(int seconds in new[]{600,660,661,10981}) {
                File.WriteAllLines(Path.Combine(directory,"native-menu.fixture"),new[]{session,directory,"combat-Endurance-Sweep","0","HollowKnight"});
                File.WriteAllLines(Path.Combine(directory,"endurance-plan.request"),new[]{matching?session:Guid.NewGuid().ToString(),"GruzMother",duplicate?"GruzMother":"VengeflyKing"});
                File.WriteAllLines(path,new[]{session,now.AddMinutes(2).ToString("O"),seconds.ToString(),Path.Combine(directory,"bootstrap.log"),mode});
                bool accepted=false;
                try{accepted=Doorstop.SessionTicket.Consume(path,now).Seconds==seconds;}catch(InvalidOperationException){}
                Require(accepted==(mode=="menu-test" && (seconds==600 || seconds==660 && matching && !duplicate)),"Continuous endurance escaped plan/mode/budget identity.");
            }
            File.Delete(Path.Combine(directory,"native-menu.fixture"));
            File.Delete(Path.Combine(directory,"endurance-plan.request"));
            Console.WriteLine("PASS: ticket replay/expiry, bounded diagnostics and explicit unlimited human sessions enforced");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static int BootstrapChild(string mode)
    {
        Environment.SetEnvironmentVariable("YIGODHOME_SESSION", null);
        Environment.SetEnvironmentVariable("YIGODHOME_BOOT_LOG", null);
        if (mode == "vanilla") {
            Doorstop.Entrypoint.Start();
            using var native = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("Assembly-CSharp", new Version(1, 0)), "fixture", ModuleKind.Dll);
            using var bytes = new MemoryStream(); native.Write(bytes);
            Assembly.Load(bytes.ToArray()); // Must not have registered the protected assembly hook.
            Require(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name!.StartsWith("BepInEx", StringComparison.Ordinal)), "Vanilla loaded BepInEx");
            Require(Environment.GetEnvironmentVariable("YIGODHOME_SESSION") == null, "Vanilla created a protected session");
            return 0;
        }
        else if (mode == "invalid-session") {
            Environment.SetEnvironmentVariable("YIGODHOME_SESSION", "invalid");
            Doorstop.Entrypoint.Start();
        }
        else
        {
            using var a = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("Assembly-CSharp", new Version(1, 0)), "fixture", ModuleKind.Dll);
            using var stream = new MemoryStream();
            a.Write(stream);
            var unguarded = Assembly.Load(stream.ToArray());
            typeof(Doorstop.Entrypoint).GetMethod("Inspect", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { unguarded });
        }
        return 99; // Fail-closed methods must terminate before this line.
    }

    private static void BootstrapFailures()
    {
        foreach (var mode in new[] { "vanilla", "invalid-session", "unguarded-assembly" })
        {
            var start = new System.Diagnostics.ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
            start.ArgumentList.Add(mode);
            using var process = System.Diagnostics.Process.Start(start)!;
            Require(process.WaitForExit(10000), "Bootstrap failure test timed out");
            Require(process.ExitCode == (mode == "vanilla" ? 0 : 73), "Bootstrap routing failed: " + mode);
        }
        Console.WriteLine("PASS: vanilla bypasses loader/hooks; invalid protected session and unguarded assembly exit 73 in isolated child processes");
    }

    private static void Require(bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException(reason);
    }

    private static void CheckGame(string managed)
    {
        var path = Path.Combine(managed, "Assembly-CSharp.dll");
        var before = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(managed);
        using var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = resolver });
        var game = Weaver.Identify(assembly);
        Require(Patcher.KnownHashes[game] == before, "Version not audited: " + game);
        var report = Weaver.Apply(assembly);
        Require(report.Count >= (game == "HollowKnight" ? 20 : 45), "Unexpected guard coverage: " + report.Count);
        var methods = Weaver.AllTypes(assembly.MainModule.Types).SelectMany(t => t.Methods).ToLookup(m => m.FullName);
        foreach (var signature in report)
        {
            var method = methods[signature].Single();
            Require(method.Body.ExceptionHandlers.Count == 0, "Old exception handler retained");
            foreach (var call in method.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>())
                Require(call.DeclaringType.FullName is "System.Console" or "System.InvalidOperationException" or "Cysharp.Threading.Tasks.UniTask" or "SaveSlotMetaData"
                    || call.DeclaringType.FullName.StartsWith("System.Action`1<", StringComparison.Ordinal), "Unexpected reachable call in guarded entrypoint: " + call);
        }
        using var bytes = new MemoryStream();
        assembly.Write(bytes);
        bytes.Position = 0;
        using var reloaded = AssemblyDefinition.ReadAssembly(bytes);
        Require(reloaded.MainModule.GetType(Weaver.Marker) != null, "Missing marker after serialization");
        Require(!reloaded.MainModule.AssemblyReferences.Any(r => r.Name == "System.Private.CoreLib"), "Host BCL leaked into game");
        Require(before == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), "Game assembly changed");
        bool duplicateRefused = false;
        try { Weaver.Apply(assembly); } catch (InvalidOperationException) { duplicateRefused = true; }
        Require(duplicateRefused, "Duplicate patch accepted");
        Console.WriteLine("PASS: " + game + " methods=" + report.Count + " bytes=" + bytes.Length);
    }

    private static void SyntheticCallbacks()
    {
        using var a = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("GuardFixture" + Guid.NewGuid().ToString("N"), new Version(1, 0)), "fixture", ModuleKind.Dll);
        var module = a.MainModule;
        // Test-only net9 console import because this assembly runs inside the test process.
        var type = new TypeDefinition("Fixture", "Callbacks", TA.Public | TA.Abstract | TA.Sealed, module.TypeSystem.Object);
        module.Types.Add(type);
        foreach (var callbackType in new[] { typeof(Action<bool>), typeof(Action<byte[]>) })
        {
            var name = callbackType == typeof(Action<bool>) ? "Boolean" : "Bytes";
            var method = new MethodDefinition(name, MA.Public | MA.Static, module.TypeSystem.Void);
            method.Parameters.Add(new ParameterDefinition(module.ImportReference(callbackType)));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            type.Methods.Add(method);
            Weaver.EmptyCallback(method, new List<string>());
            method.Body.Instructions[1].Operand = module.ImportReference(typeof(Console).GetMethod("WriteLine", new[] { typeof(string) })!);
        }
        using var bytes = new MemoryStream();
        a.Write(bytes);
        var runtime = Assembly.Load(bytes.ToArray()).GetType("Fixture.Callbacks")!;
        int calls = 0;
        runtime.GetMethod("Boolean")!.Invoke(null, new object?[] { new Action<bool>(value => { Require(!value, "Must report failed write/existence"); calls++; }) });
        runtime.GetMethod("Bytes")!.Invoke(null, new object?[] { new Action<byte[]>(value => { Require(value == null, "Must report no saved data"); calls++; }) });
        runtime.GetMethod("Boolean")!.Invoke(null, new object?[] { null });
        Require(calls == 2, "Callback count mismatch");
        Console.WriteLine("PASS: guarded callbacks return false/null exactly once; null callback accepted");
    }
}
