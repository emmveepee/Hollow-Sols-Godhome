using System;
namespace YiGodhome.Bridge;

// Encounter-local receipt identities, never live damage receivers. Retain past
// native destruction long enough to drain the 250ms hit freshness window.
public sealed class RetiredTargetReceipts
{
    private readonly (int Entity,long Activation,double Until)[] entries=new (int,long,double)[256];
    public void Retire(int entity,long activation,double now) {
        if(entity==0 || activation<=0)return;
        int free=-1;
        for(int i=0;i<entries.Length;i++) {
            var e=entries[i];
            if(e.Entity==entity && e.Activation==activation){entries[i]=(entity,activation,now+1);return;}
            if(free<0 && e.Until<=now)free=i;
        }
        if(free<0)throw new InvalidOperationException("Retired target receipt capacity exhausted.");
        entries[free]=(entity,activation,now+1);
    }
    public bool Recognizes(int entity,long activation,double now) {
        foreach(var e in entries)if(e.Entity==entity && e.Activation==activation && e.Until>now)return true;
        return false;
    }
}
