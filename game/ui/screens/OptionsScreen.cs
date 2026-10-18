using System.Collections.Generic;
using Godot;
using StickFighter.Settings;

namespace StickFighter.UI.Screens;

// Options, from the main menu or pause: audio volumes and input delay on the left, display and
// each player's controls on the right. Up and down move through everything in reading order, left
// and right change a slider. Every change goes straight into the settings and raises Changed, for
// whoever shows the screen to save and apply.
//
// The volumes are stored for Sound and the input delay for Rollback; neither reads them yet.
public partial class OptionsScreen : Screen
{
    public const string Player1Controls = "Player 1 controls";
    public const string Player2Controls = "Player 2 controls";

    private readonly UserSettings _settings;
    private readonly string? _focus;
    private readonly List<Control> _order = new();

    // `focus` is the row to land on (a controls row, coming back from it); otherwise the first slider.
    public OptionsScreen(UserSettings settings, string? focus = null)
    {
        _settings = settings;
        _focus = focus;
    }

    public event System.Action? Changed;

    public override void _Ready()
    {
        AddChild(new Panel { ThemeTypeVariation = UiTheme.ScrimPanel, Size = Size });

        var panel = Ui.Panel();
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        panel.AddChild(column);
        column.AddChild(Ui.Heading("OPTIONS"));

        var sides = new HBoxContainer();
        sides.AddThemeConstantOverride("separation", 48);
        column.AddChild(sides);
        var left = new VBoxContainer();
        var right = new VBoxContainer();
        sides.AddChild(left);
        sides.AddChild(right);

        left.AddChild(Ui.Label("AUDIO", UiTheme.Caption));
        left.AddChild(Volume("Music", _settings.MusicVolume, v => _settings.MusicVolume = v));
        left.AddChild(Volume("Effects", _settings.EffectsVolume, v => _settings.EffectsVolume = v));
        left.AddChild(Volume("Announcer", _settings.AnnouncerVolume, v => _settings.AnnouncerVolume = v));
        left.AddChild(Ui.Label("ONLINE", UiTheme.Caption));
        left.AddChild(Setting("Input delay", _settings.InputDelay, UserSettings.MaxInputDelay,
            v => _settings.InputDelay = v, v => v == 1 ? "1 frame" : $"{v} frames"));

        right.AddChild(Ui.Label("DISPLAY", UiTheme.Caption));
        right.AddChild(Switch("Fullscreen", _settings.Fullscreen, on => _settings.Fullscreen = on));
        right.AddChild(Switch("Vsync", _settings.Vsync, on => _settings.Vsync = on));
        right.AddChild(Ui.Label("CONTROLS", UiTheme.Caption));
        var rows = new VBoxContainer();
        right.AddChild(rows);
        Menu(rows, Player1Controls, Player2Controls);
        foreach (var row in rows.GetChildren())
            _order.Add((Control)row);

        column.AddChild(Ui.Label("Back to return", UiTheme.Caption));
        AddChild(panel);
        CenterAt(panel, 72);

        // Up and down in reading order, wrapping, so the two columns read as one list.
        for (var i = 0; i < _order.Count; i++)
        {
            var next = _order[(i + 1) % _order.Count];
            var previous = _order[(i + _order.Count - 1) % _order.Count];
            _order[i].FocusNeighborBottom = _order[i].GetPathTo(next);
            _order[i].FocusNeighborTop = _order[i].GetPathTo(previous);
            _order[i].FocusNext = _order[i].FocusNeighborBottom;
            _order[i].FocusPrevious = _order[i].FocusNeighborTop;
        }
        var first = _order.Find(c => c is Button b && b.Text == _focus) ?? _order[0];
        first.GrabFocus();
    }

    private Control Volume(string label, int value, System.Action<int> set) =>
        Setting(label, value, 100, set, v => $"{v}");

    // A slider with its value beside it.
    private Control Setting(string label, int value, int max, System.Action<int> set, System.Func<int, string> show)
    {
        var slider = Ui.Slider(value, max);
        slider.CustomMinimumSize = new Vector2(240, 32);
        var shown = Ui.Label(show(value), UiTheme.Strong);
        shown.CustomMinimumSize = new Vector2(92, 0);
        shown.HorizontalAlignment = HorizontalAlignment.Right;
        shown.VerticalAlignment = VerticalAlignment.Center;
        slider.ValueChanged += v =>
        {
            set((int)v);
            shown.Text = show((int)v);
            Changed?.Invoke();
        };
        _order.Add(slider);

        var both = new HBoxContainer();
        both.AddChild(slider);
        both.AddChild(shown);
        var line = Ui.Line(label, both);
        line.CustomMinimumSize = new Vector2(0, 48);
        return line;
    }

    private Control Switch(string label, bool on, System.Action<bool> set)
    {
        var toggle = new Toggle(label, on);
        toggle.Toggled += value =>
        {
            set(value);
            Changed?.Invoke();
        };
        _order.Add(toggle);
        return toggle;
    }
}
