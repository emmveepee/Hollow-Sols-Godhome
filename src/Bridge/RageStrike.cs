namespace YiGodhome.Bridge;

// One defense identity per authored mace windup. No encounter/AI authority.
public sealed class RageStrike
{
    private string state = "";
    private long countered;
    public long Action { get; private set; }
    public long Sequence { get; private set; }
    private long firstAction;
    public bool InitialCharge => InSequence && Action == firstAction && (state == "R Attack Antic" || state == "Rage Begin");
    public bool InSequence { get; private set; }
    public bool Windup => state == "R Attack Antic" || state == "Rage Begin";
    public bool Striking => InSequence && (state == "Rage" || state == "Rage Slam");
    public void Observe(string next)
    {
        if ((next == "R Attack Antic" || next == "Rage") && !InSequence) { Sequence++; firstAction = ++Action; }
        else if (next == "Rage" && state != next && state != "R Attack Antic" && state != "Rage Begin") Action++;
        state = next;
        InSequence = Action > 0 && (next == "R Attack Antic" || next == "Rage Begin" || next == "Rage" || next == "Rage Slam" || next == "Floor Crack?"
            || next == "Floor Crack" || next == "Particle Pause" || next == "Anim End" || next == "Turn");
    }
    public bool Counter(long action)
    {
        if (!InSequence || action <= 0 || action != Action || action == countered) return false;
        countered = action;
        return true; // The native sequence and next strike are deliberately unchanged.
    }
}
