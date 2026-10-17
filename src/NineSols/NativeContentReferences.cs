using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace YiGodhome;

internal static class NativeContentReferences
{
    internal static void Validate(GameObject root, Scene donor)
    {
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true)) {
            // The native camera-vote owner stays disabled and is never initialized.
            if (component.GetType().Name == "HierarchyCullingGroup" && !component.enabled) continue;
            for (var type = component.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)) {
                    if (field.IsStatic || field.IsNotSerialized || (!field.IsPublic && !Attribute.IsDefined(field, typeof(SerializeField)))) continue;
                    string path = component.GetType().Name + "." + field.Name;
                    var value = field.GetValue(component);
                    if (value is UnityEventBase events)
                        for (int i = 0; i < events.GetPersistentEventCount(); i++) Check(events.GetPersistentTarget(i), path, root, donor);
                    else if (value is UnityEngine.Object reference) Check(reference, path, root, donor);
                    else if (value is IEnumerable list && !(value is string))
                        foreach (var item in list) if (item is UnityEngine.Object entry) Check(entry, path, root, donor);
                }
        }
    }
    private static void Check(UnityEngine.Object? reference, string path, GameObject root, Scene donor)
    {
        GameObject? obj = reference is Component component ? component.gameObject : reference as GameObject;
        if (obj != null && obj.scene == donor && !obj.transform.IsChildOf(root.transform))
            throw new InvalidOperationException("Native content has an unowned donor reference: " + path + " -> " + obj.name);
    }
}
