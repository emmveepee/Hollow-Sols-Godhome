using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;

namespace YiGodhome;

// Read-only dependency inventory, never a clone/initialization permission.
internal static class NativeSummonAudit
{
    private static readonly HashSet<int> audited = new HashSet<int>();
    public static void Dump(MonsterBase owner, Action<string> log)
    {
        string directory = System.IO.Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!;
        if (!YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures || !audited.Add(owner.GetInstanceID())) return;
        var components = owner.GetComponentsInChildren<MonoBehaviour>(true).Where(c => c != null).ToArray();
        foreach (var group in components.GroupBy(c => c.GetType()).OrderBy(g => g.Key.FullName))
            log("NS_SUMMON_OWNER_COMPONENT type=" + group.Key.FullName + " count=" + group.Count());
        foreach (var component in components)
            for (Type? type = component.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.IsStatic || field.IsNotSerialized || !field.IsPublic && !field.IsDefined(typeof(SerializeField), true)) continue;
                    object? value = field.GetValue(component);
                    void Reference(UnityEngine.Object target, string kind)
                    {
                        var transform = target is Component c ? c.transform : target is GameObject g ? g.transform : null;
                        if (transform == null || transform == owner.transform || transform.IsChildOf(owner.transform)) return;
                        log("NS_SUMMON_EXTERNAL source=" + component.GetType().Name + " field=" + field.Name + " kind=" + kind
                            + " target=" + target.GetType().Name + ":" + target.name + " scene=" + transform.gameObject.scene.name);
                    }
                    if (value is UnityEngine.Object target && target != null) Reference(target, "reference");
                    if (value is IEnumerable list && !(value is string))
                        foreach (var item in list) if (item is UnityEngine.Object obj && obj != null) Reference(obj, "list");
                    if (value is UnityEventBase ev)
                        for (int i = 0; i < ev.GetPersistentEventCount(); i++)
                        {
                            var callback = ev.GetPersistentTarget(i);
                            if (callback != null) Reference(callback, "callback:" + ev.GetPersistentMethodName(i));
                        }
                }
        log("NS_SUMMON_AUDIT_DONE owner=" + owner.name + " components=" + components.Length + " readOnly=True");
    }
}
