using StickFighter.Sim.Characters;
using StickFighter.Sim.Combat;
using StickFighter.Sim.Numerics;
using Xunit;

namespace StickFighter.Sim.Tests;

public class HitStatesTests
{
    private static Character Data => Characters.Characters.Placeholder;

    // P1 at x, P2 `gap` to its right.
    private static GameState Apart(int gap, int x = 1000)
    {
        var state = GameState.NewMatch(1);
        state.Fighters[0].Position = new FixVec2(Fix.FromInt(x), state.Fighters[0].Position.Y);
        state.Fighters[1].Position = new FixVec2(Fix.FromInt(x + gap), state.Fighters[1].Position.Y);
        return state;
    }

    private static void Step(ref GameState state, Buttons p1 = default, Buttons p2 = default) =>
        Simulation.Step(ref state, new FrameInput(p1, p2));

    // Steps until P2 is hit (or 60 frames), returning the state just after the hit.
    private static GameState UntilP2IsHit(GameState state, Buttons p1First, Buttons p2Held = default, Buttons p2First = default)
    {
        Step(ref state, p1First, p2First | p2Held);
        var health = state.Fighters[1].Health;
        while (state.Fighters[1].Health == health && state.Frame < 60)
            Step(ref state, p1First & Buttons.Down, p2Held);
        return state;
    }

    [Theory]
    [InlineData(1, 100)]
    [InlineData(2, 100)]
    [InlineData(3, 80)]
    [InlineData(4, 70)]
    [InlineData(5, 60)]
    [InlineData(10, 10)]
    [InlineData(11, 10)]
    [InlineData(30, 10)]
    public void ScalingByHitInCombo(int hit, int percent) => Assert.Equal(percent, HitStates.ScalingPercent(hit));

    [Theory]
    [InlineData(1000, 100)]
    [InlineData(500, 100)]
    [InlineData(499, 95)]
    [InlineData(299, 90)]
    [InlineData(149, 75)]
    public void LowLifeReductionByHealth(int health, int percent) => Assert.Equal(percent, HitStates.LowLifePercent(health, 1000));

    [Theory]
    [InlineData(100, 1, false, 1000, true, 100)]
    [InlineData(100, 1, true, 1000, true, 125)] // counter hit
    [InlineData(100, 3, false, 1000, true, 80)]
    [InlineData(100, 1, false, 100, true, 75)] // under 15%
    [InlineData(100, 1, false, 100, false, 100)] // reduction switched off
    [InlineData(20, 12, false, 100, true, 1)] // 20 x 10% x 75% rounds to 1, and never below
    [InlineData(1, 30, false, 1000, true, 1)]
    public void DamageCombinesTheRules(int baseDamage, int hit, bool counter, int health, bool lowLife, int expected) =>
        Assert.Equal(expected, HitStates.Damage(baseDamage, hit, counter, health, 1000, lowLife));

    [Fact]
    public void HitStateConstantsComeFromTheData()
    {
        Assert.Equal(1, Data.Stats.CrouchHitstunBonus);
        Assert.True(Data.Stats.LowLifeReduction);
        var jab = Data.FindMove("5LP")!;
        Assert.Equal((1, 1, 0), (jab.JuggleStart, jab.JuggleIncrease, jab.JuggleLimit));
    }

    // P2 starts a heavy kick (11 frames of startup) and P1's jab lands in it: a counter hit.
    [Fact]
    public void AHitDuringTheDefendersStartupIsACounterHit()
    {
        var jab = Data.FindMove("5LP")!;
        var counter = UntilP2IsHit(Apart(60), Buttons.LightPunch, p2First: Buttons.HeavyKick);
        Assert.True(counter.Fighters[1].CounterHit);
        Assert.Equal(Data.Stats.Health - jab.Damage * 125 / 100, counter.Fighters[1].Health);

        var plain = UntilP2IsHit(Apart(60), Buttons.LightPunch);
        Assert.False(plain.Fighters[1].CounterHit);
        Assert.Equal(Data.Stats.Health - jab.Damage, plain.Fighters[1].Health);

        // +1 hitstun for a light (set on the hit frame, so compare the two directly).
        Assert.Equal(plain.Fighters[1].Hitstun + 1, counter.Fighters[1].Hitstun);
    }

    // A crouching jab on a crouching P2 and on a standing one: the croucher reels a frame longer.
    [Fact]
    public void ACrouchingDefenderGetsMoreHitstun()
    {
        var crouching = UntilP2IsHit(Apart(60), Buttons.LightPunch | Buttons.Down, p2Held: Buttons.Down);
        var standing = UntilP2IsHit(Apart(60), Buttons.LightPunch | Buttons.Down);
        Assert.Equal(standing.Fighters[1].Hitstun + Data.Stats.CrouchHitstunBonus, crouching.Fighters[1].Hitstun);
    }

    // P2 in the corner; P1 jabs as soon as it's free, three times. The jab links (+6 on hit), so it's
    // one combo: 20, 20, then 16 (80%).
    [Fact]
    public void AThreeHitComboScales()
    {
        var wall = Collision.Pushing.MaxX(Stage.Training).RoundToInt();
        var state = Apart(60, wall - 60);
        var hits = 0;
        var health = state.Fighters[1].Health;
        for (var f = 0; f < 120 && hits < 3; f++)
        {
            var free = state.Fighters[0].Action != (int)FighterAction.Attack;
            Step(ref state, free ? Buttons.LightPunch : default);
            if (state.Fighters[1].Health < health)
            {
                hits++;
                Assert.Equal(hits, state.Fighters[1].ComboHits);
                health = state.Fighters[1].Health;
            }
        }
        Assert.Equal(3, hits);
        Assert.Equal(56, state.Fighters[1].ComboDamage);
        Assert.Equal(Data.Stats.Health - 56, state.Fighters[1].Health);
    }

    // The combo ends when the defender recovers; the next hit starts a new one at full damage.
    [Fact]
    public void TheComboResetsWhenTheDefenderRecovers()
    {
        var state = UntilP2IsHit(Apart(60), Buttons.LightPunch);
        Assert.Equal(1, state.Fighters[1].ComboHits);
        while (state.Fighters[1].Hitstun > 0 || Hitstop.Frozen(state, state.Frame))
            Step(ref state);
        Step(ref state);
        Assert.Equal(0, state.Fighters[1].ComboHits);
        Assert.Equal(0, state.Fighters[1].ComboDamage);
    }

    // P2 is falling from an air hit (juggle count 1). A normal's juggle limit is 0, so P1's jab
    // passes through, but the same jab hits a fighter on a plain jump.
    [Theory]
    [InlineData(1, false)]
    [InlineData(0, true)]
    public void AFallingFighterCantBeHitByANormal(int juggle, bool hit)
    {
        var state = Apart(60);
        state.Fighters[1].Airborne = true;
        state.Fighters[1].Juggle = juggle;
        state.Fighters[1].Action = (int)FighterAction.Jump;
        state.Fighters[1].AirAttackUsed = true;
        state.Fighters[1].Position = new FixVec2(state.Fighters[1].Position.X, state.Stage.Floor - Fix.FromInt(30));
        var health = state.Fighters[1].Health;
        Step(ref state, Buttons.LightPunch);
        for (var i = 0; i < Data.FindMove("5LP")!.FirstActiveFrame + 2 && state.Fighters[1].Airborne; i++)
            Step(ref state);
        Assert.Equal(hit, state.Fighters[1].Health < health);
    }

    // An air hit knocks the defender into a fall with the move's juggle start, up and away.
    [Fact]
    public void AnAirHitStartsAJuggleFall()
    {
        var state = Apart(60);
        state.Fighters[1].Airborne = true;
        state.Fighters[1].Action = (int)FighterAction.Jump;
        state.Fighters[1].AirAttackUsed = true;
        state.Fighters[1].Position = new FixVec2(state.Fighters[1].Position.X, state.Stage.Floor - Fix.FromInt(30));
        var hit = UntilP2IsHit(state, Buttons.LightPunch);
        Assert.Equal(1, hit.Fighters[1].Juggle);
        Assert.True(hit.Fighters[1].Velocity.Y < Fix.Zero, "knocked upward");
        Assert.True(hit.Fighters[1].Velocity.X > Fix.Zero, "and away from P1");

        // Landing ends the juggle.
        while (hit.Fighters[1].Airborne && hit.Frame < 200)
            Step(ref hit);
        Assert.Equal(0, hit.Fighters[1].Juggle);
    }

    // The input history reaches across the longest hitstop: a press 4 frames before a 12-frame freeze,
    // made while the fighter can't act, comes out on its first free frame after it, 16 frames later.
    [Fact]
    public void APressBeforeAFreezeComesOutAfterIt()
    {
        var state = Apart(400);
        state.Fighters[0].Blockstun = 4;
        state.Fighters[0].Action = (int)FighterAction.BlockStand;
        Step(ref state, Buttons.MediumPunch); // frame 0: the press, in blockstun
        Step(ref state);
        Step(ref state);
        Hitstop.Start(ref state, 12); // frames 4 to 15 frozen
        Step(ref state); // frame 3: the last blockstun frame
        while (state.Frame < 16)
            Step(ref state);
        Assert.Equal(-1, state.Fighters[0].Move);
        Step(ref state); // frame 16: free
        Assert.Equal(Data.FindMove("5MP"), Data.Moves[state.Fighters[0].Move]);
    }

    [Fact]
    public void TheLongestHitstopFitsInTheInputHistory()
    {
        foreach (var move in Data.Moves)
            Assert.True(move.HitstopOnHit + Normals.BufferWindow <= InputHistory.MaxWindow, move.Input);
    }
}
