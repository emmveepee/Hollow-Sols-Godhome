using System;

namespace YiGodhome.Bridge;

public static class ReturnPreparation
{
    public static bool IsHub(string scene) => scene == "GG_Workshop" || scene == "GG_Atrium";
    public static bool MayAdvance(int pendingOutcomes, Guid session, Guid sourceSession, string scene, long frame, long previousFrame) =>
        pendingOutcomes == 0 && session != Guid.Empty && session == sourceSession
        && scene.StartsWith("GG_", StringComparison.Ordinal) && frame > previousFrame;
    public static bool MayReset(bool victoryPending, int pendingOutcomes, EncounterPhase peerPhase) =>
        victoryPending && pendingOutcomes == 0
        && (peerPhase == EncounterPhase.Returned || peerPhase == EncounterPhase.Defeated);

    public static bool FreshHall(Guid session, Guid sourceSession, string scene, long frame, long previousFrame) =>
        session != Guid.Empty && session == sourceSession && IsHub(scene) && frame > previousFrame;
}
