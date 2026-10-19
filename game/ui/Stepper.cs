using Godot;
using StickFighter.Style;

namespace StickFighter.UI;

// A value you step: its label on the left, the value in a pill on the right with arrows either
// side. Left and right step it down and up when it has focus (a pad's d-pad, the arrow keys); a
// click on either half of the pill does the same. Drawn from the theme's Toggle colors, so it sits
// with the kit's toggles.
public partial class Stepper : Control
{
    private const float PillWidth = 112;
    private const float PillHeight = 30;

    private readonly string _label;

    public Stepper(string label)
    {
        _label = label;
        FocusMode = FocusModeEnum.All;
        CustomMinimumSize = new Vector2(240, 36);
        ThemeTypeVariation = nameof(Toggle);
        FocusEntered += QueueRedraw;
        FocusExited += QueueRedraw;
    }

    public string Value { get; set; } = "";

    // -1 or +1: the value wants to step.
    public event System.Action<int>? Stepped;

    public void Show(string value)
    {
        Value = value;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        var step = @event.IsActionPressed("ui_left", allowEcho: true) ? -1
            : @event.IsActionPressed("ui_right", allowEcho: true) ? 1
            : @event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click
                ? (click.Position.X < Size.X - PillWidth / 2 ? -1 : 1)
            : 0;
        if (step == 0)
            return;
        AcceptEvent();
        if (@event is InputEventMouseButton)
            GrabFocus();
        Stepped?.Invoke(step);
    }

    public override void _Draw()
    {
        const string type = nameof(Toggle);
        var font = GetThemeFont("font", type);
        var size = Look.UiSmall + 2;
        var focused = HasFocus();
        var text = focused ? GetThemeColor("font_focus_color", type) : GetThemeColor("font_color", type);
        this.Text(new Rect2(0, 0, Size.X - PillWidth - 8, Size.Y), _label, font, size, text, HorizontalAlignment.Left);

        var pill = new Rect2(Size.X - PillWidth, (Size.Y - PillHeight) / 2, PillWidth, PillHeight);
        this.Pill(pill, GetThemeColor("track_off", type));
        this.Text(pill, Value, Look.UiBold, size, Look.Text);
        this.Text(new Rect2(pill.Position.X + 6, pill.Position.Y, 16, PillHeight), "‹", Look.UiBold, size, Look.TextMuted);
        this.Text(new Rect2(pill.End.X - 22, pill.Position.Y, 16, PillHeight), "›", Look.UiBold, size, Look.TextMuted);
        if (focused)
            DrawStyleBox(UiTheme.FocusRing(UiTheme.Pill(Colors.Transparent)), pill);
    }
}
