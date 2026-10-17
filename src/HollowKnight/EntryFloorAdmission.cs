// SPDX-License-Identifier: GPL-3.0-or-later
namespace YiGodhome;

internal sealed class EntryFloorAdmission
{
    internal enum Verdict { Ready, Waiting, Expired }
    internal bool Waiting { get; private set; }
    private double started;
    internal void Reset() { Waiting=false;started=0; }
    internal Verdict Observe(double now,bool floorAvailable) {
        if(floorAvailable){Reset();return Verdict.Ready;}
        if(!Waiting){Waiting=true;started=now;}
        return now-started>=8 ? Verdict.Expired : Verdict.Waiting;
    }
}
