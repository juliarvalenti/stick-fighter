using System;
using StickFighter.Sim.Numerics;
using Xunit;

namespace StickFighter.Sim.Tests;

public class GameStateTests
{
    private static readonly FrameInput Walk = new(Buttons.Right, Buttons.Left);
    private static readonly FrameInput Retreat = new(Buttons.Left, Buttons.Right);

    // A fixed ten-frame input sequence: walk in, pause, back off.
    private static FrameInput Script(int i) => i < 5 ? Walk : i < 7 ? default : Retreat;
    private static FrameInput OtherScript(int i) => i % 2 == 0 ? Retreat : default;

    [Fact]
    public void EncodingFillsExactlyEncodedSize()
    {
        var state = Busy();
        Span<byte> buffer = stackalloc byte[GameState.EncodedSize + 16];
        buffer.Fill(0xAB);
        state.Encode(buffer);
        Assert.Equal(0xAB, buffer[GameState.EncodedSize]);
        Assert.NotEqual(0xAB, buffer[GameState.EncodedSize - 1]);
    }

    [Fact]
    public void LoadOfSaveIsTheSameState()
    {
        var state = Busy();
        var buffer = new StateBuffer(8);
        buffer.Save(state);
        var saved = state;

        Simulation.Step(ref state, Walk);
        Assert.False(state.StateEquals(saved));

        Assert.True(buffer.TryLoad(saved.Frame, ref state));
        Assert.True(state.StateEquals(saved));
        Assert.Equal(saved.Checksum(), state.Checksum());
    }

    [Fact]
    public void RollbackDryRunReproducesTheSameFrames()
    {
        var state = Advance(GameState.NewMatch(7), 30, _ => Walk);
        var buffer = new StateBuffer(16);
        buffer.Save(state);
        var startFrame = state.Frame;

        var first = Checksums(ref state, Script);

        Assert.True(buffer.TryLoad(startFrame, ref state));
        var second = Checksums(ref state, Script);

        Assert.Equal(first, second);
    }

    [Fact]
    public void DifferentInputsDivergeThenReloadMatchesAgain()
    {
        var state = Advance(GameState.NewMatch(7), 30, _ => Walk);
        var buffer = new StateBuffer(16);
        buffer.Save(state);
        var startFrame = state.Frame;

        var expected = Checksums(ref state, Script);

        Assert.True(buffer.TryLoad(startFrame, ref state));
        var diverged = Checksums(ref state, OtherScript);
        Assert.NotEqual(expected[^1], diverged[^1]);

        Assert.True(buffer.TryLoad(startFrame, ref state));
        Assert.Equal(expected, Checksums(ref state, Script));
    }

    [Fact]
    public void EveryFieldIsInTheChecksum()
    {
        var baseline = Busy();
        var original = baseline.Checksum();

        void Changes(Func<GameState, GameState> mutate) =>
            Assert.NotEqual(original, mutate(baseline).Checksum());

        Changes(s => { s.Frame++; return s; });
        Changes(s => { s.Rng.NextUInt(); return s; });
        Changes(s => { s.Rng.Increment += 2; return s; });
        for (var p = 0; p < GameState.PlayerCount; p++)
        {
            var i = p;
            Changes(s => { s.Inputs[i].Record(s.Frame - 1, Buttons.HeavyKick); return s; });
            Changes(s => { s.Fighters[i].Position += new FixVec2(Fix.FromRaw(1), Fix.Zero); return s; });
            Changes(s => { s.Fighters[i].Position += new FixVec2(Fix.Zero, Fix.FromRaw(1)); return s; });
            Changes(s => { s.Fighters[i].Velocity += new FixVec2(Fix.FromRaw(1), Fix.Zero); return s; });
            Changes(s => { s.Fighters[i].Velocity += new FixVec2(Fix.Zero, Fix.FromRaw(1)); return s; });
            Changes(s => { s.Fighters[i].Facing = -s.Fighters[i].Facing; return s; });
            Changes(s => { s.Fighters[i].Action++; return s; });
            Changes(s => { s.Fighters[i].ActionFrame++; return s; });
            Changes(s => { s.Fighters[i].Airborne = !s.Fighters[i].Airborne; return s; });
            Changes(s => { s.Fighters[i].AirAttackUsed = !s.Fighters[i].AirAttackUsed; return s; });
        }
        for (var p = 0; p < GameState.MaxProjectiles; p++)
        {
            var i = p;
            Changes(s => { s.Projectiles[i].Active = !s.Projectiles[i].Active; return s; });
            Changes(s => { s.Projectiles[i].Owner++; return s; });
            Changes(s => { s.Projectiles[i].Position += new FixVec2(Fix.FromRaw(1), Fix.Zero); return s; });
            Changes(s => { s.Projectiles[i].Velocity += new FixVec2(Fix.Zero, Fix.FromRaw(1)); return s; });
            Changes(s => { s.Projectiles[i].Frame++; return s; });
        }
        Changes(s => { s.Stage = new Stage(1, 2560, 600, 1280); return s; });
        Changes(s => { s.Stage = new Stage(0, 2561, 600, 1280); return s; });
        Changes(s => { s.Stage = new Stage(0, 2560, 601, 1280); return s; });
        Changes(s => { s.Stage = new Stage(0, 2560, 600, 1281); return s; });
    }

    [Fact]
    public void FightersAreSymmetricInTheChecksum()
    {
        // Swapping who is where must change the checksum, or a desync could hide.
        var state = Busy();
        var swapped = state;
        (swapped.Fighters[0], swapped.Fighters[1]) = (state.Fighters[1], state.Fighters[0]);
        Assert.NotEqual(state.Checksum(), swapped.Checksum());
    }

    // Pins the whole pipeline: if this changes, a change in step, encoding or hashing broke
    // determinism across builds or platforms, or the change was deliberate and the pin moves with it.
    [Fact]
    public void ChecksumIsStableAcrossRuns()
    {
        var state = Advance(GameState.NewMatch(42), 600, i => Script(i % 10));
        Assert.Equal(PinnedChecksum, state.Checksum());
    }

    private const ulong PinnedChecksum = 4721810054345113420;

    private static GameState Busy()
    {
        var state = Advance(GameState.NewMatch(3), 12, i => i % 3 == 0 ? Walk : default);
        state.Projectiles[1] = new ProjectileState
        {
            Active = true,
            Owner = 1,
            Position = new FixVec2(Fix.FromInt(700), Fix.FromInt(560)),
            Velocity = new FixVec2(Fix.FromInt(-6), Fix.Zero),
            Frame = 3,
        };
        return state;
    }

    private static GameState Advance(GameState state, int frames, Func<int, FrameInput> input)
    {
        for (var i = 0; i < frames; i++)
            Simulation.Step(ref state, input(i));
        return state;
    }

    private static ulong[] Checksums(ref GameState state, Func<int, FrameInput> input)
    {
        var sums = new ulong[10];
        for (var i = 0; i < sums.Length; i++)
        {
            Simulation.Step(ref state, input(i));
            sums[i] = state.Checksum();
        }
        return sums;
    }
}
