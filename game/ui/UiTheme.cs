using Godot;
using StickFighter.Style;

namespace StickFighter.UI;

// The one Godot Theme every screen uses, built from the style guide's tokens (Look). A screen sets
// it on its root Control and styles nothing itself; a new look is a change here.
//
// The shapes follow the style guide: buttons, rows, tabs and toasts are pills, panels and dialogs
// are cards, everything is a flat fill, and focus is an accent ring you can see from the couch,
// since menus are driven by pads and sticks as much as by the mouse.
public static class UiTheme
{
    // Type variations, for the components that aren't plain Godot controls.
    public const string ListRow = "ListRow";
    public const string Toast = "Toast";
    public const string Dialog = "Dialog";
    public const string Heading = "Heading";
    public const string Muted = "Muted"; // secondary text
    public const string Strong = "Strong"; // a value that matters
    public const string Caption = "Caption"; // small secondary text: section names, hints

    // Straight on the paper (screens' own titles and prompts), ink rather than light text.
    public const string Title = "Title"; // the game's name
    public const string PaperHeading = "PaperHeading"; // a screen's heading
    public const string Prompt = "Prompt"; // "PRESS START"
    public const string PaperMuted = "PaperMuted"; // hints on the paper

    public const string Chip = "Chip"; // a small status pill: READY, CHOOSING
    public const string ChipOn = "ChipOn"; // the same, lit
    public const string Backdrop = "Backdrop"; // a full screen of paper, behind a screen's own UI
    public const string ScrimPanel = "ScrimPanel"; // the dimmed screen behind pause and dialogs
    public const string CharacterCard = "CharacterCard";

    // Dims the screen behind a dialog: ink at half strength, so the paper reads as set aside.
    public static readonly Color Scrim = new(Look.Ink, 0.55f);

    public const int FocusWidth = 4;
    public const int FocusGap = 3;

    public static Theme Default => _default ??= Build();
    private static Theme? _default;

    private static Theme Build()
    {
        var theme = new Theme
        {
            DefaultFont = Look.Ui,
            DefaultFontSize = Look.UiMedium,
        };

        Buttons(theme);
        Rows(theme);
        Panels(theme);
        Labels(theme);
        Tabs(theme);
        Sliders(theme);
        Toggles(theme);
        Screens(theme);
        return theme;
    }

    // What screens put straight on the paper: ink type, status chips, character cards.
    private static void Screens(Theme theme)
    {
        theme.SetTypeVariation(Title, "Label");
        theme.SetFont("font", Title, Look.Display);
        theme.SetFontSize("font_size", Title, Look.DisplayHero);
        theme.SetColor("font_color", Title, Look.Ink);

        theme.SetTypeVariation(PaperHeading, "Label");
        theme.SetFont("font", PaperHeading, Look.Display);
        theme.SetFontSize("font_size", PaperHeading, Look.DisplayMedium);
        theme.SetColor("font_color", PaperHeading, Look.Ink);

        theme.SetTypeVariation(Prompt, "Label");
        theme.SetFont("font", Prompt, Look.UiBold);
        theme.SetFontSize("font_size", Prompt, Look.UiLarge);
        theme.SetColor("font_color", Prompt, Look.Ink);

        theme.SetTypeVariation(PaperMuted, "Label");
        theme.SetFontSize("font_size", PaperMuted, Look.UiSmall);
        theme.SetColor("font_color", PaperMuted, Look.InkMuted);

        theme.SetTypeVariation(Chip, "Label");
        var chip = Pill(Look.SurfaceRaised, horizontal: 18);
        chip.ContentMarginTop = chip.ContentMarginBottom = 4;
        theme.SetStylebox("normal", Chip, chip);
        theme.SetFont("font", Chip, Look.UiBold);
        theme.SetFontSize("font_size", Chip, Look.UiSmall);
        theme.SetColor("font_color", Chip, Look.Text);

        theme.SetTypeVariation(ChipOn, "Label");
        var lit = Pill(Look.Surface, horizontal: 18);
        lit.ContentMarginTop = lit.ContentMarginBottom = 4;
        theme.SetStylebox("normal", ChipOn, lit);
        theme.SetFont("font", ChipOn, Look.UiBold);
        theme.SetFontSize("font_size", ChipOn, Look.UiSmall);
        theme.SetColor("font_color", ChipOn, Look.Accent);

        theme.SetTypeVariation(Backdrop, "Panel");
        theme.SetStylebox("panel", Backdrop, new StyleBoxFlat { BgColor = Look.Paper });

        theme.SetTypeVariation(ScrimPanel, "Panel");
        theme.SetStylebox("panel", ScrimPanel, new StyleBoxFlat { BgColor = Scrim });

        // A character on the select grid: a paper-shade card; focus is the accent ring, and each
        // player's cursor is drawn by the card itself in that player's color.
        theme.SetTypeVariation(CharacterCard, "Button");
        theme.SetStylebox("normal", CharacterCard, Card(Look.PaperShade, margin: 8));
        theme.SetStylebox("hover", CharacterCard, Card(Look.PaperShade.Darkened(0.04f), margin: 8));
        theme.SetStylebox("pressed", CharacterCard, Card(Look.PaperShade, margin: 8));
        theme.SetStylebox("disabled", CharacterCard, Card(Look.PaperShade.Lightened(0.3f), margin: 8));
        theme.SetStylebox("focus", CharacterCard, FocusRing(Card(Colors.Transparent, margin: 8)));
        theme.SetFont("font", CharacterCard, Look.UiBold);
        theme.SetFontSize("font_size", CharacterCard, Look.UiMedium);
        theme.SetColor("font_color", CharacterCard, Look.Ink);
        theme.SetColor("font_focus_color", CharacterCard, Look.Ink);
        theme.SetColor("font_hover_color", CharacterCard, Look.Ink);
        theme.SetColor("font_pressed_color", CharacterCard, Look.Ink);
        theme.SetColor("font_disabled_color", CharacterCard, Look.InkMuted);
    }

    // A pill on a panel: raised when idle, lighter under the pointer, accent when pressed or on.
    private static void Buttons(Theme theme)
    {
        const string type = "Button";
        theme.SetStylebox("normal", type, Pill(Look.SurfaceRaised));
        theme.SetStylebox("hover", type, Pill(Look.SurfaceRaised.Lightened(0.08f)));
        theme.SetStylebox("pressed", type, Pill(Look.Accent));
        theme.SetStylebox("hover_pressed", type, Pill(Look.Accent));
        theme.SetStylebox("disabled", type, Pill(Look.Surface));
        theme.SetStylebox("focus", type, FocusRing(Pill(Colors.Transparent)));
        theme.SetFont("font", type, Look.UiBold);
        theme.SetFontSize("font_size", type, Look.UiMedium);
        theme.SetColor("font_color", type, Look.Text);
        theme.SetColor("font_hover_color", type, Look.Text);
        theme.SetColor("font_focus_color", type, Look.Accent);
        theme.SetColor("font_pressed_color", type, Look.Ink);
        theme.SetColor("font_hover_pressed_color", type, Look.Ink);
        theme.SetColor("font_disabled_color", type, Look.TextMuted);
    }

    // A menu or list row: flat until it's the one, then a raised pill with accent text.
    private static void Rows(Theme theme)
    {
        theme.SetTypeVariation(ListRow, "Button");
        theme.SetStylebox("normal", ListRow, Pill(Colors.Transparent, horizontal: 20));
        theme.SetStylebox("hover", ListRow, Pill(Look.SurfaceRaised, horizontal: 20));
        theme.SetStylebox("pressed", ListRow, Pill(Look.Accent, horizontal: 20));
        theme.SetStylebox("hover_pressed", ListRow, Pill(Look.Accent, horizontal: 20));
        theme.SetStylebox("disabled", ListRow, Pill(Colors.Transparent, horizontal: 20));
        theme.SetStylebox("focus", ListRow, FocusRing(Pill(Look.SurfaceRaised, horizontal: 20), filled: true));
        theme.SetFont("font", ListRow, Look.Ui);
        theme.SetFontSize("font_size", ListRow, Look.UiLarge);
        theme.SetColor("font_color", ListRow, Look.Text);
        theme.SetColor("font_focus_color", ListRow, Look.Accent);
        theme.SetColor("font_hover_color", ListRow, Look.Text);
        theme.SetColor("font_pressed_color", ListRow, Look.Ink);
        theme.SetColor("font_disabled_color", ListRow, Look.TextMuted);
    }

    // Panels and dialogs are dark cards on the paper; a toast is a dark pill.
    private static void Panels(Theme theme)
    {
        theme.SetStylebox("panel", "PanelContainer", Card(Look.Surface, margin: 24));
        theme.SetStylebox("panel", "Panel", Card(Look.Surface, margin: 0));

        theme.SetTypeVariation(Dialog, "PanelContainer");
        theme.SetStylebox("panel", Dialog, Card(Look.Surface, margin: 32));

        theme.SetTypeVariation(Toast, "PanelContainer");
        var toast = Pill(Look.Surface, horizontal: 24);
        toast.ContentMarginTop = toast.ContentMarginBottom = 10;
        theme.SetStylebox("panel", Toast, toast);

        theme.SetConstant("separation", "VBoxContainer", 12);
        theme.SetConstant("separation", "HBoxContainer", 12);
    }

    // Text on panels by default; headings in the display face; muted for secondary lines.
    private static void Labels(Theme theme)
    {
        theme.SetColor("font_color", "Label", Look.Text);

        theme.SetTypeVariation(Heading, "Label");
        theme.SetFont("font", Heading, Look.Display);
        theme.SetFontSize("font_size", Heading, Look.DisplayMedium);
        theme.SetColor("font_color", Heading, Look.Text);

        theme.SetTypeVariation(Muted, "Label");
        theme.SetColor("font_color", Muted, Look.TextMuted);

        theme.SetTypeVariation(Strong, "Label");
        theme.SetFont("font", Strong, Look.UiBold);

        theme.SetTypeVariation(Caption, "Label");
        theme.SetColor("font_color", Caption, Look.TextMuted);
        theme.SetFontSize("font_size", Caption, Look.UiSmall);
    }

    // Tabs: pills in a row; the selected tab is accent with ink text.
    private static void Tabs(Theme theme)
    {
        const string type = "TabBar";
        theme.SetStylebox("tab_unselected", type, Pill(Look.Surface, horizontal: 20));
        theme.SetStylebox("tab_hovered", type, Pill(Look.SurfaceRaised, horizontal: 20));
        theme.SetStylebox("tab_selected", type, Pill(Look.Accent, horizontal: 20));
        theme.SetStylebox("tab_disabled", type, Pill(Look.Surface, horizontal: 20));
        theme.SetStylebox("tab_focus", type, FocusRing(Pill(Colors.Transparent, horizontal: 20)));
        theme.SetFont("font", type, Look.UiBold);
        theme.SetFontSize("font_size", type, Look.UiMedium);
        theme.SetColor("font_unselected_color", type, Look.TextMuted);
        theme.SetColor("font_hovered_color", type, Look.Text);
        theme.SetColor("font_selected_color", type, Look.Ink);
        theme.SetColor("font_disabled_color", type, Look.TextMuted);
        theme.SetConstant("h_separation", type, 8);
    }

    // A slider is a limb: a round-capped track, the filled part in accent, a round grabber.
    private static void Sliders(Theme theme)
    {
        const string type = "HSlider";
        var track = Pill(Look.SurfaceRaised);
        track.ContentMarginTop = track.ContentMarginBottom = 6;
        theme.SetStylebox("slider", type, track);
        var filled = Pill(Look.Accent);
        filled.ContentMarginTop = filled.ContentMarginBottom = 6;
        theme.SetStylebox("grabber_area", type, filled);
        theme.SetStylebox("grabber_area_highlight", type, filled);
        theme.SetStylebox("focus", type, FocusRing(Pill(Colors.Transparent)));
        theme.SetIcon("grabber", type, Dot(28, Look.Text, null));
        theme.SetIcon("grabber_highlight", type, Dot(28, Look.Text, Look.Accent));
        theme.SetIcon("grabber_disabled", type, Dot(28, Look.TextMuted, null));
        theme.SetConstant("center_grabber", type, 1);
    }

    // Toggles (Toggle.cs) draw themselves from these.
    private static void Toggles(Theme theme)
    {
        const string type = nameof(Toggle);
        theme.SetTypeVariation(type, "Button");
        theme.SetColor("track_off", type, Look.SurfaceRaised);
        theme.SetColor("track_on", type, Look.Accent);
        theme.SetColor("knob", type, Look.Text);
        theme.SetColor("knob_on", type, Look.Ink);
        theme.SetColor("disabled", type, Look.TextMuted);
        theme.SetStylebox("normal", type, new StyleBoxEmpty());
        theme.SetStylebox("hover", type, new StyleBoxEmpty());
        theme.SetStylebox("pressed", type, new StyleBoxEmpty());
        theme.SetStylebox("hover_pressed", type, new StyleBoxEmpty());
        theme.SetStylebox("disabled", type, new StyleBoxEmpty());
        theme.SetStylebox("focus", type, new StyleBoxEmpty());
        theme.SetFont("font", type, Look.Ui);
        theme.SetColor("font_color", type, Look.Text);
        theme.SetColor("font_focus_color", type, Look.Accent);
        theme.SetColor("font_pressed_color", type, Look.Text);
        theme.SetColor("font_hover_pressed_color", type, Look.Text);
        theme.SetColor("font_disabled_color", type, Look.TextMuted);
    }

    public static StyleBoxFlat Pill(Color fill, int horizontal = 24)
    {
        var box = new StyleBoxFlat { BgColor = fill, AntiAliasing = true };
        box.SetCornerRadiusAll(999);
        box.ContentMarginLeft = box.ContentMarginRight = horizontal;
        box.ContentMarginTop = box.ContentMarginBottom = 8;
        return box;
    }

    public static StyleBoxFlat Card(Color fill, int margin)
    {
        var box = new StyleBoxFlat { BgColor = fill, AntiAliasing = true };
        box.SetCornerRadiusAll((int)Look.CardRadius);
        box.SetContentMarginAll(margin);
        return box;
    }

    // The focus look: an accent ring just outside the control's shape (and, for rows, the fill too).
    public static StyleBoxFlat FocusRing(StyleBoxFlat shape, bool filled = false)
    {
        var box = (StyleBoxFlat)shape.Duplicate();
        box.DrawCenter = filled;
        box.BorderColor = Look.Accent;
        box.SetBorderWidthAll(FocusWidth);
        box.SetExpandMarginAll(FocusGap + FocusWidth);
        return box;
    }

    // A filled circle, optionally ringed, as a texture (Godot's slider grabber is an icon).
    private static ImageTexture Dot(int size, Color fill, Color? ring)
    {
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var center = (size - 1) / 2f;
        var radius = size / 2f;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var d = new Vector2(x - center, y - center).Length();
                var coverage = Mathf.Clamp(radius - d, 0, 1);
                if (coverage <= 0)
                    continue;
                var color = ring is { } r && d > radius - 4 ? r : fill;
                image.SetPixel(x, y, new Color(color, coverage));
            }
        }
        return ImageTexture.CreateFromImage(image);
    }
}
