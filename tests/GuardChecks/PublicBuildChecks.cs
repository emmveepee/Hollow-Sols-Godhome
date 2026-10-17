// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class PublicBuildChecks
{
    public static int Run(string[] paths)
    {
        if (paths.Length != 2) throw new ArgumentException("Supply the two public plugin DLL paths.");
        foreach (string path in paths) {
            using var assembly = AssemblyDefinition.ReadAssembly(path);
            var types = assembly.MainModule.Types;
            if (assembly.Name.Version != new Version(0, 1, 0, 0)) throw new Exception("Public assembly version differs from v0.1.0");
            var menuPlugin = types.Single(t => t.Name == "HollowSolsMenuPlugin");
            foreach (var method in menuPlugin.Methods.Where(m => m.HasBody && new[] { "StateChanged", "PreviewSave", "BeginSelectedSlot", "ShowSlots" }.Contains(m.Name)))
                if (method.Body.Instructions.Any(i => i.Operand is MethodReference call && call.DeclaringType.FullName == "HollowSols.Foundation.PairedSaveStore" && (call.Name == "Load" || call.Name == "ListSlots")))
                    throw new Exception("Save menu blocks the game-thread control pump: " + method.Name);
            var pluginDeclaration = menuPlugin.CustomAttributes.Single(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
            if (!Equals(pluginDeclaration.ConstructorArguments[2].Value, "0.1.0")) throw new Exception("Public menu plugin version differs from v0.1.0");
            var repository = types.Single(t => t.FullName == "YiGodhome.Bridge.BuildFeatures").Fields.Single(f => f.Name == "ReportRepository");
            if (!Equals(repository.Constant, "https://github.com/emmveepee/Hollow-Sols-Godhome")) throw new Exception("Public build has the wrong report repository");
            var reporting = types.Single(t => t.Name == "HollowSolsMenuPlugin").Methods.Single(m => m.Name == "InitializeFailureReporting");
            if (!reporting.Body.Instructions.Any(i => Equals(i.Operand, repository.Constant))) throw new Exception("Public reporting does not bind the repository default");
            if (types.Any(t => t.Name == "PlayerDebugMenu" || t.Name == "CollisionContourGraphic")) throw new Exception("Public build contains debug menu/renderer");
            var capture = types.Single(t => t.FullName == "YiGodhome.Bridge.DetailedEventCapture");
            if (capture.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Any(i => i.Operand is MethodReference method && method.DeclaringType.Namespace.StartsWith("System.IO", StringComparison.Ordinal)))
                throw new Exception("Public detailed capture can perform I/O");
            var feature = types.Single(t => t.FullName == "YiGodhome.Bridge.BuildFeatures").Methods.Single(m => m.Name == "get_DevelopmentTools");
            if (!feature.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldc_I4_0) || feature.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldc_I4_1)) throw new Exception("Public development feature gate enabled");
            var methods = types.SelectMany(t => t.Methods).Where(m => m.HasBody);
            var failure = types.Single(t => t.Name == "HollowSolsMenuPlugin").Methods.Single(m => m.Name == "Fail");
            var calls = failure.Body.Instructions.Where(i => i.Operand is MethodReference).Select(i => ((MethodReference)i.Operand).Name).ToArray();
            if (Array.IndexOf(calls, "CaptureReportFailure") < 0 || Array.IndexOf(calls, "CaptureReportFailure") >= Array.IndexOf(calls, "AbortNativeSession"))
                throw new Exception("Failure snapshot is not scheduled before native teardown");
            var quit = menuPlugin.Methods.Single(m => m.Name == "OnApplicationQuit");
            if (!quit.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "CompleteReportShutdown"))
                throw new Exception("Application quit does not complete orderly report shutdown");
            var orderly = menuPlugin.Methods.Single(m => m.Name == "CompleteReportShutdown");
            if (!orderly.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "DisarmBeforeExit")
                || orderly.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "Acknowledge"))
                throw new Exception("Orderly quit must drain its active marker without erasing actual incidents");
            var notice = types.Single(t => t.Name == "NativeBridgeMenu").Methods.Single(m => m.Name == "FailureNotice");
            if (!notice.Body.Instructions.Any(i => Equals(i.Operand, "Prepare Bug Report")) || !notice.Body.Instructions.Any(i => Equals(i.Operand, "Continue")))
                throw new Exception("Native failure dialog lost reporting/continue choices");
            if (methods.SelectMany(m => m.Body.Instructions).Any(i => i.OpCode == OpCodes.Ldstr && i.Operand is string s && (s == "YiDebugMenu" || s == "Godhome testing" || s == "Start detailed logging")))
                throw new Exception("Public artifact retains debug UI strings");
            foreach (string methodName in new[] { "SetDetailedLogging", "UpdateDetailedLogging" })
                foreach (var method in methods.Where(m => m.Name == methodName))
                    if (method.Body.Instructions.Any(i => i.OpCode != OpCodes.Nop && i.OpCode != OpCodes.Ret)) throw new Exception("Public detailed activation is not stripped");
            Console.WriteLine("PASS: public artifact has no F9 menu/renderer or detailed-capture activation/I/O: " + Path.GetFileName(path));
        }
        return 0;
    }
}
