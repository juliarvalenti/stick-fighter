using StickFighter.Sim.Characters;
using StickFighter.Sim.Collision;
using StickFighter.Sim.Combat;
using StickFighter.Sim.Movement;
using StickFighter.Sim.Numerics;
using Xunit;

namespace StickFighter.Sim.Tests;

public class DashTests
{
    private static Character Data => Characters.Characters.Placeholder;
    private static Stats Stats => Data.Stats;

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

    // Tap, release, then the second tap `gap` frames after the first.
    private static GameState DoubleTap(Buttons direction, int gap)
    {
        var state = At(1000, 1600);
        Simulation.Step(ref state, new FrameInput(direction, default));
        Run(ref state, gap - 1, default);
        Simulation.Step(ref state, new FrameInput(direction, default));
        return state;
    }

    [Theory]
    [InlineData(Buttons.Right, FighterAction.DashForward)] // 66 for P1, facing right
    [InlineData(Buttons.Left, FighterAction.DashBack)]     // 44
    public void ADoubleTapDashesAtTheEdgesOfTheWindow(Buttons direction, FighterAction dash)
    {
        // Second tap the frame after the release (the fastest possible) and at the window's end.
        Assert.Equal(dash, ActionOf(DoubleTap(direction, 2), 0));
        Assert.Equal(dash, ActionOf(DoubleTap(direction, Stats.DashInputWindow), 0));

        // One frame late is just a walk.
        var late = ActionOf(DoubleTap(direction, Stats.DashInputWindow + 1), 0);
        Assert.Equal(direction == Buttons.Right ? FighterAction.WalkForward : FighterAction.WalkBack, late);
    }

    [Fact]
    public void HoldingOrADiagonalDoesNotDash()
    {
        var held = At(1000, 1600);
        Run(ref held, 12, new FrameInput(Buttons.Right, default));
        Assert.Equal(FighterAction.WalkForward, ActionOf(held, 0));

        var diagonal = DoubleTap(Buttons.Right | Buttons.Down, 3);
        Assert.NotEqual(FighterAction.DashForward, ActionOf(diagonal, 0));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EachDashCoversItsDistanceThenRecovers(bool forward)
    {
        var state = DoubleTap(forward ? Buttons.Right : Buttons.Left, 3);
        var dash = forward ? FighterAction.DashForward : FighterAction.DashBack;
        var frames = Dashing.Frames(Stats, forward);
        var recovery = forward ? Stats.ForwardDashRecovery : Stats.BackDashRecovery;
        var distance = forward ? Stats.ForwardDashDistance : -Stats.BackDashDistance;

        // The double tap's frame is the dash's first: it already moved one step.
        var start = state.Fighters[0].Position.X - Dashing.Step(Stats, forward, 0) * (forward ? 1 : -1);
        var lastStep = Fix.MaxValue;
        for (var f = 1; f < frames; f++)
        {
            var before = state.Fighters[0].Position.X;
            Simulation.Step(ref state, default);
            Assert.Equal(dash, ActionOf(state, 0));
            var step = Fix.Abs(state.Fighters[0].Position.X - before);
            Assert.True(step <= lastStep, "the dash eases off, never speeds up");
            lastStep = step;
            if (f >= frames - recovery)
                Assert.Equal(Fix.Zero, step);
        }
        Assert.Equal(start + distance, state.Fighters[0].Position.X);

        Simulation.Step(ref state, default);
        Assert.Equal(FighterAction.Stand, ActionOf(state, 0));
    }

    [Fact]
    public void TheCurveAddsUpExactly()
    {
        var distance = Fix.FromInt(140);
        var sum = Fix.Zero;
        for (var f = 0; f < 14; f++)
            sum += Dashing.Offset(f + 1, 14, distance) - Dashing.Offset(f, 14, distance);
        Assert.Equal(distance, sum);
    }

    [Fact]
    public void ADashDoesNotTurnAround()
    {
        // P1 dashes forward through where P2 was put behind it: P1 keeps facing right until it ends.
        var state = DoubleTap(Buttons.Right, 3);
        state.Fighters[1].Position = new FixVec2(Fix.FromInt(700), state.Fighters[1].Position.Y);
        for (var f = 1; f < Stats.ForwardDashFrames; f++)
        {
            Simulation.Step(ref state, default);
            Assert.Equal(1, state.Fighters[0].Facing);
        }
        Simulation.Step(ref state, default);
        Assert.Equal(-1, state.Fighters[0].Facing);
    }

    [Fact]
    public void TheBackDashIsInvincibleForItsFirstFrames()
    {
        var state = DoubleTap(Buttons.Left, 3);
        for (var f = 0; f < Stats.BackDashFrames; f++)
        {
            var invincible = f < Stats.BackDashInvincibleFrames;
            Assert.Equal(invincible ? 0 : 1, System.Math.Min(1, Body.Shapes(state.Fighters[0]).HurtCount));
            Assert.Equal(!invincible, Throws.Throwable(state.Fighters[0]));
            Assert.False(Blocking.CanGuard(state.Fighters[0]), "no guarding mid-dash");
            Simulation.Step(ref state, default);
        }
    }

    // The dash replays show what their names say (a replay alone only pins checksums).
    [Theory]
    [InlineData("dash-forward")]
    [InlineData("dash-back")]
    [InlineData("dash-wall")]
    [InlineData("backdash-through-attack")]
    public void DashReplaysShowWhatTheySay(string name)
    {
        var replay = Replays.Replay.Read(System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "replays", name + ".sfr")));
        var state = GameState.NewMatch(replay.Seed, replay.Stage);
        bool forward = false, back = false, hit = false, kickActive = false, invincibleWhileActive = true, backAtWall = false;
        foreach (var input in replay.Inputs)
        {
            var before = state.Fighters[0];
            Simulation.Step(ref state, input);
            ref readonly var p1 = ref state.Fighters[0];
            forward |= p1.Action == (int)FighterAction.DashForward;
            back |= p1.Action == (int)FighterAction.DashBack;
            backAtWall |= p1.Action == (int)FighterAction.DashBack && p1.Position.X == before.Position.X && p1.Position.X == Pushing.MinX(state.Stage);
            hit |= p1.Hitstun > before.Hitstun;
            if (Body.MoveOf(state.Fighters[1]) is { } m && m.IsActive(state.Fighters[1].ActionFrame))
            {
                kickActive = true;
                invincibleWhileActive &= Dashing.Invincible(p1, Stats);
            }
        }

        switch (name)
        {
            case "dash-forward": Assert.True(forward && !back); break;
            case "dash-back": Assert.True(back && !forward); break;
            case "dash-wall": Assert.True(backAtWall && forward, "back-dashed against the wall, then dashed out"); break;
            case "backdash-through-attack":
                Assert.True(kickActive, "P2's kick came out");
                Assert.True(invincibleWhileActive, "P1 was invincible on every active frame");
                Assert.False(hit, "the kick whiffed");

                // The control: the same match without P1's taps, and the kick lands.
                var control = GameState.NewMatch(replay.Seed, replay.Stage);
                var controlHit = false;
                for (var i = 0; i < replay.Frames; i++)
                {
                    var input = replay.Inputs[i];
                    var p1 = i < 300 ? input.Player1 : Buttons.None;
                    var before = control.Fighters[0].Hitstun;
                    Simulation.Step(ref control, new FrameInput(p1, input.Player2));
                    controlHit |= control.Fighters[0].Hitstun > before;
                }
                Assert.True(controlHit, "without the back-dash, the kick hits");
                break;
        }
    }

    private static string RepoRoot()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "StickFighter.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new System.IO.DirectoryNotFoundException("repo root");
    }

    [Fact]
    public void TheForwardDashHasHurtboxesThroughout()
    {
        var state = DoubleTap(Buttons.Right, 3);
        for (var f = 0; f < Stats.ForwardDashFrames; f++)
        {
            Assert.True(Body.Shapes(state.Fighters[0]).HurtCount > 0);
            Simulation.Step(ref state, default);
        }
    }
}
