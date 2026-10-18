using Godot;
using StickFighter.Style;

namespace StickFighter.UI;

// Two round-capped strokes meeting in the middle, one per player's color: the rule under the title.
public partial class PlayerRule : Control
{
    public PlayerRule()
    {
        CustomMinimumSize = new Vector2(520, 16);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        var y = Size.Y / 2;
        var half = Size.X / 2;
        this.Stroke(new Vector2(Look.FighterStroke, y), new Vector2(half - 8, y), Look.P1, Look.FighterStroke);
        this.Stroke(new Vector2(half + 8, y), new Vector2(Size.X - Look.FighterStroke, y), Look.P2, Look.FighterStroke);
    }
}

// A player's color choices as swatches, the chosen one ringed.
public partial class ColorPick : Control
{
    private const float Radius = 13;
    private const float Step = 40;

    private readonly Color[] _colors;
    private readonly int _chosen;

    public ColorPick(Color[] colors, int chosen)
    {
        _colors = colors;
        _chosen = chosen;
        CustomMinimumSize = new Vector2(Step * colors.Length, 40);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        for (var i = 0; i < _colors.Length; i++)
        {
            var center = new Vector2(Step / 2 + i * Step, Size.Y / 2);
            DrawCircle(center, Radius, _colors[i]);
            if (i == _chosen)
                DrawArc(center, Radius + 5, 0, Mathf.Tau, 40, Look.Text, Look.LineStroke, true);
        }
    }
}

// A band across the screen in the winner's color, with the result in the display face.
public partial class WinnerBand : Control
{
    private readonly string _text;
    private readonly Color _color;

    public WinnerBand(string text, Color color)
    {
        _text = text;
        _color = color;
        CustomMinimumSize = new Vector2(1280, 120);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), _color);
        this.Text(new Rect2(Vector2.Zero, Size), _text, Look.Display, Look.DisplayLarge, Look.Text);
    }
}
