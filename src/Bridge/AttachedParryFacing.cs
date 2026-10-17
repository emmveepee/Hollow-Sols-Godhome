namespace YiGodhome.Bridge;

public static class AttachedParryFacing
{
    public static bool Restore(int attachedFacing,int currentFacing,bool accepted,bool onLedge,bool onWall,bool attached)
        => accepted && onLedge && onWall && attached && (attachedFacing==1 || attachedFacing==-1) && currentFacing!=attachedFacing;
}
