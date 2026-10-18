using System.Diagnostics;
using Godot;
using StickFighter.Sim;

namespace StickFighter;

// Runs the match: reads devices through the input layer, advances the simulation at a fixed
// 60 ticks per second, and draws the latest state. Placeholder fighters until moves exist.
public partial class Main : Node2D
{
    private static readonly Color Ink = new(0.1f, 0.1f, 0.12f);
    private static readonly Color[] PlayerColors = { new(0.85f, 0.2f, 0.25f), new(0.2f, 0.4f, 0.85f) };
    private const float Stroke = 10f;
    private const float FloorY = 600f;

    private GameState _state = GameState.NewMatch(1);
    private readonly TickDriver _driver = new();
    private readonly DeviceInputSource _input = new();
    private DebugOverlay _overlay = null!;
    private FramePacer _pacer = new(0);
    private int _screen = -1;

    public override void _Ready()
    {
        UpdatePacer();

        _overlay = new DebugOverlay();
        AddChild(_overlay);

        // `--smoke` (after `--`) runs a few frames and exits 0: the headless check in CI.
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--smoke") >= 0)
        {
            for (var i = 0; i < 10; i++)
                Simulation.Step(ref _state, default);
            GD.Print($"{GameInfo.Name}: smoke ok ({GameInfo.TicksPerSecond} ticks/s, frame {_state.Frame})");
            GetTree().CallDeferred(SceneTree.MethodName.Quit, 0);
        }
    }

    public override void _Process(double delta)
    {
        UpdatePacer();
        var start = Stopwatch.GetTimestamp();
        var elapsed = _pacer.ToMicroseconds((long)System.Math.Round(delta * 1_000_000_000));
        var ticks = _driver.Update(ref _state, elapsed, _input);
        var simMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        _overlay.Record(delta * 1000, simMs, ticks, _driver.DroppedTicks, _state.Frame);
        QueueRedraw();
    }

    // Snap frame deltas to the current screen's refresh rate; a new screen means a new rate.
    private void UpdatePacer()
    {
        var screen = DisplayServer.WindowGetCurrentScreen();
        if (screen == _screen)
            return;
        _screen = screen;
        var hz = DisplayServer.ScreenGetRefreshRate(screen); // -1 when unknown, e.g. headless
        _pacer = new FramePacer(hz > 0 ? (long)System.Math.Round(hz * 1000) : 0);
    }

    public override void _Draw()
    {
        var size = GetViewportRect().Size;
        DrawLine(new Vector2(0, FloorY), new Vector2(size.X, FloorY), Ink, 4);

        for (var i = 0; i < GameState.PlayerCount; i++)
            DrawFighter(_state.Fighters[i], PlayerColors[i]);
    }

    private void DrawFighter(in FighterState fighter, Color color)
    {
        var feet = fighter.Position.ToVector2();
        var f = fighter.Facing;
        var hip = feet + new Vector2(0, -110);
        var neck = hip + new Vector2(0, -120);

        Limb(hip, feet + new Vector2(-35 * f, 0), color);
        Limb(hip, feet + new Vector2(35 * f, 0), color);
        Limb(hip, neck, color);
        Limb(neck, neck + new Vector2(-40 * f, 70), color);
        Limb(neck, neck + new Vector2(70 * f, 20), color);
        DrawCircle(neck + new Vector2(0, -36), 28, color);
    }

    private void Limb(Vector2 from, Vector2 to, Color color)
    {
        DrawLine(from, to, color, Stroke);
        DrawCircle(from, Stroke / 2, color);
        DrawCircle(to, Stroke / 2, color);
    }

    // Live play: devices are sampled once per simulation tick.
    private sealed class DeviceInputSource : IInputSource
    {
        public FrameInput Read(int frame) => DeviceInput.Read();
    }
}
