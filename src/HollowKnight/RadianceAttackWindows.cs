namespace YiGodhome;

// Native collider eligibility is checked by the caller. Comb swords inherit
// their moving parent's Tween, rather than owning Rigidbody velocity.
internal static class RadianceAttackWindows
{
    internal static bool BodyWarning(string choice,string state) => (choice=="Eye Beam Wait" || choice=="Eye Beam")
        && (state=="A1 Cast Antic" || state=="A2 Cast Antic" || state=="Casting") || state=="Ascend Cast" || RainSequence(state);
    internal static bool Ending(string state)=>state=="Final Impact" || state=="Knight Break Antic" || state=="Knight Break";
    internal static bool RainSequence(string state) => state=="Rage1 Antic" || state=="Rage1 Start" || state=="Rage Comb" || state=="Rage Eyes" || state=="Rage1 Loop";
    internal static int ReflectedBladeDamage(bool crimsonRain) => crimsonRain?84:42;
    internal static bool BeamBurstState(string state)=>state=="EB 1" || state=="EB 2" || state=="EB 3" || state=="Aim";
    internal static long BeamAction(bool ascent,long burst,System.Func<long> allocate)
        => !ascent && burst>0?burst:allocate();
    internal static bool NailFlight(bool ownMotion, string? combState) => ownMotion || combState == "Tween";
    internal static bool OrbFlight(string state, bool radiance) => state == "Orbiting" || state == "Chase Hero"
        || radiance && state == "Chase Hero 2";
}
