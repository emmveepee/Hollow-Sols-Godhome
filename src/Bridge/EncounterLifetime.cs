using System;

namespace YiGodhome.Bridge;

// Pure policy, driven by monotonic elapsed seconds. Native adapters own effects.
// A timeout is terminal, never authorization to replay a hit or retry a statue.
public sealed class EncounterLifetime
{
    public const double PreparationSeconds = 60, ReturnSeconds = 30;
    private Guid encounter;
    private double preparationAt, returnAt = -1;
    private double contactStoppedAt = -1;
    private double deadlineCheckAt = double.NaN;
    private bool deadlineWasPaused;
    private bool active, returned, cancelled;
    private EncounterPhase phase;
    public string? Failure { get; private set; }
    public void Observe(EncounterStatus state, double now)
    {
        if (Failure != null || cancelled) throw new InvalidOperationException("Encounter lifetime is terminal.");
        if (encounter != state.Id)
        {
            if (encounter != Guid.Empty && !returned) throw new InvalidOperationException("Encounter replaced before visible return.");
            encounter = state.Id; preparationAt = now; returnAt = -1; active = returned = false;
        }
        if (state.Phase == EncounterPhase.Active) {
            if (returnAt >= 0) throw new InvalidOperationException("Retired encounter cannot become active again.");
            active = true;
        }
        // Native results deliberately wait for human input. Adapters admit this
        // state only at the authored results UI; it is not a restoration hang.
        if (state.Phase == EncounterPhase.SequenceResults) {
            if (!active || phase != EncounterPhase.Victory && phase != EncounterPhase.Restoring && phase != EncounterPhase.SequenceResults)
                throw new InvalidOperationException("Results wait requires a retired active encounter.");
            returnAt = -1;
        }
        if (state.Phase == EncounterPhase.Advancing || state.Phase == EncounterPhase.Victory || state.Phase == EncounterPhase.Defeated
            || state.Phase == EncounterPhase.Returned || active && state.Phase == EncounterPhase.Restoring)
            if (returnAt < 0) returnAt = now;
        phase = state.Phase;
    }
    public void CompleteReturn() {
        if (Failure != null || cancelled || returnAt < 0 || phase != EncounterPhase.Returned && phase != EncounterPhase.Ready)
            throw new InvalidOperationException("Visible return completed outside the return contract.");
        returned = true;
    }
    public void Cancel() { cancelled = true; }
    public string? CheckContact(double now, bool stoppedDuringCombat)
    {
        if (cancelled) return null;
        if (Failure != null) return Failure;
        if (!stoppedDuringCombat) { contactStoppedAt = -1; return null; }
        // Contact and encounter state are independent pipes. Normal victory
        // retires contacts before its terminal state necessarily reaches Yi.
        // FreshFrame already disables attacks; allow bounded publication skew.
        if (contactStoppedAt < 0) contactStoppedAt = now;
        if (now - contactStoppedAt >= 1) Failure = "contact-channel-stopped";
        return Failure;
    }
    public string? Check(double now, bool channelStopped, int pending, bool combatEnded, bool paused = false)
    {
        if (cancelled) return null;
        if (Failure != null) return Failure;
        // Native preparation/return animations stop while the player pauses.
        // Keep transport-loss checks on wall time, but charge animation deadlines
        // only for unpaused time. Repeated checks in one frame add zero time.
        if (!double.IsNaN(deadlineCheckAt) && (paused || deadlineWasPaused)) {
            double elapsed = Math.Max(0, now - deadlineCheckAt);
            preparationAt += elapsed;
            if (returnAt >= 0) returnAt += elapsed;
        }
        deadlineCheckAt = now; deadlineWasPaused = paused;
        if (channelStopped && (pending != 0 || !combatEnded)) Failure = "hit-channel-stopped pending=" + pending;
        else if (encounter != Guid.Empty && !returned && returnAt >= 0 && now - returnAt >= ReturnSeconds) Failure = "return-deadline";
        else if (encounter != Guid.Empty && !active && !returned && returnAt < 0 && now - preparationAt >= PreparationSeconds) Failure = "preparation-deadline";
        return Failure;
    }
}
