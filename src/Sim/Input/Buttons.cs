using System;

namespace StickFighter.Sim;

// One player's raw input for one frame. Directions are absolute (screen left and right);
// the simulation turns them into forward and back using the fighter's facing.
[Flags]
public enum Buttons : ushort
{
    None = 0,
    Up = 1 << 0,
    Down = 1 << 1,
    Left = 1 << 2,
    Right = 1 << 3,
    LightPunch = 1 << 4,
    MediumPunch = 1 << 5,
    HeavyPunch = 1 << 6,
    LightKick = 1 << 7,
    MediumKick = 1 << 8,
    HeavyKick = 1 << 9,

    Directions = Up | Down | Left | Right,
    Attacks = LightPunch | MediumPunch | HeavyPunch | LightKick | MediumKick | HeavyKick,
    Punches = LightPunch | MediumPunch | HeavyPunch,
}

public static class ButtonsExtensions
{
    // SOCD cleaning: left + right is neutral horizontally, up + down is neutral vertically.
    public static Buttons Cleaned(this Buttons buttons)
    {
        if ((buttons & (Buttons.Left | Buttons.Right)) == (Buttons.Left | Buttons.Right))
            buttons &= ~(Buttons.Left | Buttons.Right);
        if ((buttons & (Buttons.Up | Buttons.Down)) == (Buttons.Up | Buttons.Down))
            buttons &= ~(Buttons.Up | Buttons.Down);
        return buttons;
    }

    public static bool Has(this Buttons buttons, Buttons flags) => (buttons & flags) == flags;

    public static bool HasAny(this Buttons buttons, Buttons flags) => (buttons & flags) != 0;
}
