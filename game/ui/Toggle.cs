using Godot;
using StickFighter.Style;

namespace StickFighter.UI;

// An on/off switch with its label: a pill track that turns accent when on, and a round knob that
// slides across. Colors come from the theme's Toggle type; focus is the same accent ring as a button.
public partial class Toggle : BaseButton
{
    private const float TrackWidth = 64;
    private const float TrackHeight = 34;

    private readonly string _label;

    public Toggle(string label, bool on = false)
    {
        _label = label;
        ToggleMode = true;
        ButtonPressed = on;
        FocusMode = FocusModeEnum.All;
        CustomMinimumSize = new Vector2(320, 48);
        ThemeTypeVariation = nameof(Toggle);
        Toggled += _ => QueueRedraw();
    }

    // Draws as focused even without focus, for the ui-kit sheet (only one control can hold focus).
    public bool ShowFocus { get; set; }

    public override void _Draw()
    {
        var type = nameof(Toggle);
        var on = ButtonPressed;
        var font = GetThemeFont("font", type);
        var size = GetThemeFontSize("font_size", type);
        var focused = HasFocus() || ShowFocus;
        var textColor = Disabled ? GetThemeColor("font_disabled_color", type)
            : focused ? GetThemeColor("font_focus_color", type)
            : GetThemeColor("font_color", type);
        this.Text(new Rect2(0, 0, Size.X - TrackWidth - 16, Size.Y), _label, font, size, textColor, HorizontalAlignment.Left);

        var track = new Rect2(Size.X - TrackWidth, (Size.Y - TrackHeight) / 2, TrackWidth, TrackHeight);
        var trackColor = Disabled ? GetThemeColor("track_off", type).Darkened(0.3f)
            : GetThemeColor(on ? "track_on" : "track_off", type);
        this.Pill(track, trackColor);

        var radius = TrackHeight / 2 - 5;
        var x = on ? track.End.X - TrackHeight / 2 : track.Position.X + TrackHeight / 2;
        var knob = Disabled ? GetThemeColor("disabled", type) : GetThemeColor(on ? "knob_on" : "knob", type);
        DrawCircle(new Vector2(x, track.GetCenter().Y), radius, knob);

        if (focused)
            DrawStyleBox(UiTheme.FocusRing(UiTheme.Pill(Colors.Transparent)), track);
    }
}
