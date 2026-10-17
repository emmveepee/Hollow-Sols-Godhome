using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using YiGodhome.Bridge;

namespace YiGodhome;

// A bounded, read-only bootstrap inventory. Never runs in ordinary gameplay.
internal static class NativeCanvasServiceAudit
{
    private static bool completed;
    internal static void Dump(Player player, Action<string> log)
    {
        if (!SessionConfiguration.Current.Has("canvas-service-audit") || completed)
            throw new InvalidOperationException("Canvas service audit requires one standalone capability.");
        completed = true;
        var level = GameCore.Instance.gameLevel;
        var scene = level.gameObject.scene;
        if (scene.name != "A2_S5_BossHorseman_Final" || !ApplicationCore.IsInBossMemoryMode)
            throw new InvalidOperationException("Unexpected canvas bootstrap audit context.");
        var report = new StringBuilder();
        void Line(string text) { if (report.Length > 256 * 1024) throw new InvalidOperationException("Service inventory budget exceeded."); report.AppendLine(text); }
        string Describe(UnityEngine.Object obj)
        {
            var transform = obj is Component component ? component.transform : obj is GameObject gameObject ? gameObject.transform : null;
            return obj.GetType().Name + ":" + obj.name + (transform == null ? " asset=True" : " scene=" + transform.gameObject.scene.name + " path=" + NativeRoomIsolation.PathOf(transform));
        }
        Line("nativeMvid=" + typeof(GameLevel).Assembly.ManifestModule.ModuleVersionId + " scene=" + scene.name + " level=" + level.GetInstanceID() + " player=" + player.GetInstanceID());
        foreach (var root in scene.GetRootGameObjects()) {
            Line("ROOT " + Describe(root) + " behaviours=" + root.GetComponentsInChildren<MonoBehaviour>(true).Length + " renderers=" + root.GetComponentsInChildren<Renderer>(true).Length);
            foreach (Transform child in root.transform)
                Line("CHILD " + Describe(child) + " behaviours=" + child.GetComponentsInChildren<MonoBehaviour>(true).Length + " renderers=" + child.GetComponentsInChildren<Renderer>(true).Length + " colliders=" + child.GetComponentsInChildren<Collider2D>(true).Length);
        }
        foreach (var owner in new Component[] { level, GameCore.Instance, GameCore.Instance.resetManager, player }) {
            Line("OWNER " + Describe(owner));
            foreach (var field in owner.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)) {
                var value = field.GetValue(owner);
                if (value is UnityEngine.Object obj && obj != null) Line("REFERENCE " + owner.GetType().Name + "." + field.Name + " -> " + Describe(obj));
                else if (value is IEnumerable items && !(value is string)) {
                    var references = items.Cast<object>().OfType<UnityEngine.Object>().Where(o => o != null).ToArray();
                    if (references.Length == 0) continue;
                    Line("CACHE " + owner.GetType().Name + "." + field.Name + " count=" + references.Length);
                    foreach (var group in references.GroupBy(o => o is Component c ? (c.transform.IsChildOf(level.transform) ? c.transform.GetSiblingIndex() + ":" + c.GetType().Name : "external:" + c.gameObject.scene.name) : o.GetType().Name))
                        Line(" GROUP " + group.Key + " count=" + group.Count() + " sample=" + Describe(group.First()));
                }
                if (value is UnityEventBase ev)
                    for (int i = 0; i < ev.GetPersistentEventCount(); i++) {
                        var target = ev.GetPersistentTarget(i);
                        if (target != null) Line("CALLBACK " + owner.GetType().Name + "." + field.Name + " -> " + Describe(target) + " method=" + ev.GetPersistentMethodName(i));
                    }
            }
        }
        string directory = System.IO.Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!;
        System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "canvas-service-audit.txt"), report.ToString());
        log("NS_CANVAS_SERVICE_AUDIT level=" + level.GetInstanceID() + " scene=" + scene.name + " bytes=" + report.Length + " readOnly=True once=True");
    }
}
