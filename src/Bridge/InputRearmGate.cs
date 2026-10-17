namespace YiGodhome.Bridge;

// A safety/readiness interruption sends neutral controls. Resuming a physical
// hold must not turn that neutral interval into a second native press edge.
public sealed class InputRearmGate
{
    private const Buttons Combat = Buttons.Jump | Buttons.Dodge | Buttons.Parry | Buttons.Attack | Buttons.Heal | Buttons.Bow | Buttons.Talisman;
    private Buttons blocked;
    public void Observe(Buttons held, bool available)
    {
        blocked &= held;
        if (!available) blocked |= held & Combat;
    }
    public Controls Filter(Controls sampled) => new Controls(sampled.X, sampled.Y, sampled.Buttons & ~blocked);
}
