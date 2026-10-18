using Godot;

namespace StickFighter.UI;

// The kit's components, built ready to use: every one takes its look from UiTheme, so a screen
// only arranges them. Screens set UiTheme.Default on their root Control.
public static class Ui
{
    // A pill button, for answers and actions.
    public static Button Button(string text, bool disabled = false) => new()
    {
        Text = text,
        Disabled = disabled,
        FocusMode = Control.FocusModeEnum.All,
        CustomMinimumSize = new Vector2(140, 48),
    };

    // A menu or list row: flat until focused, then a raised pill with accent text.
    public static Button Row(string text, bool disabled = false) => new()
    {
        Text = text,
        Disabled = disabled,
        ThemeTypeVariation = UiTheme.ListRow,
        FocusMode = Control.FocusModeEnum.All,
        Alignment = HorizontalAlignment.Center,
        CustomMinimumSize = new Vector2(280, 52),
    };

    // A dark card that holds other components, with the theme's padding.
    public static PanelContainer Panel() => new();

    public static Label Heading(string text) => new() { Text = text, ThemeTypeVariation = UiTheme.Heading };

    // Text on a panel; `variation` is UiTheme.Muted, Strong or Caption.
    public static Label Label(string text, string variation = "") => new() { Text = text, ThemeTypeVariation = variation };

    public static TabBar Tabs(int selected, params string[] tabs)
    {
        var bar = new TabBar { FocusMode = Control.FocusModeEnum.All };
        foreach (var tab in tabs)
            bar.AddTab(tab);
        bar.CurrentTab = selected;
        return bar;
    }

    public static HSlider Slider(double value, double max = 100) => new()
    {
        MaxValue = max,
        Value = value,
        Step = 1,
        FocusMode = Control.FocusModeEnum.All,
        CustomMinimumSize = new Vector2(280, 32),
    };

    // A label and a row of components side by side, the label on the left: an options line.
    public static HBoxContainer Line(string label, Control control)
    {
        var line = new HBoxContainer();
        line.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(180, 0), VerticalAlignment = VerticalAlignment.Center });
        line.AddChild(control);
        return line;
    }
}
