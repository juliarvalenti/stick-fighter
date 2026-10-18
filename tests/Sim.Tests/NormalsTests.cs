using System;
using StickFighter.Sim.Characters;
using StickFighter.Sim.Combat;
using StickFighter.Sim.Numerics;
using Xunit;

namespace StickFighter.Sim.Tests;

public class NormalsTests
{
    private static Character Data => Characters.Characters.Placeholder;

    // Fighters at the given gap, both standing, facing each other.
    private static GameState Apart(int gap)
    {
        var state = GameState.NewMatch(1);
        state.Fighters[0].Position = new FixVec2(Fix.FromInt(600), state.Fighters[0].Position.Y);
        state.Fighters[1].Position = new FixVec2(Fix.FromInt(600 + gap), state.Fighters[1].Position.Y);
        return state;
    }

    private static string? CurrentMove(in FighterState f) =>
        f.Action == (int)FighterAction.Attack ? Data.Moves[f.Move].Input : null;

    [Theory]
    [InlineData(Buttons.LightPunch, Buttons.None, "5LP")]
    [InlineData(Buttons.HeavyKick, Buttons.None, "5HK")]
    [InlineData(Buttons.MediumPunch, Buttons.Down, "2MP")]
    [InlineData(Buttons.LightKick, Buttons.Down | Buttons.Right, "2LK")]
    [InlineData(Buttons.LightPunch | Buttons.HeavyPunch, Buttons.None, "5HP")] // the heavier button wins
    public void APressStartsTheMatchingNormal(Buttons button, Buttons direction, string expected)
    {
        var state = Apart(400);
        Simulation.Step(ref state, new FrameInput(button | direction, default));
        Assert.Equal(expected, CurrentMove(state.Fighters[0]));
        Assert.Equal(0, state.Fighters[0].ActionFrame);
    }

    [Fact]
    public void AMovePlaysForItsWholeLengthThenEnds()
    {
        var state = Apart(400);
        var jab = Data.FindMove("5LP")!;
        Simulation.Step(ref state, new FrameInput(Buttons.LightPunch, default));
        for (var i = 1; i < jab.TotalFrames; i++)
        {
            Simulation.Step(ref state, default);
            Assert.Equal("5LP", CurrentMove(state.Fighters[0]));
        }
        Simulation.Step(ref state, default);
        Assert.Equal((int)FighterAction.Stand, state.Fighters[0].Action);
    }

    [Fact]
    public void APressDuringRecoveryComesOutOnTheFirstFreeFrame()
    {
        var state = Apart(400);
        var jab = Data.FindMove("5LP")!;
        Simulation.Step(ref state, new FrameInput(Buttons.LightPunch, default));
        for (var i = 1; i < jab.TotalFrames - 2; i++)
            Simulation.Step(ref state, default);

        // Two frames before the jab ends: press MP, then let go.
        Simulation.Step(ref state, new FrameInput(Buttons.MediumPunch, default));
        Simulation.Step(ref state, default);
        Assert.Equal("5LP", CurrentMove(state.Fighters[0]));

        Simulation.Step(ref state, default);
        Assert.Equal("5MP", CurrentMove(state.Fighters[0]));
        Assert.Equal(0, state.Fighters[0].ActionFrame);
    }

    [Fact]
    public void AnOldPressIsNotBuffered()
    {
        var state = Apart(400);
        var hk = Data.FindMove("5HK")!; // long enough for a press early in it to fall out of the buffer
        Simulation.Step(ref state, new FrameInput(Buttons.HeavyKick, default));
        Simulation.Step(ref state, new FrameInput(Buttons.HeavyKick | Buttons.LightPunch, default));
        for (var i = 2; i <= hk.TotalFrames; i++)
            Simulation.Step(ref state, default);
        Assert.Equal((int)FighterAction.Stand, state.Fighters[0].Action);
    }

    [Fact]
    public void AHitDealsDamageAndHitstunOnce()
    {
        var state = Apart(60);
        var jab = Data.FindMove("5LP")!;
        Simulation.Step(ref state, new FrameInput(Buttons.LightPunch, default));
        for (var i = 1; i < jab.TotalFrames; i++)
            Simulation.Step(ref state, default);

        Assert.Equal(Data.Stats.Health - jab.Damage, state.Fighters[1].Health); // two active frames, one hit
        Assert.Equal(Data.Stats.Health, state.Fighters[0].Health);
    }

    [Fact]
    public void AWhiffDealsNothing()
    {
        var state = Apart(400);
        Simulation.Step(ref state, new FrameInput(Buttons.HeavyKick, default));
        for (var i = 0; i < 40; i++)
            Simulation.Step(ref state, default);
        Assert.Equal(Data.Stats.Health, state.Fighters[1].Health);
        Assert.Equal(0, state.Fighters[1].Hitstun);
    }

    [Fact]
    public void ATradeHitsBothFighters()
    {
        var state = Apart(60);
        Simulation.Step(ref state, new FrameInput(Buttons.LightPunch, Buttons.LightPunch));
        for (var i = 0; i < 6; i++)
            Simulation.Step(ref state, default);

        var damage = Data.FindMove("5LP")!.Damage;
        Assert.Equal(Data.Stats.Health - damage, state.Fighters[0].Health);
        Assert.Equal(Data.Stats.Health - damage, state.Fighters[1].Health);
    }

    [Fact]
    public void HitstunStopsTheDefenderActingButStillBuffers()
    {
        var state = Apart(60);
        Simulation.Step(ref state, new FrameInput(Buttons.LightPunch, default));
        while (state.Fighters[1].Hitstun == 0)
            Simulation.Step(ref state, default);

        // Wait in hitstun until it nearly ends, then tap LK once. Hitstun holds; LK comes out the
        // first frame P2 is free.
        while (state.Fighters[1].Hitstun > 2)
        {
            Simulation.Step(ref state, default);
            Assert.Equal((int)FighterAction.Hitstun, state.Fighters[1].Action);
        }
        Simulation.Step(ref state, new FrameInput(default, Buttons.LightKick));
        Assert.Equal((int)FighterAction.Hitstun, state.Fighters[1].Action);
        while (state.Fighters[1].Hitstun > 0)
            Simulation.Step(ref state, default);
        Simulation.Step(ref state, default);
        Assert.Equal("5LK", CurrentMove(state.Fighters[1]));
        Assert.Equal(0, state.Fighters[1].ActionFrame);
    }

    [Fact]
    public void NothingHitsAFighterLyingDown()
    {
        var state = Apart(60);
        state.Fighters[1].KnockedDown = true;
        Simulation.Step(ref state, new FrameInput(Buttons.LightPunch, default));
        for (var i = 0; i < 10; i++)
            Simulation.Step(ref state, default);
        Assert.Equal(Data.Stats.Health, state.Fighters[1].Health);
    }

    // On-hit advantage from reference/frame-data-baseline. 2HK knocks down, so it's left out.
    [Theory]
    [InlineData("5LP", 6)]
    [InlineData("5MP", 5)]
    [InlineData("5HP", 3)]
    [InlineData("5LK", 2)]
    [InlineData("5MK", 1)]
    [InlineData("5HK", 0)]
    [InlineData("2LP", 5)]
    [InlineData("2MP", 4)]
    [InlineData("2HP", -4)]
    [InlineData("2LK", 2)]
    [InlineData("2MK", 0)]
    public void OnHitAdvantageMatchesTheBaseline(string input, int expected)
    {
        var move = Data.FindMove(input)!;

        // The rule, from the data.
        Assert.Equal(expected, move.Hitstun - move.Active - move.Recovery);

        // And in the sim: hit on the first active frame at point-blank, then see who acts first.
        var state = Apart(60);
        var button = input[1..] switch
        {
            "LP" => Buttons.LightPunch,
            "MP" => Buttons.MediumPunch,
            "HP" => Buttons.HeavyPunch,
            "LK" => Buttons.LightKick,
            "MK" => Buttons.MediumKick,
            _ => Buttons.HeavyKick,
        };
        var hold = input[0] == '2' ? Buttons.Down : Buttons.None;

        int? hitFrame = null, attackerFree = null, defenderFree = null;
        var start = state.Frame;
        Simulation.Step(ref state, new FrameInput(button | hold, default));
        for (var i = 0; i < 80 && (attackerFree is null || defenderFree is null); i++)
        {
            var frame = state.Frame;
            Simulation.Step(ref state, new FrameInput(hold, default));
            if (hitFrame is null && state.Fighters[1].Hitstun > 0)
                hitFrame = frame;
            if (hitFrame is not null && attackerFree is null && state.Fighters[0].Action != (int)FighterAction.Attack)
                attackerFree = frame;
            if (hitFrame is not null && defenderFree is null && state.Fighters[1].Action != (int)FighterAction.Hitstun)
                defenderFree = frame;
        }

        Assert.Equal(start + move.FirstActiveFrame, hitFrame);
        Assert.Equal(expected, defenderFree - attackerFree);
    }
}
