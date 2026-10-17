namespace YiGodhome.Bridge;
public static class TalismanShieldPolicy
{
    // Visibility is distinct from a true authored shield. Existing burrow and
    // teleport explosion admission stays intact outside this explicit policy.
    public static bool Allows(bool shielded,bool attachment,bool explosion)=>!shielded || !attachment&&!explosion;
    // The interface event path can bypass OnBeforeHitEnter. Recheck at the
    // actual native attachment/explosion event, before deposits/listeners run.
    public static bool AllowsEvent(bool live,bool available,bool shielded,bool attachment,bool explosion)
        => live && Allows(shielded,attachment,explosion) && (available || explosion);
}
