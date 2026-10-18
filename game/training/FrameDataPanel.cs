using Godot;
using StickFighter.Sim;
using StickFighter.Style;
using StickFighter.UI;

namespace StickFighter.Training;

// Training mode's frame data: a compact panel under the HUD showing what FrameDataAnalyser measured
// for the last exchange. The move (glyphs and notation) and whether it hit or was blocked; its
// startup, active and recovery; the advantage, big, in accent when plus and danger when minus; and
// the block-string gap or combo and link window. Presentation only.
//
// F5 shows or hides it (until the training menu exists).
public partial class FrameDataPanel : Control
{
    private static readonly Vector2 PanelSize = new(560, 108);
    private const float Top = 132;

    private readonly FrameDataAnalyser _analyser = new();

    public FrameDataPanel()
    {
        Size = new Vector2(1280, 720);
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
    }

    // Called with the state after each display frame's step.
    public void Present(in GameState state)
    {
        _analyser.Observe(state);
        if (Visible)
            QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.F5 })
            Visible = !Visible;
    }

    public override void _Draw()
    {
        var panel = new Rect2((Size.X - PanelSize.X) / 2, Top, PanelSize.X, PanelSize.Y);
        this.Card(panel, Look.Surface);
        var inner = panel.Grow(-16);

        if (_analyser.Last is not { } r)
        {
            this.Text(inner, "Frame data: do a move", Look.Ui, Look.UiMedium, Look.TextMuted);
            return;
        }

        // The move and what happened.
        var x = inner.Position.X;
        this.Pill(new Rect2(x, inner.Position.Y, 40, 26), Look.Player(r.Attacker));
        this.Text(new Rect2(x, inner.Position.Y, 40, 26), $"P{r.Attacker + 1}", Look.UiBold, Look.UiSmall, Look.Text);
        x += 52;
        x += Glyphs.Draw(this, new Vector2(x, inner.Position.Y), r.Move, 26, onPanel: true) + 10;
        this.Text(new Rect2(x, inner.Position.Y, 120, 26), r.Move, Look.UiBold, Look.UiMedium, Look.Text, HorizontalAlignment.Left);
        var result = r.Blocked ? "BLOCKED" : r.Combo > 1 ? $"{r.Combo} HIT COMBO" : "HIT";
        this.Text(new Rect2(inner.Position.X, inner.Position.Y, inner.Size.X, 26), result, Look.UiBold, Look.UiSmall,
            r.Blocked ? Look.TextMuted : Look.Accent, HorizontalAlignment.Right);

        // Startup, active, recovery.
        var row = new Rect2(inner.Position.X, inner.Position.Y + 36, inner.Size.X * 0.62f, 24);
        this.Text(row, $"Startup {Value(r.Startup)}    Active {Value(r.Active)}    Recovery {Value(r.Recovery)}",
            Look.Ui, Look.UiMedium, Look.Text, HorizontalAlignment.Left);

        // The advantage, big.
        var advantage = new Rect2(inner.Position.X + inner.Size.X * 0.62f, inner.Position.Y + 28, inner.Size.X * 0.38f, 48);
        var adv = r.Advantage is { } a ? (a > 0 ? $"+{a}" : a.ToString()) : "–";
        var color = r.Advantage is { } v ? (v >= 0 ? Look.Accent : Look.Danger) : Look.TextMuted;
        this.Text(new Rect2(advantage.Position.X, advantage.Position.Y, advantage.Size.X - 84, advantage.Size.Y),
            r.Blocked ? "on block" : "on hit", Look.Ui, Look.UiSmall, Look.TextMuted, HorizontalAlignment.Right);
        this.Text(new Rect2(advantage.End.X - 76, advantage.Position.Y, 76, advantage.Size.Y), adv, Look.Display,
            Look.UiLarge + 8, color, HorizontalAlignment.Right);

        // Strings, combos, links; or a warning if frames were missed.
        var note = r.MissedFrames ? "Frames were skipped: do it again for exact numbers"
            : r.Blocked && r.Gap is { } gap ? (gap == 0 ? "True block string" : $"Gap {gap}f in the block string")
            : r.LinkWindow is { } window ? (window > 0 ? $"Link: {window}f window" : "Not a link: too slow by " + (1 - window) + "f")
            : "";
        this.Text(new Rect2(inner.Position.X, inner.Position.Y + 64, inner.Size.X * 0.62f, 22), note, Look.Ui, Look.UiSmall,
            r.MissedFrames ? Look.Danger : Look.TextMuted, HorizontalAlignment.Left);
    }

    private static string Value(int? frames) => frames?.ToString() ?? "–";
}
