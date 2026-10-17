using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using YiGodhome.SaveGuard;

internal static class PairedGuardChecks
{
    public static void Run()
    {
        foreach (string directory in new[] { "C:/Steam/steamapps/common/Hollow Knight/hollow_knight_Data/Managed", "C:/Steam/steamapps/common/Nine Sols/NineSols_Data/Managed" })
        {
            using var resolver = new DefaultAssemblyResolver(); resolver.AddSearchDirectory(directory);
            using var assembly = AssemblyDefinition.ReadAssembly(Path.Combine(directory, "Assembly-CSharp.dll"), new ReaderParameters { AssemblyResolver = resolver });
            var originals = Weaver.AllTypes(assembly.MainModule.Types).SelectMany(t => t.Methods).Where(m => m.HasBody)
                .ToDictionary(m => m, m => m.Body.Instructions.ToArray());
            var report = PairedSaveGate.Apply(assembly);
            if (report.Count < 10 || assembly.MainModule.GetType(PairedSaveGate.Marker) == null) throw new Exception("Missing paired early guard.");
            foreach (var method in Weaver.AllTypes(assembly.MainModule.Types).SelectMany(t => t.Methods).Where(m => m.HasBody && originals.ContainsKey(m)))
            {
                var original = originals[method];
                if (method.Body.Instructions.Count == original.Length) continue;
                if (!method.Body.Instructions.Where(i => original.Contains(i)).SequenceEqual(original))
                    throw new Exception("Paired guard replaced vanilla body: " + method.FullName);
                if (!method.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().Any(r => r.DeclaringType.FullName == "HollowSols.Foundation.NativeSaveContext"))
                    throw new Exception("Missing isolated context predicate: " + method.FullName);
            }
            using var bytes = new MemoryStream(); assembly.Write(bytes); bytes.Position = 0;
            using var roundTrip = AssemblyDefinition.ReadAssembly(bytes);
            if (roundTrip.MainModule.AssemblyReferences.Any(r => r.Name == "System.Private.CoreLib")) throw new Exception("Game guard imported the test runtime's core library.");
            Console.WriteLine("PASS: paired early guard preserves all vanilla bodies and serializes scoped callbacks/file operations for " + Weaver.Identify(assembly) + " methods=" + report.Count);
        }
    }
}
