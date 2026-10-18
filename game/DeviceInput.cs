using Godot;
using StickFighter.Sim;

namespace StickFighter;

// The only place devices are read. It turns keyboard and gamepad state into the simulation's
// Buttons; nothing else in the game touches Godot's Input for gameplay.
// Fixed layouts for now; rebinding comes with Menus.
//
//   Player 1 keyboard: WASD to move, U I O = LP MP HP, J K L = LK MK HK
//   Player 2 keyboard: arrows to move, numpad 4 5 6 = LP MP HP, numpad 1 2 3 = LK MK HK
//   Gamepads (first connected is player 1, second is player 2), Street Fighter layout:
//     d-pad or left stick to move, X Y RB = LP MP HP, A B RT = LK MK HK
public static class DeviceInput
{
    private const float StickThreshold = 0.5f;
    private const float TriggerThreshold = 0.5f;

    private static readonly KeyLayout[] Keyboards =
    {
        new(Key.W, Key.S, Key.A, Key.D, Key.U, Key.I, Key.O, Key.J, Key.K, Key.L),
        new(Key.Up, Key.Down, Key.Left, Key.Right, Key.Kp4, Key.Kp5, Key.Kp6, Key.Kp1, Key.Kp2, Key.Kp3),
    };

    public static FrameInput Read() => new(ReadPlayer(0), ReadPlayer(1));

    public static Buttons ReadPlayer(int player)
    {
        var buttons = ReadKeyboard(Keyboards[player]);
        var pads = Input.GetConnectedJoypads();
        if (player < pads.Count)
            buttons |= ReadGamepad(pads[player]);
        return buttons;
    }

    private static Buttons ReadKeyboard(KeyLayout keys)
    {
        var b = Buttons.None;
        if (Input.IsPhysicalKeyPressed(keys.Up)) b |= Buttons.Up;
        if (Input.IsPhysicalKeyPressed(keys.Down)) b |= Buttons.Down;
        if (Input.IsPhysicalKeyPressed(keys.Left)) b |= Buttons.Left;
        if (Input.IsPhysicalKeyPressed(keys.Right)) b |= Buttons.Right;
        if (Input.IsPhysicalKeyPressed(keys.LightPunch)) b |= Buttons.LightPunch;
        if (Input.IsPhysicalKeyPressed(keys.MediumPunch)) b |= Buttons.MediumPunch;
        if (Input.IsPhysicalKeyPressed(keys.HeavyPunch)) b |= Buttons.HeavyPunch;
        if (Input.IsPhysicalKeyPressed(keys.LightKick)) b |= Buttons.LightKick;
        if (Input.IsPhysicalKeyPressed(keys.MediumKick)) b |= Buttons.MediumKick;
        if (Input.IsPhysicalKeyPressed(keys.HeavyKick)) b |= Buttons.HeavyKick;
        return b;
    }

    private static Buttons ReadGamepad(int device)
    {
        var b = Buttons.None;
        var x = Input.GetJoyAxis(device, JoyAxis.LeftX);
        var y = Input.GetJoyAxis(device, JoyAxis.LeftY);
        if (Input.IsJoyButtonPressed(device, JoyButton.DpadUp) || y < -StickThreshold) b |= Buttons.Up;
        if (Input.IsJoyButtonPressed(device, JoyButton.DpadDown) || y > StickThreshold) b |= Buttons.Down;
        if (Input.IsJoyButtonPressed(device, JoyButton.DpadLeft) || x < -StickThreshold) b |= Buttons.Left;
        if (Input.IsJoyButtonPressed(device, JoyButton.DpadRight) || x > StickThreshold) b |= Buttons.Right;
        if (Input.IsJoyButtonPressed(device, JoyButton.X)) b |= Buttons.LightPunch;
        if (Input.IsJoyButtonPressed(device, JoyButton.Y)) b |= Buttons.MediumPunch;
        if (Input.IsJoyButtonPressed(device, JoyButton.RightShoulder)) b |= Buttons.HeavyPunch;
        if (Input.IsJoyButtonPressed(device, JoyButton.A)) b |= Buttons.LightKick;
        if (Input.IsJoyButtonPressed(device, JoyButton.B)) b |= Buttons.MediumKick;
        if (Input.GetJoyAxis(device, JoyAxis.TriggerRight) > TriggerThreshold) b |= Buttons.HeavyKick;
        return b;
    }

    private readonly record struct KeyLayout(
        Key Up, Key Down, Key Left, Key Right,
        Key LightPunch, Key MediumPunch, Key HeavyPunch,
        Key LightKick, Key MediumKick, Key HeavyKick);
}
