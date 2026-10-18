using Godot;
using StickFighter.Style;

namespace StickFighter.FX;

// The effects scenario's picture: every effect frame by frame, like a pose sheet, on the stage
// paper. Page one is the sparks (light, medium, heavy, counter hit, block); page two the rest
// (fireball, its dissipate, dust, and the full-screen K.O. and super flashes as small screens).
// The scenario turns the page partway through.
public partial class EffectSheet : Control, IScenarioClock
{
    private const float Left = 24;
    private const float Top = 64;
    private const int FirstPageUntil = 10; // frames

    private bool _secondPage;

    public EffectSheet()
    {
        Size = new Vector2(1280, 720);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void At(int frame)
    {
        var second = frame >= FirstPageUntil;
        if (second == _secondPage)
            return;
        _secondPage = second;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Look.Paper);
        var title = _secondPage ? "EFFECTS" : "SPARKS";
        this.Text(new Rect2(Left, 8, 800, 52), title, Look.Display, Look.DisplayMedium, Look.Ink, HorizontalAlignment.Left);

        var y = Top;
        if (!_secondPage)
        {
            foreach (var (effect, name) in new[]
            {
                (Effect.HitLight, "Hit, light"), (Effect.HitMedium, "Hit, medium"), (Effect.HitHeavy, "Hit, heavy"),
                (Effect.CounterHit, "Counter hit"), (Effect.Block, "Block (attacker on the left)"),
            })
                y = Row(effect, name, y, cell: new Vector2(80, 88), scale: 0.52f);
        }
        else
        {
            y = Row(Effect.Fireball, "Fireball (loops)", y, new Vector2(80, 64), 0.55f, color: Look.P1);
            y = Row(Effect.FireballDissipate, "Fireball dissipating", y, new Vector2(80, 64), 0.55f, color: Look.P2);
            y = Row(Effect.Dust, "Dust (landing, dashing)", y, new Vector2(80, 64), 0.55f);
            y = Row(Effect.KoFlash, "K.O. flash (whole screen)", y, new Vector2(88, 50), 1, fullScreen: true);
            Row(Effect.SuperFlash, "Super start (whole screen)", y, new Vector2(88, 50), 1, fullScreen: true);
        }
    }

    // One effect's frames left to right, wrapping, with the frame number under each. Returns the
    // y below the row.
    private float Row(Effect effect, string name, float y, Vector2 cell, float scale, Color? color = null, bool fullScreen = false)
    {
        this.Text(new Rect2(Left, y, 600, 18), name, Look.UiBold, Look.UiSmall, Look.Ink, HorizontalAlignment.Left);
        y += 18;
        var gap = 6f;
        var perRow = (int)((Size.X - 2 * Left + gap) / (cell.X + gap));
        var frames = Effects.Length(effect);
        for (var f = 0; f < frames; f++)
        {
            var box = new Rect2(Left + f % perRow * (cell.X + gap), y + f / perRow * (cell.Y + 14), cell.X, cell.Y);
            if (fullScreen)
            {
                DrawRect(box, Look.PaperShade);
                Effects.Draw(this, effect, f, box.Position + box.Size * new Vector2(0.4f, 0.6f), screen: box);
                DrawRect(box, Look.Ink, false, 1.5f);
            }
            else
            {
                DrawSetTransform(box.GetCenter(), 0, new Vector2(scale, scale));
                Effects.Draw(this, effect, f, Vector2.Zero, color: color);
                DrawSetTransform(Vector2.Zero);
            }
            this.Text(new Rect2(box.Position.X, box.End.Y, cell.X, 14), f.ToString(), Look.Ui, 12, Look.InkMuted);
        }
        var rows = (frames + perRow - 1) / perRow;
        return y + rows * (cell.Y + 14) + 4;
    }
}
