using System.Linq;
using System;
using System.IO;

namespace YiGodhome.Bridge;

public sealed class EncounterProfile
{
    public readonly string Key, Scene, Statue, Boss;
    // Alternate arenas retain exact scene/pool ownership and share boss rules.
    public EncounterProfile Family
    {
        get {
            // This lookup is used repeatedly for every native projectile.
            // Avoid allocating a capturing predicate/enumerator on each read.
            foreach (var profile in All) if (profile.Key == Key) return profile;
            return this;
        }
    }
    public bool Roster => EntryFsm.Length == 0 && !AuditOnly;
    public bool AuditOnly => this == FalseKnightAudit || this == FailedChampionAudit;
    public bool ArmorAndHead => this == FalseKnight || this == FailedChampion;
    public bool OwnNativePools { get; private set; }
    public bool NativeHitAuthority { get; private set; }
    private string[] actorNames = Array.Empty<string>();
    private string? displayName;
    private static readonly string[] ZoteMinions = { "Zoteling", "Zoteling (1)", "Zoteling (2)", "Zoteling (3)", "Zote Balloon", "Zote Balloon (1)", "Zote Balloon (2)", "Zote Balloon (3)" };
    private static readonly string[] FlukeMinions = { "Fluke Fly Spawner", "Fluke Fly Spawner (1)", "Fluke Fly Spawner (2)", "Fluke Fly Spawner (3)", "Fluke Fly Spawner (4)", "Fluke Fly Spawner (5)" };
    public bool IsTargetName(string name) => IsBossName(name) || this == GreyPrinceZote && Array.IndexOf(ZoteMinions, name) >= 0
        || this == Flukemarm && Array.IndexOf(FlukeMinions, name) >= 0 || ArmorAndHead && name == "Head" || Key == "Uumuu" && name.StartsWith("Lil Jellyfish", StringComparison.Ordinal)
        || Key=="SoulWarrior" && MageBalloonName(name);
    private static bool MageBalloonName(string name) {
        const string root="Mage Balloon Spawner";
        if(name==root)return true;
        if(!name.StartsWith(root+" (",StringComparison.Ordinal) || !name.EndsWith(")",StringComparison.Ordinal))return false;
        return int.TryParse(name.Substring(root.Length+2,name.Length-root.Length-3),out int index) && index>=1 && index<=35;
    }
    public bool IsBossName(string name) => actorNames.Length != 0 ? Array.IndexOf(actorNames, name) >= 0
        : this == Mantis || this == Sisters ? name.StartsWith("Mantis Lord", StringComparison.Ordinal)
        : this == Watchers ? name.StartsWith("Black Knight", StringComparison.Ordinal) : name == Boss;
    public bool UsesHealthNotches {
        get {
            var family=Family;
            return family != HornetSentinel && family != Flukemarm && family != HiveKnight
                && family != BrokenVessel && family != LostKin && family != Nosk && family != WingedNosk
                && family != Collector && family != Xero && family != Galien && family != Gorb
                && family != ElderHu && family != Sheo && family != NoEyes && family != TraitorLord && family != Sly;
        }
    }
    // Outgoing native boss damage is independent of incoming projectile damage
    // and Hall difficulty (including Radiant's lethal player hits).
    public int ReflectedProjectileDamage {
        get {
            var family = Family;
            return family == SoulMaster || family == SoulTyrant || family == Xero || family == AbsoluteRadiance ? 42 : 21;
        }
    }
    public bool IndividualBossBars => Roster && this != Watchers;
    public string DisplayName => displayName ?? (this == Gruz ? "Gruz Mother" : this == Vengefly ? "Vengefly King" : this == Mawlek ? "Brooding Mawlek" : this == PureVessel ? "Pure Vessel" : this == Mantis ? "Mantis Lords" : this == Sisters ? "Sisters of Battle" : this == NightmareGrimm ? "Nightmare King Grimm" : this == Sly ? "Great Nailsage Sly" : this == Watchers ? "Watcher Knights" : Key);
    public readonly string EntryFsm, EntryState;
    public readonly bool EntryWaitsForPlayer;
    public readonly bool EntryStartsAnimation;
    private EncounterProfile(string key, string scene, string statue, string boss, string entryFsm, string entryState, bool entryWaitsForPlayer = false, bool entryStartsAnimation = false)
    { Key = key; Scene = scene; Statue = statue; Boss = boss; EntryFsm = entryFsm; EntryState = entryState; EntryWaitsForPlayer = entryWaitsForPlayer; EntryStartsAnimation = entryStartsAnimation; }
    public static readonly EncounterProfile Gruz = new EncounterProfile("GruzMother", "GG_Gruz_Mother", "statueStateGruzMother", "Giant Fly", "Big Fly Control", "GG Extra Pause");
    public static readonly EncounterProfile Xero = new EncounterProfile("Xero", "GG_Ghost_Xero", "statueStateXero", "Ghost Warrior Xero", "Movement", "Warp In");
    public static readonly EncounterProfile Vengefly = new EncounterProfile("VengeflyKing", "GG_Vengefly", "statueStateVengefly", "Giant Buzzer Col", "Big Buzzer", "Set GG");
    public static readonly EncounterProfile Mawlek = new EncounterProfile("BroodingMawlek", "GG_Brooding_Mawlek", "statueStateBroodingMawlek", "Mawlek Body", "Mawlek Control", "GG Wake", entryStartsAnimation: true);
    public static readonly EncounterProfile PureVessel = new EncounterProfile("PureVessel", "GG_Hollow_Knight", "statueStateHollowKnight", "HK Prime", "Control", "Intro 1");
    public static readonly EncounterProfile Mantis = new EncounterProfile("MantisLords", "GG_Mantis_Lords", "statueStateMantisLords", "Mantis Lord", "", "") { NativeHitAuthority = true };
    public static readonly EncounterProfile Sisters = new EncounterProfile("SistersOfBattle", "GG_Mantis_Lords_V", "statueStateMantisLordsExtra", "Mantis Lord", "", "") { NativeHitAuthority = true };
    public static readonly EncounterProfile Grimm = new EncounterProfile("Grimm", "GG_Grimm", "statueStateGrimm", "Grimm Boss", "", "") { OwnNativePools = true };
    public static readonly EncounterProfile NightmareGrimm = new EncounterProfile("NightmareKingGrimm", "GG_Grimm_Nightmare", "statueStateNightmareGrimm", "Nightmare Grimm Boss", "", "") { OwnNativePools = true };
    public static readonly EncounterProfile Sly = new EncounterProfile("NailsageSly", "GG_Sly", "statueStateSly", "Sly Boss", "", "");
    public static readonly EncounterProfile Watchers = new EncounterProfile("WatcherKnights", "GG_Watcher_Knights", "statueStateWatcherKnights", "Black Knight 1", "", "");
    // Native-only audit; deliberately absent from ForScene combat selection.
    public static readonly EncounterProfile FalseKnightAudit = new EncounterProfile("FalseKnightAudit", "GG_False_Knight", "statueStateFalseKnight", "False Knight New", "", "");
    public static readonly EncounterProfile FailedChampionAudit = new EncounterProfile("FailedChampionAudit", "GG_Failed_Champion", "statueStateFailedChampion", "False Knight Dream", "", "");
    private static EncounterProfile Rough(string key, string scene, string statue, string display, params string[] actors)
        => new EncounterProfile(key, scene, statue, actors[0], "", "") { OwnNativePools = true, NativeHitAuthority = true, actorNames = actors, displayName = display };
    public static readonly EncounterProfile FalseKnight = Rough("FalseKnight", "GG_False_Knight", "statueStateFalseKnight", "False Knight", "False Knight New");
    public static readonly EncounterProfile FailedChampion = Rough("FailedChampion", "GG_Failed_Champion", "statueStateFailedChampion", "Failed Champion", "False Knight Dream");
    public static readonly EncounterProfile HornetProtector = Rough("HornetProtector", "GG_Hornet_1", "statueStateHornet1", "Hornet Protector", "Hornet Boss 1");
    public static readonly EncounterProfile HornetSentinel = Rough("HornetSentinel", "GG_Hornet_2", "statueStateHornet2", "Hornet Sentinel", "Hornet Boss 2");
    public static readonly EncounterProfile BrokenVessel = Rough("BrokenVessel", "GG_Broken_Vessel", "statueStateBrokenVessel", "Broken Vessel", "Infected Knight");
    public static readonly EncounterProfile LostKin = Rough("LostKin", "GG_Lost_Kin", "statueStateLostKin", "Lost Kin", "Lost Kin");
    public static readonly EncounterProfile CrystalGuardian = Rough("CrystalGuardian", "GG_Crystal_Guardian", "statueStateCrystalGuardian1", "Crystal Guardian", "Mega Zombie Beam Miner (1)");
    public static readonly EncounterProfile EnragedGuardian = Rough("EnragedGuardian", "GG_Crystal_Guardian_2", "statueStateCrystalGuardian2", "Enraged Guardian", "Zombie Beam Miner Rematch");
    public static readonly EncounterProfile DungDefender = Rough("DungDefender", "GG_Dung_Defender", "statueStateDungDefender", "Dung Defender", "Dung Defender");
    public static readonly EncounterProfile WhiteDefender = Rough("WhiteDefender", "GG_White_Defender", "statueStateWhiteDefender", "White Defender", "White Defender");
    public static readonly EncounterProfile HiveKnight = Rough("HiveKnight", "GG_Hive_Knight", "statueStateHiveKnight", "Hive Knight", "Hive Knight");
    public static readonly EncounterProfile TraitorLord = Rough("TraitorLord", "GG_Traitor_Lord", "statueStateTraitorLord", "Traitor Lord", "Mantis Traitor Lord");
    public static readonly EncounterProfile MassiveMossCharger = Rough("MassiveMossCharger", "GG_Mega_Moss_Charger", "statueStateMegaMossCharger", "Massive Moss Charger", "Mega Moss Charger");
    public static readonly EncounterProfile SoulWarrior = Rough("SoulWarrior", "GG_Mage_Knight", "statueStateMageKnight", "Soul Warrior", "Mage Knight");
    public static readonly EncounterProfile Nosk = Rough("Nosk", "GG_Nosk", "statueStateNosk", "Nosk", "Mimic Spider");
    public static readonly EncounterProfile WingedNosk = Rough("WingedNosk", "GG_Nosk_Hornet", "statueStateNoskHornet", "Winged Nosk", "Hornet Nosk");
    public static readonly EncounterProfile OroMato = Rough("OroMato", "GG_Nailmasters", "statueStateNailmasters", "Brothers Oro & Mato", "Mato", "Oro");
    public static readonly EncounterProfile Sheo = Rough("Sheo", "GG_Painter", "statueStatePaintmaster", "Paintmaster Sheo", "Sheo Boss");
    public static readonly EncounterProfile Oblobbles = Rough("Oblobbles", "GG_Oblobbles", "statueStateOblobbles", "Oblobbles", "Mega Fat Bee", "Mega Fat Bee (1)");
    public static readonly EncounterProfile GodTamer = Rough("GodTamer", "GG_God_Tamer", "statueStateGodTamer", "God Tamer", "Lancer", "Lobster");
    public static readonly EncounterProfile Galien = Rough("Galien", "GG_Ghost_Galien", "statueStateGalien", "Galien", "Ghost Warrior Galien");
    public static readonly EncounterProfile Gorb = Rough("Gorb", "GG_Ghost_Gorb", "statueStateGorb", "Gorb", "Ghost Warrior Slug");
    public static readonly EncounterProfile ElderHu = Rough("ElderHu", "GG_Ghost_Hu", "statueStateElderHu", "Elder Hu", "Ghost Warrior Hu");
    public static readonly EncounterProfile Markoth = Rough("Markoth", "GG_Ghost_Markoth", "statueStateMarkoth", "Markoth", "Ghost Warrior Markoth");
    public static readonly EncounterProfile Marmu = Rough("Marmu", "GG_Ghost_Marmu", "statueStateMarmu", "Marmu", "Ghost Warrior Marmu");
    public static readonly EncounterProfile NoEyes = Rough("NoEyes", "GG_Ghost_No_Eyes", "statueStateNoEyes", "No Eyes", "Ghost Warrior No Eyes");
    public static readonly EncounterProfile SoulMaster = Rough("SoulMaster", "GG_Soul_Master", "statueStateSoulMaster", "Soul Master", "Mage Lord", "Mage Lord Phase2");
    public static readonly EncounterProfile SoulTyrant = Rough("SoulTyrant", "GG_Soul_Tyrant", "statueStateSoulTyrant", "Soul Tyrant", "Dream Mage Lord", "Dream Mage Lord Phase2");
    public static readonly EncounterProfile Collector = Rough("Collector", "GG_Collector", "statueStateCollector", "The Collector", "Jar Collector");
    public static readonly EncounterProfile GreyPrinceZote = Rough("GreyPrinceZote", "GG_Grey_Prince_Zote", "statueStateGreyPrince", "Grey Prince Zote", "Grey Prince");
    public static readonly EncounterProfile Flukemarm = Rough("Flukemarm", "GG_Flukemarm", "statueStateFlukemarm", "Flukemarm", "Fluke Mother");
    public static readonly EncounterProfile Uumuu = Rough("Uumuu", "GG_Uumuu", "statueStateUumuu", "Uumuu", "Mega Jellyfish GG");
    public static readonly EncounterProfile AbsoluteRadiance = Rough("AbsoluteRadiance", "GG_Radiance", "statueStateRadiance", "Absolute Radiance", "Absolute Radiance");
    public static readonly EncounterProfile[] All = { Gruz, Xero, Vengefly, Mawlek, PureVessel, Mantis, Sisters, Grimm, NightmareGrimm, Sly, Watchers, HornetProtector, HornetSentinel, BrokenVessel, LostKin, CrystalGuardian, EnragedGuardian, DungDefender, WhiteDefender, HiveKnight, TraitorLord, MassiveMossCharger, SoulWarrior, Nosk, WingedNosk, OroMato, Sheo, Oblobbles, GodTamer, Galien, Gorb, ElderHu, Markoth, Marmu, NoEyes, SoulMaster, SoulTyrant, Collector, GreyPrinceZote, Flukemarm, FalseKnight, FailedChampion, Uumuu, AbsoluteRadiance };
    private static readonly EncounterProfile[] Variants = new[] { Gorb, Markoth, Marmu, NoEyes, Xero, Collector, Nosk, SoulWarrior, Uumuu }
        .Select(p => p.Roster ? Rough(p.Key, p.Scene + "_V", p.Statue, p.DisplayName, p.Boss)
            : new EncounterProfile(p.Key,p.Scene+"_V",p.Statue,p.Boss,p.EntryFsm,p.EntryState,p.EntryWaitsForPlayer,p.EntryStartsAnimation) { displayName=p.DisplayName })
        .Concat(new[] { Rough(Gruz.Key, Gruz.Scene + "_V", Gruz.Statue, Gruz.DisplayName, Gruz.Boss),
            Rough(Vengefly.Key, Vengefly.Scene + "_V", Vengefly.Statue, Vengefly.DisplayName, "Giant Buzzer Col", "Giant Buzzer Col (1)"),
            new EncounterProfile(Mawlek.Key,Mawlek.Scene+"_V",Mawlek.Statue,Mawlek.Boss,Mawlek.EntryFsm,Mawlek.EntryState,entryStartsAnimation:true) }).ToArray();
    public static EncounterProfile Selected { get; private set; } = Load();
    public static EncounterProfile? ForScene(string scene)
    {
        foreach (var profile in All) if (profile.Scene == scene) return profile;
        // These installed native alternate arenas retain the same authored
        // actors; admit their own scene/pool scope, never the base room's scope.
        foreach (var profile in Variants) if (scene == profile.Scene) return profile;
        return null;
    }
    public static bool SelectScene(string scene)
    {
        var profile = ForScene(scene);
        if (profile == null) return false;
        Selected = profile; return true;
    }
    private static EncounterProfile Load()
    {
        string? log = Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG");
        if (string.IsNullOrEmpty(log)) return Gruz;
        string path = Path.Combine(Path.GetDirectoryName(log)!, "encounter-profile.request");
        if (!File.Exists(path)) return Gruz;
        string name = File.ReadAllText(path).Trim();
        foreach (var profile in All) if (name == profile.Key) return profile;
        if (name == FalseKnightAudit.Key) return FalseKnightAudit;
        if (name == FailedChampionAudit.Key) return FailedChampionAudit;
        throw new InvalidDataException("Unknown encounter profile.");
    }
}
