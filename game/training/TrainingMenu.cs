using Godot;
using StickFighter.Sim.Training;
using StickFighter.UI;
using StickFighter.UI.Screens;

namespace StickFighter.Training;

// The training menu, over the frozen match: what the dummy does, recording, where resets go, and
// the training views. Settings change the session as they're picked; the actions ("Record",
// "Reset", "Back") are Chosen, for whoever shows the menu (pause, from Menus) to carry out.
public partial class TrainingMenu : Screen
{
    private static readonly (DummyAction Action, string Name)[] Actions =
    {
        (DummyAction.Stand, "Stand"),
        (DummyAction.Crouch, "Crouch"),
        (DummyAction.Jump, "Jump"),
        (DummyAction.BlockAll, "Block all"),
        (DummyAction.BlockAfterFirstHit, "Block after first hit"),
        (DummyAction.Playback, "Playback"),
    };

    private static readonly (ResetPosition Position, string Name)[] Positions =
    {
        (ResetPosition.Middle, "Middle"),
        (ResetPosition.LeftCorner, "Left corner"),
        (ResetPosition.RightCorner, "Right corner"),
    };

    private readonly TrainingSession _session;

    public TrainingMenu(TrainingSession session) => _session = session;

    public override void _Ready()
    {
        AddChild(new Panel { ThemeTypeVariation = UiTheme.ScrimPanel, Size = Size });
        // Two columns under the heading, so it fits the screen: the dummy and resets on the left,
        // the views and Back on the right.
        var panel = Ui.Panel();
        var rows = new VBoxContainer();
        panel.AddChild(rows);
        rows.AddChild(new Label { Text = "TRAINING", ThemeTypeVariation = UiTheme.Heading, HorizontalAlignment = HorizontalAlignment.Center });
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 32);
        rows.AddChild(columns);
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(400, 0) };
        var right = new VBoxContainer { CustomMinimumSize = new Vector2(340, 0) };
        columns.AddChild(left);
        columns.AddChild(right);

        var dummy = Cycle(left, "Dummy", Actions, () => _session.Dummy.Action, a => _session.Dummy.Action = a);
        var record = Ui.Row(_session.Dummy.Recording.Frames > 0 ? "Record again" : "Record");
        record.Pressed += () => Choose("Record");
        left.AddChild(record);
        Switch(left, "Loop playback", _session.Dummy.Loop, on => _session.Dummy.Loop = on);
        Cycle(left, "Reset to", Positions, () => _session.ResetTo, p => _session.ResetTo = p);
        var reset = Ui.Row("Reset now");
        reset.Pressed += () => Choose("Reset");
        left.AddChild(reset);

        Switch(right, "Input display", _session.InputDisplay, on => _session.InputDisplay = on);
        Switch(right, "Frame data", _session.FrameData, on => _session.FrameData = on);
        Switch(right, "Hitbox view", _session.Hitboxes, on => _session.Hitboxes = on);
        Switch(right, "Floor grid", _session.Grid, on => _session.Grid = on);
        Menu(right, "Back");

        AddChild(panel);
        CenterAt(panel, 120);
        dummy.GrabFocus();
    }

    // A row showing "Label: value" that steps to the next value each time it's pressed.
    private static Button Cycle<T>(VBoxContainer into, string label, (T Value, string Name)[] values, System.Func<T> get, System.Action<T> set)
        where T : struct
    {
        int Index()
        {
            for (var i = 0; i < values.Length; i++)
            {
                if (values[i].Value.Equals(get()))
                    return i;
            }
            return 0;
        }

        var row = Ui.Row($"{label}: {values[Index()].Name}");
        row.Pressed += () =>
        {
            var next = values[(Index() + 1) % values.Length];
            set(next.Value);
            row.Text = $"{label}: {next.Name}";
        };
        into.AddChild(row);
        return row;
    }

    private static void Switch(VBoxContainer into, string label, bool on, System.Action<bool> set)
    {
        var toggle = new Toggle(label, on);
        toggle.Toggled += value => set(value);
        into.AddChild(toggle);
    }
}
