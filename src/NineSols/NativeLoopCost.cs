using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine.LowLevel;
using Loop = UnityEngine.PlayerLoop;

namespace YiGodhome;

// Bounded diagnostic brackets. Original native systems and their order stay intact.
internal sealed class NativeLoopCost : IDisposable
{
    private sealed class Boundary { }
    private sealed class Stage
    {
        internal readonly Type Type;
        internal readonly string Label;
        internal readonly NativeWorkCost.Meter Meter = new NativeWorkCost.Meter();
        internal long Started;
        internal Stage(Type type) { Type = type; Label = "loop_" + type.Name; }
    }
    private readonly Stage[] stages = {
        new Stage(typeof(Loop.Update.ScriptRunBehaviourUpdate)),
        new Stage(typeof(Loop.PreLateUpdate.ScriptRunBehaviourLateUpdate)),
        new Stage(typeof(Loop.FixedUpdate.Physics2DFixedUpdate)),
        new Stage(typeof(Loop.PreUpdate.Physics2DUpdate)),
        new Stage(typeof(Loop.PreLateUpdate.Physics2DLateUpdate)),
        new Stage(typeof(Loop.PostLateUpdate.UpdateAllRenderers)),
        new Stage(typeof(Loop.PostLateUpdate.UpdateAllSkinnedMeshes)),
        new Stage(typeof(Loop.PostLateUpdate.FinishFrameRendering))
    };
    private readonly Action<string> log;
    private bool disposed;
    internal NativeLoopCost(Action<string> log)
    {
        if (!NativeWorkCost.Enabled) throw new InvalidOperationException("Engine loop timing requires bounded NKG trace authorization.");
        this.log = log;
        var original = PlayerLoop.GetCurrentPlayerLoop();
        if (HasBoundary(original)) throw new InvalidOperationException("Duplicate diagnostic engine brackets.");
        var measured = original;
        foreach (var stage in stages) {
            int found = Insert(ref measured, stage);
            if (found != 1) throw new InvalidOperationException("Diagnostic engine stage is absent or ambiguous: " + stage.Type.FullName);
        }
        var restored = measured; Remove(ref restored);
        if (!Same(original, restored)) throw new InvalidOperationException("Engine timing brackets changed the native loop.");
        PlayerLoop.SetPlayerLoop(measured);
        log("NS_LOOP_TIMING_ATTACHED stages=" + stages.Length + " originalOrderVerified=True nativeSystemsUnchanged=True");
    }
    private int Insert(ref PlayerLoopSystem root, Stage stage)
    {
        if (root.subSystemList == null) return 0;
        var result = new List<PlayerLoopSystem>(root.subSystemList.Length + 2);
        int found = 0;
        foreach (var original in root.subSystemList) {
            var child = original;
            if (child.type == typeof(Boundary)) { result.Add(child); continue; }
            found += Insert(ref child, stage);
            if (child.type == stage.Type) {
                found++;
                result.Add(new PlayerLoopSystem { type = typeof(Boundary), updateDelegate = () => { if (!disposed) stage.Started = Stopwatch.GetTimestamp(); } });
                result.Add(child);
                result.Add(new PlayerLoopSystem { type = typeof(Boundary), updateDelegate = () => { if (!disposed) stage.Meter.Record(stage.Started, stage.Label, log); } });
            } else result.Add(child);
        }
        root.subSystemList = result.ToArray(); return found;
    }
    private static void Remove(ref PlayerLoopSystem root)
    {
        if (root.subSystemList == null) return;
        var result = new List<PlayerLoopSystem>(root.subSystemList.Length);
        foreach (var original in root.subSystemList) {
            if (original.type == typeof(Boundary)) continue;
            var child = original; Remove(ref child); result.Add(child);
        }
        root.subSystemList = result.ToArray();
    }
    private static bool HasBoundary(PlayerLoopSystem root)
    {
        if (root.type == typeof(Boundary)) return true;
        if (root.subSystemList != null) foreach (var child in root.subSystemList) if (HasBoundary(child)) return true;
        return false;
    }
    private static bool Same(PlayerLoopSystem a, PlayerLoopSystem b)
    {
        if (a.type != b.type || a.updateDelegate != b.updateDelegate || a.updateFunction != b.updateFunction
            || a.loopConditionFunction != b.loopConditionFunction || (a.subSystemList == null) != (b.subSystemList == null)) return false;
        if (a.subSystemList == null) return true;
        if (b.subSystemList == null || a.subSystemList.Length != b.subSystemList.Length) return false;
        for (int i = 0; i < a.subSystemList.Length; i++) if (!Same(a.subSystemList[i], b.subSystemList[i])) return false;
        return true;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var current = PlayerLoop.GetCurrentPlayerLoop(); Remove(ref current); PlayerLoop.SetPlayerLoop(current);
        log("NS_LOOP_TIMING_DETACHED ownedBrackets=0 nativeSystemsRetained=True");
    }
}
