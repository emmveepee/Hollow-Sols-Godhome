namespace YiGodhome;

// Borrow the initialized gameplay owner already verified by player-HUD capture.
// Never rewrite the game's singleton; title/vanilla handling remains native.
internal static class NativeGameplayUI
{
    private static UIManager? bound;
    internal static UIManager Current => bound!=null?bound:UIManager.Instance;
    internal static void Bind(UIManager owner)=>bound=owner;
    internal static void Release(UIManager? owner){if(ReferenceEquals(bound,owner))bound=null;}
}
