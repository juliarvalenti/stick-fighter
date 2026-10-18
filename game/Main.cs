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
    private TickDriver _driver = new();

    // Every match records its replay; F9 saves it to user://replays/.
    private ReplayRecorder _recorder = new(MatchSeed, Stage.Training, new DeviceInputSource());
    private IInputSource _input = null!;
    private DebugOverlay _overlay = null!;
    private DebugView _debugView = null!;
    private Training.InputDisplay _inputDisplay = null!;
    private Training.FrameDataPanel _frameData = null!;

    // The menus' flow: press start, the main menu, character select (versus; training skips it), the
    // match, its results, and from there a rematch, select or the title. Back steps one screen back
    // anywhere. Pause is an overlay on the match that stops the simulation outright. Menus never
    // touch the match except to start one with its settings and seed.
    private enum Flow
    {
        PressStart,
        Menu,
        Select,
        Match,
        Results,
    }

    private const string PauseAction = "pause";

    private Flow _flow = Flow.Match;
    private int _matchNumber;
    private Menus.MatchSettings _settings = Menus.MatchSettings.Training;
    private static readonly Color DefaultP1 = new(0.85f, 0.2f, 0.25f);
    private static readonly Color DefaultP2 = new(0.2f, 0.4f, 0.85f);

    // Paused: the match is frozen (no ticks run) under the pause screens. Local only: online play
    // can't stop the other player's clock, so it will need its own rule (a mutual pause, or none).
    private UI.Screens.PauseScreen.Page? _paused;

    // Flow scenarios count display frames, and check the paused match doesn't move.
    private int _displayFrame;
    private string? _heldAction;
    private (int Frame, ulong Checksum, int SimFrame)? _pauseCheck;
    private MatchPresenter _presenter = null!;
    private FX.EffectsView _effects = null!;
    private CanvasLayer _screens = null!;
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

        // The training input display (F4) and frame data (F5), screen-anchored over the stage.
        var inputLayer = new CanvasLayer { Layer = 6 };
        _inputDisplay = new Training.InputDisplay();
        inputLayer.AddChild(_inputDisplay);
        _frameData = new Training.FrameDataPanel();
        inputLayer.AddChild(_frameData);
        AddChild(inputLayer);

        _camera = new StageCamera();
        AddChild(_camera);
        _backdrop = new StageBackdrop();
        AddChild(_backdrop);
        _presenter = new MatchPresenter();
        AddChild(_presenter);
        _effects = new FX.EffectsView();
        _effects.Attach(_camera);
        AddChild(_effects);
        _screens = new CanvasLayer { Layer = 10 };
        AddChild(_screens);

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

        EnsurePauseAction();
        UI.Screens.SelectScreen.EnsureColorAction();

        if (Arg(args, "--scenario") is { } name)
            StartScenario(name, args);
        else
            ShowPressStart();
        PresentView(0);
    }

    private void ShowScreen(UI.Screens.Screen? screen)
    {
        foreach (var child in _screens.GetChildren())
            child.QueueFree();
        if (screen is not null)
            _screens.AddChild(screen);
    }

    // The title, waiting for start (or confirm), over the fighters standing ready.
    private void ShowPressStart()
    {
        _flow = Flow.PressStart;
        _paused = null;
        _presenter.Visible = false;
        _state = GameState.NewMatch(MatchSeed);
        _camera.Cut();
        _effects.Clear();
        ShowScreen(new UI.Screens.TitleScreen());
    }

    // The main menu. Options arrive with the options task.
    private void ShowMenu()
    {
        _flow = Flow.Menu;
        var menu = new UI.Screens.TitleScreen(menu: true);
        menu.Chosen += item =>
        {
            switch (item)
            {
                case "Versus":
                    ShowSelect();
                    break;
                case "Training":
                    StartMatch(Menus.MatchSettings.Training); // training goes straight in
                    break;
                case "Quit":
                    GetTree().Quit();
                    break;
            }
        };
        ShowScreen(menu);
    }

    // Character select for versus; then the match. One stage, so no stage select yet.
    private void ShowSelect()
    {
        _flow = Flow.Select;
        var select = new UI.Screens.SelectScreen(interactive: true, seed: NextSeed);
        select.Done += (p1, p2) => StartMatch(new Menus.MatchSettings(MatchMode.Versus, p1, p2));
        select.Back += ShowMenu;
        ShowScreen(select);
    }

    // The seed the next match will have: each match gets the next, so a session's matches are each
    // reproducible from the match number.
    private ulong NextSeed => MatchSeed + (ulong)_matchNumber;

    // A new match from the start positions, recording its own replay.
    private void StartMatch(Menus.MatchSettings settings)
    {
        var seed = NextSeed;
        _matchNumber++;
        _settings = settings;
        _playerColors = settings.Colors(DefaultP1, DefaultP2);
        _state = GameState.NewMatch(seed, Stage.Training, settings.Mode);
        _recorder = new ReplayRecorder(seed, Stage.Training, new DeviceInputSource(), settings.Mode);

        // A flow scenario plays its scripted players instead of the devices.
        _input = _scenario is { Flow: true } ? new Scenarios.Source(_scenario) : _recorder;
        _driver = new TickDriver();
        _camera.Cut();
        _effects.Clear();
        _presenter.Begin(new[] { settings.P1Color, settings.P2Color });
        _flow = Flow.Match;
        _paused = null;
        ShowScreen(null);
    }

    // The match is decided: results over the stage, rematch focused. Rematch skips select (the
    // same characters and colors, the next seed).
    private void ShowResults()
    {
        _flow = Flow.Results;
        var results = new UI.Screens.ResultsScreen(_presenter.Results(_state), new[] { _settings.P1Color, _settings.P2Color });
        results.Chosen += item =>
        {
            switch (item)
            {
                case "Rematch":
                    StartMatch(_settings);
                    break;
                case "Character select":
                    ShowSelect();
                    break;
                case "Title":
                    ShowPressStart();
                    break;
            }
        };
        ShowScreen(results);
    }

    // Pause, or a page of it. The sim stops while any is up (see _Process).
    private void Pause(UI.Screens.PauseScreen.Page page, string focus = UI.Screens.PauseScreen.Resume)
    {
        _paused = page;
        var screen = new UI.Screens.PauseScreen(page, focus);
        screen.Chosen += item =>
        {
            switch (item)
            {
                case UI.Screens.PauseScreen.Resume:
                    Unpause();
                    break;
                case UI.Screens.PauseScreen.MoveListItem:
                    Pause(UI.Screens.PauseScreen.Page.MoveList);
                    break;
                case UI.Screens.PauseScreen.CommandListItem:
                    Pause(UI.Screens.PauseScreen.Page.Commands);
                    break;
                case UI.Screens.PauseScreen.Quit:
                    Pause(UI.Screens.PauseScreen.Page.ConfirmQuit);
                    break;
                case UI.Screens.PauseScreen.Stay:
                    Pause(UI.Screens.PauseScreen.Page.Menu, UI.Screens.PauseScreen.Quit);
                    break;
                case UI.Screens.PauseScreen.QuitConfirmed:
                    _paused = null;
                    _presenter.Visible = false;
                    ShowMenu();
                    break;
            }
        };
        ShowScreen(screen);
    }

    private void Unpause()
    {
        _paused = null;
        _pauseCheck = null;
        ShowScreen(null);
    }

    // Back, one screen: from a pause page to the pause menu, from pause to the match, from the menu
    // to the title. Select handles its own (undoing P1's pick first).
    private void GoBack()
    {
        switch (_flow)
        {
            case Flow.Match when _paused is UI.Screens.PauseScreen.Page.MoveList:
                Pause(UI.Screens.PauseScreen.Page.Menu, UI.Screens.PauseScreen.MoveListItem);
                break;
            case Flow.Match when _paused is UI.Screens.PauseScreen.Page.Commands:
                Pause(UI.Screens.PauseScreen.Page.Menu, UI.Screens.PauseScreen.CommandListItem);
                break;
            case Flow.Match when _paused is UI.Screens.PauseScreen.Page.ConfirmQuit:
                Pause(UI.Screens.PauseScreen.Page.Menu, UI.Screens.PauseScreen.Quit);
                break;
            case Flow.Match when _paused is not null:
                Unpause();
                break;
            case Flow.Menu:
                ShowPressStart();
                break;
        }
    }

    // Start on a pad, Escape or P on the keyboard.
    private static void EnsurePauseAction()
    {
        if (InputMap.HasAction(PauseAction))
            return;
        InputMap.AddAction(PauseAction);
        InputMap.ActionAddEvent(PauseAction, new InputEventJoypadButton { ButtonIndex = JoyButton.Start });
        InputMap.ActionAddEvent(PauseAction, new InputEventKey { PhysicalKeycode = Key.Escape });
        InputMap.ActionAddEvent(PauseAction, new InputEventKey { PhysicalKeycode = Key.P });
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

        _state = GameState.NewMatch(_scenario.Seed, Stage.Training, _scenario.Versus ? MatchMode.Versus : MatchMode.Sandbox);
        if (_scenario.Setup is { } setup)
            _state = setup(_state);
        if (_scenario.Versus)
            _presenter.Begin();
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

        // A flow scenario plays the real menus from the title.
        if (_scenario.Flow)
            ShowPressStart();
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

        if (_scenario is { Flow: true } flow)
            PressScenarioActions(flow);

        var start = Stopwatch.GetTimestamp();
        var elapsed = _pacer.ToMicroseconds((long)System.Math.Round(delta * 1_000_000_000));

        // Only a match (and its results) runs the simulation: on the menus the fighters stand still,
        // and paused, no ticks run at all, so the match is exactly as it was when play resumes.
        // (Not calling the driver also keeps it from banking time to catch up on afterwards.)
        var running = _flow is Flow.Match or Flow.Results && _paused is null;
        var ticks = running ? _driver.Update(ref _state, elapsed, _input) : 0;
        var simMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if (_scenario is { Flow: true })
            CheckPauseHolds();

        _overlay.Record(delta * 1000, simMs, ticks, _driver.DroppedTicks, _state.Frame);
        PresentView(ticks);
        _debugView.Present(_state);
        _inputDisplay.Present(_state);
        _frameData.Present(_state);
        _presenter.Present(_state);
        _effects.Present(_state);
        if (_flow == Flow.Match && _paused is null && _state.Match is { Phase: MatchPhase.MatchEnd, PhaseFrame: >= UI.MatchHud.ResultsDelay })
            ShowResults();
        _clock?.At(_state.Frame);
        QueueRedraw();

        _scenarioRun?.AfterStep(_scenario is { Flow: true } ? _displayFrame : _state.Frame, ticks);
        _displayFrame++;
    }

    // A flow scenario's scripted UI actions: pressed on their display frame, released on the next.
    private void PressScenarioActions(Scenario scenario)
    {
        if (_heldAction is { } held)
        {
            Input.ParseInputEvent(new InputEventAction { Action = held, Pressed = false });
            _heldAction = null;
        }
        if (scenario.Actions?.TryGetValue(_displayFrame, out var action) == true)
        {
            Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = true });
            _heldAction = action;
        }
    }

    // The pause test: 60 display frames into a pause, the match must be exactly as it was (same
    // frame, same checksum).
    private void CheckPauseHolds()
    {
        if (_paused is null)
        {
            _pauseCheck = null;
            return;
        }
        if (_pauseCheck is not { } check)
        {
            _pauseCheck = (_displayFrame, _state.Checksum(), _state.Frame);
            return;
        }
        if (_displayFrame - check.Frame == 60)
        {
            _scenarioRun?.Check(_state.Checksum() == check.Checksum && _state.Frame == check.SimFrame,
                $"60 frames paused: sim frame {check.SimFrame} -> {_state.Frame}, checksum {check.Checksum:x16} -> {_state.Checksum():x16}");
        }
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
        {
            SaveReplay();
            return;
        }

        // Pause (Start, Escape or P) first: in a match it pauses, and while paused it steps back
        // like cancel (Escape is both), so one press never does two things.
        if (@event.IsActionPressed(PauseAction))
        {
            if (_flow == Flow.PressStart)
                ShowMenu();
            else if (_flow == Flow.Match && _paused is null && _scenario is null or { Flow: true })
                Pause(UI.Screens.PauseScreen.Page.Menu);
            else if (_paused is not null)
                GoBack();
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed("ui_accept") && _flow == Flow.PressStart)
        {
            ShowMenu();
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed("ui_cancel"))
        {
            GoBack();
            GetViewport().SetInputAsHandled();
        }
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
