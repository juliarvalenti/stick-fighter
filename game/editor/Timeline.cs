using System.Collections.Generic;
using Godot;
using StickFighter.Style;
using StickFighter.UI;

namespace StickFighter.Editor;

// A move's frames as a strip of cells, colored by phase as on the pose sheets (startup yellow,
// active red, recovery blue), the current frame raised and ringed, and a dot under any frame a
// check flags. Click or drag to scrub; with focus, left and right step a frame.
public partial class Timeline : Control
{
    private static readonly Color Startup = new(0.95f, 0.75f, 0.1f);
    private static readonly Color Hit = new(0.9f, 0.1f, 0.1f);
    private static readonly Color Recovery = new(0.25f, 0.45f, 0.9f);

    public int Frames { get; set; } = 1;
    public int FirstActive { get; set; }
    public int Active { get; set; } = 1;
    public int Frame { get; private set; }
    public HashSet<int> Flagged { get; } = new();

    public event System.Action<int>? Scrubbed;

    public Timeline()
    {
        FocusMode = FocusModeEnum.All;
        CustomMinimumSize = new Vector2(0, 52);
        FocusEntered += QueueRedraw;
        FocusExited += QueueRedraw;
    }

    public void Show(int frame)
    {
        Frame = Mathf.Clamp(frame, 0, Frames - 1);
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        int? to = @event switch
        {
            InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } b => At(b.Position.X),
            InputEventMouseMotion { ButtonMask: MouseButtonMask.Left } m => At(m.Position.X),
            _ when @event.IsActionPressed("ui_left", allowEcho: true) => Frame - 1,
            _ when @event.IsActionPressed("ui_right", allowEcho: true) => Frame + 1,
            _ => null,
        };
        if (to is not { } frame)
            return;
        AcceptEvent();
        if (@event is InputEventMouseButton)
            GrabFocus();
        Show(frame);
        Scrubbed?.Invoke(Frame);
    }

    private int At(float x) => (int)(x / Cell);

    private float Cell => Size.X / Mathf.Max(Frames, 1);

    public override void _Draw()
    {
        var cell = Cell;
        for (var f = 0; f < Frames; f++)
        {
            var color = f < FirstActive ? Startup : f < FirstActive + Active ? Hit : Recovery;
            var current = f == Frame;
            var rect = new Rect2(f * cell + 1, current ? 0 : 8, cell - 2, current ? 32 : 18);
            this.Card(rect, current ? color : new Color(color, 0.75f), 4);
            if (Flagged.Contains(f))
                DrawCircle(new Vector2(f * cell + cell / 2, 44), 4, Look.Danger);
        }
        var at = new Rect2(Frame * cell + 1, 0, cell - 2, 32);
        if (HasFocus())
            DrawStyleBox(UiTheme.FocusRing(UiTheme.Card(Colors.Transparent, 0)), at);
    }
}
