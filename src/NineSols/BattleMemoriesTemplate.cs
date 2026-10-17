using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace YiGodhome;

// Inspects a fixed installed resource into isolated data. Never applies flags or opens a save.
internal static class BattleMemoriesTemplate
{
    public static void Inspect(Action<string> log)
    {
        try
        {
            var asset = Resources.Load<TextAsset>("PlayerAbilityUnlockAll");
            if (asset == null) throw new InvalidOperationException("Native Battle Memories template is missing.");
            var decode = typeof(GameFlagManager).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                .Single(m => m.Name == "BinaryDeserialize" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2)
                .MakeGenericMethod(typeof(GameFlagManager.SerializedSaveData));
            var decoded = (GameFlagManager.SerializedSaveData)decode.Invoke(null, new object[] { GzipCompression.Decompress(asset.bytes), false });
            var manager = UnityEngine.Object.FindFirstObjectByType<SaveManager>();
            if (manager == null || manager.allFlags == null || decoded.flagDict == null) throw new InvalidOperationException("Native flag catalog is unavailable.");
            int matched = 0, combat = 0;
            foreach (var flag in manager.allFlags.Flags.OrderBy(f => f.name, StringComparer.Ordinal))
            {
                if (!decoded.flagDict.TryGetValue(flag.FinalSaveID, out var values)) continue;
                matched++;
                string type = flag.GetType().Name;
                if (!new[] { "Ability", "Skill", "Jade", "Stat", "Item", "Player" }.Any(s => type.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                combat++;
                string fields = string.Join(";", values.OrderBy(p => p.Key, StringComparer.Ordinal)
                    .Where(p => p.Value is bool || p.Value is int || p.Value is long || p.Value is float || p.Value is double)
                    .Select(p => p.Key + "=" + Convert.ToString(p.Value, CultureInfo.InvariantCulture)));
                log("NS_TEMPLATE_FLAG type=" + type + " name=" + flag.name + " values=" + fields);
            }
            if (matched == 0 || combat == 0) throw new InvalidOperationException("Template did not match the loaded flag catalog.");
            var abilities = manager.playerAbilityCollection;
            if (abilities == null) throw new InvalidOperationException("Native combat catalog is unavailable.");
            foreach (var field in typeof(PlayerMainAbilityCollection).GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => typeof(PlayerAbilityData).IsAssignableFrom(f.FieldType)))
            {
                var ability = (PlayerAbilityData)field.GetValue(abilities);
                if (ability == null || !decoded.flagDict.TryGetValue(ability.FinalSaveID, out var values)) continue;
                log("NS_TEMPLATE_ABILITY field=" + field.Name + " acquired=" + values["acquired"] + " equipped=" + values["equipped"]);
            }
            // The native completion check uses this skill catalog; the legacy skill field is unassigned.
            foreach (var catalog in new AbstractGameFlagCollection[] { abilities.jadeDataColleciton, abilities.skillNodeDataCollection_Achievement })
            {
                if (catalog == null || catalog.rawCollection == null) throw new InvalidOperationException("Native progression catalog is unavailable.");
                int acquired = catalog.rawCollection.Count(f => decoded.flagDict.TryGetValue(f.FinalSaveID, out var values)
                    && values.TryGetValue("acquired", out var value) && value is bool enabled && enabled);
                log("NS_TEMPLATE_CATALOG type=" + catalog.GetType().Name + " entries=" + catalog.rawCollection.Count + " acquired=" + acquired);
            }
            log("NS_TEMPLATE_INSPECTED resource=PlayerAbilityUnlockAll version=" + decoded.version + " records=" + decoded.flagDict.Count
                + " matched=" + matched + " combatRecords=" + combat + " applied=False saveIO=disabled");
        }
        catch (Exception error) { log("NS_TEMPLATE_FAILED " + error); }
    }
}
