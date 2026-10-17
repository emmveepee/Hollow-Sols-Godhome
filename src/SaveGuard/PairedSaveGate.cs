using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace YiGodhome.SaveGuard;

// Ordinary-launch guard: original native code is retained while inactive.
// The isolated branch never resolves native save paths or delegates to Steam.
public static class PairedSaveGate
{
    public const string Marker = "YiGodhome.GuardedPairedSavesV1";
    private const string ContextName = "HollowSols.Foundation.NativeSaveContext";
    public static List<string> Apply(AssemblyDefinition assembly)
    {
        var module = assembly.MainModule;
        if (module.GetType(Marker) != null) throw new InvalidOperationException("Paired save guard already installed.");
        var report = new List<string>();
        if (Weaver.Identify(assembly) == "HollowKnight") HollowKnight(module, report); else NineSols(module, report);
        module.Types.Add(new TypeDefinition("YiGodhome", "GuardedPairedSavesV1", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, module.TypeSystem.Object));
        return report;
    }
    private static MethodReference Context(ModuleDefinition module, string name, TypeReference returns, params TypeReference[] parameters)
    {
        var reference = module.AssemblyReferences.FirstOrDefault(r => r.Name == "HollowSols.Foundation");
        if (reference == null) { reference = new AssemblyNameReference("HollowSols.Foundation", new Version(1, 0, 0, 0)); module.AssemblyReferences.Add(reference); }
        var type = new TypeReference("HollowSols.Foundation", "NativeSaveContext", module, reference);
        var method = new MethodReference(name, returns, type) { HasThis = false };
        foreach (var parameter in parameters) method.Parameters.Add(new ParameterDefinition(parameter));
        return method;
    }
    private static void Prefix(MethodDefinition method, List<string> report, Action<ILProcessor> emit, int? path = null, bool write = false)
    {
        if (!method.HasBody || method.Body.Instructions.Count == 0) throw new InvalidOperationException("Missing paired guard body: " + method.FullName);
        var original = method.Body; var first = original.Instructions[0];
        method.Body = new MethodBody(method) { InitLocals = true };
        var il = method.Body.GetILProcessor();
        if (path.HasValue)
        { il.Emit(OpCodes.Ldarg, method.Parameters[path.Value]); il.Emit(OpCodes.Call, Context(method.Module, write ? "ShouldHandleWritePath" : "ShouldHandlePath", method.Module.TypeSystem.Boolean, method.Module.TypeSystem.String)); }
        else il.Emit(OpCodes.Call, Context(method.Module, write ? "get_ProtectingWrites" : "get_Enabled", method.Module.TypeSystem.Boolean));
        il.Emit(OpCodes.Brfalse, first); emit(il);
        var generated = method.Body; method.Body = original; original.InitLocals = true;
        foreach (var variable in generated.Variables) original.Variables.Add(variable);
        foreach (var instruction in generated.Instructions) original.GetILProcessor().InsertBefore(first, instruction);
        report.Add(method.FullName + " [scoped paired save]");
    }
    private static void Completed(MethodDefinition method, ILProcessor il)
    {
        if (method.ReturnType.MetadataType != MetadataType.Void)
        {
            var value = new VariableDefinition(method.ReturnType); method.Body.Variables.Add(value);
            il.Emit(OpCodes.Ldloca, value); il.Emit(OpCodes.Initobj, value.VariableType); il.Emit(OpCodes.Ldloc, value);
        }
        il.Emit(OpCodes.Ret);
    }
    private static MethodDefinition Method(TypeDefinition type, string name) => type.Methods.Single(m => m.Name == name);
    private static void HollowKnight(ModuleDefinition module, List<string> report)
    {
        foreach (string typeName in new[] { "DesktopPlatform", "GameCoreOnlineSubsystem" })
        {
            var type = module.GetType(typeName) ?? throw new MissingMemberException(typeName);
            foreach (var method in type.Methods.Where(m => new[] { "IsSaveSlotInUse", "LocalIsSaveSlotInUse", "ReadSaveSlot", "LocalReadSaveSlot", "WriteSaveSlot", "ClearSaveSlot", "EnsureSaveSlotSpace" }.Contains(m.Name)))
            {
                if (method.ReturnType.MetadataType != MetadataType.Void || method.Parameters[0].ParameterType.MetadataType != MetadataType.Int32) throw new InvalidOperationException("HK callback contract changed.");
                string name = method.Name.Contains("IsSaveSlot") ? "HKOccupiedCallback" : method.Name.Contains("ReadSaveSlot") ? "HKRead"
                    : method.Name == "WriteSaveSlot" ? "HKWrite" : method.Name == "ClearSaveSlot" ? "HKClear" : "HKSpace";
                Prefix(method, report, il => {
                    foreach (var parameter in method.Parameters) il.Emit(OpCodes.Ldarg, parameter);
                    il.Emit(OpCodes.Call, Context(module, name, module.TypeSystem.Void, method.Parameters.Select(p => p.ParameterType).ToArray())); il.Emit(OpCodes.Ret);
                }, write: name == "HKWrite" || name == "HKClear");
            }
        }
        var desktop = module.GetType("DesktopPlatform"); var path = Method(desktop, "GetSaveSlotPath");
        Prefix(path, report, il => { il.Emit(OpCodes.Ldarg, path.Parameters[0]); il.Emit(OpCodes.Call, Context(module, "SlotPath", module.TypeSystem.String, module.TypeSystem.Int32)); il.Emit(OpCodes.Ret); });
        var migrate = Method(module.GetType("GameCoreOnlineSubsystem"), "MigrateLocalSaves");
        Prefix(migrate, report, il => Completed(migrate, il), write: true);
        foreach (var method in Weaver.AllTypes(module.Types).Where(t => t.Name == "DesktopPlatform" || t.Name == "GameCoreOnlineSubsystem").SelectMany(t => t.Methods).Where(m => m.Name == "PrepareForNewGame"))
            Prefix(method, report, il => Completed(method, il));
        var achievements = module.GetType("AchievementHandler");
        foreach (var method in achievements.Methods.Where(m => m.Name == "FlushRecordsToDisk")) Prefix(method, report, il => Completed(method, il), write: true);
    }
    private static void NineSols(ModuleDefinition module, List<string> report)
    {
        var save = module.GetType("SaveManager"); var slot = Method(save, "GetSlotDirPath");
        Prefix(slot, report, il => { il.Emit(OpCodes.Ldarg, slot.Parameters[0]); il.Emit(OpCodes.Call, Context(module, "SlotPath", module.TypeSystem.String, module.TypeSystem.Int32)); il.Emit(OpCodes.Ret); });
        foreach (var method in save.Methods.Where(m => m.Name == "CheckSaveExist" || m.Name == "CheckSaveFileExistInCache"))
        {
            var task = method.ReturnType is GenericInstanceType ? FromResult(method) : null;
            Prefix(method, report, il => { il.Emit(OpCodes.Ldarg, method.Parameters[0]); il.Emit(OpCodes.Call, Context(module, "NSOccupied", module.TypeSystem.Boolean, module.TypeSystem.Int32)); if (task != null) il.Emit(OpCodes.Call, task); il.Emit(OpCodes.Ret); });
        }
        Prefix(Method(save, "SavePlayerPref"), report, il => Completed(Method(save, "SavePlayerPref"), il));
        var loadFlags = Method(save, "LoadOrResetFlag");
        var originalLoadEntry = loadFlags.Body.Instructions[0];
        // Native LoadSaveAtSlot constructs saveslotN directly rather than calling
        // GetSlotDirPath. Redirect that serializer directory before flag I/O.
        Prefix(loadFlags, report, il => {
            il.Emit(OpCodes.Ldarg, loadFlags.Parameters[0]); il.Emit(OpCodes.Ldarg, loadFlags.Parameters[1]);
            il.Emit(OpCodes.Call, Context(module, "RedirectNativeLoad", module.TypeSystem.String, module.TypeSystem.String, module.TypeSystem.Boolean));
            il.Emit(OpCodes.Starg, loadFlags.Parameters[0]); il.Emit(OpCodes.Br, originalLoadEntry);
        });
        foreach (var method in save.Methods.Where(m => new[] { "DeleteSave", "SetSlotToDemo", "ZipUpSaveSlotFolder", "ZipUpCurrentSlotFlag" }.Contains(m.Name)))
            Prefix(method, report, il => { il.Emit(OpCodes.Call, Context(module, "RefuseNativeSlotDeletion", module.TypeSystem.Void)); Completed(method, il); });
        var fs = save.NestedTypes.Single(t => t.Name == "UnityFileSystem");
        var mappings = new Dictionary<string, (string Name, bool Value)> {
            ["FileExists"] = ("FileExists", true), ["ReadAllBytes"] = ("Read", true), ["WriteAllBytes"] = ("Write", false),
            ["RenameFile"] = ("Rename", false), ["DeleteFile"] = ("DeleteFile", false), ["DirectoryExists"] = ("DirectoryExists", true),
            ["CreateDirectory"] = ("CreateDirectory", false), ["GetDirectories"] = ("GetDirectories", true), ["DeleteDirectory"] = ("DeleteDirectory", false)
        };
        foreach (var entry in mappings)
        {
            var method = fs.Methods.Single(m => m.Name == "SaveManager.IFileSystem." + entry.Key);
            var parameters = method.Parameters.Where(p => p.ParameterType.FullName != "System.Threading.CancellationToken").ToArray();
            TypeReference returns = entry.Value.Value ? ((GenericInstanceType)method.ReturnType).GenericArguments[0] : module.TypeSystem.Void;
            var fromResult = entry.Value.Value ? FromResult(method) : null;
            Prefix(method, report, il => {
                foreach (var parameter in parameters) il.Emit(OpCodes.Ldarg, parameter);
                il.Emit(OpCodes.Call, Context(module, entry.Value.Name, returns, parameters.Select(p => p.ParameterType).ToArray()));
                if (fromResult != null) { il.Emit(OpCodes.Call, fromResult); il.Emit(OpCodes.Ret); }
                else Completed(method, il);
            }, 0, !entry.Value.Value);
        }
        // Prevent native developer export/load commands from resolving arbitrary files.
        foreach (var method in save.Methods.Where(m => m.Name == "SaveWithCommand" || m.Name == "LoadWithCommand"))
            Prefix(method, report, il => { il.Emit(OpCodes.Call, Context(module, "RefuseNativeSlotDeletion", module.TypeSystem.Void)); Completed(method, il); });
        foreach (var type in Weaver.AllTypes(module.Types).Where(t => t.DeclaringType == save || t.DeclaringType == fs
                || t.FullName.StartsWith("StartMenuLogic/<CreateOrLoadSaveSlotAndPlay>", StringComparison.Ordinal)))
        {
            var step = type.Methods.FirstOrDefault(m => m.Name == "MoveNext" && m.HasBody && m.ReturnType.MetadataType == MetadataType.Void);
            var state = type.Fields.FirstOrDefault(f => f.Name == "<>1__state");
            if (step == null || state == null) continue;
            ScopeAsync(step, state, report);
        }
    }
    private static void ScopeAsync(MethodDefinition method, FieldDefinition state, List<string> report)
    {
        var module = method.Module;
        var guid = new TypeReference("System", "Guid", module, module.TypeSystem.CoreLibrary, true);
        var owner = new FieldDefinition("__hollowSolsAsyncOwner", FieldAttributes.Private, guid); method.DeclaringType.Fields.Add(owner);
        var start = method.Body.ExceptionHandlers.Where(h => h.HandlerType == ExceptionHandlerType.Catch).Select(h => h.TryStart).OrderBy(i => i.Offset).FirstOrDefault()
            ?? throw new InvalidOperationException("Async method has no native failure handler: " + method.FullName);
        var il = method.Body.GetILProcessor(); var validate = il.Create(OpCodes.Ldarg_0);
        var prefix = new[] {
            il.Create(OpCodes.Ldarg_0), il.Create(OpCodes.Ldfld, state), il.Create(OpCodes.Ldc_I4_M1), il.Create(OpCodes.Bne_Un, validate),
            il.Create(OpCodes.Ldarg_0), il.Create(OpCodes.Call, Context(module, "AsyncOwner", guid)), il.Create(OpCodes.Stfld, owner),
            validate, il.Create(OpCodes.Ldfld, owner), il.Create(OpCodes.Call, Context(module, "ValidateAsyncOwner", module.TypeSystem.Void, guid))
        };
        foreach (var instruction in prefix) il.InsertBefore(start, instruction);
        // Native builder catches cancellation and completes its task; do not leave
        // a dangling awaiter by throwing outside the authored exception region.
        foreach (var handler in method.Body.ExceptionHandlers) if (handler.TryStart == start) handler.TryStart = prefix[0];
        report.Add(method.FullName + " [captured paired async owner]");
    }
    private static MethodReference FromResult(MethodDefinition method)
    {
        var existing = Weaver.AllTypes(method.Module.Types).SelectMany(t => t.Methods).Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions).Select(i => i.Operand).OfType<GenericInstanceMethod>()
            .First(m => m.ElementMethod.DeclaringType.FullName == "Cysharp.Threading.Tasks.UniTask" && m.Name == "FromResult").ElementMethod;
        var result = new GenericInstanceMethod(existing); result.GenericArguments.Add(((GenericInstanceType)method.ReturnType).GenericArguments[0]); return result;
    }
}
