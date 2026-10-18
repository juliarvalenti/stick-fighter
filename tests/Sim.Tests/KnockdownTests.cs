using StickFighter.Sim.Characters;
using StickFighter.Sim.Combat;
using StickFighter.Sim.Numerics;
using Xunit;

namespace StickFighter.Sim.Tests;

public class KnockdownTests
{
    private static Character Data => Characters.Characters.Placeholder;
    private static Stats Stats => Data.Stats;

    private static GameState Apart(int gap)
    {
        var state = GameState.NewMatch(1);
        state.Fighters[0].Position = new FixVec2(Fix.FromInt(1000), state.Fighters[0].Position.Y);
        state.Fighters[1].Position = new FixVec2(Fix.FromInt(1000 + gap), state.Fighters[1].Position.Y);
        return state;
    }

    private static void Step(ref GameState state, Buttons p1 = default, Buttons p2 = default) =>
        Simulation.Step(ref state, new FrameInput(p1, p2));

    // Steps until P2 is hit, P2 holding `p2` throughout. Returns the state just after the hit.
    private static GameState UntilP2IsHit(GameState state, Buttons p1First, Buttons p2 = default)
    {
        var health = state.Fighters[1].Health;
        Step(ref state, p1First, p2);
        while (state.Fighters[1].Health == health && state.Frame < 60)
            Step(ref state, p1First & Buttons.Down, p2);
        return state;
    }

    // Steps (P2 holding `p2`) until P2 is up from its knockdown; returns how many unfrozen frames it
    // lay down, counting the frame it went down (hitstop doesn't count: nothing moves then).
    private static int FramesDown(ref GameState state, Buttons p2 = default)
    {
        var frames = 1;
        while (true)
        {
            var frozen = Hitstop.Frozen(state, state.Frame);
            Step(ref state, default, p2);
            if (!state.Fighters[1].KnockedDown)
                return frames;
            if (!frozen)
                frames++;
        }
    }

    [Fact]
    public void ASweepIsAHardKnockdown()
    {
        var sweep = Data.FindMove("2HK")!;
        Assert.True(sweep.HardKnockdown);
        var state = UntilP2IsHit(Apart(60), Buttons.HeavyKick | Buttons.Down);
        Assert.True(state.Fighters[1].KnockedDown);
        Assert.Equal(0, state.Fighters[1].Hitstun);
        Assert.Equal(Stats.Health - sweep.Damage, state.Fighters[1].Health);

        // Down and buttons the whole time: a hard knockdown has no quick rise.
        Assert.Equal(Stats.KnockdownFrames, FramesDown(ref state, Buttons.Down | Buttons.LightPunch));
    }

    // P2 is falling from an air hit, and presses `rise` (or nothing) just before landing.
    [Theory]
    [InlineData(Buttons.None, false)]
    [InlineData(Buttons.Down, true)]
    [InlineData(Buttons.LightPunch | Buttons.HeavyKick, true)] // two buttons
    [InlineData(Buttons.LightPunch, false)] // one isn't enough
    public void ALandingFromAJuggleIsASoftKnockdownThatCanQuickRise(Buttons rise, bool quick)
    {
        var state = Apart(60);
        state.Fighters[1].Airborne = true;
        state.Fighters[1].Action = (int)FighterAction.Jump;
        state.Fighters[1].AirAttackUsed = true;
        state.Fighters[1].Position = new FixVec2(state.Fighters[1].Position.X, state.Stage.Floor - Fix.FromInt(30));
        state = UntilP2IsHit(state, Buttons.LightPunch);
        Assert.Equal(1, state.Fighters[1].Juggle);

        // Fall, pressing `rise` a few frames before touchdown (the fall's length is found first).
        var probe = state;
        var fall = 0;
        while (probe.Fighters[1].Airborne)
        {
            Step(ref probe);
            fall++;
        }
        for (var i = 0; i < fall - 1; i++)
            Step(ref state, default, i == fall - 4 ? rise : default);
        Step(ref state);
        Assert.True(state.Fighters[1].KnockedDown);
        Assert.Equal(quick ? Stats.QuickRiseFrames : Stats.KnockdownFrames, FramesDown(ref state));
    }

    // A press in the last frames of a knockdown comes out on the first frame up: a reversal.
    [Fact]
    public void APressDuringTheKnockdownComesOutOnWakeup()
    {
        var state = UntilP2IsHit(Apart(60), Buttons.HeavyKick | Buttons.Down);
        while (state.Fighters[1].Knockdown > 3)
            Step(ref state);
        Step(ref state, default, Buttons.MediumPunch);
        while (state.Fighters[1].KnockedDown)
            Step(ref state);
        Assert.Equal(Data.FindMove("5MP"), Data.Moves[state.Fighters[1].Move]);
        Assert.Equal(0, state.Fighters[1].ActionFrame);
    }

    // P2's gauge is nearly full; a jab fills it. Once out of hitstun P2 is dizzy for DizzyFrames,
    // can't guard, and the gauge is reset.
    [Fact]
    public void AFullStunGaugeMeansDizzy()
    {
        var state = Apart(60);
        state.Fighters[1].Stun = Stats.Stun - 10;
        state = UntilP2IsHit(state, Buttons.LightPunch);
        Assert.True(state.Fighters[1].Stun >= Stats.Stun);
        while (state.Fighters[1].Hitstun > 0 || Hitstop.Frozen(state, state.Frame))
            Step(ref state, default, Buttons.Right);

        var dizzy = 0;
        while (true)
        {
            Step(ref state, default, Buttons.Right);
            if (state.Fighters[1].Action != (int)FighterAction.Dizzy)
                break;
            Assert.Equal(0, state.Fighters[1].Stun);
            Assert.False(Blocking.CanGuard(state.Fighters[1]));
            dizzy++;
        }
        Assert.Equal(Stats.DizzyFrames, dizzy);
    }

    [Fact]
    public void AHitEndsTheDizzy()
    {
        var state = Apart(60);
        state.Fighters[1].Dizzy = 50;
        state.Fighters[1].Action = (int)FighterAction.Dizzy;
        state = UntilP2IsHit(state, Buttons.LightPunch, Buttons.Right); // holding back: no guard while dizzy
        Assert.Equal(0, state.Fighters[1].Dizzy);
        Assert.Equal((int)FighterAction.Hitstun, state.Fighters[1].Action);
    }

    [Fact]
    public void TheGaugeRecoversAfterAQuietSpell()
    {
        var state = UntilP2IsHit(Apart(60), Buttons.LightPunch);
        var afterHit = state.Fighters[1].Stun;
        Assert.Equal(Data.FindMove("5LP")!.Stun, afterHit);
        for (var i = 0; i < Stats.StunRecoveryDelay; i++)
            Step(ref state);
        Assert.Equal(afterHit, state.Fighters[1].Stun); // still waiting (hitstop frames don't count)
        while (state.Fighters[1].Stun == afterHit)
            Step(ref state);
        Assert.Equal(afterHit - Stats.StunRecoveryRate, state.Fighters[1].Stun);
    }

    // The sweep adds its stun (100) and knocks down: from 500 the gauge resets; from 950 the sweep fills
    // it, so it's kept and P2 gets up dizzy.
    [Theory]
    [InlineData(500, false)]
    [InlineData(950, true)]
    public void AKnockdownResetsTheGaugeUnlessItsFull(int before, bool full)
    {
        var state = Apart(60);
        state.Fighters[1].Stun = before;
        state = UntilP2IsHit(state, Buttons.HeavyKick | Buttons.Down);
        Assert.True(state.Fighters[1].KnockedDown);
        Assert.Equal(full, state.Fighters[1].Stun >= Stats.Stun);
        if (!full)
            Assert.Equal(0, state.Fighters[1].Stun);

        FramesDown(ref state);
        Assert.Equal(full, state.Fighters[1].Action == (int)FighterAction.Dizzy);
    }
}
