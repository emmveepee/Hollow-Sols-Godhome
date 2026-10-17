using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using YiGodhome.SaveGuard;
using TA = Mono.Cecil.TypeAttributes;
using MA = Mono.Cecil.MethodAttributes;

internal static class MemoryChecks
{
    public static void Run()
    {
        using var fixture = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("MemoryGate" + Guid.NewGuid().ToString("N"), new Version(1, 0)), "fixture", ModuleKind.Dll);
        var module = fixture.MainModule;
        var type = new TypeDefinition("Fixture", "Memory", TA.Public | TA.Abstract | TA.Sealed, module.TypeSystem.Object);
        module.Types.Add(type);
        var loading = new FieldDefinition("LoadingMemory", Mono.Cecil.FieldAttributes.Public | Mono.Cecil.FieldAttributes.Static, module.TypeSystem.Boolean);
        type.Fields.Add(loading);
        foreach (string name in new[] { "SetSlot", "GetSlotDirPath", "CreateOrLoadSaveSlotAndPlay", "LoadSaveFromResourceWithCommand", "LoadOrResetFlag" })
        {
            var method = new MethodDefinition(name, MA.Public | MA.Static, module.TypeSystem.Int32);
            bool resource = name.StartsWith("Load", StringComparison.Ordinal);
            method.Parameters.Add(new ParameterDefinition(resource ? module.TypeSystem.String : module.TypeSystem.Int32));
            int booleans = name == "CreateOrLoadSaveSlotAndPlay" ? 3 : name == "LoadOrResetFlag" ? 1 : 0;
            for (int i = 0; i < booleans; i++) method.Parameters.Add(new ParameterDefinition(module.TypeSystem.Boolean));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4, 42));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            type.Methods.Add(method);
            MemoryGate.Apply(method, new List<string>(), loading);
            foreach (var instruction in method.Body.Instructions)
                if (instruction.Operand is MethodReference call && call.DeclaringType.FullName == "System.Console")
                    instruction.Operand = module.ImportReference(typeof(Console).GetMethod("WriteLine", new[] { typeof(string) })!);
        }
        using var bytes = new MemoryStream(); fixture.Write(bytes);
        var runtime = Assembly.Load(bytes.ToArray()).GetType("Fixture.Memory")!;
        bool Accepted(string name, params object?[] args)
        {
            try { return (int)runtime.GetMethod(name)!.Invoke(null, args)! == 42; }
            catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { return false; }
        }
        void Require(bool condition) { if (!condition) throw new InvalidOperationException("Memory gate admitted an invalid input or rejected valid input."); }
        foreach (int slot in new[] { -1, 0, 1, 2, 3, 4, 5 })
        {
            Require(Accepted("SetSlot", slot) == (slot == 3));
            Require(Accepted("GetSlotDirPath", slot) == (slot == 3));
            foreach (bool exists in new[] { false, true })
            foreach (bool backup in new[] { false, true })
            foreach (bool memory in new[] { false, true })
                Require(Accepted("CreateOrLoadSaveSlotAndPlay", slot, exists, backup, memory) == (slot == 3 && !exists && !backup && memory));
        }
        foreach (bool memory in new[] { false, true })
        foreach (string? resource in new[] { null, "", "PlayerAbilityUnlockAll", "playerabilityunlockall", "user4.dat" })
        {
            runtime.GetField("LoadingMemory")!.SetValue(null, memory);
            bool expected = memory && resource == "PlayerAbilityUnlockAll";
            Require(Accepted("LoadSaveFromResourceWithCommand", resource) == expected);
            foreach (bool fromAsset in new[] { false, true }) Require(Accepted("LoadOrResetFlag", resource, fromAsset) == (expected && fromAsset));
        }
        const string managed = "C:/Steam/steamapps/common/Nine Sols/NineSols_Data/Managed";
        using var resolver = new DefaultAssemblyResolver(); resolver.AddSearchDirectory(managed);
        using var native = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "Assembly-CSharp.dll"), new ReaderParameters { AssemblyResolver = resolver });
        Weaver.Apply(native, memoryNineSols: true);
        Require(native.MainModule.GetType(Weaver.MemoryMarker) != null);
        var save = native.MainModule.GetType("SaveManager");
        foreach (var method in save.Methods.Where(m => new[] { "LoadSaveAtSlot", "ReadSaveFile", "WriteSaveFile", "AutoSave", "DeleteSave", "RecoverSlotFromNoReturnBackup" }.Contains(m.Name)))
            Require(method.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().All(m => m.DeclaringType.FullName == "System.Console"));
        using var patched = new MemoryStream(); native.Write(patched);
        using var hk = AssemblyDefinition.ReadAssembly("C:/Steam/steamapps/common/Hollow Knight/hollow_knight_Data/Managed/Assembly-CSharp.dll");
        bool wrongGameRejected = false;
        try { Weaver.Apply(hk, memoryNineSols: true); } catch (InvalidOperationException) { wrongGameRejected = true; }
        Require(wrongGameRejected);
        Console.WriteLine("PASS: memory gates enforce slot index 3, fresh memory branch and exact native resource/loading context; disk I/O remains blocked");
    }
}
