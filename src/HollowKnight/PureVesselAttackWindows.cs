namespace YiGodhome;

// Authored damaging states, independent of child objects left enabled by interruption.
internal static class PureVesselAttackWindows
{
    public static bool Dash(string state) => state == "Dash";
    public static bool Tendril(string state) => state == "Tendril Hit" || state == "T2" || state == "T3";
    public static bool Descending(string state) => state == "Stomp Down";
    public static bool DashWindup(string state) => state == "Dash Antic";
    public static bool Focus(string state) => state == "Focus Charge" || state == "Ball Up" || state == "Focus Burst" || state == "Hit End" || state == "Focus Recover" || state == "Focus Wait";
    public static bool FocusWindup(string state) => state == "Focus Charge" || state == "Ball Up";
}
