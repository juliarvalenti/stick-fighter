using StickFighter.Sim.Characters;
using StickFighter.Sim.Combat;
using StickFighter.Sim.Numerics;
using Xunit;

namespace StickFighter.Sim.Tests;

public class HitstopTests
{
    private static Character Data => Characters.Characters.Placeholder;

    private static GameState Apart(int gap)
    {
        var state = GameState.NewMatch(1);
        state.Fighters[0].Position = new FixVec2(Fix.FromInt(1000), state.Fighters[0].Position.Y);
        state.Fighters[1].Position = new FixVec2(Fix.FromInt(1000 + gap), state.Fighters[1].Position.Y);
        return state;
    }

    // P1 does the move at point-blank while P2 holds `defence`, until it connects. Returns the state
    // just after the contact frame.
    private static GameState UntilContact(Buttons press, Buttons defence)
    {
        var state = Apart(60);
        Simulation.Step(ref state, new FrameInput(press, defence));
        while (state.FreezeUntil == 0 && state.Frame < 30)
            Simulation.Step(ref state, new FrameInput(press & Buttons.Down, defence));
        return state;
    }

    [Theory]
    [InlineData("5LP", Buttons.LightPunch, 8)]
    [InlineData("5MP", Buttons.MediumPunch, 10)]
    [InlineData("5HP", Buttons.HeavyPunch, 12)]
    [InlineData("2LK", Buttons.LightKick | Buttons.Down, 8)]
    public void AHitFreezesByStrength(string input, Buttons press, int frames)
    {
        Assert.Equal(frames, Data.FindMove(input)!.HitstopOnHit);
        var state = UntilContact(press, default);
        Assert.True(state.Fighters[1].Hitstun > 0);
        Assert.Equal(state.Frame, state.FreezeFrom); // from the frame after the contact
        Assert.Equal(frames, state.FreezeUntil - state.FreezeFrom);
    }

    [Theory]
    [InlineData(Buttons.LightPunch, 6)]
    [InlineData(Buttons.MediumPunch, 8)]
    [InlineData(Buttons.HeavyPunch, 10)]
    public void ABlockFreezesForLess(Buttons press, int frames)
    {
        var state = UntilContact(press, Buttons.Right);
        Assert.True(state.Fighters[1].Blockstun > 0);
        Assert.Equal(frames, state.FreezeUntil - state.FreezeFrom);
    }

    // Nothing moves while frozen: positions, action frames and stun all hold; only the frame count
    // and the recorded input move on.
    [Fact]
    public void BothFightersHoldStillWhileFrozen()
    {
        var state = UntilContact(Buttons.HeavyPunch, default);
        var atContact = state;
        for (var i = 0; i < state.FreezeUntil - state.FreezeFrom; i++)
        {
            Simulation.Step(ref state, new FrameInput(Buttons.Left, Buttons.Right));
            for (var p = 0; p < GameState.PlayerCount; p++)
            {
                Assert.Equal(atContact.Fighters[p].Position, state.Fighters[p].Position);
                Assert.Equal(atContact.Fighters[p].ActionFrame, state.Fighters[p].ActionFrame);
                Assert.Equal(atContact.Fighters[p].Hitstun, state.Fighters[p].Hitstun);
            }
        }
        Assert.Equal(atContact.Frame + 12, state.Frame);

        // The freeze is over: the next frame moves on.
        Simulation.Step(ref state, default);
        Assert.Equal(atContact.Fighters[1].Hitstun - 1, state.Fighters[1].Hitstun);
    }

    // A press on the first frame of a 12-frame freeze is 13 frames old when the freeze ends, far
    // past the 5-frame buffer, but the buffer counts only unfrozen frames, so it comes out right then.
    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void APressDuringHitstopComesOutRightAfter(int frozenFramesBeforePress)
    {
        var state = Apart(400);
        Simulation.Step(ref state, default);
        Hitstop.Start(ref state, 12);
        Simulation.Step(ref state, default); // the frame that started the freeze
        for (var i = 0; i < 12; i++)
        {
            var press = i == frozenFramesBeforePress ? Buttons.MediumPunch : default;
            Simulation.Step(ref state, new FrameInput(press, default));
            Assert.Equal(-1, state.Fighters[0].Move);
        }

        Simulation.Step(ref state, default);
        Assert.Equal(Data.FindMove("5MP"), Data.Moves[state.Fighters[0].Move]);
        Assert.Equal(0, state.Fighters[0].ActionFrame);
    }

    // Unfrozen frames still age a press. P1 whiffs a heavy kick, a freeze comes early in it, and
    // P1 presses MP during the freeze: P1 is free far more than 5 unfrozen frames later, so it's gone.
    [Fact]
    public void APressDuringHitstopStillExpires()
    {
        var state = Apart(400);
        var hk = Data.FindMove("5HK")!;
        Simulation.Step(ref state, new FrameInput(Buttons.HeavyKick, default));
        Hitstop.Start(ref state, 4);
        Simulation.Step(ref state, default);
        Simulation.Step(ref state, new FrameInput(Buttons.MediumPunch, default)); // frozen
        while (state.Fighters[0].Action == (int)FighterAction.Attack && state.Frame < 60)
            Simulation.Step(ref state, default);
        Assert.Equal(hk.TotalFrames + 4, state.Frame - 1); // the whiff, delayed by the freeze
        Assert.Equal(-1, state.Fighters[0].Move);
    }

    // A trade freezes both for the longer hitstop: P1's heavy punch and P2's jab land on the same frame.
    [Fact]
    public void ATradeFreezesForTheLongerHitstop()
    {
        var state = Apart(60);
        var hp = Data.FindMove("5HP")!;
        var jab = Data.FindMove("5LP")!;
        var p2Press = hp.FirstActiveFrame - jab.FirstActiveFrame;
        for (var f = 0; f <= hp.FirstActiveFrame; f++)
            Simulation.Step(ref state, new FrameInput(f == 0 ? Buttons.HeavyPunch : default, f == p2Press ? Buttons.LightPunch : default));

        Assert.True(state.Fighters[0].Hitstun > 0 && state.Fighters[1].Hitstun > 0, "both should be hit");
        Assert.Equal(hp.HitstopOnHit, state.FreezeUntil - state.FreezeFrom);
    }
}
