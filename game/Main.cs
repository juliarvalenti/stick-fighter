using System.Diagnostics;
using Godot;
using StickFighter.Sim;
using StickFighter.Sim.Characters;
using StickFighter.Sim.Collision;
using StickFighter.Sim.Numerics;
using StickFighter.Sim.Replays;
using StickFighter.Style;

namespace StickFighter;

// Runs the match: reads devices through the input layer, advances the simulation at a fixed
// 60 ticks per second, and draws the latest state. Placeholder fighters until moves exist.
public partial class Main : Node2D
{
    private const float Stroke = 10f;

    // The style frames draw in the style guide's palette; the game switches to it once they're signed off.
    private Color _ink = new(0.1f, 0.1f, 0.12f);
    private Color[] _playerColors = { new(0.85f, 0.2f, 0.25f), new(0.2f, 0.4f, 0.85f) };

    private const ulong MatchSeed = 1;

    // Scenario runs assume a 60 Hz display, whatever the CI machine reports.
    private const long ScenarioRefreshMilliHz = 60_000;

    private GameState _state = GameState.NewMatch(MatchSeed);
    private readonly TickDriver _driver = new();

    // Every match records its replay; F9 saves it to user://replays/.
    private readonly ReplayRecorder _recorder = new(MatchSeed, Stage.Training, new DeviceInputSource());
    private IInputSource _input = null!;
    private DebugOverlay _overlay = null!;
    private DebugView _debugView = null!;
    private StageCamera _camera = null!;
    private StageBackdrop _backdrop = null!;
    private FramePacer _pacer = new(0);
    private int _screen = -1;

    private Scenario? _scenario;
    private ScenarioRun? _scenarioRun;
    private IScenarioClock? _clock;
    private Sim.Numerics.Rng _jitter = Sim.Numerics.Rng.Create(2024);

    public override void _Ready()
    {
        _input = _recorder;
        UpdatePacer();

        _overlay = new DebugOverlay();
        AddChild(_overlay);
        _debugView = new DebugView();
        AddChild(_debugView);
        _camera = new StageCamera();
        AddChild(_camera);
        _backdrop = new StageBackdrop();
        AddChild(_backdrop);

        var args = OS.GetCmdlineUserArgs();

        // `--smoke` (after `--`) runs a few frames and exits 0: the headless check in CI.
        if (System.Array.IndexOf(args, "--smoke") >= 0)
        {
            for (var i = 0; i < 10; i++)
                Simulation.Step(ref _state, default);
            GD.Print($"{GameInfo.Name}: smoke ok ({GameInfo.TicksPerSecond} ticks/s, frame {_state.Frame})");
            GetTree().CallDeferred(SceneTree.MethodName.Quit, 0);
            return;
        }

        // `--list-scenarios` prints one `scenario-name <name>` line each, for CI to loop over.
        if (System.Array.IndexOf(args, "--list-scenarios") >= 0)
        {
            foreach (var scenario in Scenarios.All)
                GD.Print($"scenario-name {scenario.Name}");
            GetTree().CallDeferred(SceneTree.MethodName.Quit, 0);
            return;
        }

        if (Arg(args, "--scenario") is { } name)
            StartScenario(name, args);
        PresentView(0);
    }

    // The camera first, then the backdrop, which scrolls by where the camera ended up.
    private void PresentView(int ticks)
    {
        _camera.Present(_state, ticks);
        _backdrop.Present(_state.Stage, _camera.Position.X);
    }

    private void StartScenario(string name, string[] args)
    {
        _scenario = Scenarios.Find(name);
        if (_scenario is null)
        {
            GD.PrintErr($"FAIL unknown scenario '{name}'");
            GetTree().CallDeferred(SceneTree.MethodName.Quit, 1);
            return;
        }

        _state = GameState.NewMatch(_scenario.Seed);
        if (_scenario.Setup is { } setup)
            _state = setup(_state);
        _input = new Scenarios.Source(_scenario);
        _pacer = new FramePacer(ScenarioRefreshMilliHz);
        _debugView.Visible = _scenario.Boxes;
        _debugView.ShowChecksum = false;
        _overlay.Visible = _scenario.Overlay;
        _overlay.Deterministic = true;

        // A pose sheet replaces the match picture with every frame of one move. It's on its own
        // layer, so it's fixed to the screen whatever the camera does.
        if (_scenario.PoseSheet is { } input)
        {
            var move = Characters.Placeholder.FindMove(input)!;
            var layer = new CanvasLayer { Layer = 20 };
            layer.AddChild(new PoseSheet(move));
            AddChild(layer);
        }

        if (_scenario.StyleFrame is { } frame)
        {
            _ink = Look.Ink;
            _playerColors = new[] { Look.P1, Look.P2 };
            RenderingServer.SetDefaultClearColor(Look.Paper);

            // Screen-anchored UI over the stage, so it stays put whatever the camera does.
            var layer = new CanvasLayer { Layer = 5 };
            var control = frame();
            _clock = control as IScenarioClock;
            layer.AddChild(control);
            AddChild(layer);
        }

        _scenarioRun = new ScenarioRun(
            _scenario,
            Arg(args, "--out") ?? "user://scenarios",
            Arg(args, "--golden"),
            System.Array.IndexOf(args, "--update-goldens") >= 0);
        AddChild(_scenarioRun);
        GD.Print($"scenario {_scenario.Name}: seed {_scenario.Seed}, {_scenario.Frames} frames");
    }

    private static string? Arg(string[] args, string key)
    {
        var i = System.Array.IndexOf(args, key);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    public override void _Process(double delta)
    {
        UpdatePacer();

        // The pacing scenario adds ±0.3 ms of vsync-like jitter to each frame (run with --fixed-fps 60).
        if (_scenario is { PacingCheck: true })
            delta += _jitter.NextInt(-300, 300) / 1_000_000.0;

        // Some scenarios set the stage up again between steps (the harness, not gameplay). The
        // camera cuts to a reset rather than panning across the stage to it.
        if (_scenario?.EachFrame is { } eachFrame)
        {
            var before = _state;
            _state = eachFrame(_state);
            if (!_state.StateEquals(before))
                _camera.Cut();
        }

        var start = Stopwatch.GetTimestamp();
        var elapsed = _pacer.ToMicroseconds((long)System.Math.Round(delta * 1_000_000_000));
        var ticks = _driver.Update(ref _state, elapsed, _input);
        var simMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        _overlay.Record(delta * 1000, simMs, ticks, _driver.DroppedTicks, _state.Frame);
        PresentView(ticks);
        _debugView.Present(_state);
        _clock?.At(_state.Frame);
        QueueRedraw();

        _scenarioRun?.AfterStep(_state, ticks);
    }

    // Snap frame deltas to the current screen's refresh rate; a new screen means a new rate.
    private void UpdatePacer()
    {
        if (_scenario is not null)
            return;
        var screen = DisplayServer.WindowGetCurrentScreen();
        if (screen == _screen)
            return;
        _screen = screen;
        var hz = DisplayServer.ScreenGetRefreshRate(screen); // -1 when unknown, e.g. headless
        _pacer = new FramePacer(hz > 0 ? (long)System.Math.Round(hz * 1000) : 0);
    }

    // Drawn in stage coordinates; the camera decides which part is on screen.
    public override void _Draw()
    {
        var stage = _state.Stage;
        var floor = stage.Floor.ToFloat();
        DrawLine(new Vector2(stage.LeftWall.ToFloat(), floor), new Vector2(stage.RightWall.ToFloat(), floor), _ink, 4);

        for (var i = 0; i < GameState.PlayerCount; i++)
            DrawFighter(_state.Fighters[i], _playerColors[i]);
    }

    private void DrawFighter(FighterState fighter, Color color)
    {
        // Drawn from the same posed joints the hurtboxes come from, so what you see is what gets hit.
        var joints = Body.Pose(fighter);
        for (var limb = Limb.Spine; limb <= Limb.FrontShin; limb++)
        {
            if (limb == Limb.Neck)
                continue;
            var (from, to) = Skeleton.Ends(limb);
            DrawBone(joints[from].ToVector2(), joints[to].ToVector2(), color);
        }
        DrawCircle(joints[Joint.Head].ToVector2(), Body.HeadRadius.ToFloat(), color);
    }

    private void DrawBone(Vector2 from, Vector2 to, Color color)
    {
        DrawLine(from, to, color, Stroke);
        DrawCircle(from, Stroke / 2, color);
        DrawCircle(to, Stroke / 2, color);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.F9 })
            SaveReplay();
    }

    // Saves the match so far. Copy the file into replays/ to make it a CI test.
    private void SaveReplay()
    {
        DirAccess.MakeDirRecursiveAbsolute("user://replays");
        var path = $"user://replays/match-{Time.GetDatetimeStringFromSystem().Replace(':', '-')}.sfr";
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        file.StoreString(_recorder.Build().Write());
        GD.Print($"Saved replay ({_recorder.Frames} frames) to {ProjectSettings.GlobalizePath(path)}");
    }

    // Live play: devices are sampled once per simulation tick.
    private sealed class DeviceInputSource : IInputSource
    {
        public FrameInput Read(int frame) => DeviceInput.Read();
    }
}
