using Godot;
using StickFighter.Sim.Characters;
using StickFighter.Style;

namespace StickFighter.UI.Screens;

// Character select. A grid of cards (the one character, Random, and a locked slot for the second
// character), each player's cursor in their color, and under it each player's panel: their pick
// as a portrait in their color, the color choices, and whether they've locked in.
//
// Mirror match: when both pick the same character, the second picker takes their alternate color,
// so the two fighters never read alike. Here both are on Stick, so P2 shows in violet.
public partial class SelectScreen : Screen
{
    public override void _Ready()
    {
        AddChild(new Panel { ThemeTypeVariation = UiTheme.Backdrop, Size = Size });

        var heading = new Label { Text = "CHOOSE YOUR FIGHTER", ThemeTypeVariation = UiTheme.PaperHeading };
        AddChild(heading);
        CenterAt(heading, 16);

        var character = Characters.Placeholder;
        var grid = new HBoxContainer();
        grid.AddThemeConstantOverride("separation", 28);
        var stick = new CharacterCard(character, "STICK");
        stick.Cursors.Add(0);
        stick.Cursors.Add(1);
        grid.AddChild(stick);
        grid.AddChild(new CharacterCard(null, "RANDOM"));
        grid.AddChild(new CharacterCard(null, "SOON", locked: true));
        AddChild(grid);
        CenterAt(grid, 120);
        stick.GrabFocus();

        var mirror = true;
        AddChild(PlayerPanel(0, character, new[] { Look.P1, Look.P1Alt }, chosen: 0, ready: true, note: null, x: 48));
        AddChild(PlayerPanel(1, character, new[] { Look.P2, Look.P2Alt }, chosen: mirror ? 1 : 0, ready: false,
            note: mirror ? "Mirror match: alternate color" : null, x: 1280 - 48 - 400));

        var hint = new Label { Text = "Confirm with a punch   ·   Change color with a kick   ·   Back to leave", ThemeTypeVariation = UiTheme.PaperMuted };
        AddChild(hint);
        CenterAt(hint, 684);
    }

    private static Control PlayerPanel(int player, Character character, Color[] colors, int chosen, bool ready, string? note, float x)
    {
        var panel = Ui.Panel();
        panel.CustomMinimumSize = new Vector2(400, 0);
        panel.Position = new Vector2(x, 330);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 20);
        panel.AddChild(row);
        row.AddChild(new Portrait(character, colors[chosen], facingLeft: player == 1) { CustomMinimumSize = new Vector2(120, 240) });

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        row.AddChild(column);
        column.AddChild(Ui.Label($"PLAYER {player + 1}", UiTheme.Caption));
        column.AddChild(Ui.Heading("STICK"));
        column.AddChild(new ColorPick(colors, chosen));
        column.AddChild(new Label { Text = ready ? "READY" : "CHOOSING", ThemeTypeVariation = ready ? UiTheme.ChipOn : UiTheme.Chip, SizeFlagsHorizontal = SizeFlags.ShrinkBegin });
        if (note is not null)
            column.AddChild(Ui.Label(note, UiTheme.Caption));
        return panel;
    }
}
