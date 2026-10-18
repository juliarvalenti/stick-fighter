using System;
using StickFighter.Sim;
using StickFighter.Sim.Training;

namespace StickFighter.Training;

// Training mode around the match: what drives P2 (the dummy, or player 1's controller while
// recording), the recording, where resets go, and the training views that are on. The match reads
// its input from here, so the dummy's buttons go through the input layer and into the replay like
// anyone's. No Godot here, so it's tested without the engine.
public sealed class TrainingSession : IInputSource
{
    private readonly IInputSource _device;
    private readonly Func<GameState> _state;
    private DummyAction _beforePlayback = DummyAction.Stand;

    // `device`: the players' controllers. `state`: the match as it stands before each frame.
    public TrainingSession(IInputSource device, Func<GameState> state)
    {
        _device = device;
        _state = state;
    }

    public Dummy Dummy { get; } = new();

    // Player 1's controller drives P2, and P2's buttons are recorded, until stopped or ten
    // seconds are up. P1 stands meanwhile.
    public bool Recording { get; private set; }

    public ResetPosition ResetTo { get; set; } = ResetPosition.Middle;

    // The training views, as the menu sets them; Main shows them.
    public bool InputDisplay { get; set; }
    public bool FrameData { get; set; }
    public bool Hitboxes { get; set; }
    public bool Grid { get; set; } = true;

    public FrameInput Read(int frame)
    {
        var device = _device.Read(frame);
        var state = _state();
        if (Recording)
        {
            Dummy.Recording.Add(device.Player1, state.Fighters[1].Facing);
            if (Dummy.Recording.Full)
                StopRecording();
            return new FrameInput(Buttons.None, device.Player1);
        }
        return new FrameInput(device.Player1, Dummy.Next(state, 1));
    }

    // A new recording replaces the last one.
    public void StartRecording()
    {
        Dummy.Recording.Clear();
        Recording = true;
    }

    // Stops recording and sets the dummy to play it back.
    public void StopRecording()
    {
        Recording = false;
        if (Dummy.Recording.Frames > 0)
            StartPlayback();
    }

    public void StartPlayback()
    {
        if (Dummy.Action != DummyAction.Playback)
            _beforePlayback = Dummy.Action;
        Dummy.Action = DummyAction.Playback;
    }

    // Back to what the dummy did before the playback.
    public void StopPlayback() => Dummy.Action = _beforePlayback;

    // A fresh training state at `ResetTo`. The dummy starts its setting over (a playback from the
    // top), and a recording in progress stops.
    public GameState Reset(ulong seed, in Stage stage)
    {
        Recording = false;
        Dummy.Restart();
        return TrainingReset.Fresh(seed, stage, MatchMode.Training, ResetTo);
    }
}
