using StickFighter.Sim;
using StickFighter.Sim.Training;
using StickFighter.Training;
using Xunit;

namespace StickFighter.Presentation.Tests;

public class TrainingSessionTests
{
    // The players' controllers: player 1 holds whatever's set, player 2 mashes heavy kick (which
    // training ignores: the dummy is P2).
    private sealed class Pads : IInputSource
    {
        public Buttons Player1 { get; set; }

        public FrameInput Read(int frame) => new(Player1, Buttons.HeavyKick);
    }

    // A training match, a session reading it, and a step that runs the match a frame on the
    // session's input and returns the state.
    private static (TrainingSession Session, Pads Pads, System.Func<GameState> Step) Make()
    {
        var pads = new Pads();
        var state = GameState.NewMatch(1, Stage.Training, MatchMode.Training);
        var session = new TrainingSession(pads, () => state);
        GameState Step()
        {
            Simulation.Step(ref state, session.Read(state.Frame));
            return state;
        }
        return (session, pads, Step);
    }

    [Fact]
    public void Player1PlaysAndTheDummyIsP2()
    {
        var (session, pads, _) = Make();
        session.Dummy.Action = DummyAction.Crouch;
        pads.Player1 = Buttons.Right | Buttons.LightPunch;
        Assert.Equal(new FrameInput(Buttons.Right | Buttons.LightPunch, Buttons.Down), session.Read(0));
    }

    // Recording: player 1's controller drives P2 and is recorded, P1 stands; stopping sets the
    // dummy to play it back, and stopping that goes back to what the dummy did before.
    [Fact]
    public void RecordingDrivesP2ThenPlaysBack()
    {
        var (session, pads, _) = Make();
        session.Dummy.Action = DummyAction.BlockAll;
        session.StartRecording();
        pads.Player1 = Buttons.Left;
        for (var i = 0; i < 10; i++)
            Assert.Equal(new FrameInput(Buttons.None, Buttons.Left), session.Read(i));
        Assert.Equal(10, session.Dummy.Recording.Frames);

        session.StopRecording();
        Assert.False(session.Recording);
        Assert.Equal(DummyAction.Playback, session.Dummy.Action);
        pads.Player1 = Buttons.MediumPunch;
        Assert.Equal(new FrameInput(Buttons.MediumPunch, Buttons.Left), session.Read(10)); // P2 facing left: left as recorded

        session.StopPlayback();
        Assert.Equal(DummyAction.BlockAll, session.Dummy.Action);
    }

    [Fact]
    public void RecordingStopsByItselfAtTenSeconds()
    {
        var (session, pads, _) = Make();
        session.StartRecording();
        pads.Player1 = Buttons.Up;
        for (var i = 0; i < InputRecording.MaxFrames + 5; i++)
            session.Read(i);
        Assert.False(session.Recording);
        Assert.Equal(InputRecording.MaxFrames, session.Dummy.Recording.Frames);
        Assert.Equal(DummyAction.Playback, session.Dummy.Action);
    }

    [Fact]
    public void AResetGoesWhereTheMenuSaysAndStartsThePlaybackOver()
    {
        var (session, pads, _) = Make();
        session.StartRecording();
        pads.Player1 = Buttons.LightPunch;
        session.Read(0);
        pads.Player1 = Buttons.None;
        session.Read(1);
        session.StopRecording();
        session.Read(2); // the playback's first frame, LP

        session.ResetTo = ResetPosition.RightCorner;
        var reset = session.Reset(7, Stage.Training);
        Assert.True(reset.StateEquals(TrainingReset.Fresh(7, Stage.Training, MatchMode.Training, ResetPosition.RightCorner)));
        Assert.Equal(Buttons.LightPunch, session.Read(0).Player2); // from the top again
    }

    [Fact]
    public void AResetStopsARecordingInProgress()
    {
        var (session, _, _) = Make();
        session.StartRecording();
        session.Reset(1, Stage.Training);
        Assert.False(session.Recording);
    }

    [Fact]
    public void TheDummyThroughTheSessionGuardsInTheMatch()
    {
        var (session, pads, step) = Make();
        session.Dummy.Action = DummyAction.BlockAll;
        GameState state = default;
        var blocked = false;
        for (var f = 0; f < 120; f++)
        {
            pads.Player1 = f < 40 ? Buttons.Right : f == 40 ? Buttons.MediumPunch : Buttons.None;
            state = step();
            blocked |= state.Fighters[1].Blockstun > 0;
        }
        Assert.True(blocked);
        Assert.Equal(0, state.Fighters[1].Hitstun);
    }
}
