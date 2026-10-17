using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace YiGodhome.SaveGuard;

public static class MemoryGate
{
    public static void Apply(MethodDefinition method, List<string> report, FieldReference? loadingMemory = null)
    {
        var original = method.Body;
        var first = original.Instructions[0];
        Weaver.Deny(method, report);
        var denied = method.Body.Instructions.ToArray();
        method.Body = original;
        var prefix = new List<Instruction>();
        void Arg(int index) => prefix.Add(Instruction.Create(OpCodes.Ldarg, method.Parameters[index]));
        void DenyIf(OpCode branch) => prefix.Add(Instruction.Create(branch, denied[0]));
        bool resource = method.Name == "LoadSaveFromResourceWithCommand" || method.Name == "LoadOrResetFlag";
        if (resource)
        {
            if (loadingMemory == null || method.Parameters[0].ParameterType.MetadataType != MetadataType.String)
                throw new InvalidOperationException("Resource gate signature changed.");
            prefix.Add(Instruction.Create(OpCodes.Ldsfld, loadingMemory)); DenyIf(OpCodes.Brfalse);
            var equals = new MethodReference("op_Equality", method.Module.TypeSystem.Boolean, method.Module.TypeSystem.String);
            equals.Parameters.Add(new ParameterDefinition(method.Module.TypeSystem.String));
            equals.Parameters.Add(new ParameterDefinition(method.Module.TypeSystem.String));
            Arg(0); prefix.Add(Instruction.Create(OpCodes.Ldstr, "PlayerAbilityUnlockAll"));
            prefix.Add(Instruction.Create(OpCodes.Call, equals)); DenyIf(OpCodes.Brfalse);
            if (method.Name == "LoadOrResetFlag")
            {
                if (method.Parameters.Count != 2 || method.Parameters[1].ParameterType.MetadataType != MetadataType.Boolean)
                    throw new InvalidOperationException("Asset flag signature changed.");
                Arg(1); DenyIf(OpCodes.Brfalse);
            }
        }
        else
        {
            if (method.Parameters[0].ParameterType.MetadataType != MetadataType.Int32) throw new InvalidOperationException("Slot gate signature changed.");
            Arg(0); prefix.Add(Instruction.Create(OpCodes.Ldc_I4_3)); DenyIf(OpCodes.Bne_Un);
            if (method.Name == "CreateOrLoadSaveSlotAndPlay")
            {
                if (method.Parameters.Count != 4 || method.Parameters.Skip(1).Any(p => p.ParameterType.MetadataType != MetadataType.Boolean))
                    throw new InvalidOperationException("Memory start signature changed.");
                Arg(1); DenyIf(OpCodes.Brtrue); Arg(2); DenyIf(OpCodes.Brtrue); Arg(3); DenyIf(OpCodes.Brfalse);
            }
        }
        prefix.Add(Instruction.Create(OpCodes.Br, first));
        prefix.AddRange(denied);
        var il = original.GetILProcessor();
        foreach (var instruction in prefix) il.InsertBefore(first, instruction);
    }
}
