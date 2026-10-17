// SPDX-License-Identifier: GPL-3.0-or-later
namespace YiGodhome;

internal static class PureVesselTitleSource
{
    internal static bool Matches(string scene,string? parent,string name,bool parentIsRoot)
        => scene=="GG_Hollow_Knight" && parent=="Battle Scene" && name=="Title" && parentIsRoot;
}
