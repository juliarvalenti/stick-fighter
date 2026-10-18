using System.Diagnostics;
using Godot;
using StickFighter.Sim;
using StickFighter.Sim.Characters;
using StickFighter.Sim.Collision;
using StickFighter.Sim.Numerics;
using StickFighter.Sim.Replays;

namespace StickFighter;

// Runs the match: reads devices through the input layer, advances the simulation at a fixed
// 60 ticks per second, and draws the latest state. Placeholder fighters until moves exist.
public partial class Main : Node2D
{
    private static readonly Color Ink = new(0.1f, 0.1f, 0.12f);
    private static readonly Color[] PlayerColors = { new(0.85f, 0.2f, 0.25f), new(0.2f, 0.4f, 0.85f) };
    private const float Stroke = 10f;

    private const ulong MatchSeed = 1;

    // Scenario runs assume a 60 Hz display, whatever the CI machine reports.
    private const long ScenarioRefreshMilliHz = 60_000;

    private GameState _state = GameState.NewMatch(MatchSeed);
    private readonly TickDriver _driver = new();

    // Every match records its replay; F9 saves it to user://replays/.
    private readonly ReplayRecorder _recorder = new(MatchSeed, new DeviceInputSource());
    private IInputSource _input = null!;
    private DebugOverlay _overlay = null!;
    private DebugView _debugView = null!;
    private FramePacer _pacer = new(0);
    private int _screen = -1;

    private Scenario? _scenario;
    private ScenarioRun? _scenarioRun;
    private Sim.Numerics.Rng _jitter = Sim.Numerics.Rng.Create(2024);

    public override void _Ready()
    {
        _input = _recorder;
        UpdatePacer();

        _overlay = new DebugOverlay();
        AddChild(_overlay);
        _debugView = new DebugView();
        AddChild(_debugView);

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
        _overlay.Visible = _scenario.Overlay;
        _overlay.Deterministic = true;

        // A pose sheet replaces the match picture with every frame of one move.
        if (_scenario.PoseSheet is { } input)
        {
            var move = Characters.Placeholder.FindMove(input)!;
            AddChild(new PoseSheet(move) { ZIndex = 20 });
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

        var start = Stopwatch.GetTimestamp();
        var elapsed = _pacer.ToMicroseconds((long)System.Math.Round(delta * 1_000_000_000));
        var ticks = _driver.Update(ref _state, elapsed, _input);
        var simMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        _overlay.Record(delta * 1000, simMs, ticks, _driver.DroppedTicks, _state.Frame);
        _debugView.Present(_state);
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

    public override void _Draw()
    {
        var size = GetViewportRect().Size;
        var floor = Stage.Floor.ToFloat();
        DrawLine(new Vector2(0, floor), new Vector2(size.X, floor), Ink, 4);

        for (var i = 0; i < GameState.PlayerCount; i++)
            DrawFighter(_state.Fighters[i], PlayerColors[i]);
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
