namespace YiGodhome.Bridge;

// Identity only. The native game supplies the verdict; this never computes one.
public sealed class ContactVerdict
{
    public long Generation { get; private set; }
    public bool Enabled { get; private set; }
    public bool Consumed { get; private set; }
    public bool CanResolve => Enabled && !Consumed;
    private float drainUntil = -1;
    public bool Draining => drainUntil >= 0;
    public void Begin() { drainUntil = -1; Generation++; Enabled = true; Consumed = false; }
    public void Suspend() { drainUntil = -1; Enabled = false; }
    public void Drain(float now, float duration) {
        if (!CanResolve || Draining) return;
        if (float.IsNaN(now) || float.IsInfinity(now) || now < 0 || float.IsNaN(duration) || float.IsInfinity(duration) || duration <= 0 || duration > .2f) throw new System.ArgumentException("Invalid native verdict drain.");
        drainUntil = now + duration;
    }
    public void Tick(float now) { if (Draining && now >= drainUntil) Suspend(); }
    public void Resume() { if (Generation == 0 || Draining) return; Generation++; Enabled = true; }
    public bool Accept(long generation, bool nativeAccepted)
    {
        if (!nativeAccepted || generation != Generation || !CanResolve) return false;
        Consumed = true; return true;
    }
}
