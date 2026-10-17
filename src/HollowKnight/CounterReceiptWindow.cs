namespace YiGodhome;

// A verified strike can finish locally while its peer acknowledgement is in
// flight. Callers explicitly opt in to crossing the following native move.
internal static class CounterReceiptWindow
{
    internal static bool Matches(long acknowledged,long current,long last,float age,bool allowFollowingMove)
        => acknowledged>0 && acknowledged==last && age>=0 && age<=.25f
            && (acknowledged==current || allowFollowingMove);
}
