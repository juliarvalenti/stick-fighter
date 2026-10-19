using System;
using StickFighter.Sim.Net;
using StickFighter.Sim.Numerics;
using StickFighter.Sim.Replays;
using Xunit;

namespace StickFighter.Sim.Tests;

// Two peers in one process, each with its own RollbackSession, over a seeded fake network. The
// proof is that both end a scripted match exactly where an offline replay of the same inputs ends,
// whatever the network did in between.
public class RollbackSessionTests
{
    private const int MatchFrames = 1800;

    // Both players mashing: held directions and buttons that change every few frames, attacks
    // included, so predictions keep going wrong and rollbacks keep happening.
    private static FrameInput[] Script(ulong seed, int frames)
    {
        var rng = Rng.Create(seed, 11);
        var script = new FrameInput[frames];
        Buttons p1 = Buttons.None, p2 = Buttons.None;
        for (var f = 0; f < frames; f++)
        {
            if (rng.Chance(1, 6))
                p1 = Mash(ref rng);
            if (rng.Chance(1, 6))
                p2 = Mash(ref rng);
            script[f] = new FrameInput(p1, p2);
        }
        return script;
    }

    private static Buttons Mash(ref Rng rng)
    {
        Buttons[] pick =
        {
            Buttons.None, Buttons.Left, Buttons.Right, Buttons.Down, Buttons.Up | Buttons.Right,
            Buttons.LightPunch, Buttons.MediumPunch, Buttons.HeavyKick, Buttons.Down | Buttons.MediumKick,
            Buttons.Left | Buttons.LightPunch | Buttons.LightKick,
        };
        return pick[rng.NextInt(pick.Length)];
    }

    private sealed record Played(RollbackSession A, RollbackSession B, SimulatedNetwork Network, FrameInput[] Inputs, int Ticks);

    // Plays the script on both peers until both have every frame confirmed. Each peer, on each tick,
    // presses what the script has for the frame its input lands on (now + delay). `during` runs
    // before each tick (to break the network for a while, say). `fastEvery`: P2's clock runs fast,
    // ticking an extra time every that many ticks (50 is 2% fast).
    private static Played Play(int pingMs, int jitter = 10, int loss = 2, int reorder = 5, int delay = 2,
        MatchMode mode = MatchMode.Sandbox, Stepper? stepB = null, Action<SimulatedNetwork, int>? during = null, Func<Played, bool>? stop = null,
        bool timeSync = true, int fastEvery = 0)
    {
        var script = Script(7, MatchFrames);
        var network = new SimulatedNetwork(3, pingMs, jitter, loss, reorder);
        var a = new RollbackSession(0, 1, Stage.Training, mode, delay, network.Ends[0], timeSync: timeSync);
        var b = new RollbackSession(1, 1, Stage.Training, mode, delay, network.Ends[1], stepB, timeSync);

        Buttons Press(RollbackSession s)
        {
            var frame = s.Frame + delay;
            return frame < MatchFrames ? script[frame].For(s.LocalPlayer) : Buttons.None;
        }

        var tick = 0;
        for (; tick < 20 * MatchFrames; tick++)
        {
            if (Done(a) && Done(b))
                break;
            if (stop?.Invoke(new Played(a, b, network, script, tick)) == true)
                break;
            during?.Invoke(network, tick);
            foreach (var s in new[] { a, b })
            {
                if (s.Frame < MatchFrames)
                    s.Tick(Press(s));
                else
                    s.Poll();
            }
            if (fastEvery > 0 && tick % fastEvery == fastEvery - 1 && b.Frame < MatchFrames)
                b.Tick(Press(b));
            network.Tick();
        }

        // What the match was, in the end: no input for the first `delay` frames, then the script.
        var inputs = new FrameInput[MatchFrames];
        for (var f = delay; f < MatchFrames; f++)
            inputs[f] = script[f];
        return new Played(a, b, network, inputs, tick);
    }

    private static bool Done(RollbackSession s) => s.Frame == MatchFrames && s.ConfirmedFrame == MatchFrames;

    [Theory]
    [InlineData(0)]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(200)]
    public void BothPeersEndWhereAnOfflineReplayEnds(int pingMs)
    {
        var played = Play(pingMs);
        var offline = Replay.Record(1, Stage.Training, played.Inputs).FinalChecksum;

        Assert.True(Done(played.A) && Done(played.B), $"finished in {played.Ticks} ticks");
        Assert.Equal(offline, played.A.State.Checksum());
        Assert.Equal(offline, played.B.State.Checksum());
        Assert.Null(played.A.Desynced);
        Assert.Null(played.B.Desynced);
        Assert.True(played.Network.Dropped > 0 || pingMs == 0, "the network lost messages");

        foreach (var s in new[] { played.A, played.B })
        {
            Assert.InRange(s.DeepestRollback, 0, RollbackSession.MaxRollback);
            if (pingMs > 0)
                Assert.True(s.Rollbacks > 0, "predictions went wrong and were rolled back");
        }
    }

    // A whole versus match: rounds, KOs and the timer roll back like everything else.
    [Fact]
    public void AVersusMatchSurvivesRollbackToo()
    {
        var played = Play(120, mode: MatchMode.Versus);
        var offline = Replay.Record(1, Stage.Training, played.Inputs, MatchMode.Versus).FinalChecksum;
        Assert.Equal(offline, played.A.State.Checksum());
        Assert.Equal(offline, played.B.State.Checksum());
    }

    // Input delay trades latency for rollbacks: the same match over the same network rolls back
    // less with 4 frames of delay than with none.
    [Fact]
    public void InputDelayMeansFewerRollbacks()
    {
        var none = Play(120, delay: 0);
        var four = Play(120, delay: 4);
        Assert.True(four.A.ResimulatedFrames < none.A.ResimulatedFrames,
            $"resimulated {four.A.ResimulatedFrames} frames with delay 4, {none.A.ResimulatedFrames} with none");
    }

    // Past the cap: a ping that would need more than 8 frames of prediction. The peers stall
    // rather than predict further, never roll back more than 8, and still end in sync.
    [Fact]
    public void BeyondTheCapThePeersStallAndStayInSync()
    {
        var played = Play(400, delay: 0);
        var offline = Replay.Record(1, Stage.Training, played.Inputs).FinalChecksum;
        Assert.True(played.A.StalledTicks > 0 && played.B.StalledTicks > 0, "both stalled");
        Assert.InRange(played.A.DeepestRollback, 1, RollbackSession.MaxRollback);
        Assert.Equal(offline, played.A.State.Checksum());
        Assert.Equal(offline, played.B.State.Checksum());
    }

    // Everything lost for half a second: both stall, then recover from the resent inputs.
    [Fact]
    public void AfterABlackoutThePeersRecover()
    {
        var played = Play(120, during: (network, tick) => network.LossPercent = tick is >= 300 and < 330 ? 100 : 2);
        var offline = Replay.Record(1, Stage.Training, played.Inputs).FinalChecksum;
        Assert.True(played.A.StalledTicks >= 20, $"stalled {played.A.StalledTicks} ticks");
        Assert.Equal(offline, played.A.State.Checksum());
        Assert.Equal(offline, played.B.State.Checksum());
    }

    // P2's machine gets frame 500 wrong (a bug: a health point gone). The next checksum the peers
    // compare catches it, and the two states at that frame name the field.
    [Fact]
    public void ADesyncIsCaughtAndTheDiffNamesTheField()
    {
        Stepper broken = (ref GameState state, in FrameInput input) =>
        {
            Simulation.Step(ref state, input);
            if (state.Frame == 500)
                state.Fighters[1].Health--;
        };
        var played = Play(60, stepB: broken, stop: p => p.A.Desynced is not null && p.B.Desynced is not null);

        var desync = played.A.Desynced;
        Assert.NotNull(desync);
        Assert.Equal(510, desync.Value.Frame); // the first checksum frame after it
        Assert.Equal(desync.Value.Frame, played.B.Desynced!.Value.Frame);
        Assert.True(played.A.TryGetChecksumState(desync.Value.Frame, out var good));
        Assert.True(played.B.TryGetChecksumState(desync.Value.Frame, out var bad));
        Assert.Contains(StateDiff.Between(good, bad), line => line.StartsWith("Fighters[1].Health: ", StringComparison.Ordinal));
    }

    // P2's clock runs 2% fast. Without time sync it pulls ahead until it's predicting the full 8
    // frames, then stalls, again and again, dropping inputs. With time sync it skips the odd tick
    // instead, the advantage stays within a frame or two, and stalls all but vanish.
    [Fact]
    public void ClockSkewWithoutTimeSyncDriftsIntoStalls()
    {
        var played = Play(120, timeSync: false, fastEvery: 50);
        Assert.True(played.B.StalledTicks >= 20, $"P2 stalled {played.B.StalledTicks} ticks");
        Assert.True(played.B.WidestAdvantage >= 4, $"P2 got {played.B.WidestAdvantage} frames ahead");
        Assert.Equal(0, played.B.SkippedTicks);
    }

    [Fact]
    public void ClockSkewWithTimeSyncStaysLevel()
    {
        var played = Play(120, fastEvery: 50);
        var offline = Replay.Record(1, Stage.Training, played.Inputs).FinalChecksum;
        Assert.True(played.B.SkippedTicks > 0, "P2, running fast, skipped ticks to stay level");
        Assert.True(played.B.StalledTicks <= 2, $"P2 stalled {played.B.StalledTicks} ticks");
        Assert.True(played.A.StalledTicks <= 2, $"P1 stalled {played.A.StalledTicks} ticks");
        Assert.InRange(played.B.WidestAdvantage, 0, 2);
        Assert.Equal(offline, played.A.State.Checksum());
        Assert.Equal(offline, played.B.State.Checksum());
    }

    // At 200 ms with time sync: next to no stalls, and the rollbacks shared between the peers.
    [Fact]
    public void At200MsStallsAreRareAndRollbacksShared()
    {
        var played = Play(200);
        Assert.True(played.A.StalledTicks + played.B.StalledTicks <= 2, $"stalls {played.A.StalledTicks} and {played.B.StalledTicks}");
        var (a, b) = (played.A.Rollbacks, played.B.Rollbacks);
        Assert.True(a > 0 && b > 0 && a <= 2 * b && b <= 2 * a, $"rollbacks {a} and {b}");
    }

    // A real peer can't diff the other's state, but it can say where the desync began: after a
    // checksum mismatch the peers swap their per-frame checksums since the last match, and both name
    // frame 500, where P2's broken step took the health point.
    [Fact]
    public void ADesyncReportSaysWhichFrameFirstDiverged()
    {
        Stepper broken = (ref GameState state, in FrameInput input) =>
        {
            Simulation.Step(ref state, input);
            if (state.Frame == 500)
                state.Fighters[1].Health--;
        };
        var played = Play(60, stepB: broken,
            stop: p => p.A.Desynced is { FirstDiverged: >= 0 } && p.B.Desynced is { FirstDiverged: >= 0 });

        Assert.Equal(500, played.A.Desynced!.Value.FirstDiverged);
        Assert.Equal(500, played.B.Desynced!.Value.FirstDiverged);
        Assert.Equal(510, played.A.Desynced!.Value.Frame);
    }

    // The ping each peer measures (input sent to input acknowledged) tracks the network's actual round
    // trip (whole frames each way, so 120 ms is 3 frames each way, 100 ms), to within two frames
    // either way: jitter, and the ack going out on the other's next tick.
    [Theory]
    [InlineData(0)]
    [InlineData(120)]
    [InlineData(200)]
    public void ThePingMatchesTheRoundTrip(int pingMs)
    {
        var played = Play(pingMs);
        var roundTrip = 2 * played.Network.OneWayFrames * 1000 / GameInfo.TicksPerSecond;
        foreach (var s in new[] { played.A, played.B })
            Assert.InRange(s.PingMs, roundTrip - 34, roundTrip + 34);
    }

    [Fact]
    public void TheNetworkIsDeterministic()
    {
        var first = Play(120);
        var second = Play(120);
        Assert.Equal(first.Ticks, second.Ticks);
        Assert.Equal(first.A.Rollbacks, second.A.Rollbacks);
        Assert.Equal(first.Network.Dropped, second.Network.Dropped);
    }
}
