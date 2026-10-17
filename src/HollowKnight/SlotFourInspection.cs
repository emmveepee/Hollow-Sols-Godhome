using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace YiGodhome;

internal static class SlotFourInspection
{
    public static void Begin(Action<string> log)
    {
        log("HK_SLOT4_INSPECTION_BEGIN writes=disabled sceneEntry=disabled");
        string? originalHash;
        try { originalHash = SlotFourPreparation.ReadSourceHash(); }
        catch (Exception e) { log("HK_SLOT4_INSPECTION result=unresolved-io " + e.GetType().Name + " replacement=blocked"); return; }
        if (originalHash == null)
        {
            try
            {
                var fresh = GodseekerProfile.CreateAndValidate();
                SlotFourPreparation.Stage(fresh, null, false);
                log("HK_SLOT4_INSPECTION result=absent-confirmed writes=disabled");
                log("HK_GODSEEKER_CANDIDATE validatedInMemory=True statues=45 pantheons=5 policy=create-empty-slot activeDataUnchanged=True writes=disabled");
            }
            catch (Exception e) { log("HK_SLOT4_INSPECTION result=unresolved " + e.GetType().Name + " replacement=blocked"); }
            return;
        }
        GameManager.instance.LoadGame(4, success =>
        {
            if (!success)
            {
                log("HK_SLOT4_INSPECTION result=unresolved-native-load-failure replacement=blocked");
                return;
            }
            try
            {
                var data = GameManager.instance.playerData;
                var fields = typeof(PlayerData).GetFields(BindingFlags.Public | BindingFlags.Instance);
                var statues = fields.Where(f => f.FieldType == typeof(BossStatue.Completion))
                    .Select(f => (BossStatue.Completion)f.GetValue(data)).ToArray();
                var doors = fields.Where(f => f.FieldType == typeof(BossSequenceDoor.Completion))
                    .Select(f => (BossSequenceDoor.Completion)f.GetValue(data)).ToArray();
                bool sceneExists = !string.IsNullOrEmpty(data.respawnScene) && Application.CanStreamedLevelBeLoaded(data.respawnScene);
                // Candidate assessment only. Native Godhome availability must be verified before replacement is enabled.
                log("HK_SLOT4_INSPECTION result=native-load-ok godseeker=" + data.bossRushMode
                    + " statues=" + statues.Length + " unlocked=" + statues.Count(s => s.isUnlocked)
                    + " ascendedCompletions=" + statues.Count(s => s.completedTier2)
                    + " pantheons=" + doors.Length + " unlockedPantheons=" + doors.Count(d => d.unlocked)
                    + " radianceGate=" + (data.unlockedBossScenes != null && data.unlockedBossScenes.Contains(GodseekerProfile.RadianceSceneUnlock))
                    + " spawnSceneExists=" + sceneExists + " writes=disabled replacement=not-enabled");
                var original = new SaveGameData(data, GameManager.instance.sceneData);
                var before = JsonUtility.ToJson(original);
                bool preserve = GodseekerProfile.HasRequiredAccess(original);
                var candidate = GodseekerProfile.CreateAndValidate();
                if (!ReferenceEquals(GameManager.instance.playerData, data) || before != JsonUtility.ToJson(original))
                    throw new InvalidOperationException("Isolated preparation altered active data.");
                SlotFourPreparation.Stage(candidate, originalHash, preserve);
                log("HK_GODSEEKER_CANDIDATE validatedInMemory=" + GodseekerProfile.HasRequiredAccess(candidate)
                    + " statues=45 pantheons=5 policy=" + (preserve ? "preserve-existing" : "backup-required-before-replacement")
                    + " activeDataUnchanged=True writes=disabled");
            }
            catch (Exception e) { log("HK_SLOT4_INSPECTION result=unresolved " + e.GetType().Name + " replacement=blocked"); }
        });
    }
}
