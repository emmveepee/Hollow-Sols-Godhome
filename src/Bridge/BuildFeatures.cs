// SPDX-License-Identifier: GPL-3.0-or-later
namespace YiGodhome.Bridge;

public static class BuildFeatures
{
    public const string ReleaseVersion = "0.1.0";
#if HOLLOWSOLS_PUBLIC_RELEASE
    public static bool DevelopmentTools => false;
    public const string Flavor = "public";
    public const string ReportRepository = "https://github.com/emmveepee/Hollow-Sols-Godhome";
#else
    public static bool DevelopmentTools => true;
    public const string Flavor = "development";
    public const string ReportRepository = "";
#endif
}
