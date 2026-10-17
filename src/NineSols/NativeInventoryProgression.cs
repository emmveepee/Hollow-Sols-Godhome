using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using HollowSols.Foundation;

namespace YiGodhome;

// Native IsActivated caches a boolean independently of the CharacterStat
// registry. A true cached result must retain its native permanent effects.
// Repair only that invariant; never change ownership, equipment or resources.
internal sealed class NativeInventoryProgression : IDisposable
{
    private static readonly Dictionary<PlayerAbilityData,StatModifierEntry[]> entries=new();
    private static readonly Dictionary<ItemData,StatModifierEntry[]> items=new();
    private static readonly System.Reflection.FieldInfo ItemEntries=AccessTools.Field(typeof(ItemData),"effectModifiers");
    private static readonly System.Reflection.FieldInfo Modifier=AccessTools.Field(typeof(StatModifierEntry),"modifier");
    private static readonly System.Reflection.FieldInfo RuntimeEntries=AccessTools.Field(typeof(PlayerAbilityData),"RunTimeEffectModifierEntries");
    private static readonly System.Reflection.FieldInfo EquipEntries=AccessTools.Field(typeof(PlayerAbilityData),"EquipEffectModifierEntries");
    private static readonly System.Reflection.FieldInfo Conditions=AccessTools.Field(typeof(PlayerAbilityData),"conditions");
    private static readonly System.Reflection.FieldInfo AutoEquip=AccessTools.Field(typeof(PlayerAbilityData),"IsEquipWhenAcquired");
    private static Action<string>? log;
    private static SessionToken? token;
    private static Player? player;
    private static bool restoring;
    private static int mutationLogs;
    private static int restorationLogs;
    private readonly Harmony harmony=new("local.hollowsols.inventory-progression");
    private static bool Current=>NativeSaveContext.Enabled && token.HasValue && NativeSaveContext.Session.HasValue
        && token.Value.Equals(NativeSaveContext.Session.Value) && Player.i==player && !NativePairedGame.Returning;
    internal NativeInventoryProgression(Action<string> logger)
    {
        log=logger;
        try {
            harmony.Patch(AccessTools.Method(typeof(MenuUIPanel),"ShowTab"),prefix:new HarmonyMethod(typeof(NativeInventoryProgression),nameof(Begin)));
            harmony.Patch(AccessTools.Method(typeof(MenuUIPanel),"ShowComplete"),prefix:new HarmonyMethod(typeof(NativeInventoryProgression),nameof(Complete)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(PlayerAbilityData),"IsActivated"),postfix:new HarmonyMethod(typeof(NativeInventoryProgression),nameof(Activated)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(ItemData),"IsActivated"),postfix:new HarmonyMethod(typeof(NativeInventoryProgression),nameof(ItemActivated)));
            harmony.Patch(AccessTools.Method(typeof(ItemData),"RemoveEffect"),prefix:new HarmonyMethod(typeof(NativeInventoryProgression),nameof(ItemRemoving)));
            harmony.Patch(AccessTools.Method(typeof(PlayerAbilityData),"DeActivate"),prefix:new HarmonyMethod(typeof(NativeInventoryProgression),nameof(Deactivating)));
            harmony.Patch(AccessTools.Method(typeof(CharacterStat),"Clear"),prefix:new HarmonyMethod(typeof(NativeInventoryProgression),nameof(Clearing)));
            harmony.Patch(AccessTools.Method(typeof(CharacterStat),"RemoveModifier"),prefix:new HarmonyMethod(typeof(NativeInventoryProgression),nameof(Removing)));
        } catch {Dispose();throw;}
    }
    private static void Begin(MenuUIPanel __instance)
    {
        if(restoring || !NativeSaveContext.Enabled || !NativeSaveContext.Session.HasValue || NativePairedGame.Returning || NativeMenu.InventoryOpen
            || __instance!=NativeGameplayUI.Current?.menuUI || Player.i==null || Player.i.health.IsDead)return;
        if(!Current){entries.Clear();items.Clear();mutationLogs=restorationLogs=0;}
        token=NativeSaveContext.Session;player=Player.i;
        foreach(var owner in SaveManager.Instance.allFlags.Flags.OfType<PlayerAbilityData>()) {
            // Do not restore jade cooldowns, conditional skill effects, temporary
            // buffs or player-selectable talisman/bow/loadout configurations.
            if(owner==null || owner.GetType()!=typeof(PlayerAbilityData) || owner.IsCanUseNeeded || !(bool)AutoEquip.GetValue(owner)!
                || Conditions.GetValue(owner) is List<ConditionData> conditions && conditions.Count>0
                || !owner.IsAcquired || !owner.IsEquipped)continue;
            if(!entries.ContainsKey(owner)) {
                var runtime=(List<StatModifierEntry>)RuntimeEntries.GetValue(owner)!;
                var equipped=(List<StatModifierEntry>)EquipEntries.GetValue(owner)!;
                var effects=runtime.Concat(equipped).Where(e=>e!=null && e.statData!=null && e.DurationType==StatModDurationType.Permanent).Distinct().ToArray();
                if(effects.Length>0)entries.Add(owner,effects);
            }
        }
        foreach(var item in SaveManager.Instance.allFlags.Flags.OfType<ItemData>()) {
            if(item==null || item.GetType()!=typeof(ItemData) || !item.IsAcquired || item.ownNum.CurrentValue<=0 || items.ContainsKey(item))continue;
            var effects=((List<StatModifierEntry>)ItemEntries.GetValue(item)!).Where(e=>e!=null && e.statData!=null && e.DurationType==StatModDurationType.Permanent).Distinct().ToArray();
            if(effects.Length>0)items.Add(item,effects);
        }
        Reconcile();
        log?.Invoke("NS_INVENTORY_PROGRESSION_CHECK abilities="+entries.Count+" items="+items.Count+" nativePermanentOnly=True health="+Player.i.health.maxHealth.Value+" ammo="+Player.i.ammo.MaxValue);
    }
    private static void Complete(MenuUIPanel __instance){if(__instance==NativeGameplayUI.Current?.menuUI)Reconcile();}
    private static void Reconcile()
    {
        if(!Current)return;
        foreach(var owner in entries.Keys) {
            if(owner==null || !owner.IsAcquired || !owner.IsEquipped)continue;
            // This is the real native eligibility decision. Its normal false→
            // true activation applies effects itself; the postfix repairs only
            // a cached true result whose entries disappeared from the registry.
            bool active=owner.IsActivated;
            if(!active && mutationLogs++<4)log?.Invoke("NS_UPGRADE_NATIVE_INACTIVE ability="+owner.name+" acquired=True equipped=True");
        }
        foreach(var item in items.Keys)if(item!=null && item.IsAcquired && item.ownNum.CurrentValue>0){bool active=item.IsActivated;}
    }
    private static void ItemActivated(ItemData __instance,bool __result)
    {
        if(!__result || restoring || !Current || !items.TryGetValue(__instance,out var effects) || !__instance.IsAcquired || __instance.ownNum.CurrentValue<=0)return;
        int restored=0;restoring=true;
        try {
            foreach(var entry in effects) {
                var modifier=Modifier.GetValue(entry) as StatModifier;
                if(modifier!=null && entry.statData.Stat.StatModifiers.Contains(modifier))continue;
                // Same native count scaling as ItemData.ApplyEffect, restricted
                // to its missing permanent entries; never change ownNum.
                entry.AdditionalMultiplier=__instance.ownNum.CurrentValue;
                entry.Apply(__instance);restored++;
            }
        } finally {restoring=false;}
        if(restored>0 && restorationLogs++<16)log?.Invoke("NS_UPGRADE_ITEM_EFFECTS_RESTORED item="+__instance.name+" count="+__instance.ownNum.CurrentValue+" modifiers="+restored+" ownershipUnchanged=True");
    }
    private static void ItemRemoving(ItemData __instance)
    {
        if(restoring || !Current || !items.ContainsKey(__instance) || mutationLogs++>=4)return;
        log?.Invoke("NS_UPGRADE_NATIVE_ITEM_REMOVED item="+__instance.name+" count="+__instance.ownNum.CurrentValue+" source="+Environment.StackTrace);
    }
    private static void Activated(PlayerAbilityData __instance,bool __result)
    {
        if(!__result || restoring || !Current || !entries.TryGetValue(__instance,out var effects)
            || !__instance.IsAcquired || !__instance.IsEquipped)return;
        int restored=0;restoring=true;
        try {
            foreach(var entry in effects) {
                var modifier=Modifier.GetValue(entry) as StatModifier;
                if(modifier!=null && entry.statData.Stat.StatModifiers.Contains(modifier))continue;
                entry.Apply(__instance);restored++;
            }
        } finally {restoring=false;}
        if(restored>0 && restorationLogs++<16)log?.Invoke("NS_UPGRADE_NATIVE_EFFECTS_RESTORED ability="+__instance.name+" modifiers="+restored+" nativeActivated=True ownershipUnchanged=True loadoutUnchanged=True");
    }
    private static void Deactivating(PlayerAbilityData __instance)
    {
        if(restoring || !Current || !entries.ContainsKey(__instance) || mutationLogs++>=4)return;
        log?.Invoke("NS_UPGRADE_NATIVE_DEACTIVATE ability="+__instance.name+" acquired="+__instance.IsAcquired+" equipped="+__instance.IsEquipped+" source="+Environment.StackTrace);
    }
    private static void Clearing(CharacterStat __instance)
    {
        if(restoring || !Current || !entries.Values.Concat(items.Values).Any(effects=>effects.Any(e=>ReferenceEquals(e.statData.Stat,__instance))) || mutationLogs++>=4)return;
        log?.Invoke("NS_UPGRADE_NATIVE_STAT_CLEAR source="+Environment.StackTrace);
    }
    private static void Removing(StatModifier __0)
    {
        if(restoring || !Current || __0?.Source is not PlayerAbilityData owner || !entries.ContainsKey(owner) || mutationLogs++>=4)return;
        log?.Invoke("NS_UPGRADE_NATIVE_MODIFIER_REMOVED owner="+owner.name+" source="+Environment.StackTrace);
    }
    public void Dispose(){harmony.UnpatchSelf();entries.Clear();items.Clear();token=null;player=null;log=null;}
}
