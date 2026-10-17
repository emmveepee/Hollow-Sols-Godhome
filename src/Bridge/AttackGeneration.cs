namespace YiGodhome.Bridge;

// Exported damaging windows and native pool enables both define fresh attacks.
public sealed class AttackGeneration
{
    private bool active;
    private long source, activation;
    // Policy decoration needs the incoming native window's identity before
    // final translated activity/action retirement is committed.
    public long Preview(bool enabled,long nativeActivation)
        => enabled && (!active || nativeActivation!=source)?activation+1:activation;
    public long Observe(bool enabled, long nativeActivation)
    {
        if (enabled && (!active || nativeActivation != source)) activation++;
        active = enabled; source = nativeActivation;
        return activation;
    }
}
