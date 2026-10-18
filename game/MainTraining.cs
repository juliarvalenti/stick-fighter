using Godot;
using StickFighter.Sim;
using StickFighter.Sim.Collision;
using StickFighter.Sim.Training;
using StickFighter.Training;
using StickFighter.UI;

namespace StickFighter;

// Training mode's part of the game loop: the session that drives the dummy, its menu, resets, the
// floor grid and the recording and playback status. Kept apart from Main.cs so the screen flow
// (Menus) and training don't edit the same lines.
//
// The training menu is a page of pause (its "Training menu" row, in training only), so the match
// is frozen under it like any pause page, and Back returns to pause. Quick keys in a training match:
//   F6 record P2: your controller drives the dummy for up to ten seconds; F6 again stops
//   F7 play the recording back on the dummy, or stop playing it
//   F8 reset to the position set in the menu
public partial class Main
{
    private TrainingSession? _training;
    private ulong _seed = MatchSeed;
    private bool _trainingMenuOpen;

    // A reset to a corner starts from a state no replay header describes, so F9 can't save it.
    private bool _replayFromMatchStart = true;

    private CanvasLayer? _trainingStatus;
    private string? _statusShown;

    // Who drives the match in training: the session, reading the players' controllers through
    // `device`.
    private IInputSource StartTraining(ulong seed, IInputSource device)
    {
        _seed = seed;
        _training = new TrainingSession(device, () => _state);
        _replayFromMatchStart = true;
        return _training;
    }

    // A training scenario: the session set up as the scenario says, reset, and driving the match
    // with the scenario's script as the players' controllers.
    private IInputSource StartTrainingScenario(System.Action<TrainingSession> setup, IInputSource script)
    {
        var session = (TrainingSession)StartTraining(_scenario!.Seed, script);
        setup(session);
        _state = session.Reset(_scenario.Seed, Stage.Training);
        _presenter.Begin();
        return session;
    }

    private void TrainingKey(InputEvent @event)
    {
        if (_training is null || _flow != Flow.Match || _paused is not null || @event is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        switch (key.PhysicalKeycode)
        {
            case Key.F6 when _training.Recording:
                _training.StopRecording();
                break;
            case Key.F6:
                _training.StartRecording();
                break;
            case Key.F7 when _training.Dummy.Action == DummyAction.Playback:
                _training.StopPlayback();
                break;
            case Key.F7:
                _training.StartPlayback();
                break;
            case Key.F8:
                ResetTraining();
                break;
        }
    }

    // The training menu, a pause page over the frozen match. Its views start from what's on now
    // (F2, F4 and F5 still work). Record and Reset go back to the match and do it; Back returns
    // to the pause menu.
    private void OpenTrainingMenu()
    {
        if (_training is null)
            return;
        _training.InputDisplay = _inputDisplay.Showing != InputDisplay.Mode.Off;
        _training.FrameData = _frameData.Visible;
        _training.Hitboxes = _debugView.Visible;
        _paused = UI.Screens.PauseScreen.Page.Menu;
        _trainingMenuOpen = true;

        var menu = new TrainingMenu(_training);
        menu.Chosen += item =>
        {
            ApplyTrainingViews();
            switch (item)
            {
                case "Record":
                    Unpause();
                    _training.StartRecording();
                    break;
                case "Reset":
                    Unpause();
                    ResetTraining();
                    break;
                default:
                    CloseTrainingMenu();
                    break;
            }
        };
        ShowScreen(menu);
    }

    // Back from the training menu: its views applied, and the pause menu again.
    private void CloseTrainingMenu()
    {
        ApplyTrainingViews();
        Pause(UI.Screens.PauseScreen.Page.Menu, UI.Screens.PauseScreen.TrainingMenuItem);
    }

    private void ApplyTrainingViews()
    {
        if (_training is null)
            return;
        _inputDisplay.Showing = _training.InputDisplay ? InputDisplay.Mode.Both : InputDisplay.Mode.Off;
        _frameData.Visible = _training.FrameData;
        _debugView.Visible = _training.Hitboxes;
    }

    // A fresh state where the menu says, with the same seed; the replay starts over with it.
    private void ResetTraining()
    {
        if (_training is null)
            return;
        _state = _training.Reset(_seed, Stage.Training);
        _replayFromMatchStart = _training.ResetTo == ResetPosition.Middle;
        if (_scenario is null)
        {
            _recorder = new Sim.Replays.ReplayRecorder(_seed, Stage.Training, _training, MatchMode.Training);
            _input = _recorder;
        }
        _driver = new TickDriver();
        _camera.Cut();
        _presenter.Begin();
    }

    // "Recording" or "Playback" in a toast at the bottom while either is on.
    private void PresentTraining()
    {
        var status = _training switch
        {
            { Recording: true } => "Recording P2: F6 to stop",
            { Dummy.Action: DummyAction.Playback } => "Playback: F7 to stop",
            _ => null,
        };
        if (status == _statusShown)
            return;
        _statusShown = status;
        _trainingStatus?.QueueFree();
        _trainingStatus = null;
        if (status is null)
            return;
        _trainingStatus = new CanvasLayer { Layer = 7 };
        var toast = new Toast(status, _training!.Recording ? Toast.Kind.Warning : Toast.Kind.Info) { Theme = UiTheme.Default };
        _trainingStatus.AddChild(toast);
        AddChild(_trainingStatus);
        toast.Position = new Vector2((1280 - toast.GetCombinedMinimumSize().X) / 2, 600);
    }

    // Training's spacing grid: a tick on the floor every character width (a pushbox across), from
    // the left wall.
    private void DrawTrainingGrid()
    {
        if (_training is not { Grid: true })
            return;
        var stage = _state.Stage;
        var floor = stage.Floor.ToFloat();
        var step = 2 * Body.PushHalfWidth.ToFloat();
        var tick = new Color(_ink, 0.35f);
        for (var x = stage.LeftWall.ToFloat(); x <= stage.RightWall.ToFloat(); x += step)
            DrawLine(new Vector2(x, floor - 14), new Vector2(x, floor + 14), tick, 2);
    }
}
