using Godot;
using StickFighter.Style;

namespace StickFighter.StyleFrames;

// Style frame: character select. A row of character cards with both players' cursors on the one
// character there is, and each player's fighter standing below as the preview.
public partial class SelectFrame : Control
{
    private static readonly Vector2 CardSize = new(132, 148);
    private const int Cards = 3;

    public SelectFrame()
    {
        Size = new Vector2(1280, 720);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        this.Text(new Rect2(0, 24, Size.X, 64), "CHOOSE YOUR FIGHTER", Look.Display, Look.DisplayMedium, Look.Ink);

        const float gap = 24;
        var left = (Size.X - (Cards * CardSize.X + (Cards - 1) * gap)) / 2;
        for (var i = 0; i < Cards; i++)
        {
            var card = new Rect2(left + i * (CardSize.X + gap), 104, CardSize.X, CardSize.Y);
            this.Card(card, Look.PaperShade);
            if (i == 0)
                DrawPortrait(card);
            else
                this.Text(card, "?", Look.Display, Look.DisplayMedium, Look.InkMuted);
        }

        // Both cursors on the first card: player 1's ring outside, player 2's inside it.
        var picked = new Rect2(left, 104, CardSize.X, CardSize.Y);
        this.CardOutline(picked.Grow(10), Look.P1, Look.BoldStroke, Look.CardRadius + 10);
        this.CardOutline(picked.Grow(2), Look.P2, Look.BoldStroke, Look.CardRadius + 2);
        DrawCursorTag(0, new Vector2(picked.Position.X - 10, picked.Position.Y - 26));
        DrawCursorTag(1, new Vector2(picked.End.X + 10 - 56, picked.Position.Y - 26));

        DrawPlayerPanel(0, 300, ready: true, colors: new[] { Look.P1, Look.P1Alt });
        DrawPlayerPanel(1, Size.X - 300, ready: false, colors: new[] { Look.P2, Look.P2Alt });

        this.Text(new Rect2(0, 668, Size.X, 36), "Enter  confirm     Tab  color     Esc  back", Look.Ui,
            Look.UiSmall, Look.InkMuted);
    }

    // The character drawn as a little stick figure in ink: the portrait style for every character.
    private void DrawPortrait(Rect2 card)
    {
        var c = card.GetCenter();
        const float w = 7;
        DrawCircle(c + new Vector2(0, -44), 16, Look.Ink);
        this.Stroke(c + new Vector2(0, -24), c + new Vector2(0, 16), Look.Ink, w);
        this.Stroke(c + new Vector2(0, -16), c + new Vector2(-22, 2), Look.Ink, w);
        this.Stroke(c + new Vector2(-22, 2), c + new Vector2(-6, -10), Look.Ink, w);
        this.Stroke(c + new Vector2(0, -16), c + new Vector2(24, -4), Look.Ink, w);
        this.Stroke(c + new Vector2(24, -4), c + new Vector2(30, -22), Look.Ink, w);
        this.Stroke(c + new Vector2(0, 16), c + new Vector2(-18, 52), Look.Ink, w);
        this.Stroke(c + new Vector2(0, 16), c + new Vector2(20, 52), Look.Ink, w);
        this.Text(new Rect2(card.Position.X, card.End.Y + 8, card.Size.X, 28), "STICK", Look.UiBold,
            Look.UiMedium, Look.Ink);
    }

    private void DrawCursorTag(int player, Vector2 at)
    {
        var rect = new Rect2(at, new Vector2(56, 28));
        this.Pill(rect, Look.Player(player));
        this.Text(rect, $"{player + 1}P", Look.UiBold, Look.UiSmall, Look.Text);
    }

    // Under each fighter: the character name in the player's color, the color choices, and
    // whether the player has locked in.
    private void DrawPlayerPanel(int player, float centerX, bool ready, Color[] colors)
    {
        var name = new Rect2(centerX - 160, 604, 320, 60);
        this.Text(name, "STICK", Look.Display, Look.DisplayMedium, Look.Player(player));

        for (var i = 0; i < colors.Length; i++)
        {
            var center = new Vector2(centerX - 96 + i * 40, 686);
            DrawCircle(center, 12, colors[i]);
            if (i == 0)
                DrawArc(center, 18, 0, Mathf.Tau, 40, Look.Ink, Look.LineStroke, true);
        }

        var status = new Rect2(centerX - 36, 668, 144, 36);
        this.Pill(status, ready ? Look.Surface : Look.SurfaceRaised);
        this.Text(status, ready ? "READY" : "CHOOSING", Look.UiBold, Look.UiMedium, ready ? Look.Accent : Look.Text);
    }
}
