using Godot;
using StickFighter.Style;

namespace StickFighter.UI;

// The ui-kit scenarios' pictures: every component in every state, the dialog and toasts over a
// dimmed screen, and the input glyphs. They're the kit's golden images and what a reviewer reads.
//
// Hover and focus can't be held on many controls at once (only one control has focus, and there's
// no pointer in CI), so those samples show the theme's own hover and focus looks forced on.
public partial class KitSheet : Control
{
    public enum Page
    {
        Components,
        Overlays,
        Glyphs,
    }

    // Every input the kit must draw beyond the character's own moves: each direction, each button,
    // and the shapes of motions and throws to come.
    private static readonly string[] Basics =
    {
        "1P", "2P", "3P", "4P", "5P", "6P", "7P", "8P", "9P",
        "LP", "MP", "HP", "LK", "MK", "HK", "P", "K",
        "236P", "623P", "214K", "41236HP", "LP+LK", "j.HK",
    };

    private readonly Page _page;

    public KitSheet(Page page)
    {
        _page = page;
        Size = new Vector2(1280, 720);
        MouseFilter = MouseFilterEnum.Ignore;
        Theme = UiTheme.Default;
    }

    public override void _Ready()
    {
        switch (_page)
        {
            case Page.Components:
                BuildComponents();
                break;
            case Page.Overlays:
                BuildOverlays();
                break;
            default:
                BuildGlyphs();
                break;
        }
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Look.Paper);
        var title = _page switch { Page.Components => "UI KIT", Page.Overlays => "DIALOGS AND TOASTS", _ => "INPUT GLYPHS" };
        this.Text(new Rect2(32, 12, 800, 56), title, Look.Display, Look.DisplayMedium, Look.Ink, HorizontalAlignment.Left);
        if (_page == Page.Overlays)
            DrawRect(new Rect2(0, 80, Size.X, Size.Y - 80), UiTheme.Scrim);
    }

    private void BuildComponents()
    {
        var left = Place(Ui.Panel(), new Vector2(32, 80), new Vector2(680, 0));
        var column = new VBoxContainer();
        left.AddChild(column);

        column.AddChild(Ui.Label("Buttons", UiTheme.Caption));
        var buttons = new HBoxContainer();
        column.AddChild(buttons);
        foreach (var (state, text) in new[] { ("normal", "Play"), ("hover", "Play"), ("focus", "Play"), ("pressed", "Play"), ("disabled", "Play") })
            buttons.AddChild(Captioned(Force(Small(Ui.Button(text, disabled: state == "disabled")), state), state));

        var lower = new HBoxContainer();
        lower.AddThemeConstantOverride("separation", 32);
        column.AddChild(lower);

        var rows = new VBoxContainer();
        lower.AddChild(rows);
        rows.AddChild(Ui.Label("List rows", UiTheme.Caption));
        rows.AddChild(Ui.Row("Versus"));
        rows.AddChild(Force(Ui.Row("Training"), "focus"));
        rows.AddChild(Force(Ui.Row("Replays"), "hover"));
        rows.AddChild(Ui.Row("Online (soon)", disabled: true));

        var toggles = new VBoxContainer();
        lower.AddChild(toggles);
        toggles.AddChild(Ui.Label("Toggles", UiTheme.Caption));
        toggles.AddChild(new Toggle("Hitboxes"));
        toggles.AddChild(new Toggle("Input display", on: true));
        toggles.AddChild(new Toggle("Frame data", on: true) { ShowFocus = true });
        toggles.AddChild(new Toggle("Rollback", on: false) { Disabled = true });

        var right = Place(Ui.Panel(), new Vector2(736, 80), new Vector2(512, 0));
        var side = new VBoxContainer();
        right.AddChild(side);
        side.AddChild(Ui.Label("Tabs", UiTheme.Caption));
        side.AddChild(Ui.Tabs(1, "Audio", "Video", "Controls"));
        side.AddChild(Ui.Label("Sliders", UiTheme.Caption));
        side.AddChild(Ui.Line("Music", Ui.Slider(70)));
        side.AddChild(Ui.Line("Effects", Force(Ui.Slider(40), "focus")));
        side.AddChild(Ui.Line("Announcer", new HSlider { Value = 0, Editable = false, CustomMinimumSize = new Vector2(280, 32) }));

    }

    // What appears over a screen: a dialog over the dimmed screen, and toasts stacked in a corner.
    private void BuildOverlays()
    {
        var toasts = new VBoxContainer();
        Place(toasts, new Vector2(800, 96), Vector2.Zero);
        toasts.AddChild(new Toast("Controller 2 connected"));
        toasts.AddChild(new Toast("Replay saved", Toast.Kind.Success));
        toasts.AddChild(new Toast("Connection is unstable", Toast.Kind.Warning));

        var dialog = new Dialog("Quit to menu?", "The match so far won't be saved.", "Stay", "Quit");
        AddChild(dialog);
        dialog.Position = (Size - dialog.GetCombinedMinimumSize()) / 2 + new Vector2(0, 60);
    }

    private void BuildGlyphs()
    {
        // Every move the character has, on the paper (the move list's look).
        var list = new GridContainer { Columns = 4 };
        list.AddThemeConstantOverride("h_separation", 16);
        list.AddThemeConstantOverride("v_separation", 8);
        Place(list, new Vector2(32, 92), Vector2.Zero);
        foreach (var move in Sim.Characters.Characters.Placeholder.Moves)
        {
            var label = new Label { Text = move.Input, CustomMinimumSize = new Vector2(56, 30), VerticalAlignment = VerticalAlignment.Center };
            label.AddThemeColorOverride("font_color", Look.Ink);
            list.AddChild(label);
            list.AddChild(Glyphs.For(move.Input, 30));
        }

        // The basics, light on a panel (training mode's input display sits on one).
        var panel = Place(Ui.Panel(), new Vector2(496, 84), Vector2.Zero);
        var basics = new GridContainer { Columns = 4 };
        basics.AddThemeConstantOverride("h_separation", 16);
        basics.AddThemeConstantOverride("v_separation", 8);
        panel.AddChild(basics);
        foreach (var notation in Basics)
        {
            basics.AddChild(new Label { Text = notation, ThemeTypeVariation = UiTheme.Muted, CustomMinimumSize = new Vector2(96, 0), VerticalAlignment = VerticalAlignment.Center });
            basics.AddChild(Glyphs.For(notation, 32, onPanel: true));
        }
    }

    private T Place<T>(T control, Vector2 at, Vector2 size)
        where T : Control
    {
        control.Position = at;
        if (size != Vector2.Zero)
            control.CustomMinimumSize = size;
        AddChild(control);
        return control;
    }

    private static Button Small(Button button)
    {
        button.CustomMinimumSize = new Vector2(104, 48);
        return button;
    }

    private static Control Captioned(Control control, string caption)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        box.AddChild(control);
        box.AddChild(new Label { Text = caption, ThemeTypeVariation = UiTheme.Caption, HorizontalAlignment = HorizontalAlignment.Center });
        return box;
    }

    // Shows a state by giving the control that state's own theme look as its resting look.
    private static T Force<T>(T control, string state)
        where T : Control
    {
        // Read from the kit's theme directly: the control isn't in the tree yet.
        var theme = UiTheme.Default;
        string type = control.ThemeTypeVariation != "" ? control.ThemeTypeVariation : control.GetClass();
        switch (state)
        {
            case "hover" when control is Button button:
                button.AddThemeStyleboxOverride("normal", theme.GetStylebox("hover", type));
                break;
            case "pressed" when control is Button button:
                button.ToggleMode = true;
                button.ButtonPressed = true;
                break;
            case "focus":
                control.AddChild(new FocusLook(theme.GetStylebox("focus", type)));
                if (control is Button)
                    control.AddThemeColorOverride("font_color", theme.GetColor("font_focus_color", type));
                break;
        }
        return control;
    }

    // Draws a control's theme focus look over it, as Godot does for the focused control.
    private sealed partial class FocusLook : Control
    {
        private readonly StyleBox _look;

        public FocusLook(StyleBox look) => _look = look;

        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            ShowBehindParent = true; // under the label, as the real focus look is
            SetAnchorsPreset(LayoutPreset.FullRect);
        }

        public override void _Draw() => DrawStyleBox(_look, new Rect2(Vector2.Zero, GetParent<Control>().Size));
    }
}
