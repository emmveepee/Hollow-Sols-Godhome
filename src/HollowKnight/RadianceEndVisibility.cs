namespace YiGodhome;

// A retained ending source can outlive its arena while hit outcomes drain.
// Such a source must never acquire a new hide lease in the return room.
internal sealed class RadianceEndVisibility
{
    private int? arena;
    internal bool Request(string scene,int handle) {
        if(scene!="GG_Radiance")return false;
        arena=handle;return true;
    }
    internal bool Hidden(int handle) {
        if(arena!=handle)arena=null;
        return arena.HasValue;
    }
}
