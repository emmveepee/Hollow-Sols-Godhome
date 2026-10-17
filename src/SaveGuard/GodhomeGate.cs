using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace YiGodhome.SaveGuard;

// Injected into the game assembly; no dependency on the plugin's loading order.
public static class GodhomeGate
{
    public static TypeDefinition CreatePolicy(ModuleDefinition module)
    {
        var type = new TypeDefinition("YiGodhome", "GodhomePolicy", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, module.TypeSystem.Object);
        module.Types.Add(type);
        foreach (bool menus in new[] { false, true })
        {
            var method = new MethodDefinition(menus ? "CanTransition" : "CanContinue", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Boolean);
            method.Parameters.Add(new ParameterDefinition("managerProfile", ParameterAttributes.None, module.TypeSystem.Int32));
            method.Parameters.Add(new ParameterDefinition("dataProfile", ParameterAttributes.None, module.TypeSystem.Int32));
            method.Parameters.Add(new ParameterDefinition("godseeker", ParameterAttributes.None, module.TypeSystem.Boolean));
            method.Parameters.Add(new ParameterDefinition("scene", ParameterAttributes.None, module.TypeSystem.String));
            type.Methods.Add(method);
            var il = method.Body.GetILProcessor();
            var denied = il.Create(OpCodes.Ldc_I4_0);
            var allowed = il.Create(OpCodes.Ldc_I4_1);
            if (menus)
            {
                var equals = new MethodReference("op_Equality", module.TypeSystem.Boolean, module.TypeSystem.String);
                equals.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
                equals.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
                foreach (string menu in new[] { "Menu_Title", "Quit_To_Menu" })
                {
                    il.Emit(OpCodes.Ldarg_3); il.Emit(OpCodes.Ldstr, menu); il.Emit(OpCodes.Call, equals); il.Emit(OpCodes.Brtrue, allowed);
                }
            }
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_I4_4); il.Emit(OpCodes.Bne_Un, denied);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4_4); il.Emit(OpCodes.Bne_Un, denied);
            il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Brfalse, denied);
            il.Emit(OpCodes.Ldarg_3); il.Emit(OpCodes.Brfalse, denied);
            var comparison = new TypeReference("System", "StringComparison", module, module.TypeSystem.CoreLibrary, true);
            var startsWith = new MethodReference("StartsWith", module.TypeSystem.Boolean, module.TypeSystem.String) { HasThis = true };
            startsWith.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
            startsWith.Parameters.Add(new ParameterDefinition(comparison));
            il.Emit(OpCodes.Ldarg_3); il.Emit(OpCodes.Ldstr, "GG_"); il.Emit(OpCodes.Ldc_I4_4); il.Emit(OpCodes.Callvirt, startsWith);
            il.Emit(OpCodes.Brtrue, allowed);
            il.Append(denied); il.Emit(OpCodes.Ret);
            il.Append(allowed); il.Emit(OpCodes.Ret);
        }
        return type;
    }

    public static void Apply(ModuleDefinition module, TypeDefinition manager, List<string> report)
    {
        // Native quit-to-menu otherwise cancels when the deliberate read-only SaveGame guard returns false.
        var returnToMenu = manager.Methods.Single(m => m.Name == "ReturnToMainMenu");
        var saveMode = returnToMenu.Parameters.Single(p => p.ParameterType.Name == "ReturnToMainMenuSaveModes");
        var noSave = manager.NestedTypes.Single(t => t.Name == "ReturnToMainMenuSaveModes").Fields.Single(f => f.Name == "DontSave");
        if ((int)noSave.Constant != 2) throw new InvalidOperationException("Native no-save quit mode changed.");
        var menuIl = returnToMenu.Body.GetILProcessor();
        var menuFirst = returnToMenu.Body.Instructions[0];
        menuIl.InsertBefore(menuFirst, Instruction.Create(OpCodes.Ldc_I4_2));
        menuIl.InsertBefore(menuFirst, Instruction.Create(OpCodes.Starg, saveMode));
        report.Add(returnToMenu.FullName + " [native DontSave]");
        var policy = CreatePolicy(module);
        var data = module.Types.Single(t => t.Name == "PlayerData");
        var managerProfile = manager.Fields.Single(f => f.Name == "profileID");
        var playerData = manager.Fields.Single(f => f.Name == "playerData");
        var dataProfile = data.Fields.Single(f => f.Name == "profileID");
        var godseeker = data.Fields.Single(f => f.Name == "bossRushMode");
        var respawn = data.Fields.Single(f => f.Name == "respawnScene");
        foreach (string name in new[] { "RunContinueGame", "BeginSceneTransition", "LoadScene", "LoadSceneAdditive" })
        {
            var method = manager.Methods.Single(m => m.Name == name);
            bool continuing = name == "RunContinueGame";
            if (!continuing && method.Parameters.Count != 1) throw new InvalidOperationException("Scene entry signature changed: " + name);
            var original = method.Body;
            var first = original.Instructions[0];
            Weaver.Deny(method, report);
            var denied = method.Body.Instructions.ToArray();
            method.Body = original;
            var prefix = new List<Instruction>();
            void Emit(OpCode code) => prefix.Add(Instruction.Create(code));
            void Field(FieldReference field) => prefix.Add(Instruction.Create(OpCodes.Ldfld, field));
            Emit(OpCodes.Ldarg_0); Field(playerData); prefix.Add(Instruction.Create(OpCodes.Brfalse, denied[0]));
            Emit(OpCodes.Ldarg_0); Field(managerProfile);
            Emit(OpCodes.Ldarg_0); Field(playerData); Field(dataProfile);
            Emit(OpCodes.Ldarg_0); Field(playerData); Field(godseeker);
            if (continuing) { Emit(OpCodes.Ldarg_0); Field(playerData); Field(respawn); }
            else
            {
                Emit(OpCodes.Ldarg_1);
                if (name == "BeginSceneTransition") Field(manager.NestedTypes.Single(t => t.Name == "SceneLoadInfo").Fields.Single(f => f.Name == "SceneName"));
                else if (method.Parameters[0].ParameterType.MetadataType != MetadataType.String) throw new InvalidOperationException("Expected scene string.");
            }
            prefix.Add(Instruction.Create(OpCodes.Call, policy.Methods.Single(m => m.Name == (continuing ? "CanContinue" : "CanTransition"))));
            prefix.Add(Instruction.Create(OpCodes.Brtrue, first));
            prefix.AddRange(denied);
            var il = original.GetILProcessor();
            foreach (var instruction in prefix) il.InsertBefore(first, instruction);
        }
    }
}
