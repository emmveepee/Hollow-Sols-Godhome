namespace YiGodhome;

// Installed level442 owns this independent root, despite using the same leaf
// name as the persistent AreaTitle UI. Keep the full ownership contract explicit.
internal static class GreyPrinceTitleSource
{
    internal static bool Matches(string scene,string? root,string? parent,string leaf,bool sceneRoot)
        => scene=="GG_Grey_Prince_Zote" && sceneRoot && root=="Grey Prince Title"
            && parent=="Title Main" && leaf=="Title Small Main";
}
