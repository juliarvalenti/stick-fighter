using System.Collections.Generic;
using Godot;

namespace StickFighter;

// Presses keys on given frames of a scenario, through Godot's input path, as a player at the
// keyboard would: for testing things a key toggles (F4's input display). Draws nothing.
public partial class ScenarioKeys : Control, IScenarioClock
{
    private readonly Dictionary<int, Key> _presses;
    private Key? _held;

    public ScenarioKeys(Dictionary<int, Key> presses)
    {
        _presses = presses;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void At(int frame)
    {
        if (_held is { } held)
        {
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = held, Keycode = held, Pressed = false });
            _held = null;
        }
        if (_presses.TryGetValue(frame, out var key))
        {
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = true });
            _held = key;
        }
    }
}
