using System;
using StickFighter.Sim;
using StickFighter.Sim.Characters;
using StickFighter.Sim.Numerics;
using StickFighter.Training;
using Xunit;

namespace StickFighter.Presentation.Tests;

// The frame data display is the tuning tool, so it has to agree with the sim. Each ground normal is
// done at point-blank exactly as the sim's Normals and Blocking tests do it, the analyser watches
// every frame, and its numbers must equal the move data, the baseline advantages those tests check,
// and an independent run of their own measurement.
public class FrameDataAnalyserTests
{
    private static Character Data => Characters.Placeholder;

    // On hit and on block, from reference/frame-data-baseline (as in NormalsTests and BlockingTests).
    // 2HK knocks down on hit, which the baseline leaves out; it's checked against the sim's measure only.
    [Theory]
    [InlineData("5LP", 6, 3)]
    [InlineData("5MP", 5, 2)]
    [InlineData("5HP", 3, -1)]
    [InlineData("5LK", 2, -1)]
    [InlineData("5MK", 1, -2)]
    [InlineData("5HK", 0, -4)]
    [InlineData("2LP", 5, 2)]
    [InlineData("2MP", 4, 1)]
    [InlineData("2HP", -4, -9)]
    [InlineData("2LK", 2, -1)]
    [InlineData("2MK", 0, -2)]
    [InlineData("2HK", null, -7)]
    public void TheAnalyserAgreesWithTheSimOnHitAndOnBlock(string input, int? onHit, int onBlock)
    {
        var move = Data.FindMove(input)!;
        foreach (var blocked in new[] { false, true })
        {
            var (report, simsOwn) = Run(input, blocked);
            Assert.NotNull(report);
            Assert.Equal(input, report!.Move);
            Assert.Equal(blocked, report.Blocked);
            Assert.False(report.MissedFrames);

            // Measured, and equal to the move's data.
            Assert.Equal(move.Startup, report.Startup);
            Assert.Equal(move.Active, report.Active);
            Assert.Equal(move.Recovery, report.Recovery);

            // The advantage equals the sim tests' own measurement, and the baseline.
            Assert.Equal(simsOwn, report.Advantage);
            var expected = blocked ? onBlock : onHit;
            if (expected is not null)
                Assert.Equal(expected, report.Advantage);
        }
    }

    [Fact]
    public void ABlockStringWithAOneFrameGapSaysSo()
    {
        // 5LP is +3 on block and starts up in 4, so a second 5LP as soon as P1 can act leaves P2 one
        // frame free: a 1-frame gap, not a true block string.
        var state = Apart(60);
        var analyser = new FrameDataAnalyser();
        var jab = Data.FindMove("5LP")!;
        for (var frame = 0; frame < 40; frame++)
        {
            // The second press lands on P1's last frame of the first jab, counting the block's hitstop
            // (frozen frames don't advance the move), so it comes out on the first free frame.
            var second = jab.TotalFrames - 1 + jab.HitstopOnBlock;
            var press = frame == 0 || frame == second ? Buttons.LightPunch : Buttons.None;
            Simulation.Step(ref state, new FrameInput(press, Buttons.Right));
            analyser.Observe(state);
        }
        Assert.True(analyser.Last!.Blocked);
        Assert.Equal(jab.Startup - 3, analyser.Last.Gap);
    }

    [Fact]
    public void ALinkReportsItsWindow()
    {
        // 5LP is +6 on hit and starts up in 4: a 5LP into 5LP link has a 3-frame window (6 - 4 + 1).
        var state = Apart(60);
        var analyser = new FrameDataAnalyser();
        var jab = Data.FindMove("5LP")!;
        for (var frame = 0; frame < 40; frame++)
        {
            var second = jab.TotalFrames - 1 + jab.HitstopOnHit; // as above, after the hit's hitstop
            var press = frame == 0 || frame == second ? Buttons.LightPunch : Buttons.None;
            Simulation.Step(ref state, new FrameInput(press, Buttons.None));
            analyser.Observe(state);
        }
        Assert.False(analyser.Last!.Blocked);
        Assert.Equal(2, analyser.Last.Combo);
        Assert.Equal(6 - jab.Startup + 1, analyser.Last.LinkWindow);
    }

    [Fact]
    public void AWhiffAfterABlockIsReportedOnItsOwn()
    {
        // A blocked 5MP, then a 5LP that touches nobody: the report is the jab's, as a whiff, not the
        // jab paired with the old block's advantage.
        var state = Apart(60);
        var analyser = new FrameDataAnalyser();
        for (var frame = 0; frame < 70; frame++)
        {
            var p1 = frame == 0 ? Buttons.MediumPunch : frame == 50 ? Buttons.LightPunch : Buttons.None;
            Simulation.Step(ref state, new FrameInput(p1, frame < 45 ? Buttons.Right : Buttons.None)); // P2 walks off
            analyser.Observe(state);
            if (frame == 40)
            {
                Assert.True(analyser.Last!.Blocked);
                Assert.Equal(2, analyser.Last.Advantage);
            }
        }
        var jab = Data.FindMove("5LP")!;
        var report = analyser.Last!;
        Assert.Equal("5LP", report.Move);
        Assert.False(report.Contact);
        Assert.True(report.Whiffed);
        Assert.Null(report.Advantage);
        Assert.Equal((jab.Startup, jab.Active, jab.Recovery), (report.Startup!.Value, report.Active!.Value, report.Recovery!.Value));
    }

    [Fact]
    public void SkippedFramesAreFlaggedNotGuessed()
    {
        var state = Apart(60);
        var analyser = new FrameDataAnalyser();
        Simulation.Step(ref state, new FrameInput(Buttons.MediumPunch, Buttons.None));
        analyser.Observe(state);
        for (var frame = 0; frame < 30; frame++)
        {
            Simulation.Step(ref state, default);
            if (frame % 2 == 0)
                analyser.Observe(state); // the display missed every other frame
        }
        Assert.True(analyser.Last is null || analyser.Last.MissedFrames);
    }

    private static GameState Apart(int gap)
    {
        var state = GameState.NewMatch(1);
        state.Fighters[0].Position = new FixVec2(Fix.FromInt(1000), state.Fighters[0].Position.Y);
        state.Fighters[1].Position = new FixVec2(Fix.FromInt(1000 + gap), state.Fighters[1].Position.Y);
        return state;
    }

    private static Buttons ButtonFor(string input) => input[1..] switch
    {
        "LP" => Buttons.LightPunch,
        "MP" => Buttons.MediumPunch,
        "HP" => Buttons.HeavyPunch,
        "LK" => Buttons.LightKick,
        "MK" => Buttons.MediumKick,
        _ => Buttons.HeavyKick,
    };

    // P1 does the move at point-blank; P2 stands still (hit) or holds back (low or high as needed).
    // Returns the analyser's report, and the advantage measured the way the sim's tests measure it.
    private static (FrameReport? Report, int? SimsOwn) Run(string input, bool blocked)
    {
        var move = Data.FindMove(input)!;
        var state = Apart(60);
        var hold = input[0] == '2' ? Buttons.Down : Buttons.None;
        var defence = blocked ? Buttons.Right | (move.Guard == Guard.Low ? Buttons.Down : Buttons.None) : Buttons.None;
        var analyser = new FrameDataAnalyser();

        int? contact = null, attackerFree = null, defenderFree = null;
        Simulation.Step(ref state, new FrameInput(ButtonFor(input) | hold, defence));
        analyser.Observe(state);
        for (var i = 0; i < 120; i++)
        {
            var frame = state.Frame;
            var p2Before = state.Fighters[1];
            Simulation.Step(ref state, new FrameInput(hold, defence));
            analyser.Observe(state);

            // The sim tests' measurement, independently.
            var stunnedBefore = p2Before.Blockstun > 0 || p2Before.Hitstun > 0 || p2Before.KnockedDown;
            var p2 = state.Fighters[1];
            if (contact is not null && defenderFree is null && !stunnedBefore)
                defenderFree = frame;
            if (contact is null && (p2.Blockstun > 0 || p2.Hitstun > 0 || p2.KnockedDown))
                contact = frame;
            if (contact is not null && attackerFree is null && state.Fighters[0].Action != (int)FighterAction.Attack)
                attackerFree = frame;
        }
        return (analyser.Last, defenderFree - attackerFree);
    }
}
