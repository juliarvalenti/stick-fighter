using System.Collections.Generic;
using StickFighter.Sim.Replays;
using Xunit;

namespace StickFighter.Sim.Tests;

public class SyncTestTests
{
    // P1 walks right then left, a frame each, while P2 jabs now and then: no input is held for two
    // frames running, so a held (predicted) input never occurs in the real match.
    private static List<FrameInput> Alternating(int frames)
    {
        var inputs = new List<FrameInput>();
        for (var i = 0; i < frames; i++)
            inputs.Add(new FrameInput(i % 2 == 0 ? Buttons.Right : Buttons.Left, i % 7 == 3 ? Buttons.LightPunch : Buttons.None));
        return inputs;
    }

    [Fact]
    public void TheRealSimulationSurvivesRollback()
    {
        Assert.Null(SyncTest.Run(1, Stage.Training, Alternating(120)));
    }

    // A step that counts its calls outside GameState: resimulating a frame is another call, so the
    // count, and the state it touches, comes out different the first time a frame is rolled back.
    [Fact]
    public void StateOutsideGameStateIsCaught()
    {
        var calls = 0;
        Stepper leaky = (ref GameState state, in FrameInput input) =>
        {
            Simulation.Step(ref state, input);
            if (++calls % 5 == 0)
                state.Fighters[0].Health--;
        };

        var failure = SyncTest.Run(1, Stage.Training, Alternating(60), leaky);
        Assert.NotNull(failure);
        Assert.Equal(1, failure.Value.Depth);
        Assert.False(failure.Value.Mispredicted);

        // It says which field: health, one point apart, and nothing else.
        var full = Characters.Characters.Placeholder.Stats.Health;
        Assert.Equal(new[] { $"Fighters[0].Health: {full - 1} vs {full}" }, failure.Value.Differences);
    }

    [Fact]
    public void TheDiffNamesNestedFieldsAndPrintsFixAsNumbers()
    {
        var a = GameState.NewMatch(1);
        var b = a;
        b.Fighters[1].Hitstun = 6;
        b.Fighters[0].Position = new Numerics.FixVec2(a.Fighters[0].Position.X + Numerics.Fix.FromRatio(3, 2), a.Fighters[0].Position.Y);
        b.Inputs[1].Record(0, Buttons.LightPunch);

        Assert.Equal(
            new[]
            {
                "Inputs[1][0]: None vs LightPunch",
                $"Fighters[0].Position.X: {a.Fighters[0].Position.X} vs {b.Fighters[0].Position.X}",
                "Fighters[1].Hitstun: 0 vs 6",
            },
            StateDiff.Between(a, b));
        Assert.Empty(StateDiff.Between(a, a));
    }

    // GameState's own fields are found by reflection too, so a field added to it (here, the match)
    // is named in a desync without anyone listing it.
    [Fact]
    public void TheDiffNamesMatchFields()
    {
        var a = GameState.NewMatch(1, Stage.Training, MatchMode.Versus);
        var b = a;
        b.Match.TimerFrames--;
        b.Match.P2Wins = 1;
        b.Match.Phase = MatchPhase.RoundEnd;

        Assert.Equal(
            new[]
            {
                "Match.Phase: Intro vs RoundEnd",
                $"Match.TimerFrames: {a.Match.TimerFrames} vs {b.Match.TimerFrames}",
                "Match.P2Wins: 0 vs 1",
            },
            StateDiff.Between(a, b));
    }

    // A step that remembers, outside GameState, whether it ever saw the same input three frames
    // running. The real match never holds an input, so rolling back on the same inputs never sets it;
    // only a misprediction (a held input) does, and the correction then comes out different.
    [Fact]
    public void ATraceLeftByAMispredictionIsCaught()
    {
        var last = default(FrameInput);
        var run = 0;
        var tainted = false;
        Stepper leaky = (ref GameState state, in FrameInput input) =>
        {
            run = input.Player1 == last.Player1 && input.Player2 == last.Player2 ? run + 1 : 1;
            last = input;
            tainted |= run >= 3;
            Simulation.Step(ref state, input);
            if (tainted)
                state.Fighters[0].Health--;
        };

        var failure = SyncTest.Run(1, Stage.Training, Alternating(60), leaky);
        Assert.NotNull(failure);
        Assert.True(failure.Value.Mispredicted);
    }

    [Fact]
    public void AFailureSaysWhereAndHow()
    {
        var failure = new SyncTest.Failure(41, 3, true, 0xabc, 0xdef, new[] { "Fighters[1].Hitstun: 7 vs 6" });
        Assert.Equal(
            "frame 41 differs after rolling back 3 frames from a misprediction: expected 0000000000000abc, got 0000000000000def"
            + "\n  Fighters[1].Hitstun: 7 vs 6",
            failure.ToString());
    }
}
