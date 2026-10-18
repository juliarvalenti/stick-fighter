using Godot;

namespace StickFighter;

// Frame-time overlay, toggled with F3: sim ms, render ms, ticks, rollback frames and a
// frame-time graph. Presentation only; nothing here feeds back into the simulation.
public partial class DebugOverlay : CanvasLayer
{
    private const int GraphFrames = 180;
    private const float GraphHeight = 80f;
    private const float GraphMaxMs = 50f;

    private readonly float[] _frameMs = new float[GraphFrames];
    private int _next;
    private double _simMs;
    private int _ticks;
    private long _dropped;
    private int _frame;
    private Panel _panel = null!;
    private Label _label = null!;

    public override void _Ready()
    {
        Layer = 100;
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);

        _panel = new Panel { Position = new Vector2(12, 12), Size = new Vector2(380, 210) };
        _label = new Label { Position = new Vector2(10, 6) };
        _label.AddThemeFontSizeOverride("font_size", 14);
        _panel.AddChild(_label);
        _panel.Draw += DrawGraph;
        AddChild(_panel);
        Visible = false;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.F3 })
            Visible = !Visible;
    }

    public void Record(double frameMs, double simMs, int ticks, long dropped, int frame)
    {
        _frameMs[_next] = (float)frameMs;
        _next = (_next + 1) % GraphFrames;
        _simMs = simMs;
        _ticks = ticks;
        _dropped = dropped;
        _frame = frame;

        if (!Visible)
            return;

        var viewport = GetViewport().GetViewportRid();
        var renderCpu = RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport);
        var renderGpu = RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport);
        _label.Text =
            $"FPS {Engine.GetFramesPerSecond():0}   frame {frameMs:0.0} ms\n" +
            $"sim {_simMs:0.000} ms   ticks {_ticks}   dropped {_dropped}\n" +
            $"render cpu {renderCpu:0.00} ms   gpu {renderGpu:0.00} ms\n" +
            $"rollback frames 0   sim frame {_frame}";
        _panel.QueueRedraw();
    }

    private void DrawGraph()
    {
        var origin = new Vector2(10, 196);
        var width = _panel.Size.X - 20;
        var step = width / (GraphFrames - 1);

        // Reference line at one 60 Hz frame.
        var budgetY = origin.Y - 16.67f / GraphMaxMs * GraphHeight;
        _panel.DrawLine(new Vector2(origin.X, budgetY), new Vector2(origin.X + width, budgetY), Colors.DimGray, 1);

        var points = new Vector2[GraphFrames];
        for (var i = 0; i < GraphFrames; i++)
        {
            var ms = Mathf.Min(_frameMs[(_next + i) % GraphFrames], GraphMaxMs);
            points[i] = new Vector2(origin.X + i * step, origin.Y - ms / GraphMaxMs * GraphHeight);
        }
        _panel.DrawPolyline(points, Colors.LimeGreen, 1.5f);
    }
}
