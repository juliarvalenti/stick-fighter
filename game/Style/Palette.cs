namespace StickFighter.Style;

// Every color in the game, as a named hex token. The style guide (context/art-direction in room
// memory) explains them; this file is the source of truth.
//
// Plain C#, no Godot types, so tests/Art.Tests can check contrast and color-blind safety without
// an engine. Godot code reads them through Look.
public static class Palette
{
    // The stage: warm paper, black ink. Plain and high-contrast, as in the rules.
    public const string Paper = "#F2EEE3"; // stage background
    public const string PaperShade = "#E2DCCC"; // cards and empty slots laid on paper
    public const string Ink = "#17161C"; // floor line, outlines, text on paper

    // The fighters: one flat color each. The alternates are for mirror matches and color select.
    public const string P1 = "#C8102E"; // red
    public const string P2 = "#1D4ED8"; // blue
    public const string P1Alt = "#AB2B6B"; // rose, a red-family alternate for player 1
    public const string P2Alt = "#530674"; // violet, a blue-family alternate for player 2

    // UI: dark panels laid over the paper stage.
    public const string Surface = "#1F1D25"; // panels, bar tracks, the timer
    public const string SurfaceRaised = "#2E2B35"; // a selected row or card on a panel
    public const string Text = "#F2EEE3"; // text on a panel (the paper color)
    public const string TextMuted = "#A8A396"; // secondary text on a panel
    public const string InkMuted = "#5E594E"; // secondary text on paper
    public const string Accent = "#FFC21A"; // health, the timer, "Fight!", the focused item
    public const string Danger = "#FF5A66"; // low health, damage just taken, "K.O."

    public static readonly string[] Players = { P1, P2, P1Alt, P2Alt };

    // Every pairing of text over a background that the UI uses. Each must reach WCAG AA (4.5:1).
    // When a screen puts text on a new background, add the pair here first.
    public static readonly (string Text, string Background)[] TextPairs =
    {
        (Ink, Paper),
        (Ink, PaperShade),
        (InkMuted, Paper),
        (InkMuted, PaperShade),
        (Text, Surface),
        (Text, SurfaceRaised),
        (TextMuted, Surface),
        (TextMuted, SurfaceRaised),
        (Accent, Surface),
        (Accent, SurfaceRaised),
        (Danger, Surface),
        (Ink, Accent),
        (Text, P1),
        (Text, P2),
        (Text, P1Alt),
        (Text, P2Alt),
        (P1, Paper),
        (P2, Paper),
        (P1Alt, Paper),
        (P2Alt, Paper),
    };
}
