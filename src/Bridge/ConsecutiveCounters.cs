namespace YiGodhome.Bridge;

// Acknowledgements count once per authored strike, never per collider/callback.
public sealed class ConsecutiveCounters
{
    private long action;
    private bool resolved;
    public int Count { get; private set; }
    public void Begin(long next)
    {
        if (next <= 0 || next == action) return;
        if (action > 0 && !resolved) Count = 0;
        action = next; resolved = false;
    }
    public bool Accept(long value)
    {
        if (value <= 0 || value != action || resolved) return false;
        resolved = true; Count++; return true;
    }
    public void Reset() { action = 0; resolved = false; Count = 0; }
}
