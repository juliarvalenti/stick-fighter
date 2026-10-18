using Godot;
using StickFighter.Style;

namespace StickFighter.StyleFrames;

// Style frame: the title screen, the two fighters squared up on the stage below the menu.
public partial class TitleFrame : Control
{
    private static readonly string[] Items = { "Versus", "Training", "Options", "Quit" };
    private const int Focused = 0;

    public TitleFrame()
    {
        Size = new Vector2(1280, 720);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        var title = new Rect2(0, 56, Size.X, 140);
        this.Text(title, "STICK FIGHTER", Look.Display, Look.DisplayHero, Look.Ink);

        // Two strokes under the title, one per player, meeting in the middle.
        var y = title.End.Y + 8;
        this.Stroke(new Vector2(380, y), new Vector2(632, y), Look.P1, Look.FighterStroke);
        this.Stroke(new Vector2(648, y), new Vector2(900, y), Look.P2, Look.FighterStroke);

        // The menu: the focused item is a dark pill with accent text; the rest are ink on paper.
        var row = new Vector2(240, 52);
        for (var i = 0; i < Items.Length; i++)
        {
            var rect = new Rect2((Size.X - row.X) / 2, 264 + i * (row.Y + 8), row.X, row.Y);
            if (i == Focused)
            {
                this.Pill(rect, Look.Surface);
                this.Text(rect, Items[i], Look.UiBold, Look.UiLarge, Look.Accent);
            }
            else
            {
                this.Text(rect, Items[i], Look.Ui, Look.UiLarge, Look.Ink);
            }
        }

        this.Text(new Rect2(0, Size.Y - 72, Size.X, 32), "Enter  select       Esc  back", Look.Ui, Look.UiSmall,
            Look.InkMuted);
        this.Text(new Rect2(Size.X - 248, Size.Y - 72, 200, 32), "v0.1 style frame", Look.Ui, Look.UiSmall,
            Look.InkMuted, HorizontalAlignment.Right);
    }
}
