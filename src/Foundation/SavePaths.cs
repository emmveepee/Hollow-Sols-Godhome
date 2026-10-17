using System;
using System.IO;
using System.Linq;

namespace HollowSols.Foundation;

public static class SavePaths
{
    public static string DefaultRoot(string localApplicationData)
    {
        string home = Path.GetFullPath(Path.Combine(localApplicationData, "HollowSols"));
        // Startup logs are application infrastructure, not save contents. Keep
        // them outside the strict namespace. Previously initialized namespaces
        // stay in place; unidentified older data must still fail closed there.
        if (Directory.Exists(home) && Directory.EnumerateFileSystemEntries(home).Any(path => {
            string name = Path.GetFileName(path);
            return name != "store.lock" && !(name == "logs" && Directory.Exists(path)) && !(name == "paired" && Directory.Exists(path));
        })) return home;
        return Path.Combine(home, "paired", "HollowSols");
    }
}
