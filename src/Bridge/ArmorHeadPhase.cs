using System;

namespace YiGodhome.Bridge;

// Presentation/lease policy for the audited native FalseyControl state names.
// There is deliberately no completion operation or writable native health.
public sealed class ArmorHeadPhase
{
    public long ArmorLease { get; private set; } = 1;
    public long HeadLease { get; private set; } = 1;
    public bool Broken { get; private set; }
    public bool Exposed { get; private set; }
    public bool HeadDamageState { get; private set; }
    public void Observe(string state, bool headActive)
    {
        bool broken = Broken;
        if (state == "Stun Start" || state.StartsWith("Stun ", StringComparison.Ordinal) || state.StartsWith("Open", StringComparison.Ordinal) || state.StartsWith("Death", StringComparison.Ordinal)) broken = true;
        else if (state == "Idle Pause" || state == "Idle" || state == "First Idle") broken = false;
        HeadDamageState = state == "Opened" || state == "Opened 2";
        // Native per-hit reactions belong to this exposure, not a fresh target.
        bool exposed = headActive && (HeadDamageState || state == "Hit" || state == "Hit 2");
        if (Broken && !broken) ArmorLease++;
        if (!Exposed && exposed) HeadLease++;
        Broken = broken; Exposed = exposed;
    }
    public int ArmorHealth(int nativeHealth) => Broken ? 0 : Math.Max(0, nativeHealth);
}
