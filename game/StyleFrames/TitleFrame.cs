using Godot;
using StickFighter.Style;
using StickFighter.UI;

namespace StickFighter.StyleFrames;

// Style frame: the title screen, the two fighters squared up on the stage below the menu.
public partial class TitleFrame : Control
{
    private static readonly string[] Items = { "Versus", "Training", "Options", "Quit" };

    public TitleFrame()
    {
        Size = new Vector2(1280, 720);
        MouseFilter = MouseFilterEnum.Ignore;
        Theme = UiTheme.Default;
    }

    // The menu: the kit's list rows on a panel, the first one focused, as a pad would leave it.
    public override void _Ready()
    {
        var panel = Ui.Panel();
        AddChild(panel);
        var rows = new VBoxContainer();
        panel.AddChild(rows);
        Button? first = null;
        foreach (var item in Items)
        {
            var row = Ui.Row(item);
            rows.AddChild(row);
            first ??= row;
        }
        panel.Position = new Vector2((Size.X - panel.GetCombinedMinimumSize().X) / 2, 244);
        first?.GrabFocus();
    }

    public override void _Draw()
    {
        var title = new Rect2(0, 56, Size.X, 140);
        this.Text(title, "STICK FIGHTER", Look.Display, Look.DisplayHero, Look.Ink);

        // Two strokes under the title, one per player, meeting in the middle.
        var y = title.End.Y + 8;
        this.Stroke(new Vector2(380, y), new Vector2(632, y), Look.P1, Look.FighterStroke);
        this.Stroke(new Vector2(648, y), new Vector2(900, y), Look.P2, Look.FighterStroke);

        this.Text(new Rect2(0, Size.Y - 72, Size.X, 32), "Enter  select       Esc  back", Look.Ui, Look.UiSmall,
            Look.InkMuted);
        this.Text(new Rect2(Size.X - 248, Size.Y - 72, 200, 32), "v0.1 style frame", Look.Ui, Look.UiSmall,
            Look.InkMuted, HorizontalAlignment.Right);
    }
}
