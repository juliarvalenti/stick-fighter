using Godot;
using StickFighter.Sim;
using StickFighter.Style;
using StickFighter.UI;

namespace StickFighter.Training;

// Training mode's input display: each player's input log down the side of the screen, newest at
// the top, SF-style. Each row is how many frames it was held, the direction as a numpad glyph
// relative to facing, and the buttons held, drawn with the kit's glyphs so it reads like the move
// list. Presentation only: it reads the input history in GameState and writes nothing.
//
// F4 cycles both players, player 1, player 2, off (until the training menu exists).
public partial class InputDisplay : Control
{
    public enum Mode
    {
        Off,
        Both,
        Player1,
        Player2,
    }

    private const float RowHeight = 30;
    private const float GlyphHeight = 26;
    private const float PanelWidth = 232;
    private const float Top = 150;
    private const int VisibleRows = 16;

    private readonly InputLog[] _logs = { new(), new() };

    public InputDisplay()
    {
        Size = new Vector2(1280, 720);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public Mode Showing { get; set; } = Mode.Off;

    // Called after each display frame's simulation step(s). Re-reads the whole window the sim keeps
    // (the log rewinds to the first frame it's told again), so frames that ran in one display frame
    // are all logged, and a rollback that changed recent inputs corrects the log.
    public void Present(in GameState state)
    {
        // A step records its frame's input and then advances Frame, so the latest input is Frame - 1.
        var last = state.Frame - 1;
        for (var p = 0; p < GameState.PlayerCount; p++)
            _logs[p].Read(state.Inputs[p], last, state.Fighters[p].Facing);
        if (Showing != Mode.Off)
            QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.F4 })
        {
            Showing = Showing switch
            {
                Mode.Off => Mode.Both,
                Mode.Both => Mode.Player1,
                Mode.Player1 => Mode.Player2,
                _ => Mode.Off,
            };
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (Showing is Mode.Both or Mode.Player1)
            DrawLog(0, new Vector2(24, Top));
        if (Showing is Mode.Both or Mode.Player2)
            DrawLog(1, new Vector2(Size.X - 24 - PanelWidth, Top));
    }

    private void DrawLog(int player, Vector2 at)
    {
        var rows = _logs[player].Rows;
        var shown = System.Math.Min(rows.Count, VisibleRows);
        var panel = new Rect2(at, new Vector2(PanelWidth, 40 + shown * RowHeight));
        this.Card(panel, Look.Surface);

        var tag = new Rect2(at + new Vector2(12, 8), new Vector2(48, 22));
        this.Pill(tag, Look.Player(player));
        this.Text(tag, $"P{player + 1}", Look.UiBold, Look.UiSmall, Look.Text);

        for (var i = 0; i < shown; i++)
        {
            var row = rows[rows.Count - 1 - i];
            var y = at.Y + 36 + i * RowHeight;
            var count = new Rect2(at.X + 8, y, 40, RowHeight);
            this.Text(count, row.Frames.ToString(), Look.UiBold, Look.UiSmall, i == 0 ? Look.Accent : Look.TextMuted, HorizontalAlignment.Right);
            Glyphs.Draw(this, new Vector2(at.X + 58, y + (RowHeight - GlyphHeight) / 2), InputLog.Glyphs(row), GlyphHeight, onPanel: true);
        }
    }
}
