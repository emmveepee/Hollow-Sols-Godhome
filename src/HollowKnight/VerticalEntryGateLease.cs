// SPDX-License-Identifier: GPL-3.0-or-later
namespace YiGodhome;

internal sealed class VerticalEntryGateLease
{
    private int room,gate;
    private long geometry;
    internal bool Pending { get; private set; }
    internal void Reset(){Pending=false;}
    internal void Arm(int scene,long frame,int gateId){room=scene;geometry=frame;gate=gateId;Pending=true;}
    internal bool Blocks(int scene,int gateId)=>Pending && room==scene && gate==gateId;
    internal bool Observe(long frame,bool entering,bool grounded) {
        if(!Pending || frame!=geometry || entering || !grounded)return false;
        Pending=false;return true;
    }
}
