namespace YiGodhome.Bridge;

// Track the incoming hold, not the current panel's interactive/visible state.
// Re-enabling a page must not turn the same bumper hold into another press.
internal sealed class MenuTabPress
{
    internal const Buttons Tabs = Buttons.Parry | Buttons.Talisman | Buttons.ChildPrevious | Buttons.ChildNext;
    private Buttons previous;
    internal Buttons Advance(Buttons raw)
    {
        var current = raw & Tabs;
        var pressed = current & ~previous;
        previous = current;
        return pressed;
    }
}
