using System;

namespace YiGodhome;

// Called at native FSM entry, before its synchronous projectile spawns.
internal sealed class DefenderEruptionSequence
{
    internal long Sequence {get;private set;}
    internal long Strike {get;private set;}
    private bool landingRetired;
    internal bool HarmlessLanding(string state,float y,float launchY,float apexY)
    {
        if(Sequence>0 || state!="Erupt Fall" && state!="Erupt Land")return false;
        // Use authored world heights; retire permanently for this descent once
        // halfway down, so collision corrections cannot flicker it back on.
        if(state=="Erupt Land" || y<=launchY+(apexY-launchY)*.5f)landingRetired=true;
        return landingRetired;
    }
    internal static bool Launch(string state)=>state=="Erupt Out" || state=="Erupt Out First";
    private static bool Within(string state)=>Launch(state) || state=="Rage Roar" || state=="Set Rage"
        || state=="Dive In 1" || state=="Dive In 2" || state=="Underground" || state=="Erupt Tunneling"
        || state=="Tunneling L" || state=="Tunneling R" || state=="Set Min X" || state=="Set Max X"
        || state=="Erupt Antic" || state=="Erupt Antic R" || state=="First?" || state=="Erupt Fall"
        || state=="Erupt Land" || state=="Rage In";
    internal bool Enter(string state,Func<long> allocate)
    {
        if(state=="Rage Roar" || state=="Set Rage") {
            if(Sequence==0){Sequence=allocate();Strike=0;}
        } else if(!Within(state)){Sequence=0;landingRetired=false;}
        if(!Launch(state))return false;
        landingRetired=false;Strike=allocate();return true;
    }
}
