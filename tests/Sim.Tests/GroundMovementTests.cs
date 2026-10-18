using StickFighter.Sim.Characters;
using StickFighter.Sim.Collision;
using StickFighter.Sim.Numerics;
using Xunit;

namespace StickFighter.Sim.Tests;

public class GroundMovementTests
{
    private static Character Data => Characters.Characters.Placeholder;
    private static int Transition => Data.Stats.CrouchTransitionFrames;

    // Fighters at the given feet positions, both standing and facing each other.
    private static GameState At(int p1X, int p2X)
    {
        var state = GameState.NewMatch(1);
        state.Fighters[0].Position = new FixVec2(Fix.FromInt(p1X), state.Fighters[0].Position.Y);
        state.Fighters[1].Position = new FixVec2(Fix.FromInt(p2X), state.Fighters[1].Position.Y);
        state.Fighters[0].Facing = p1X < p2X ? 1 : -1;
        state.Fighters[1].Facing = -state.Fighters[0].Facing;
        return state;
    }

    private static FighterAction ActionOf(in GameState state, int player) => (FighterAction)state.Fighters[player].Action;

    private static void Run(ref GameState state, int frames, FrameInput input)
    {
        for (var i = 0; i < frames; i++)
            Simulation.Step(ref state, input);
    }

    [Theory]
    [InlineData(Buttons.Down)]                // 2
    [InlineData(Buttons.Down | Buttons.Left)] // 1, down-back for P1
    [InlineData(Buttons.Down | Buttons.Right)] // 3, down-forward for P1
    public void DownCrouchesInPlaceThroughTheTransition(Buttons down)
    {
        var state = At(600, 1000);
        var x = state.Fighters[0].Position.X;
        for (var i = 0; i < Transition; i++)
        {
            Simulation.Step(ref state, new FrameInput(down, default));
            Assert.Equal(FighterAction.CrouchDown, ActionOf(state, 0));
        }
        Simulation.Step(ref state, new FrameInput(down, default));
        Assert.Equal(FighterAction.Crouch, ActionOf(state, 0));
        Assert.Equal(x, state.Fighters[0].Position.X);
    }

    [Fact]
    public void TheTransitionIsTallButCountsAsCrouching()
    {
        var state = At(600, 1000);
        Simulation.Step(ref state, new FrameInput(Buttons.Down, default));
        Assert.Equal(FighterAction.CrouchDown, ActionOf(state, 0));
        Assert.Equal(Data.PushHeight(FighterAction.Stand), Body.PushHeight(state.Fighters[0]));

        // A press during it starts the crouching normal.
        Simulation.Step(ref state, new FrameInput(Buttons.Down | Buttons.LightPunch, default));
        Assert.Equal("2LP", Data.Moves[state.Fighters[0].Move].Input);
    }

    [Fact]
    public void ReleasingDownStandsAtOnce()
    {
        var state = At(600, 1000);
        Run(ref state, Transition + 3, new FrameInput(Buttons.Down, default));
        Simulation.Step(ref state, default);
        Assert.Equal(FighterAction.Stand, ActionOf(state, 0));
    }

    [Fact]
    public void ACrouchingNormalEndsStraightInTheCrouch()
    {
        var state = At(600, 1000);
        Simulation.Step(ref state, new FrameInput(Buttons.Down | Buttons.LightPunch, default));
        var move = Data.Moves[state.Fighters[0].Move];
        Run(ref state, move.TotalFrames - 1, new FrameInput(Buttons.Down, default));
        Simulation.Step(ref state, new FrameInput(Buttons.Down, default));
        Assert.Equal(FighterAction.Crouch, ActionOf(state, 0));
    }

    [Fact]
    public void CrouchingLowersThePushbox()
    {
        var state = At(600, 1000);
        Run(ref state, Transition + 1, new FrameInput(Buttons.Down, default));
        Assert.Equal(FighterAction.Crouch, ActionOf(state, 0));
        Assert.True(Body.PushHeight(state.Fighters[0]) < Body.PushHeight(state.Fighters[1]));
        Assert.Equal(Data.PushHeight(FighterAction.Crouch), Body.PushBox(state.Fighters[0]).Max.Y - Body.PushBox(state.Fighters[0]).Min.Y);
    }

    [Fact]
    public void FreeFightersTurnOnTheSameFrameWhenTheyCross()
    {
        var state = At(600, 1000);
        (state.Fighters[0].Position, state.Fighters[1].Position) = (state.Fighters[1].Position, state.Fighters[0].Position);
        Simulation.Step(ref state, default);
        Assert.Equal(-1, state.Fighters[0].Facing);
        Assert.Equal(1, state.Fighters[1].Facing);
    }

    [Fact]
    public void AFighterMidMoveDoesNotTurnUntilItEnds()
    {
        var state = At(600, 1000);
        Simulation.Step(ref state, new FrameInput(Buttons.HeavyKick, default));
        var move = Data.Moves[state.Fighters[0].Move];

        // P2 is put behind P1 while P1's kick plays: P2 turns at once, P1 only when the kick ends.
        state.Fighters[1].Position = new FixVec2(Fix.FromInt(300), state.Fighters[1].Position.Y);
        for (var i = 1; i < move.TotalFrames; i++)
        {
            Simulation.Step(ref state, default);
            Assert.Equal(1, state.Fighters[0].Facing);
            Assert.Equal(1, state.Fighters[1].Facing);
        }
        Simulation.Step(ref state, default);
        Assert.Equal(-1, state.Fighters[0].Facing);
    }

    [Fact]
    public void AFighterInHitstunDoesNotTurn()
    {
        var state = At(600, 1000);
        state.Fighters[1].Hitstun = 10;
        state.Fighters[1].Action = (int)FighterAction.Hitstun;
        state.Fighters[1].Position = new FixVec2(Fix.FromInt(300), state.Fighters[1].Position.Y);
        Simulation.Step(ref state, default);
        Assert.Equal(-1, state.Fighters[1].Facing); // still facing where P1 was
        Assert.Equal(-1, state.Fighters[0].Facing); // P1 is free and turns
    }

    [Fact]
    public void AWalkerPushesAStandingFighterAtTheShareOfItsSpeed()
    {
        var state = At(600, 660); // pushboxes touching
        Run(ref state, 5, new FrameInput(Buttons.Right, default));
        var p1 = state.Fighters[0].Position.X;
        var p2 = state.Fighters[1].Position.X;
        Run(ref state, 10, new FrameInput(Buttons.Right, default));

        var step = Data.Stats.WalkForward * Data.Stats.PushShare;
        Assert.Equal(p2 - p1, state.Fighters[1].Position.X - state.Fighters[0].Position.X); // still touching
        Assert.InRange((state.Fighters[1].Position.X - p2 - step * 10).Raw, -10, 10);
    }

    [Fact]
    public void WalkingInTogetherStopsBoth()
    {
        var state = At(600, 660);
        var p1 = state.Fighters[0].Position.X;
        var p2 = state.Fighters[1].Position.X;
        Run(ref state, 20, new FrameInput(Buttons.Right, Buttons.Left));
        Assert.Equal(p1, state.Fighters[0].Position.X);
        Assert.Equal(p2, state.Fighters[1].Position.X);
    }

    [Fact]
    public void PushingIsTheSameWithThePlayersSwapped()
    {
        // P1 walks into a standing P2, and the mirror: P2 walks into a standing P1.
        var a = At(600, 700);
        var b = At(700, 600);
        Run(ref a, 30, new FrameInput(Buttons.Right, default));
        Run(ref b, 30, new FrameInput(default, Buttons.Right));
        Assert.Equal(a.Fighters[0].Position, b.Fighters[1].Position);
        Assert.Equal(a.Fighters[1].Position, b.Fighters[0].Position);
    }

    [Fact]
    public void SplitOverlapFollowsWhoWalkedIn()
    {
        var share = Fix.FromRatio(2, 5);
        var step = Fix.FromInt(10);

        // Only the left one walked in: it gives up 60% of the overlap, the right one is pushed 40%
        // (to a few raw units: 2/5 isn't exact in 16.16).
        var (leftBack, rightBack) = Pushing.SplitOverlap(step, step, Fix.Zero, share);
        Assert.InRange((leftBack - Fix.FromInt(6)).Raw, -8, 8);
        Assert.InRange((rightBack - Fix.FromInt(4)).Raw, -8, 8);
        Assert.True(leftBack + rightBack >= step);

        // Mirrored.
        Assert.Equal((rightBack, leftBack), Pushing.SplitOverlap(step, Fix.Zero, step, share));

        // Both alike, or nobody: even.
        Assert.Equal((Fix.FromInt(5), Fix.FromInt(5)), Pushing.SplitOverlap(step, step, step, share));
        Assert.Equal((Fix.FromInt(5), Fix.FromInt(5)), Pushing.SplitOverlap(step, Fix.Zero, Fix.Zero, share));
    }
}
