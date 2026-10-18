using System;
using Xunit;

namespace StickFighter.Sim.Tests;

public class InputTests
{
    [Theory]
    [InlineData(Buttons.Left | Buttons.Right, Buttons.None)]
    [InlineData(Buttons.Left | Buttons.Right | Buttons.Down, Buttons.Down)]
    [InlineData(Buttons.Up | Buttons.Down, Buttons.None)]
    [InlineData(Buttons.Up | Buttons.Down | Buttons.Left, Buttons.Left)]
    [InlineData(Buttons.Directions | Buttons.HeavyPunch, Buttons.HeavyPunch)]
    [InlineData(Buttons.Right | Buttons.Down, Buttons.Right | Buttons.Down)]
    public void SocdCleaningNeutralisesOpposites(Buttons raw, Buttons expected)
    {
        Assert.Equal(expected, raw.Cleaned());
    }

    [Theory]
    [InlineData(Buttons.None, 1, 5)]
    [InlineData(Buttons.Right, 1, 6)]
    [InlineData(Buttons.Right, -1, 4)]
    [InlineData(Buttons.Left, 1, 4)]
    [InlineData(Buttons.Left, -1, 6)]
    [InlineData(Buttons.Down | Buttons.Right, 1, 3)]
    [InlineData(Buttons.Down | Buttons.Right, -1, 1)]
    [InlineData(Buttons.Up | Buttons.Left, 1, 7)]
    [InlineData(Buttons.Up | Buttons.Left, -1, 9)]
    [InlineData(Buttons.Up, -1, 8)]
    [InlineData(Buttons.Down, 1, 2)]
    public void NumpadIsRelativeToFacing(Buttons buttons, int facing, int expected)
    {
        Assert.Equal(expected, Numpad.Direction(buttons, facing));
    }

    [Theory]
    [InlineData(1, -1, -1)]
    [InlineData(5, 0, 0)]
    [InlineData(6, 1, 0)]
    [InlineData(9, 1, 1)]
    [InlineData(8, 0, 1)]
    public void NumpadSplitsIntoAxes(int direction, int horizontal, int vertical)
    {
        Assert.Equal(horizontal, Numpad.Horizontal(direction));
        Assert.Equal(vertical, Numpad.Vertical(direction));
    }

    [Fact]
    public void PressIsSeenForTheWholeWindowThenExpires()
    {
        var history = new InputHistory();
        Record(ref history, 0, Buttons.None, Buttons.LightPunch, Buttons.LightPunch, Buttons.None, Buttons.None, Buttons.None);

        // Pressed on frame 1.
        Assert.True(history.Pressed(Buttons.LightPunch, now: 1, window: 1));
        Assert.False(history.Pressed(Buttons.LightPunch, now: 2, window: 1)); // held, not a new press
        Assert.True(history.Pressed(Buttons.LightPunch, now: 5, window: 5));   // frames 1..5
        Assert.False(history.Pressed(Buttons.LightPunch, now: 5, window: 4));  // frames 2..5
    }

    [Fact]
    public void ReleaseEdgesSupportNegativeEdge()
    {
        var history = new InputHistory();
        Record(ref history, 0, Buttons.HeavyKick, Buttons.HeavyKick, Buttons.None);

        Assert.True(history.Released(Buttons.HeavyKick, now: 2, window: 1));
        Assert.False(history.Released(Buttons.HeavyKick, now: 1, window: 1));
        Assert.Equal(Buttons.HeavyKick, history.ReleasedOn(2, now: 2));
    }

    [Fact]
    public void FirstFrameOfTheMatchCountsAsAPress()
    {
        var history = new InputHistory();
        history.Record(0, Buttons.MediumPunch);
        Assert.True(history.Pressed(Buttons.MediumPunch, now: 0, window: 1));
    }

    [Fact]
    public void OldFramesFallOutOfTheBuffer()
    {
        var history = new InputHistory();
        history.Record(0, Buttons.LightKick);
        for (var frame = 1; frame <= InputHistory.Depth; frame++)
            history.Record(frame, Buttons.None);

        var now = InputHistory.Depth;
        Assert.Equal(Buttons.None, history.At(0, now));
        Assert.Equal(Buttons.None, history.At(now + 1, now));
        Assert.False(history.Pressed(Buttons.LightKick, now, InputHistory.MaxWindow));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(InputHistory.Depth)]
    public void WindowsOutsideTheBufferAreRejected(int window)
    {
        var history = new InputHistory();
        Assert.Throws<ArgumentOutOfRangeException>(() => history.Pressed(Buttons.LightPunch, 20, window));
    }

    [Fact]
    public void StepRecordsCleanedInputForBothPlayers()
    {
        var state = GameState.NewMatch(1);
        Simulation.Step(ref state, new FrameInput(Buttons.Left | Buttons.Right | Buttons.HeavyPunch, Buttons.Down));

        Assert.Equal(Buttons.HeavyPunch, state.Inputs[0].At(0, 0));
        Assert.Equal(Buttons.Down, state.Inputs[1].At(0, 0));
    }

    [Fact]
    public void ForwardWalksTowardTheOpponentOnEitherSide()
    {
        var state = GameState.NewMatch(1);
        var p1Start = state.Fighters[0].Position.X;
        var p2Start = state.Fighters[1].Position.X;

        // P1 faces right and P2 faces left, so each player's forward is a different screen direction.
        Simulation.Step(ref state, new FrameInput(Buttons.Right, Buttons.Left));

        Assert.True(state.Fighters[0].Position.X > p1Start);
        Assert.True(state.Fighters[1].Position.X < p2Start);
    }

    [Fact]
    public void FacingFlipsWhenFightersCrossOver()
    {
        var state = GameState.NewMatch(1);
        var walkIn = new FrameInput(Buttons.Right, Buttons.Left);
        // No pushboxes yet, so they walk through each other.
        while (state.Fighters[0].Position.X <= state.Fighters[1].Position.X)
            Simulation.Step(ref state, walkIn);

        Assert.Equal(-1, state.Fighters[0].Facing);
        Assert.Equal(1, state.Fighters[1].Facing);

        // The same screen direction is now "back" for P1.
        Assert.Equal(4, Numpad.Direction(Buttons.Right, state.Fighters[0].Facing));
    }

    private static void Record(ref InputHistory history, int firstFrame, params Buttons[] frames)
    {
        for (var i = 0; i < frames.Length; i++)
            history.Record(firstFrame + i, frames[i]);
    }
}
