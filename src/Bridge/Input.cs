using System;

namespace YiGodhome.Bridge;

[Flags]
public enum Buttons : uint { None = 0, Jump = 1, Dodge = 2, Parry = 4, Attack = 8, Heal = 16, Bow = 32, Talisman = 64, Inventory = 128, Cancel = 256, Interact = 512, ChildPrevious = 1024, ChildNext = 2048, Confirm = 4096, HostPaused = 8192, Keyboard = 16384, DoorEnter = 32768, EntryRevealed = 65536, NativePauseMenu = 131072, NextItem = 262144, PreviousItem = 524288, StatueEnter = 1048576, ParryDebug = 2097152, MenuPointer = 4194304, CloseInventory = 8388608, MenuHover = 16777216, BindingMouse = 33554432 }

public readonly struct Controls : IEquatable<Controls>
{
    public readonly float X, Y;
    public readonly Buttons Buttons;
    public Controls(float x, float y, Buttons buttons) { X = x; Y = y; Buttons = buttons; }
    public bool Equals(Controls other) => X == other.X && Y == other.Y && Buttons == other.Buttons;
}

public sealed class InputCommand
{
    public readonly long Sequence, Frame, Tick;
    public readonly Controls Controls;
    public InputCommand(long sequence, long frame, long tick, Controls controls)
    { Sequence = sequence; Frame = frame; Tick = tick; Controls = controls; }
}
