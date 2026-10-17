using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace YiGodhome.SaveGuard;

// Operates on metadata only. Never executes or writes the original game assembly.
public static class Weaver
{
    public const string Marker = "YiGodhome.GuardedBaselineV1";
    public const string InspectionMarker = "YiGodhome.GuardedHollowKnightInspectionV1";
    public const string GodhomeMarker = "YiGodhome.GuardedGodhomePreviewV1";
    public const string MemoryMarker = "YiGodhome.GuardedBattleMemoriesV1";

    public static string Identify(AssemblyDefinition assembly)
    {
        var names = assembly.MainModule.Types.Select(t => t.Name).ToArray();
        if (names.Contains("HeroController") && names.Contains("DesktopPlatform")) return "HollowKnight";
        if (names.Contains("SaveManager") && names.Contains("PlayerParryState")) return "NineSols";
        throw new InvalidOperationException("Unrecognized game assembly.");
    }

    public static List<string> Apply(AssemblyDefinition assembly, bool inspectHollowKnightSlot4 = false, bool previewGodhome = false, bool memoryNineSols = false)
    {
        if (new[] { Marker, InspectionMarker, GodhomeMarker, MemoryMarker }.Any(m => assembly.MainModule.GetType(m) != null)) throw new InvalidOperationException("Already patched.");
        var report = new List<string>();
        if ((inspectHollowKnightSlot4 || previewGodhome) && Identify(assembly) != "HollowKnight") throw new InvalidOperationException("HK inspection requested in another game.");
        if (memoryNineSols && (Identify(assembly) != "NineSols" || inspectHollowKnightSlot4 || previewGodhome)) throw new InvalidOperationException("Invalid memory probe game/mode.");
        if (Identify(assembly) == "HollowKnight") PatchHollowKnight(assembly.MainModule, report, inspectHollowKnightSlot4 || previewGodhome, previewGodhome);
        else PatchNineSols(assembly.MainModule, report, memoryNineSols);
        assembly.MainModule.Types.Add(new TypeDefinition("YiGodhome", memoryNineSols ? "GuardedBattleMemoriesV1" : previewGodhome ? "GuardedGodhomePreviewV1" : inspectHollowKnightSlot4 ? "GuardedHollowKnightInspectionV1" : "GuardedBaselineV1", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, assembly.MainModule.TypeSystem.Object));
        return report;
    }

    private static TypeDefinition Type(ModuleDefinition module, string name) => AllTypes(module.Types).Single(t => t.FullName == name);
    public static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types) { yield return type; foreach (var child in AllTypes(type.NestedTypes)) yield return child; }
    }
    private static MethodDefinition Method(TypeDefinition type, string name, int? parameters = null) => type.Methods.Single(m => m.Name == name && (!parameters.HasValue || m.Parameters.Count == parameters));

    private static void PatchHollowKnight(ModuleDefinition module, List<string> report, bool inspection, bool preview)
    {
        var desktop = Type(module, "DesktopPlatform");
        foreach (var name in new[] { "IsSaveSlotInUse", "LocalIsSaveSlotInUse", "ReadSaveSlot", "LocalReadSaveSlot", "EnsureSaveSlotSpace", "WriteSaveSlot", "ClearSaveSlot" })
        {
            var method = Method(desktop, name);
            if (inspection && new[] { "IsSaveSlotInUse", "LocalIsSaveSlotInUse", "ReadSaveSlot", "LocalReadSaveSlot" }.Contains(name)) ReadOnlySlotFour(method, report);
            else EmptyCallback(method, report);
        }
        if (inspection) ReadOnlySlotFour(Method(desktop, "GetSaveSlotPath"), report, true);
        else Deny(Method(desktop, "GetSaveSlotPath"), report);
        // Defense in depth for alternate platform save paths; no online/auth functions are modified.
        foreach (var name in new[] { "IsSaveSlotInUse", "ReadSaveSlot", "WriteSaveSlot", "ClearSaveSlot" })
            EmptyCallback(Method(Type(module, "GameCoreOnlineSubsystem"), name), report);
        Empty(Method(Type(module, "GameCoreOnlineSubsystem"), "MigrateLocalSaves"), report);
        var manager = Type(module, "GameManager");
        // Native Pantheon completion may queue an achievement independently of
        // SaveGame. Protected offline play must remain nonpersistent.
        Empty(Method(manager, "QueueAchievement"), report);
        foreach (var method in manager.Methods.Where(m => new[] { "SaveGame", "LoadGame", "ClearSaveFile" }.Contains(m.Name)))
        {
            if (inspection && method.Name == "LoadGame") ReadOnlySlotFour(method, report);
            else EmptyCallback(method, report);
        }
        foreach (var name in new[] { "LoadGameFromUI", "StartNewGame", "ContinueGame" }) Empty(Method(manager, name), report);
        if (preview) GodhomeGate.Apply(module, manager, report);
        else Empty(Method(manager, "RunContinueGame"), report);
    }

    public static void ReadOnlySlotFour(MethodDefinition method, List<string> report, bool throwOnDenied = false)
    {
        if (method.Parameters.Count == 0 || method.Parameters[0].ParameterType.MetadataType != MetadataType.Int32)
            throw new InvalidOperationException("Expected audited first slot argument: " + method.FullName);
        var original = method.Body;
        var originalFirst = original.Instructions[0];
        if (throwOnDenied) Deny(method, report); else EmptyCallback(method, report);
        var denied = method.Body;
        method.Body = original;
        foreach (var variable in denied.Variables) original.Variables.Add(variable);
        var il = original.GetILProcessor();
        il.InsertBefore(originalFirst, il.Create(OpCodes.Ldarg, method.Parameters[0]));
        il.InsertBefore(originalFirst, il.Create(OpCodes.Ldc_I4_4));
        il.InsertBefore(originalFirst, il.Create(OpCodes.Beq, originalFirst));
        foreach (var instruction in denied.Instructions) il.InsertBefore(originalFirst, instruction);
    }

    private static void PatchNineSols(ModuleDefinition module, List<string> report, bool memory)
    {
        var save = Type(module, "SaveManager");
        var menu = Type(module, "StartMenuLogic");
        var loading = menu.Fields.Single(f => f.Name == "LoadingMemory");
        var fs = Type(module, "SaveManager/UnityFileSystem");
        foreach (var method in fs.Methods.Where(m => m.Name.StartsWith("SaveManager.IFileSystem.", StringComparison.Ordinal) && !m.Name.EndsWith(".Init", StringComparison.Ordinal) && !m.Name.EndsWith(".DeInit", StringComparison.Ordinal)))
        {
            if (method.Name.EndsWith(".GetDirectories", StringComparison.Ordinal)) EmptyStringArrayTask(method, report);
            else Empty(method, report);
        }
        foreach (var method in save.Methods.Where(m => new[] {
            "SetSlot", "SetSlotToDemo", "DeleteSave", "CheckSaveExist", "CheckSaveFileExistInCache", "LoadSaveAtSlot", "RecoverSlotFromNoReturnBackup",
            "LoadSaveFromResourceWithCommand", "LoadWithCommand", "SaveWithCommand", "ForceSaveAt", "AutoSave", "AsyncWriteSaveFiles",
            "SaveAllFlagsAndMeta", "WriteSaveFileAndBackItUp", "ValidateSaveFileBytes", "ReadSaveFileAndBackup", "WriteSaveFile", "ReadSaveFile", "WriteSaveFileWithBytes",
            "SaveFileExists", "ReadFlagsInSave", "SaveTexture", "LoadTexture", "DeleteTexture", "DeleteFile", "DeleteDirectory", "_EnsureSaveSlotDirPath"
        }.Contains(m.Name)))
        {
            if (memory && (method.Name == "SetSlot" || method.Name == "LoadSaveFromResourceWithCommand")) MemoryGate.Apply(method, report, loading);
            else Empty(method, report);
        }
        foreach (var name in new[] { "GetSlotDirPath", "ZipUpSaveSlotFolder", "ZipUpCurrentSlotFlag" })
        {
            if (memory && name == "GetSlotDirPath") MemoryGate.Apply(Method(save, name), report);
            else Deny(Method(save, name), report);
        }
        EmptyStringArrayTask(Method(save, "FindAllSavedDirs"), report);
        EmptyMetadataTask(Method(save, "ReadMetaInSave"), Type(module, "SaveSlotMetaData"), report);
        foreach (var name in new[] { "CreateOrLoadSaveSlotAndPlay", "StartMemoryChallenge", "NewGame", "CreateNewGame" })
        {
            if (memory && name == "CreateOrLoadSaveSlotAndPlay") MemoryGate.Apply(Method(menu, name), report);
            else Empty(Method(menu, name), report);
        }
        if (memory) MemoryGate.Apply(Method(save, "LoadOrResetFlag"), report, loading);
        // Refuse to proceed if an expected critical interception point disappeared.
        foreach (var name in new[] { "AutoSave", "ReadSaveFileAndBackup", "WriteSaveFileAndBackItUp", "LoadSaveAtSlot" })
            if (!report.Any(r => r.Contains("SaveManager::" + name + "("))) throw new MissingMethodException(name);
    }

    private static ILProcessor Reset(MethodDefinition method, List<string> report)
    {
        if (!method.HasBody) throw new InvalidOperationException("Cannot guard bodyless method: " + method.FullName);
        method.Body = new MethodBody(method) { InitLocals = true };
        method.DebugInformation.SequencePoints.Clear();
        method.DebugInformation.Scope = null;
        report.Add(method.FullName);
        var il = method.Body.GetILProcessor();
        il.Emit(OpCodes.Ldstr, "[YiGodhome] SAVE_IO_BLOCKED " + method.DeclaringType.Name + "." + method.Name);
        // Import against the game's own mscorlib, not the inspection tool's System.Private.CoreLib.
        var console = new TypeReference("System", "Console", method.Module, method.Module.TypeSystem.CoreLibrary);
        var write = new MethodReference("WriteLine", method.Module.TypeSystem.Void, console) { HasThis = false };
        write.Parameters.Add(new ParameterDefinition(method.Module.TypeSystem.String));
        il.Emit(OpCodes.Call, write);
        return il;
    }

    public static void Empty(MethodDefinition method, List<string> report)
    {
        var il = Reset(method, report);
        DefaultReturn(method, il);
    }

    private static void DefaultReturn(MethodDefinition method, ILProcessor il)
    {
        if (method.ReturnType.MetadataType != MetadataType.Void)
        {
            var local = new VariableDefinition(method.ReturnType);
            method.Body.Variables.Add(local);
            il.Emit(OpCodes.Ldloca, local); il.Emit(OpCodes.Initobj, method.ReturnType); il.Emit(OpCodes.Ldloc, local);
        }
        il.Emit(OpCodes.Ret);
    }

    public static void EmptyCallback(MethodDefinition method, List<string> report)
    {
        var il = Reset(method, report);
        var callback = method.Parameters.LastOrDefault(p => p.ParameterType is GenericInstanceType g && g.ElementType.FullName == "System.Action`1");
        if (callback != null)
        {
            var type = (GenericInstanceType)callback.ParameterType;
            var done = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldarg, callback); il.Emit(OpCodes.Brfalse, done);
            il.Emit(OpCodes.Ldarg, callback);
            var local = new VariableDefinition(type.GenericArguments[0]); method.Body.Variables.Add(local);
            il.Emit(OpCodes.Ldloca, local); il.Emit(OpCodes.Initobj, local.VariableType); il.Emit(OpCodes.Ldloc, local);
            var invoke = new MethodReference("Invoke", method.Module.TypeSystem.Void, type) { HasThis = true };
            invoke.Parameters.Add(new ParameterDefinition(type.ElementType.GenericParameters[0]));
            il.Emit(OpCodes.Callvirt, invoke); il.Append(done);
        }
        DefaultReturn(method, il);
    }

    private static MethodReference FromResult(MethodDefinition method)
    {
        // Reuse the game's existing generic UniTask.FromResult reference to preserve its exact assembly identity.
        var existing = AllTypes(method.Module.Types).SelectMany(t => t.Methods).Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions).Select(i => i.Operand).OfType<GenericInstanceMethod>()
            .First(m => m.ElementMethod.DeclaringType.FullName == "Cysharp.Threading.Tasks.UniTask" && m.Name == "FromResult").ElementMethod;
        var result = new GenericInstanceMethod(existing);
        result.GenericArguments.Add(((GenericInstanceType)method.ReturnType).GenericArguments[0]);
        return result;
    }

    private static void EmptyMetadataTask(MethodDefinition method, TypeDefinition metadata, List<string> report)
    {
        var fromResult = FromResult(method);
        var il = Reset(method, report);
        il.Emit(OpCodes.Newobj, Method(metadata, ".ctor", 0)); il.Emit(OpCodes.Call, fromResult); il.Emit(OpCodes.Ret);
    }

    private static void EmptyStringArrayTask(MethodDefinition method, List<string> report)
    {
        var fromResult = FromResult(method);
        var il = Reset(method, report);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Newarr, method.Module.TypeSystem.String); il.Emit(OpCodes.Call, fromResult); il.Emit(OpCodes.Ret);
    }

    internal static void Deny(MethodDefinition method, List<string> report)
    {
        var il = Reset(method, report);
        var exception = new TypeReference("System", "InvalidOperationException", method.Module, method.Module.TypeSystem.CoreLibrary);
        var ctor = new MethodReference(".ctor", method.Module.TypeSystem.Void, exception) { HasThis = true };
        ctor.Parameters.Add(new ParameterDefinition(method.Module.TypeSystem.String));
        il.Emit(OpCodes.Ldstr, "Save access is disabled during the YiGodhome loader baseline."); il.Emit(OpCodes.Newobj, ctor); il.Emit(OpCodes.Throw);
    }
}
