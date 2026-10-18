using Godot;
using StickFighter.Sim.Characters;
using StickFighter.Style;

namespace StickFighter.UI.Screens;

// Results: a band in the winner's color, the winner in their victory pose, the rounds as they went,
// and what next (rematch focused, since that's what most players want).
public partial class ResultsScreen : Screen
{
    private static readonly (string Round, int Winner, string How)[] Rounds =
    {
        ("ROUND 1", 0, "K.O."),
        ("ROUND 2", 1, "TIME"),
        ("ROUND 3", 0, "PERFECT"),
    };

    public override void _Ready()
    {
        AddChild(new Panel { ThemeTypeVariation = UiTheme.Backdrop, Size = Size });
        AddChild(new WinnerBand("PLAYER 1 WINS", Look.P1) { Position = new Vector2(0, 40) });

        AddChild(new Portrait(Characters.Placeholder, Look.P1, Portrait.Pose.Victory)
        {
            Position = new Vector2(120, 200),
            Size = new Vector2(320, 420),
        });

        var panel = Ui.Panel();
        panel.CustomMinimumSize = new Vector2(440, 0);
        var column = new VBoxContainer();
        panel.AddChild(column);

        column.AddChild(Ui.Label("ROUNDS  2 – 1", UiTheme.Caption));
        var tally = new GridContainer { Columns = 3 };
        tally.AddThemeConstantOverride("h_separation", 24);
        tally.AddThemeConstantOverride("v_separation", 6);
        column.AddChild(tally);
        foreach (var (round, winner, how) in Rounds)
        {
            tally.AddChild(Ui.Label(round, UiTheme.Muted));
            tally.AddChild(new Label { Text = $"P{winner + 1}", ThemeTypeVariation = winner == 0 ? UiTheme.ChipOn : UiTheme.Chip });
            tally.AddChild(Ui.Label(how, UiTheme.Strong));
        }

        var first = Menu(column, "Rematch", "Character select", "Title");
        panel.Position = new Vector2(1280 - 120 - 440, 200);
        AddChild(panel);
        first.GrabFocus();
    }
}
