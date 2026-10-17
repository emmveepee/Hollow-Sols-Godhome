// SPDX-License-Identifier: GPL-3.0-or-later
using System;
namespace YiGodhome.Bridge;

// Presentation only: a missing pose still blocks all controls. Never extend
// gameplay freshness, retain foreign-room artwork, or hide a real damage blink.
public sealed class EntryImageHold
{
    private Guid session;
    private long geometry;
    private int room;
    private double began,lastOpaque;
    private bool armed;
    public void Observe(Guid id,long generation,int handle,double now,bool entry,bool opaque)
    {
        if(id!=session || generation!=geometry || handle!=room){session=id;geometry=generation;room=handle;began=now;armed=false;}
        armed=entry && opaque && now-began<=2;
        if(opaque)lastOpaque=now;
    }
    public bool Holds(Guid id,long generation,int handle,double now)
        => armed && id==session && generation==geometry && handle==room
            && now>=lastOpaque && now-lastOpaque<=.5 && now-began<=2;
}
