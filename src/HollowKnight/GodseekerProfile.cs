using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace YiGodhome;

// Creates isolated native data only. No file access, active-state assignment, achievement calls or scene loading.
internal static class GodseekerProfile
{
    internal const string RadianceSceneUnlock = "Radiance Boss Scene";
    private static readonly FieldInfo[] Statues = typeof(PlayerData).GetFields(BindingFlags.Public | BindingFlags.Instance)
        .Where(f => f.FieldType == typeof(BossStatue.Completion)).ToArray();
    private static readonly FieldInfo[] Doors = typeof(PlayerData).GetFields(BindingFlags.Public | BindingFlags.Instance)
        .Where(f => f.FieldType == typeof(BossSequenceDoor.Completion)).ToArray();

    public static SaveGameData CreateAndValidate()
    {
        if (Statues.Length != 45 || Doors.Length != 5) throw new InvalidOperationException("Native Godhome field manifest changed.");
        var data = (PlayerData)Activator.CreateInstance(typeof(PlayerData), true);
        var scenes = (SceneData)Activator.CreateInstance(typeof(SceneData), true);
        data.AddGGPlayerDataOverrides();
        data.profileID = 4;
        UseGodhomeBench(data);
        UnlockRadianceGate(data);
        foreach (var field in Statues)
        {
            var statue = (BossStatue.Completion)field.GetValue(data);
            statue.hasBeenSeen = true; statue.isUnlocked = true;
            statue.completedTier1 = true; statue.completedTier2 = true; statue.completedTier3 = true;
            statue.seenTier3Unlock = true;
            field.SetValue(data, statue);
        }
        foreach (var field in Doors)
        {
            var door = (BossSequenceDoor.Completion)field.GetValue(data);
            door.canUnlock = true; door.unlocked = true; door.completed = true;
            door.boundNail = true; door.boundShell = true; door.boundCharms = true; door.boundSoul = true;
            door.allBindings = true;
            field.SetValue(data, door);
        }
        data.visitedGodhome = true; data.godseekerUnlocked = true;
        data.bossDoorCageUnlocked = true; data.finalBossDoorUnlocked = true;
        data.blueRoomDoorUnlocked = true; data.blueRoomActivated = true;
        data.zoteStatueWallBroken = true; data.ordealAchieved = true;
        var candidate = new SaveGameData(data, scenes);
        // Use the game's Unity serializer and native save container, without emitting game/save data to disk.
        var roundTrip = JsonUtility.FromJson<SaveGameData>(JsonUtility.ToJson(candidate));
        if (!HasRequiredAccess(roundTrip)) throw new InvalidOperationException("Godseeker candidate failed native JSON round trip.");
        return roundTrip;
    }

    public static void UseGodhomeBench(PlayerData data)
    {
        if (data.profileID != 4 || !data.bossRushMode || !Application.CanStreamedLevelBeLoaded("GG_Atrium"))
            throw new InvalidOperationException("Bench checkpoint requires validated Godseeker slot 4.");
        // Native GG_Atrium marker and bench respawn type verified in the installed build.
        data.respawnScene = "GG_Atrium";
        data.respawnMarkerName = "RestBench";
        data.respawnType = 1;
        data.respawnFacingRight = false;
        data.atBench = true;
    }

    public static void UnlockRadianceGate(PlayerData data)
    {
        if (data.profileID != 4 || !data.bossRushMode) throw new InvalidOperationException("Radiance access requires Godseeker slot 4.");
        if (data.unlockedBossScenes == null) data.unlockedBossScenes = new List<string>();
        if (!data.unlockedBossScenes.Contains(RadianceSceneUnlock)) data.unlockedBossScenes.Add(RadianceSceneUnlock);
    }

    public static bool HasRequiredAccess(SaveGameData save)
    {
        var data = save?.playerData;
        return data != null && save!.sceneData != null && data.profileID == 4 && data.bossRushMode
            && Statues.Length == 45 && Doors.Length == 5
            && Statues.All(f => { var s = (BossStatue.Completion)f.GetValue(data); return s.isUnlocked && s.completedTier2; })
            && Doors.All(f => ((BossSequenceDoor.Completion)f.GetValue(data)).unlocked)
            && data.bossDoorCageUnlocked && data.finalBossDoorUnlocked
            && data.unlockedBossScenes != null && data.unlockedBossScenes.Contains(RadianceSceneUnlock)
            && !string.IsNullOrEmpty(data.respawnScene) && data.respawnScene.StartsWith("GG_", StringComparison.Ordinal)
            && Application.CanStreamedLevelBeLoaded(data.respawnScene);
    }
}
