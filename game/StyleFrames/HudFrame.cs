using Godot;
using StickFighter.Style;

namespace StickFighter.StyleFrames;

// Style frame: the in-match HUD over the fighters. A mock with placeholder numbers and no
// behavior; the HUD design task builds the real one from the same tokens.
public partial class HudFrame : Control
{
    private const float Margin = 48;
    private const float BarTop = 32;
    private const float BarHeight = 32;
    private const float BarInset = 4;
    private const float TimerWidth = 112;

    public HudFrame()
    {
        Size = new Vector2(1280, 720);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        var barWidth = (Size.X - TimerWidth) / 2 - Margin - 16;

        // Player 1 has taken some damage, the last hit still trailing; player 2 is low.
        DrawHealth(new Rect2(Margin, BarTop, barWidth, BarHeight), health: 0.72f, trail: 0.81f, mirrored: false);
        DrawHealth(new Rect2(Size.X - Margin - barWidth, BarTop, barWidth, BarHeight), health: 0.22f, trail: 0.22f,
            mirrored: true);

        // The timer sits between the bars.
        var timer = new Rect2((Size.X - TimerWidth) / 2, BarTop - 12, TimerWidth, 88);
        this.Card(timer, Look.Surface);
        this.Text(timer, "58", Look.Display, Look.DisplayMedium, Look.Accent);

        DrawNameplate(0, "STICK", wins: 1, new Vector2(Margin, BarTop + BarHeight + 12), mirrored: false);
        DrawNameplate(1, "STICK", wins: 0, new Vector2(Size.X - Margin, BarTop + BarHeight + 12), mirrored: true);

        DrawMeter(0, 0.6f, level: 1, new Vector2(Margin, Size.Y - 64), mirrored: false);
        DrawMeter(1, 0.15f, level: 0, new Vector2(Size.X - Margin, Size.Y - 64), mirrored: true);

        // A combo counter on the attacker's side, in the attacker's color.
        this.Text(new Rect2(Margin, 220, 160, 96), "3", Look.Display, Look.DisplayLarge, Look.P1,
            HorizontalAlignment.Left);
        this.Text(new Rect2(Margin + 4, 300, 160, 28), "HITS", Look.UiBold, Look.UiMedium, Look.Ink,
            HorizontalAlignment.Left);
    }

    // A health bar: a dark pill track, the health left in it anchored at the outer end, and the
    // damage just taken trailing in paper until it drains. Low health turns the fill to danger.
    private void DrawHealth(Rect2 bar, float health, float trail, bool mirrored)
    {
        this.Pill(bar, Look.Surface);
        var inner = bar.Grow(-BarInset);
        var fill = health <= 0.25f ? Look.Danger : Look.Accent;
        this.Pill(Span(inner, trail, mirrored), Look.Text);
        this.Pill(Span(inner, health, mirrored), fill);
    }

    private static Rect2 Span(Rect2 inner, float fraction, bool mirrored)
    {
        var width = inner.Size.X * fraction;
        var x = mirrored ? inner.End.X - width : inner.Position.X;
        return new Rect2(x, inner.Position.Y, width, inner.Size.Y);
    }

    // A name tag in the player's color, then the round-win pips toward the middle.
    private void DrawNameplate(int player, string name, int wins, Vector2 anchor, bool mirrored)
    {
        var tag = new Vector2(168, 36);
        var tagRect = new Rect2(mirrored ? anchor.X - tag.X : anchor.X, anchor.Y, tag.X, tag.Y);
        this.Pill(tagRect, Look.Player(player));
        this.Text(tagRect, $"P{player + 1}  {name}", Look.UiBold, Look.UiMedium, Look.Text);

        const float pip = 10;
        for (var i = 0; i < 2; i++)
        {
            var step = 16 + pip + i * (pip * 2 + 10);
            var x = mirrored ? tagRect.Position.X - step : tagRect.End.X + step;
            var center = new Vector2(x, tagRect.GetCenter().Y);
            if (i < wins)
                DrawCircle(center, pip, Look.Ink);
            else
                DrawArc(center, pip - 2, 0, Mathf.Tau, 32, Look.Ink, Look.LineStroke, true);
        }
    }

    // The super meter: a level number, then a pill that fills in the player's color.
    private void DrawMeter(int player, float fill, int level, Vector2 anchor, bool mirrored)
    {
        var levelBox = new Rect2(mirrored ? anchor.X - 40 : anchor.X, anchor.Y - 10, 40, 40);
        this.Text(levelBox, level.ToString(), Look.Display, Look.UiLarge, Look.Ink);

        var track = new Rect2(mirrored ? anchor.X - 48 - 260 : anchor.X + 48, anchor.Y, 260, 20);
        this.Pill(track, Look.Surface);
        this.Pill(Span(track.Grow(-4), fill, mirrored), Look.Player(player));
    }
}
