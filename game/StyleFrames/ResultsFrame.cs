using Godot;
using StickFighter.Style;
using StickFighter.UI;

namespace StickFighter.StyleFrames;

// Style frame: the results screen. A band in the winner's color, the match in numbers, and
// rematch focused, over the winner standing and the loser down.
public partial class ResultsFrame : Control
{
    private static readonly (string Label, string Value)[] Stats =
    {
        ("Rounds", "2 – 1"),
        ("Damage dealt", "1,240"),
        ("Best combo", "5 hits"),
        ("Match time", "1:47"),
    };

    private static readonly string[] Items = { "Rematch", "Character select", "Main menu" };

    public ResultsFrame()
    {
        Size = new Vector2(1280, 720);
        MouseFilter = MouseFilterEnum.Ignore;
        Theme = UiTheme.Default;
    }

    // One kit panel: the match in numbers, then the next steps as list rows, rematch focused.
    public override void _Ready()
    {
        var panel = Ui.Panel();
        panel.CustomMinimumSize = new Vector2(400, 0);
        AddChild(panel);
        var column = new VBoxContainer();
        panel.AddChild(column);

        var stats = new GridContainer { Columns = 2 };
        stats.AddThemeConstantOverride("v_separation", 6);
        column.AddChild(stats);
        foreach (var (label, value) in Stats)
        {
            stats.AddChild(new Label { Text = label, ThemeTypeVariation = UiTheme.Muted, SizeFlagsHorizontal = SizeFlags.ExpandFill });
            stats.AddChild(new Label { Text = value, ThemeTypeVariation = UiTheme.Strong, HorizontalAlignment = HorizontalAlignment.Right });
        }

        Button? first = null;
        foreach (var item in Items)
        {
            var row = Ui.Row(item);
            column.AddChild(row);
            first ??= row;
        }
        panel.Position = new Vector2((Size.X - panel.GetCombinedMinimumSize().X) / 2, 192);
        first?.GrabFocus();
    }

    public override void _Draw()
    {
        var band = new Rect2(0, 48, Size.X, 120);
        DrawRect(band, Look.P1);
        this.Text(band, "PLAYER 1 WINS", Look.Display, Look.DisplayLarge, Look.Text);
    }
}
