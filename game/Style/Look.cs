using Godot;

namespace StickFighter.Style;

// The style guide as Godot values: colors, fonts and sizes, strokes and radii, motion. Screens and
// components take everything from here rather than picking their own numbers.
public static class Look
{
    public static readonly Color Paper = Color.FromHtml(Palette.Paper);
    public static readonly Color PaperShade = Color.FromHtml(Palette.PaperShade);
    public static readonly Color Ink = Color.FromHtml(Palette.Ink);
    public static readonly Color BackdropFar = Color.FromHtml(Palette.BackdropFar);
    public static readonly Color BackdropNear = Color.FromHtml(Palette.BackdropNear);
    public static readonly Color P1 = Color.FromHtml(Palette.P1);
    public static readonly Color P2 = Color.FromHtml(Palette.P2);
    public static readonly Color P1Alt = Color.FromHtml(Palette.P1Alt);
    public static readonly Color P2Alt = Color.FromHtml(Palette.P2Alt);
    public static readonly Color Surface = Color.FromHtml(Palette.Surface);
    public static readonly Color SurfaceRaised = Color.FromHtml(Palette.SurfaceRaised);
    public static readonly Color Text = Color.FromHtml(Palette.Text);
    public static readonly Color TextMuted = Color.FromHtml(Palette.TextMuted);
    public static readonly Color InkMuted = Color.FromHtml(Palette.InkMuted);
    public static readonly Color Accent = Color.FromHtml(Palette.Accent);
    public static readonly Color Danger = Color.FromHtml(Palette.Danger);
    public static readonly Color LowHealth = Color.FromHtml(Palette.LowHealth);
    public static readonly Color Dust = Color.FromHtml(Palette.Dust);

    public static Color Player(int index) => index == 0 ? P1 : P2;

    // A player's color by their choice: 0 their own, 1 their alternate (mirror matches, color select).
    public static Color Player(int index, int choice) =>
        choice == 0 ? Player(index) : index == 0 ? P1Alt : P2Alt;

    // Type. Lilita One for display (titles, "Fight!", "K.O.", the timer); Atkinson Hyperlegible for
    // everything read at speed (names, menus, frame data). Both SIL OFL, in assets/fonts/.
    public static Font Display => _display ??= GD.Load<Font>("res://assets/fonts/lilita-one/LilitaOne-Regular.ttf");
    public static Font Ui => _ui ??= GD.Load<Font>("res://assets/fonts/atkinson-hyperlegible/AtkinsonHyperlegible-Regular.ttf");
    public static Font UiBold => _uiBold ??= GD.Load<Font>("res://assets/fonts/atkinson-hyperlegible/AtkinsonHyperlegible-Bold.ttf");
    private static Font? _display;
    private static Font? _ui;
    private static Font? _uiBold;

    // Sizes in pixels at 1280x720.
    public const int DisplayHero = 128; // the title, "K.O."
    public const int DisplayLarge = 88; // "Fight!", "Player 1 wins"
    public const int DisplayMedium = 56; // the timer, screen headings
    public const int UiLarge = 32; // menu items
    public const int UiMedium = 22; // names, labels
    public const int UiSmall = 16; // hints, frame data

    // Strokes. Everything is a round-capped line or a flat fill: no gradients, textures or bevels.
    public const float FighterStroke = 10f; // limbs
    public const float BoldStroke = 6f; // UI outlines and focus rings
    public const float LineStroke = 4f; // the floor, dividers

    // Corners. Bars, tags and buttons are pills (fully round ends, like a limb); cards and panels
    // get one fixed radius.
    public const float CardRadius = 16f;

    // Motion, in seconds, with the Godot easing each one uses. UI moves fast and settles: things
    // enter with ease-out, leave with ease-in, and announcements pop with a back overshoot.
    public const float MotionQuick = 0.08f; // focus moving between items
    public const float MotionBase = 0.16f; // panels and cards entering or leaving
    public const float MotionSlow = 0.32f; // screen to screen
    public const float MotionPop = 0.24f; // "Fight!", "K.O." scaling in
    public const Tween.TransitionType EnterTransition = Tween.TransitionType.Cubic;
    public const Tween.EaseType Enter = Tween.EaseType.Out;
    public const Tween.EaseType Exit = Tween.EaseType.In;
    public const Tween.TransitionType PopTransition = Tween.TransitionType.Back;
}
