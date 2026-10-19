using System.Linq;
using Godot;
using StickFighter.Sim;
using StickFighter.Sim.Characters;
using StickFighter.Style;

namespace StickFighter.UI.Screens;

// Results: a band in the winner's color, the winner in their victory pose, the rounds as they went,
// and what next (rematch focused, since that's what most players want).
public partial class ResultsScreen : Screen
{
    // What the kit's screen shows on its own (the `screen-results` scenario): P1 2-1.
    private static readonly MatchResults Example = new(MatchWinner.P1, 2, 1, new[]
    {
        new RoundOutcome(1, MatchWinner.P1, Announcement.KO),
        new RoundOutcome(2, MatchWinner.P2, Announcement.Time),
        new RoundOutcome(3, MatchWinner.P1, Announcement.Perfect),
    });

    private readonly MatchResults _results;

    private readonly int[] _colors;
    private readonly bool _online;

    // `colors` are the players' color choices (0 own, 1 alternate), so the winner shows as they played.
    // `online`: after an online match there's no rematch or select yet, only back to the title.
    public ResultsScreen(MatchResults? results = null, int[]? colors = null, bool online = false)
    {
        _results = results ?? Example;
        _colors = colors ?? new[] { 0, 0 };
        _online = online;
    }

    public override void _Ready()
    {
        var winner = _results.Winner;
        var color = winner switch
        {
            MatchWinner.P1 => Look.Player(0, _colors[0]),
            MatchWinner.P2 => Look.Player(1, _colors[1]),
            _ => Look.InkMuted,
        };
        var title = winner switch
        {
            MatchWinner.P1 => "PLAYER 1 WINS",
            MatchWinner.P2 => "PLAYER 2 WINS",
            _ => "DRAW",
        };

        AddChild(new Panel { ThemeTypeVariation = UiTheme.Backdrop, Size = Size });
        AddChild(new WinnerBand(title, color) { Position = new Vector2(0, 40) });

        var pose = winner is MatchWinner.P1 or MatchWinner.P2 ? Portrait.Pose.Victory : Portrait.Pose.Ready;
        AddChild(new Portrait(Characters.Placeholder, color, pose)
        {
            Position = new Vector2(120, 200),
            Size = new Vector2(320, 420),
        });

        var panel = Ui.Panel();
        panel.CustomMinimumSize = new Vector2(440, 0);
        var column = new VBoxContainer();
        panel.AddChild(column);

        // Rounds taken, a double KO or draw counting for both, as the score does.
        var p1 = _results.Rounds.Count(r => r.Winner != MatchWinner.P2);
        var p2 = _results.Rounds.Count(r => r.Winner != MatchWinner.P1);
        column.AddChild(Ui.Label($"ROUNDS  {p1} – {p2}", UiTheme.Caption));
        var tally = new GridContainer { Columns = 3 };
        tally.AddThemeConstantOverride("h_separation", 24);
        tally.AddThemeConstantOverride("v_separation", 6);
        column.AddChild(tally);
        foreach (var round in _results.Rounds)
        {
            tally.AddChild(Ui.Label($"ROUND {round.Round}", UiTheme.Muted));
            var who = round.Winner switch
            {
                MatchWinner.P1 => "P1",
                MatchWinner.P2 => "P2",
                _ => "TIE",
            };
            tally.AddChild(new Label { Text = who, ThemeTypeVariation = round.Winner == winner ? UiTheme.ChipOn : UiTheme.Chip });
            tally.AddChild(Ui.Label(How(round.How), UiTheme.Strong));
        }

        var first = _online ? Menu(column, "Title") : Menu(column, "Rematch", "Character select", "Title");
        panel.Position = new Vector2(1280 - 120 - 440, 200);
        AddChild(panel);
        first.GrabFocus();
    }

    private static string How(Announcement how) => how switch
    {
        Announcement.KO => "K.O.",
        Announcement.Time => "TIME",
        Announcement.Perfect => "PERFECT",
        Announcement.DoubleKO => "DOUBLE K.O.",
        _ => "",
    };
}
