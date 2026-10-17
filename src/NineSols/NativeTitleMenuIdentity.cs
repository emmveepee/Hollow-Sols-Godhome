namespace YiGodhome;

internal static class NativeTitleMenuIdentity
{
    internal static bool IsTitleGroup(UIControlGroup group)
    {
        // Instance creates a missing title controller. Its Awake clears all
        // live character stats and unloads audio banks. UI identity checks must
        // only observe the existing owner, including during native menu return.
        var menu=StartMenuLogic.InstanceRaw;
        return menu!=null && group==menu.startControlGroup;
    }
}
