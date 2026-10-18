using StickFighter.Sim.Characters;
using StickFighter.Sim.Combat;
using StickFighter.Sim.Numerics;
using Xunit;

namespace StickFighter.Sim.Tests;

public class ThrowsTests
{
    private static Character Data => Characters.Characters.Placeholder;
    private static Move Throw => Data.FindMove("LP+LK")!;
    private const Buttons LpLk = Buttons.LightPunch | Buttons.LightKick;

    // P1 on the left, P2 on the right at the given gap. P2's back is Right, P1's is Left.
    private static GameState Apart(int gap)
    {
        var state = GameState.NewMatch(1);
        state.Fighters[0].Position = new FixVec2(Fix.FromInt(1000), state.Fighters[0].Position.Y);
        state.Fighters[1].Position = new FixVec2(Fix.FromInt(1000 + gap), state.Fighters[1].Position.Y);
        return state;
    }

    private static void Step(ref GameState state, Buttons p1 = default, Buttons p2 = default) =>
        Simulation.Step(ref state, new FrameInput(p1, p2));

    private static int Gap(in GameState state) => (state.Fighters[1].Position.X - state.Fighters[0].Position.X).RoundToInt();

    // The throw is active on its 3rd and 4th frames, so pressed on frame 0 it connects on frame 2.
    private static int ConnectFrame => Throw.FirstActiveFrame;

    [Fact]
    public void LpPlusLkStartsTheThrow()
    {
        var state = Apart(300);
        Step(ref state, LpLk);
        Assert.Equal(Normals.Throw, state.Fighters[0].Move);
    }

    [Fact]
    public void LkAFrameAfterLpStillThrows()
    {
        var state = Apart(300);
        Step(ref state, Buttons.LightPunch);
        Step(ref state, LpLk);
        Assert.Equal(Normals.Throw, state.Fighters[0].Move);
        Assert.Equal(0, state.Fighters[0].ActionFrame);
    }

    [Fact]
    public void TwoFramesApartIsAJabThenNothing()
    {
        var state = Apart(300);
        Step(ref state, Buttons.LightPunch);
        Step(ref state, Buttons.LightPunch);
        Step(ref state, LpLk);
        Assert.Equal(Data.FindMove("5LP"), Data.Moves[state.Fighters[0].Move]);
    }

    [Fact]
    public void CrouchingLpPlusLkIsCrouchingLightKick()
    {
        var state = Apart(300);
        Step(ref state, LpLk | Buttons.Down);
        Assert.Equal(Data.FindMove("2LK"), Data.Moves[state.Fighters[0].Move]);
    }

    [Fact]
    public void AThrowGrabsThenKnocksDownInFront()
    {
        var state = Apart(60);
        Step(ref state, LpLk);
        while (state.Frame <= ConnectFrame)
            Step(ref state);
        Assert.Equal((int)FighterAction.Throwing, state.Fighters[0].Action);
        Assert.Equal((int)FighterAction.Thrown, state.Fighters[1].Action);

        // The thrown animation plays, then the throw lands: damage and a knockdown.
        while (state.Frame <= ConnectFrame + Data.Stats.TechFrame)
            Step(ref state);
        var p2 = state.Fighters[1];
        Assert.Equal(Data.Stats.Health - Throw.Damage, p2.Health);
        Assert.True(p2.KnockedDown);
        Assert.Equal((int)FighterAction.Knockdown, p2.Action);
        Assert.Equal(Data.Stats.ThrowDistance.RoundToInt(), Gap(state));
    }

    [Fact]
    public void HoldingBackThrowsBehind()
    {
        var state = Apart(60);
        var throwerX = state.Fighters[0].Position.X;
        for (var i = 0; i <= ConnectFrame + Data.Stats.TechFrame; i++)
            Step(ref state, Buttons.Left | (i == 0 ? LpLk : default));
        Assert.True(state.Fighters[1].KnockedDown);
        Assert.Equal(throwerX - Data.Stats.ThrowDistance, state.Fighters[1].Position.X);
        Assert.Equal(-1, state.Fighters[0].Facing); // the fighters swapped sides
    }

    [Fact]
    public void TheKnockdownLastsItsFramesThenTheFighterStands()
    {
        var state = Apart(60);
        Step(ref state, LpLk);
        var landed = ConnectFrame + Data.Stats.TechFrame;
        while (state.Frame <= landed)
            Step(ref state);
        for (var i = 1; i < Data.Stats.KnockdownFrames; i++)
        {
            Step(ref state);
            Assert.True(state.Fighters[1].KnockedDown, $"{i} frames after the landing");
        }
        // Up and acting on the next frame.
        Step(ref state, p2: Buttons.Left);
        Assert.False(state.Fighters[1].KnockedDown);
        Assert.Equal((int)FighterAction.WalkForward, state.Fighters[1].Action);
    }

    // P1 throws P2 and then throws again to arrive on the frames P2 gets up. P2 can't be thrown on
    // its first WakeupThrowInvulnerable frames.
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void AFighterGettingUpCantBeThrownAtOnce(int framesAfterGettingUp, bool thrown)
    {
        var state = Apart(60);
        Step(ref state, LpLk);
        var up = ConnectFrame + Data.Stats.TechFrame + Data.Stats.KnockdownFrames;
        var connect = up + framesAfterGettingUp;
        while (state.Frame < connect - ConnectFrame - 1)
            Step(ref state);
        // Walk-up is Movement's business: put P1 back in range.
        state.Fighters[0].Position = state.Fighters[1].Position - new FixVec2(Fix.FromInt(60), Fix.Zero);
        Step(ref state);
        Step(ref state, LpLk);
        while (state.Frame <= connect)
            Step(ref state);
        Assert.Equal(thrown, state.Fighters[1].Action == (int)FighterAction.Thrown);
    }

    [Fact]
    public void AWhiffCostsTheWholeMove()
    {
        var state = Apart(300);
        Step(ref state, LpLk);
        for (var i = 1; i < Throw.TotalFrames; i++)
        {
            Step(ref state);
            Assert.Equal((int)FighterAction.Attack, state.Fighters[0].Action);
        }
        Step(ref state);
        Assert.Equal((int)FighterAction.Stand, state.Fighters[0].Action);
        Assert.Equal(Throw.Startup - 1 + Throw.Active + 20, Throw.TotalFrames);
    }

    [Fact]
    public void AWhiffedThrowIsPunished()
    {
        var state = Apart(110);
        Step(ref state, LpLk);
        while (state.Frame < Throw.FirstActiveFrame + Throw.Active)
            Step(ref state);
        Assert.Equal(-1, state.Fighters[1].Grab); // just out of range: it whiffed

        // P2 answers with a standing heavy kick, which lands in the throw's recovery.
        Step(ref state, p2: Buttons.HeavyKick);
        while (state.Fighters[0].Hitstun == 0 && state.Frame < Throw.TotalFrames)
            Step(ref state);
        Assert.True(state.Fighters[0].Hitstun > 0, "P1 recovered before the punish landed");
        Assert.Equal(Data.Stats.Health - Data.FindMove("5HK")!.Damage, state.Fighters[0].Health);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AFighterInStunCantBeThrown(bool blockstun)
    {
        var state = Apart(60);
        if (blockstun)
        {
            state.Fighters[1].Blockstun = 10;
            state.Fighters[1].Action = (int)FighterAction.BlockStand;
        }
        else
        {
            state.Fighters[1].Hitstun = 10;
            state.Fighters[1].Action = (int)FighterAction.Hitstun;
        }
        Step(ref state, LpLk, Buttons.Right);
        for (var i = 1; i < Throw.TotalFrames; i++)
            Step(ref state, p2: Buttons.Right);
        Assert.Equal(-1, state.Fighters[1].Grab);
        Assert.Equal(Data.Stats.Health, state.Fighters[1].Health);
    }

    [Fact]
    public void AThrowGrabsAFighterInTheMiddleOfAStrike()
    {
        var state = Apart(60);
        Step(ref state, LpLk, Buttons.HeavyPunch);
        while (state.Frame <= ConnectFrame)
            Step(ref state);
        Assert.Equal((int)FighterAction.Thrown, state.Fighters[1].Action);
    }

    [Fact]
    public void SameFrameThrowsBothTech()
    {
        var state = Apart(60);
        Step(ref state, LpLk, LpLk);
        while (state.Frame <= ConnectFrame)
            Step(ref state);
        var p1 = state.Fighters[0];
        var p2 = state.Fighters[1];
        Assert.Equal((int)FighterAction.Stand, p1.Action);
        Assert.Equal((int)FighterAction.Stand, p2.Action);
        Assert.Equal(Data.Stats.Health, p1.Health);
        Assert.Equal(Data.Stats.Health, p2.Health);
        Assert.Equal(60 + 2 * Data.Stats.TechSeparation.RoundToInt(), Gap(state));
        // Symmetric: both moved the same distance from where they stood.
        Assert.Equal(Fix.FromInt(1000) - p1.Position.X, p2.Position.X - Fix.FromInt(1060));
    }

    // The tech window. P1 jabs into P2's guard, then throws so the throw arrives on P2's first
    // frame out of blockstun (the meaty throw). P2 presses LP+LK `techAt` frames after the throw
    // connects (negative: before, buffered during blockstun, so it comes out as P2's own throw and
    // is in startup when P1's lands). The window is the TechWindow frames ending TechFrame frames
    // after the connect.
    [Theory]
    [InlineData(-4, true)] // the first frame of the window
    [InlineData(-5, false)]
    [InlineData(3, true)]
    [InlineData(7, true)] // the last
    [InlineData(8, false)]
    public void ATechInTheWindowBreaksTheThrow(int techAt, bool teched)
    {
        var free = FirstFrameOutOfBlockstun();
        var connect = free;
        var state = MeatyThrow(throwAt: connect - ConnectFrame, techAt: connect + techAt, until: connect + Data.Stats.TechFrame);
        var p2 = state.Fighters[1];
        if (teched)
        {
            Assert.False(p2.KnockedDown);
            Assert.Equal(Data.Stats.Health, p2.Health);
            Assert.Equal((int)FighterAction.Stand, state.Fighters[0].Action);
            Assert.Equal((int)FighterAction.Stand, p2.Action);

            // The tech used up P2's press: no throw comes out of the buffer afterwards.
            for (var i = 0; i < Normals.BufferWindow; i++)
                Step(ref state, p2: Buttons.Right);
            Assert.NotEqual((int)FighterAction.Attack, state.Fighters[1].Action);
        }
        else
        {
            Assert.True(p2.KnockedDown);
            Assert.Equal(Data.Stats.Health - Throw.Damage, p2.Health);
        }
    }

    [Fact]
    public void TheMeatyThrowConnectsOnTheFirstFrameOutOfBlockstun()
    {
        var connect = FirstFrameOutOfBlockstun();
        var state = MeatyThrow(throwAt: connect - ConnectFrame, techAt: null, until: connect);
        Assert.Equal((int)FighterAction.Thrown, state.Fighters[1].Action);
        Assert.Equal(0, state.Fighters[1].Grab);

        // A frame earlier P2 spends its last blockstun frame: a throw active then doesn't grab yet,
        // and catches P2 on its second active frame.
        var early = MeatyThrow(throwAt: connect - ConnectFrame - 1, techAt: null, until: connect - 1);
        Assert.True(Throw.IsActive(early.Fighters[0].ActionFrame));
        Assert.Equal(-1, early.Fighters[1].Grab);
        Step(ref early, p2: Buttons.Right);
        Assert.Equal(0, early.Fighters[1].Grab);
    }

    // P2 guards a P1 jab at point-blank; the first frame P2 can act again.
    private static int FirstFrameOutOfBlockstun()
    {
        var state = Apart(60);
        Step(ref state, Buttons.LightPunch, Buttons.Right);
        var blocked = false;
        while (state.Frame < 60)
        {
            var stunned = state.Fighters[1].Blockstun > 0;
            blocked |= stunned;
            if (blocked && !stunned)
                return state.Frame;
            Step(ref state, p2: Buttons.Right);
        }
        throw new Xunit.Sdk.XunitException("P2 never blocked");
    }

    // P1 jabs on frame 0 and presses LP+LK on throwAt; P2 guards throughout and presses LP+LK on
    // techAt. Runs through frame `until`.
    private static GameState MeatyThrow(int throwAt, int? techAt, int until)
    {
        var state = Apart(60);
        while (state.Frame <= until)
        {
            var frame = state.Frame;
            var p1 = frame == 0 ? Buttons.LightPunch : frame == throwAt ? LpLk : default;
            var p2 = Buttons.Right | (frame == techAt ? LpLk : default);
            Step(ref state, p1, p2);
        }
        return state;
    }
}
