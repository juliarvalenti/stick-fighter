using System.Collections.Generic;
using StickFighter.Sim.Characters;
using StickFighter.Sim.Combat;
using StickFighter.Sim.Numerics;
using Xunit;

namespace StickFighter.Sim.Tests;

public class SpecialsTests
{
    private static Character Data => Characters.Characters.Placeholder;
    private static Move Fireball => Data.FindMove("236P")!;

    // P1 at 1000 facing right, P2 `gap` to its right.
    private static GameState Apart(int gap)
    {
        var state = GameState.NewMatch(1);
        state.Fighters[0].Position = new FixVec2(Fix.FromInt(1000), state.Fighters[0].Position.Y);
        state.Fighters[1].Position = new FixVec2(Fix.FromInt(1000 + gap), state.Fighters[1].Position.Y);
        return state;
    }

    private static void Step(ref GameState state, Buttons p1 = default, Buttons p2 = default) =>
        Simulation.Step(ref state, new FrameInput(p1, p2));

    // P1 plays these inputs, one per frame (P2 holds `p2`), then nothing.
    private static GameState Play(GameState state, Buttons[] p1, int then = 0, Buttons p2 = default)
    {
        foreach (var b in p1)
            Step(ref state, b, p2);
        for (var i = 0; i < then; i++)
            Step(ref state, default, p2);
        return state;
    }

    private static readonly Buttons Down = Buttons.Down;
    private static readonly Buttons DownForward = Buttons.Down | Buttons.Right;
    private static readonly Buttons Forward = Buttons.Right;

    private static Buttons[] Qcf(Buttons punch) => new[] { Down, DownForward, Forward | punch };

    private static bool StartedFireball(in GameState state) => state.Fighters[0].Move == Specials.Fireball;

    [Fact]
    public void QuarterCircleForwardAndAPunchIsAFireball()
    {
        var state = Play(Apart(400), Qcf(Buttons.LightPunch));
        Assert.True(StartedFireball(state));
        Assert.Equal(0, state.Fighters[0].SpecialStrength);
    }

    // The special beats the normal its button would give: the press is a jab's too.
    [Fact]
    public void TheSpecialComesOutInsteadOfTheNormal()
    {
        var state = Play(Apart(400), Qcf(Buttons.LightPunch));
        Assert.NotEqual(Data.FindMove("5LP"), Data.Moves[state.Fighters[0].Move]);
    }

    [Theory]
    [InlineData(new[] { 2, 1, 2, 3, 6 }, true)] // extra directions in between
    [InlineData(new[] { 2, 3, 5, 6 }, true)] // a neutral frame
    [InlineData(new[] { 2, 2, 2, 3, 3, 6, 6 }, true)] // slow, within the window
    [InlineData(new[] { 2, 6 }, false)] // no 3
    [InlineData(new[] { 3, 6 }, false)] // no 2
    [InlineData(new[] { 6, 3, 2 }, false)] // backwards
    public void TheMotionIsLenient(int[] directions, bool fireball)
    {
        var inputs = new List<Buttons>();
        foreach (var d in directions)
            inputs.Add(Numpad(d));
        inputs[^1] |= Buttons.MediumPunch;
        Assert.Equal(fireball, StartedFireball(Play(Apart(400), inputs.ToArray())));
    }

    // The punch a few frames after the 6 still counts; too late, it's a jab.
    [Theory]
    [InlineData(0, true)]
    [InlineData(3, true)]
    [InlineData(6, false)]
    public void ThePunchCanComeAFewFramesAfterTheMotion(int frames, bool fireball)
    {
        var inputs = new List<Buttons> { Down, DownForward, Forward };
        for (var i = 0; i < frames; i++)
            inputs.Add(default);
        inputs.Add(Buttons.LightPunch);
        Assert.Equal(fireball, StartedFireball(Play(Apart(400), inputs.ToArray())));
    }

    // Too spread out for the window: not a fireball.
    [Fact]
    public void AMotionSpreadPastTheWindowDoesntCount()
    {
        var inputs = new List<Buttons> { Down };
        for (var i = 0; i < Data.Stats.MotionWindow; i++)
            inputs.Add(DownForward);
        inputs.Add(Forward | Buttons.LightPunch);
        Assert.False(StartedFireball(Play(Apart(400), inputs.ToArray())));
    }

    // Facing left, forward is Left.
    [Fact]
    public void TheMotionIsReadRelativeToFacing()
    {
        var state = Apart(400);
        (state.Fighters[0].Position, state.Fighters[1].Position) = (state.Fighters[1].Position, state.Fighters[0].Position);
        Step(ref state); // turn to face
        state = Play(state, new[] { Down, Buttons.Down | Buttons.Left, Buttons.Left | Buttons.LightPunch });
        Assert.True(StartedFireball(state));
    }

    // Negative edge: the punch held through the motion and released after it.
    [Fact]
    public void ReleasingThePunchAfterTheMotionThrowsIt()
    {
        var p = Buttons.HeavyPunch;
        var state = Apart(400);
        Step(ref state, p); // pressed: a heavy punch comes out (5HP)...
        while (state.Fighters[0].Action == (int)FighterAction.Attack)
            Step(ref state, p); // ...held until it's over
        state = Play(state, new[] { Down | p, DownForward | p, Forward | p, Forward });
        Assert.True(StartedFireball(state));
        Assert.Equal(2, state.Fighters[0].SpecialStrength);
    }

    [Theory]
    [InlineData(Buttons.LightPunch, 6)]
    [InlineData(Buttons.MediumPunch, 8)]
    [InlineData(Buttons.HeavyPunch, 10)]
    public void TheFireballAppearsAtTheHandOnItsSpawnFrameAtItsStrengthsSpeed(Buttons punch, int speed)
    {
        var state = Play(Apart(800), Qcf(punch));
        var start = state.Frame - 1;
        while (state.Frame - 1 < start + Fireball.Spawn!.Frame)
        {
            Assert.False(state.Projectiles[0].Active);
            Step(ref state);
        }
        var projectile = state.Projectiles[0];
        Assert.True(projectile.Active);
        Assert.Equal(0, projectile.Owner);
        Assert.Equal(Fix.FromInt(speed), projectile.Velocity.X);
        Assert.True(projectile.Position.X > Fix.FromInt(1000) && projectile.Position.Y < state.Stage.Floor);
    }

    // One fireball per player: the motion again while it's still out gives the normal instead.
    [Fact]
    public void OnlyOneFireballOnScreen()
    {
        var state = Play(Apart(1200), Qcf(Buttons.LightPunch));
        while (state.Fighters[0].Action == (int)FighterAction.Attack)
            Step(ref state);
        Assert.True(Projectiles.HasOneOut(state, 0));
        state = Play(state, Qcf(Buttons.LightPunch));
        Assert.False(StartedFireball(state));
        var active = 0;
        for (var slot = 0; slot < GameState.MaxProjectiles; slot++)
            active += state.Projectiles[slot].Active ? 1 : 0;
        Assert.Equal(1, active);
    }

    // Steps until P1's fireball has been thrown and is gone again (it hit, was blocked, or flew off),
    // P2 holding `p2`.
    private static GameState UntilTheFireballIsSpent(GameState state, Buttons p2 = default)
    {
        for (var i = 0; i < 60 && !Projectiles.HasOneOut(state, 0); i++)
            Step(ref state, default, p2);
        Assert.True(Projectiles.HasOneOut(state, 0), "the fireball was thrown");
        for (var i = 0; i < 400 && Projectiles.HasOneOut(state, 0); i++)
            Step(ref state, default, p2);
        return state;
    }

    [Fact]
    public void AFireballHitsForTheMovesDamageAndIsSpent()
    {
        var state = UntilTheFireballIsSpent(Play(Apart(400), Qcf(Buttons.HeavyPunch)));
        Assert.Equal(Data.Stats.Health - Fireball.Damage, state.Fighters[1].Health);
    }

    [Fact]
    public void ABlockedFireballChips()
    {
        var state = UntilTheFireballIsSpent(Play(Apart(400), Qcf(Buttons.HeavyPunch), p2: Buttons.Right), Buttons.Right);
        Assert.Equal(Data.Stats.Health - Fireball.Chip, state.Fighters[1].Health);
        Assert.True(Fireball.ChipKo);
    }

    [Fact]
    public void AFireballCanChipKo()
    {
        var state = Apart(400);
        state.Fighters[1].Health = Fireball.Chip;
        state = UntilTheFireballIsSpent(Play(state, Qcf(Buttons.HeavyPunch), p2: Buttons.Right), Buttons.Right);
        Assert.Equal(0, state.Fighters[1].Health);
    }

    // Both throw at once from far apart: the fireballs meet in the middle and cancel out, and neither
    // fighter is touched.
    [Fact]
    public void TwoFireballsCancelOut()
    {
        var state = Apart(600);
        var mirrored = new[] { Down, Buttons.Down | Buttons.Left, Buttons.Left | Buttons.LightPunch };
        var qcf = Qcf(Buttons.LightPunch);
        for (var i = 0; i < 3; i++)
            Step(ref state, qcf[i], mirrored[i]);
        var clashed = false;
        for (var i = 0; i < 200; i++)
        {
            Step(ref state);
            for (var slot = 0; slot < SimEventRing.Capacity; slot++)
                clashed |= state.Events[slot].Kind == EventKind.Clash;
        }
        Assert.True(clashed);
        Assert.False(Projectiles.HasOneOut(state, 0) || Projectiles.HasOneOut(state, 1));
        Assert.Equal(Data.Stats.Health, state.Fighters[0].Health);
        Assert.Equal(Data.Stats.Health, state.Fighters[1].Health);
    }

    [Fact]
    public void AFireballDiesAtTheStageEdge()
    {
        var state = Apart(60);
        state.Fighters[1].Position = new FixVec2(Fix.FromInt(800), state.Fighters[1].Position.Y); // behind P1: nothing to hit
        Step(ref state); // P1 turns to face P2 (left), so the fireball flies at the left wall
        state = Play(state, new[] { Down, Buttons.Down | Buttons.Left, Buttons.Left | Buttons.HeavyPunch });
        state.Fighters[1].Position = new FixVec2(Fix.FromInt(2400), state.Fighters[1].Position.Y);
        var flew = false;
        for (var i = 0; i < 300; i++)
        {
            Step(ref state);
            flew |= Projectiles.HasOneOut(state, 0);
        }
        Assert.True(flew);
        Assert.False(Projectiles.HasOneOut(state, 0));
    }

    // Combo scaling counts moves, not hits. P2 is mid-combo, two moves in. A jab from a move instance
    // that already hit (the same key) is still the second move: 100%, 20. A new one is the third: 80%, 16.
    [Theory]
    [InlineData(true, 20)]
    [InlineData(false, 16)]
    public void ScalingCountsMovesNotHits(bool sameMove, int damage)
    {
        var state = Apart(60);
        Step(ref state, Buttons.LightPunch); // the jab starts on frame 0 (its key is 0 * 4 + P1)
        Step(ref state);
        Step(ref state);
        state.Fighters[1].ComboHits = 2;
        state.Fighters[1].ComboMoves = 2;
        state.Fighters[1].ComboKey = sameMove ? 0 : 999;
        state.Fighters[1].Hitstun = 10;
        state.Fighters[1].Action = (int)FighterAction.Hitstun;
        var health = state.Fighters[1].Health;
        Step(ref state); // frame 3: the jab's first active frame
        Assert.Equal(damage, health - state.Fighters[1].Health);
        Assert.Equal(3, state.Fighters[1].ComboHits);
    }

    // Owed from Normals: a fireball hitting someone at the separation limit pushes them back, and the
    // limit stops them; it never drags the thrower forward.
    [Fact]
    public void AFireballAtTheSeparationLimitDoesntDragTheThrower()
    {
        var state = Apart(60);
        var limit = Collision.Pushing.MaxGap(state.Stage).RoundToInt();
        state.Fighters[1].Position = new FixVec2(Fix.FromInt(1000 + limit), state.Fighters[1].Position.Y);
        state = Play(state, Qcf(Buttons.HeavyPunch));
        var thrower = state.Fighters[0].Position.X;
        state = UntilTheFireballIsSpent(state);
        Assert.True(state.Fighters[1].Health < Data.Stats.Health, "the fireball hit");
        Assert.Equal(thrower, state.Fighters[0].Position.X);
        Assert.True(state.Fighters[1].Position.X - state.Fighters[0].Position.X <= Fix.FromInt(limit));
    }

    private static Buttons Numpad(int d) => d switch
    {
        1 => Buttons.Down | Buttons.Left,
        2 => Buttons.Down,
        3 => Buttons.Down | Buttons.Right,
        4 => Buttons.Left,
        6 => Buttons.Right,
        _ => Buttons.None,
    };
}
