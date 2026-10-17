namespace YiGodhome.Bridge;

// One collider's accepted defense consumes only that activation. Snapshot updates,
// separation and suspended transport cannot rearm it; a later native activation can.
public sealed class AttackRetirement
{
    private long retired;
    public bool Contains(long activation) => activation > 0 && activation <= retired;
    public bool Retire(long activation)
    {
        if (activation <= 0 || Contains(activation)) return false;
        retired = activation;
        return true;
    }
}
