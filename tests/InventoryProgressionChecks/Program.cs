using System;
using System.Collections.Generic;
using System.Reflection;
using YiGodhome;
using HollowSols.Foundation;

// Fake native endpoints exercise the actual reveal guard, not Unity/Harmony.
namespace HarmonyLib {
    public sealed class HarmonyMethod {public HarmonyMethod(Type type,string name){}}
    public sealed class Harmony {public Harmony(string id){} public void Patch(MethodInfo method,HarmonyMethod? prefix=null,HarmonyMethod? postfix=null){} public void UnpatchSelf(){}}
    public static class AccessTools {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        public static FieldInfo Field(Type t,string n)=>t.GetField(n,Flags)!;
        public static MethodInfo Method(Type t,string n)=>t.GetMethod(n,Flags)!;
        public static MethodInfo PropertyGetter(Type t,string n)=>t.GetProperty(n,Flags)!.GetMethod!;
    }
}
namespace HollowSols.Foundation {
    public readonly record struct SessionToken(Guid Id);
    public static class NativeSaveContext {public static bool Enabled=true;public static SessionToken? Session=new SessionToken(Guid.NewGuid());}
}
public enum StatModDurationType {Permanent,Temporary}
public class ConditionData {}
public sealed class BoolFlag {public bool CurrentValue=true;}
public sealed class FloatFlag {public float CurrentValue=3;}
public class PlayerAbilityData {
    public string name="upgrade";
    public bool IsCanUseNeeded,IsEquipWhenAcquired=true;
    public List<ConditionData> conditions=new();
    public List<StatModifierEntry> RunTimeEffectModifierEntries=new(),EquipEffectModifierEntries=new();
    public BoolFlag acquired=new(),equipped=new();
    public bool IsAcquired=>acquired.CurrentValue;
    public bool IsEquipped=>equipped.CurrentValue;
    public bool NativeActive=true;
    public bool IsActivated {get {
        bool result=NativeActive && IsAcquired && IsEquipped;
        typeof(NativeInventoryProgression).GetMethod("Activated",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{this,result});return result;
    }}
    public void DeActivate(){}
}
public sealed class JadeData:PlayerAbilityData {}
public sealed class IntFlag {public int CurrentValue=3;}
public sealed class ItemData:PlayerAbilityData {
    public List<StatModifierEntry> effectModifiers=new();public IntFlag ownNum=new();
    public new bool IsActivated {get {bool result=IsAcquired;typeof(NativeInventoryProgression).GetMethod("ItemActivated",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{this,result});return result;}}
    public void RemoveEffect(){}
}
public sealed class StatModifier {public StatModDurationType DurationType;public PlayerAbilityData Source=null!;}
public sealed class CharacterStat {
    public List<StatModifier> StatModifiers=new();
    public void Clear()=>StatModifiers.Clear();
    public void RemoveModifier(StatModifier modifier)=>StatModifiers.Remove(modifier);
}
public sealed class StatData {public CharacterStat Stat=new();public float Value=240;}
public sealed class StatModifierEntry {
    public float AdditionalMultiplier;public StatModDurationType DurationType;public StatData statData=new();public StatModifier? modifier;public FloatFlag? ValueSource=new();public int Applies;
    public void OnValueChange(float value){}
    public void Apply(PlayerAbilityData owner){Applies++;modifier??=new StatModifier{Source=owner};if(!statData.Stat.StatModifiers.Contains(modifier))statData.Stat.StatModifiers.Add(modifier);}
}
public sealed class Health {public bool IsDead;public StatData maxHealth=new();}
public sealed class Ammo {public float MaxValue=5;}
public sealed class Player {public static Player i=new();public Health health=new();public Ammo ammo=new();}
public sealed class FlagCollection {public List<PlayerAbilityData> Flags=new();}
public sealed class SaveManager {public static SaveManager Instance=new();public FlagCollection allFlags=new();}
public sealed class MenuUIPanel {public void ShowTab(){}public void ShowComplete(){}public void HideInit(){}}
public sealed class UIManager {public MenuUIPanel menuUI=new();}
public sealed class UIControlGroup {}
public sealed class StartMenuLogic {
    public UIControlGroup startControlGroup=new();
    public static StartMenuLogic? Existing;
    public static int Created,StatClears,AudioUnloads;
    public static StartMenuLogic? InstanceRaw=>Existing;
    public static StartMenuLogic Instance {get {
        if(Existing==null){Created++;StatClears++;AudioUnloads++;Existing=new StartMenuLogic();}return Existing;
    }}
}
namespace YiGodhome {
    internal static class NativeMenu {internal static bool InventoryOpen;}
    internal static class NativeGameplayUI {internal static UIManager Current=new();}
    internal static class NativePairedGame {internal static bool Returning=false;}
}
static class Program {
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static void Call(string name,params object[] args)=>typeof(NativeInventoryProgression).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,args);
    static StatModifierEntry Add(PlayerAbilityData owner,StatModDurationType duration=StatModDurationType.Permanent){
        var e=new StatModifierEntry{DurationType=duration,modifier=new StatModifier{Source=owner,DurationType=duration}};
        e.statData.Stat.StatModifiers.Add(e.modifier);owner.RunTimeEffectModifierEntries.Add(e);return e;
    }
    static void Main(){
        var inventoryGroup=new UIControlGroup();
        StartMenuLogic.Existing=null;
        for(int i=0;i<100;i++)Check(!NativeTitleMenuIdentity.IsTitleGroup(inventoryGroup),"Inventory was classified as title");
        Check(StartMenuLogic.Created==0 && StartMenuLogic.StatClears==0 && StartMenuLogic.AudioUnloads==0,"Inventory inspection constructed the title and reset gameplay");
        var title=new StartMenuLogic();StartMenuLogic.Existing=title;
        Check(NativeTitleMenuIdentity.IsTitleGroup(title.startControlGroup),"Existing native title group no longer bypasses save descendants on return");
        Check(!NativeTitleMenuIdentity.IsTitleGroup(inventoryGroup),"Existing title makes inventory a vanilla selection");
        StartMenuLogic.Existing=null;
        Check(!NativeTitleMenuIdentity.IsTitleGroup(inventoryGroup) && StartMenuLogic.Created==0,"Unloading the title reintroduced a destructive singleton lookup");
        _=StartMenuLogic.Instance;Check(StartMenuLogic.StatClears==1,"Regression fixture failed to model the destructive getter");
        StartMenuLogic.Existing=null;StartMenuLogic.Created=StartMenuLogic.StatClears=StartMenuLogic.AudioUnloads=0;
        Console.WriteLine("PASS: production title identity checks never create missing title objects, clear live stats or unload audio; existing title return is recognized and inventory remains distinct across unload/re-entry. Fake native endpoints.");
        var logs=new List<string>();using var guard=new NativeInventoryProgression(logs.Add);
        var owner=new PlayerAbilityData();var entry=Add(owner);SaveManager.Instance.allFlags.Flags.Add(owner);
        var item=new ItemData();var itemEntry=Add(item);item.effectModifiers.Add(itemEntry);SaveManager.Instance.allFlags.Flags.Add(item);
        var jade=new JadeData();var jadeEntry=Add(jade);SaveManager.Instance.allFlags.Flags.Add(jade);
        var conditional=new PlayerAbilityData{IsCanUseNeeded=true};var conditionalEntry=Add(conditional);SaveManager.Instance.allFlags.Flags.Add(conditional);
        var temporary=Add(owner,StatModDurationType.Temporary);
        var unowned=new PlayerAbilityData();unowned.acquired.CurrentValue=false;var unownedEntry=Add(unowned);SaveManager.Instance.allFlags.Flags.Add(unowned);
        var panel=NativeGameplayUI.Current.menuUI;
        Call("Begin",panel);NativeMenu.InventoryOpen=true;
        itemEntry.statData.Stat.Clear();bool itemActive=item.IsActivated;
        Check(itemActive && itemEntry.statData.Stat.StatModifiers.Contains(itemEntry.modifier!) && itemEntry.AdditionalMultiplier==3,"Item upgrades did not retain exact native quantity scaling");
        item.ownNum.CurrentValue=0;itemEntry.statData.Stat.Clear();itemActive=item.IsActivated;
        Check(itemEntry.statData.Stat.StatModifiers.Count==0 && item.ownNum.CurrentValue==0,"Item repair granted consumed inventory");
        entry.statData.Stat.Clear();
        jadeEntry.statData.Stat.Clear();conditionalEntry.statData.Stat.Clear();temporary.statData.Stat.Clear();unownedEntry.statData.Stat.Clear();
        bool active=owner.IsActivated;
        Check(active && entry.statData.Stat.StatModifiers.Contains(entry.modifier!),"Cached native true lost its permanent effect");
        Check(jadeEntry.Applies==0 && conditionalEntry.Applies==0 && temporary.Applies==0 && unownedEntry.Applies==0,"Repair restored a jade, conditional/temporary effect or unowned upgrade");
        int applies=entry.Applies;active=owner.IsActivated;Check(entry.Applies==applies,"Repeated eligibility reads duplicated an existing modifier");
        entry.statData.Stat.Clear();Call("Complete",panel);Check(entry.Applies==applies+1,"Deferred menu reveal retained stale effect cache");
        owner.equipped.CurrentValue=false;entry.statData.Stat.Clear();active=owner.IsActivated;
        Check(!active && !owner.IsEquipped && entry.statData.Stat.StatModifiers.Count==0,"Repair overwrote a player equipment choice");
        owner.equipped.CurrentValue=true;owner.NativeActive=false;active=owner.IsActivated;
        Check(!active && entry.statData.Stat.StatModifiers.Count==0,"Repair bypassed native inactive verdict");
        owner.NativeActive=true;owner.acquired.CurrentValue=false;active=owner.IsActivated;
        Check(!owner.IsAcquired && entry.statData.Stat.StatModifiers.Count==0,"Repair granted ownership");
        owner.acquired.CurrentValue=true;entry.Apply(owner);NativeSaveContext.Session=new SessionToken(Guid.NewGuid());entry.statData.Stat.Clear();active=owner.IsActivated;
        Check(entry.statData.Stat.StatModifiers.Count==0,"Stale native cache modified another session");
        NativeMenu.InventoryOpen=false;Call("Begin",new MenuUIPanel());active=owner.IsActivated;
        Check(entry.statData.Stat.StatModifiers.Count==0,"Unrelated UI acquired progression cache");
        Console.WriteLine("PASS: production native-effect repair reconciles cached-true and deferred-reveal permanent upgrades, is idempotent, excludes jades/temporary/conditional/unowned effects, preserves item quantity scaling/zero-count consumption, ownership/equipment and native false verdicts, rejects stale sessions/unrelated UI. Fake endpoints; no native runtime pass.");
    }
}
