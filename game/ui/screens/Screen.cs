using System.Collections.Generic;
using Godot;

namespace StickFighter.UI.Screens;

// A whole screen: full size, the kit's theme, built only from kit components. Behavior and
// navigation between screens belong to Menus; a screen here is the look, with real focus so a pad
// moves through it the way the kit's focus states show.
//
// In a scenario, `Presses` sends pad directions as UI actions on given frames, so a capture after
// them shows focus having moved: pad navigation, through the real input path.
public abstract partial class Screen : Control, IScenarioClock
{
    protected Screen()
    {
        Size = new Vector2(1280, 720);
        MouseFilter = MouseFilterEnum.Ignore;
        Theme = UiTheme.Default;
    }

    // Frame -> UI action to press on it ("ui_down", "ui_right", ...), released the frame after.
    public Dictionary<int, string> Presses { get; } = new();

    private string? _held;

    public void At(int frame)
    {
        if (_held is { } held)
        {
            Input.ParseInputEvent(new InputEventAction { Action = held, Pressed = false });
            _held = null;
        }
        if (Presses.TryGetValue(frame, out var action))
        {
            Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = true });
            _held = action;
        }
    }

    // Centers a control horizontally at height `y` once it knows its size.
    protected static void CenterAt(Control control, float y, float width = 1280) =>
        control.Position = new Vector2((width - control.GetCombinedMinimumSize().X) / 2, y);

    // A menu row was pressed: its text. What it leads to is up to whoever shows the screen.
    public event System.Action<string>? Chosen;

    // For choices that aren't menu rows (a dialog's answers).
    protected void Choose(string item) => Chosen?.Invoke(item);

    protected Button Menu(VBoxContainer into, params string[] items)
    {
        Button? first = null;
        foreach (var item in items)
        {
            var row = Ui.Row(item);
            row.Pressed += () => Choose(item);
            into.AddChild(row);
            first ??= row;
        }
        return first!;
    }
}
