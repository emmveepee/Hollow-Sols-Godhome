// SPDX-License-Identifier: GPL-3.0-or-later
namespace YiGodhome;

internal static class GodhomeRoofAccess
{
    internal static bool Applies(bool paired,bool fullAccess,bool godhome,string scene,string owner,string fsm,string state,string variable)
        => paired && fullAccess && godhome && scene=="GG_Atrium" && owner=="gg_roof_lever"
            && fsm=="Switch Control" && state=="Initiate" && variable=="Activated";
}
