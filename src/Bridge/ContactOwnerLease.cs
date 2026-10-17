namespace YiGodhome.Bridge;

public static class ContactOwnerLease
{
    public static bool Live(SummonedTarget[] targets,int entity,long generation) {
        foreach(var target in targets)if(target.Entity==entity && target.Generation==generation)return target.Health>0;
        return false;
    }
}
