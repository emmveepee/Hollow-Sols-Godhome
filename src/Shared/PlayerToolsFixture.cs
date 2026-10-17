#if HOLLOW_KNIGHT
using System;
using UnityEngine;
using YiGodhome.Bridge;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

namespace YiGodhome;

public sealed partial class BaselinePlugin
{
    private int travelFixtureStage;
    private float travelFixtureAt = -1;
    private bool travelFixtureRetryReleased;
    private float travelWallWaitAt=-1;
    private void UpdatePlayerToolsFixture()
    {
        if (!SessionConfiguration.Current.Has("travel-probe")) return;
        if (!SessionConfiguration.Current.RunFixtures || !hubCombat) throw new InvalidOperationException("Unauthorized travel fixture.");
        string scene = UnitySceneManager.GetActiveScene().name;
        if (travelFixtureStage == 0 && encounterStatus?.Phase == EncounterPhase.Active && CanDebugTravel()) {
            if (travelFixtureAt < 0) travelFixtureAt = Time.realtimeSinceStartup;
            if (Time.realtimeSinceStartup - travelFixtureAt < 0.5f) return;
            RequestDebugTravel("GG_Workshop"); travelFixtureStage = 1;
            Log("HK_TRAVEL_FIXTURE_CANCEL encounter=" + encounterStatus.Id + " geometry=" + encounterStatus.GeometryFrame);
        }
        else if (travelFixtureStage == 1 && scene == "GG_Workshop" && hubPresence?.Ready == true && debugTravel == null && CanDebugTravel()) {
            if (!travelFixtureRetryReleased) {
                travelFixtureRetryReleased = true;
                Log("HK_TRAVEL_HALL_RETRY_RELEASE drained=True nativeReturnAlreadyArrived=True geometry=" + hubPresence.GeometryFrame);
            }
        }
        else if (travelFixtureStage == 1 && encounterStatus?.Phase == EncounterPhase.Active && CanDebugTravel()) {
            travelFixtureRetryReleased = false;
            Log("HK_TRAVEL_HALL_RETRY_ENTERED nativeStatue=True encounter=" + encounterStatus.Id);
            RequestDebugTravel("GG_Atrium"); travelFixtureStage = 2;
        }
        else if (travelFixtureStage == 2 && scene == "GG_Atrium" && hubPresence?.Ready == true && debugTravel == null && CanDebugTravel()) {
            if(SessionConfiguration.Current.Has("wall-probe")) {
                if(travelWallWaitAt<0) {travelWallWaitAt=Time.realtimeSinceStartup;Log("HK_TRAVEL_WALL_FIXTURE_WAIT scene=GG_Atrium seconds=44");}
                if(Time.realtimeSinceStartup-travelWallWaitAt<44) return;
            }
            var frame = endpoint?.FreshPose;
            var root = hubPresence.FirstRestPoint;
            if (frame == null || !root.HasValue || Math.Abs(frame.X - root.Value.x) > 2 || Math.Abs(frame.Y - root.Value.y) > 2)
                throw new InvalidOperationException("Translated Yi did not arrive at the first hub root.");
            Log("HK_TRAVEL_ROOT_VERIFIED geometry=" + frame.GeometryFrame + " yiX=" + frame.X + " yiY=" + frame.Y + " root=" + root.Value);
            RequestDebugTravel("GG_Workshop"); travelFixtureStage = 3;
        }
        else if (travelFixtureStage == 3 && scene == "GG_Workshop" && hubPresence?.Ready == true && debugTravel == null
            && visualPresenter?.ReadyForControl == true && CanDebugTravel()) {
            travelFixtureStage = 4;
            Log("HK_TRAVEL_FIXTURE_COMPLETE nativeCancellation=True aliveYi=True hubHallRoundTrip=True geometry=" + hubPresence.GeometryFrame);
        }
    }
}
#endif
