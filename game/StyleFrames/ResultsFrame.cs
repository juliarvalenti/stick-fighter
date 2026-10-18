using Godot;
using StickFighter.Style;

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
    }

    public override void _Draw()
    {
        var band = new Rect2(0, 48, Size.X, 120);
        DrawRect(band, Look.P1);
        this.Text(band, "PLAYER 1 WINS", Look.Display, Look.DisplayLarge, Look.Text);

        var panel = new Rect2(440, 200, 400, 4 * 40 + 32);
        this.Card(panel, Look.Surface);
        for (var i = 0; i < Stats.Length; i++)
        {
            var row = new Rect2(panel.Position.X + 28, panel.Position.Y + 16 + i * 40, panel.Size.X - 56, 40);
            this.Text(row, Stats[i].Label, Look.Ui, Look.UiMedium, Look.TextMuted, HorizontalAlignment.Left);
            this.Text(row, Stats[i].Value, Look.UiBold, Look.UiMedium, Look.Text, HorizontalAlignment.Right);
        }

        var size = new Vector2(280, 48);
        for (var i = 0; i < Items.Length; i++)
        {
            var rect = new Rect2((Size.X - size.X) / 2, panel.End.Y + 24 + i * (size.Y + 4), size.X, size.Y);
            if (i == 0)
            {
                this.Pill(rect, Look.Surface);
                this.Text(rect, Items[i], Look.UiBold, Look.UiLarge, Look.Accent);
            }
            else
            {
                this.Text(rect, Items[i], Look.Ui, Look.UiLarge, Look.Ink);
            }
        }
    }
}
