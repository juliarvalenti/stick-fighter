using Godot;
using StickFighter.Settings;
using StickFighter.Sim;

namespace StickFighter;

// The only place devices are read. It turns keyboard and gamepad state into the simulation's
// Buttons through each player's bindings (rebindable in Options); nothing else in the game touches
// Godot's Input for gameplay. The defaults:
//
//   Player 1 keyboard: WASD to move, U I O = LP MP HP, J K L = LK MK HK
//   Player 2 keyboard: arrows to move, numpad 4 5 6 = LP MP HP, numpad 1 2 3 = LK MK HK
//   Gamepads, Street Fighter layout: d-pad to move, X Y RB = LP MP HP, A B RT = LK MK HK
//
// The left stick always moves too, whatever the d-pad is bound to. Which pad is whose is Pads's
// call: the first connected is player 1's, the second player 2's, and they follow hot-plugging.
public static class DeviceInput
{
    private const float StickThreshold = 0.5f;
    private const float AxisThreshold = 0.5f;

    public static readonly Bindings Defaults = BuildDefaults();

    // Replaced by the saved settings at startup (Config.Load).
    public static UserSettings Settings { get; set; } = new(Defaults);

    public static PadSlots Pads { get; set; } = new();

    public static FrameInput Read() => new(ReadPlayer(0), ReadPlayer(1));

    public static Buttons ReadPlayer(int player)
    {
        var bindings = Settings.Bindings;
        var buttons = Buttons.None;
        foreach (var binding in Bindings.All)
        {
            if (bindings.Key(player, binding) is var key and not 0 && Input.IsPhysicalKeyPressed((Key)key))
                buttons |= ButtonFor(binding);
        }
        if (Pads.PadOf(player) is { } device)
            buttons |= ReadGamepad(bindings, player, device);
        return buttons;
    }

    private static Buttons ReadGamepad(Bindings bindings, int player, int device)
    {
        var b = Buttons.None;
        foreach (var binding in Bindings.All)
        {
            if (bindings.Pad(player, binding) is { } pad && IsDown(device, pad))
                b |= ButtonFor(binding);
        }
        var x = Input.GetJoyAxis(device, JoyAxis.LeftX);
        var y = Input.GetJoyAxis(device, JoyAxis.LeftY);
        if (y < -StickThreshold) b |= Buttons.Up;
        if (y > StickThreshold) b |= Buttons.Down;
        if (x < -StickThreshold) b |= Buttons.Left;
        if (x > StickThreshold) b |= Buttons.Right;
        return b;
    }

    private static bool IsDown(int device, PadInput pad) => pad.Axis
        ? Input.GetJoyAxis(device, (JoyAxis)pad.Index) * pad.Sign > AxisThreshold
        : Input.IsJoyButtonPressed(device, (JoyButton)pad.Index);

    public static Buttons ButtonFor(Binding binding) => binding switch
    {
        Binding.Up => Buttons.Up,
        Binding.Down => Buttons.Down,
        Binding.Left => Buttons.Left,
        Binding.Right => Buttons.Right,
        Binding.LP => Buttons.LightPunch,
        Binding.MP => Buttons.MediumPunch,
        Binding.HP => Buttons.HeavyPunch,
        Binding.LK => Buttons.LightKick,
        Binding.MK => Buttons.MediumKick,
        _ => Buttons.HeavyKick,
    };

    private static Bindings BuildDefaults()
    {
        Key[][] keyboards =
        {
            new[] { Key.W, Key.S, Key.A, Key.D, Key.U, Key.I, Key.O, Key.J, Key.K, Key.L },
            new[] { Key.Up, Key.Down, Key.Left, Key.Right, Key.Kp4, Key.Kp5, Key.Kp6, Key.Kp1, Key.Kp2, Key.Kp3 },
        };
        PadInput[] pad =
        {
            Button(JoyButton.DpadUp), Button(JoyButton.DpadDown), Button(JoyButton.DpadLeft), Button(JoyButton.DpadRight),
            Button(JoyButton.X), Button(JoyButton.Y), Button(JoyButton.RightShoulder),
            Button(JoyButton.A), Button(JoyButton.B), PadInput.Stick((int)JoyAxis.TriggerRight, +1),
        };

        var defaults = new Bindings();
        for (var p = 0; p < 2; p++)
        {
            foreach (var binding in Bindings.All)
            {
                defaults.SetKey(p, binding, (long)keyboards[p][(int)binding]);
                defaults.SetPad(p, binding, pad[(int)binding]);
            }
        }
        return defaults;
    }

    private static PadInput Button(JoyButton button) => PadInput.Button((int)button);
}
