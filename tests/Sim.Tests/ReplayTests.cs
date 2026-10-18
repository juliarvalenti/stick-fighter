using System;
using StickFighter.Sim.Combat;
using StickFighter.Sim.Replays;
using Xunit;

namespace StickFighter.Sim.Tests;

public class ReplayTests
{
    private sealed class Pattern : IInputSource
    {
        public FrameInput Read(int frame) => (frame / 10 % 3) switch
        {
            0 => new FrameInput(Buttons.Right, Buttons.Left | Buttons.LightPunch),
            1 => new FrameInput(Buttons.Right, Buttons.Left),
            _ => default,
        };
    }

    private static Replay Recorded(int frames)
    {
        var recorder = new ReplayRecorder(11, Stage.Training, new Pattern());
        var state = GameState.NewMatch(11);
        var driver = new TickDriver();
        while (state.Frame < frames)
            driver.Update(ref state, 16_667, recorder);
        return recorder.Build();
    }

    [Fact]
    public void ARecordedMatchReplaysTheSame()
    {
        var replay = Recorded(240);
        Assert.Null(replay.Verify());
    }

    // Behavior every 60 frames and on the last, not a checksum per frame: 1,800 frames is 30 lines.
    [Fact]
    public void CheckpointsAreSparse()
    {
        var replay = Replay.Record(11, Stage.Training, Recorded(1800).Inputs);
        Assert.Equal(30, replay.Checkpoints.Count);
        Assert.Equal(59, replay.Checkpoints[0].Frame);
        Assert.Equal(1799, replay.Checkpoints[^1].Frame);
        Assert.Equal(2, Replay.Record(11, Stage.Training, Recorded(70).Inputs).Checkpoints.Count); // 59 and the last, 69
    }

    [Fact]
    public void RecordingMatchesTheLiveState()
    {
        var recorder = new ReplayRecorder(11, Stage.Training, new Pattern());
        var state = GameState.NewMatch(11);
        var driver = new TickDriver();
        while (state.Frame < 120)
            driver.Update(ref state, 16_667, recorder);

        Assert.Equal(state.Checksum(), recorder.Build().FinalChecksum);
    }

    [Fact]
    public void TextRoundTripsExactly()
    {
        var replay = Recorded(200);
        var text = replay.Write();
        var read = Replay.Read(text);

        Assert.Equal(replay.Seed, read.Seed);
        Assert.Equal(replay.Frames, read.Frames);
        Assert.Equal(replay.FinalChecksum, read.FinalChecksum);
        Assert.Equal(replay.Checkpoints.Count, read.Checkpoints.Count);
        Assert.Equal(text, read.Write());
    }

    [Fact]
    public void ACheckpointReadsAsBehavior()
    {
        var text = Replay.Record(1, Stage.Training, new FrameInput[60]).Write();
        Assert.Contains("at 59 p1 1080 600 1 Stand 60 1000 p2 1480 600 -1 Stand 60 1000 match Fight 0 0 0 0\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void InputsAreRunLengthEncoded()
    {
        var text = Recorded(60).Write();
        Assert.Contains("input 0008 0014 10\n", text); // p1 Right, p2 Left + LP, for 10 frames
    }

    // A changed input shows at the first checkpoint after it, naming what differs; a trace of that
    // range on the original and the tampered replay narrows it to the frame.
    [Fact]
    public void AChangedInputShowsAtTheNextCheckpointAndTraceFindsTheFrame()
    {
        var replay = Recorded(100);
        var inputs = new FrameInput[replay.Frames];
        for (var i = 0; i < inputs.Length; i++)
            inputs[i] = replay.Inputs[i];
        inputs[42] = new FrameInput(Buttons.HeavyKick, Buttons.Left); // P1 kicks instead of walking in

        var tampered = new Replay(replay.Seed, replay.DataHash, replay.Stage, inputs, replay.Checkpoints, replay.FinalChecksum);
        var mismatch = tampered.Verify();
        Assert.NotNull(mismatch);
        Assert.Equal(59, mismatch.Value.Frame);
        Assert.Contains(mismatch.Value.Differences, d => d.StartsWith("P1 action: ", StringComparison.Ordinal));

        var before = System.Linq.Enumerable.ToList(replay.Trace(0, 59));
        var after = System.Linq.Enumerable.ToList(tampered.Trace(0, 59));
        Assert.Equal(42, before.FindIndex(line => !after.Contains(line)));
    }

    // Same behavior at every checkpoint but a different final state: what a new field does. It's
    // reported as such, not as a divergence somewhere.
    [Fact]
    public void AnEncodingOnlyDifferenceIsReportedAsSuch()
    {
        var replay = Recorded(100);
        var other = new Replay(replay.Seed, replay.DataHash, replay.Stage, replay.Inputs, replay.Checkpoints, replay.FinalChecksum ^ 1);
        var mismatch = replay.Compare(other);
        Assert.NotNull(mismatch);
        Assert.Equal(-1, mismatch.Value.Frame);
        Assert.StartsWith("final checksum", mismatch.Value.Differences[0], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not-a-replay 1\nseed 1\nframes 0\nfinal 0\n")]
    [InlineData("stick-fighter-replay 99\nseed 1\ndata 00\nframes 0\nfinal 0\n")]
    [InlineData("stick-fighter-replay 5\nseed 1\ndata 00\nstage 0 2560 600 1280\nmode training\nframes 2\ninput 0000 0000 1\nfinal 00\n")] // too few inputs
    [InlineData("stick-fighter-replay 5\nseed 1\ndata 00\nstage 0 2560 600 1280\nmode training\nframes 1\ninput 0000 0000 1\nfinal 00\n")] // no checkpoint
    [InlineData("stick-fighter-replay 5\nseed 1\ndata 00\nstage 0 2560 600 1280\nmode training\nframes 1\ninput 0000 0000 1\nat 0 p1 1\nfinal 00\n")] // short checkpoint
    [InlineData("stick-fighter-replay 5\nseed 1\ndata 00\nstage 0 2560 600 1280\nmode training\nframes 0\nbogus line\n")]
    [InlineData("stick-fighter-replay 5\nseed 1\ndata 00\nstage 0 2560 600 1280\nmode training\nframes 0\n")] // no final checksum
    [InlineData("stick-fighter-replay 5\nseed 1\nstage 0 2560 600 1280\nmode training\nframes 0\nfinal 0\n")] // no data line
    [InlineData("stick-fighter-replay 5\nseed 1\ndata 00\nmode training\nframes 0\nfinal 0\n")] // no stage line
    [InlineData("stick-fighter-replay 5\nseed 1\ndata 00\nstage 0 2560 600\nmode training\nframes 0\nfinal 0\n")] // short stage line
    [InlineData("stick-fighter-replay 5\nseed 1\ndata 00\nstage 0 2560 600 1280\nframes 0\nfinal 0\n")] // no mode line
    [InlineData("stick-fighter-replay 5\nseed 1\ndata 00\nstage 0 2560 600 1280\nmode arcade\nframes 0\nfinal 0\n")] // unknown mode
    [InlineData("stick-fighter-replay 4\nseed 1\ndata 00\nstage 0 2560 600 1280\nmode training\nframes 1\ninput 0000 0000 1\nchecksum 00\nfinal 00\n")] // old: rerecord it
    public void MalformedReplaysAreRejected(string text)
    {
        Assert.Throws<FormatException>(() => Replay.Read(text));
    }

    // The well-formed version of the cases above, so each one fails for the reason it gives.
    [Fact]
    public void AMinimalReplayReads()
    {
        var replay = Replay.Read("stick-fighter-replay 5\nseed 1\ndata 00\nstage 0 2560 600 1280\nmode versus\nframes 0\nfinal 00\n");
        Assert.Equal(MatchMode.Versus, replay.Mode);
        Assert.Empty(replay.Checkpoints);
    }

    [Fact]
    public void Format4IsUpgradedByRerecordAsPerFrameChecksums()
    {
        var old = Replay.Read("stick-fighter-replay 4\nseed 1\ndata 00\nstage 0 2560 600 1280\nmode versus\nframes 1\ninput 0000 0000 1\nchecksum 00\nfinal 00\n", allowOldVersions: true);
        Assert.Equal(MatchMode.Versus, old.Mode);
        Assert.Empty(old.Checkpoints);
    }

    [Fact]
    public void ReplaysRecordTheStage()
    {
        var stage = new Stage(100, 1900, 580, 900);
        var replay = Replay.Record(5, stage, Recorded(120).Inputs);
        Assert.Contains("stage 100 1900 580 900\n", replay.Write(), StringComparison.Ordinal);

        var read = Replay.Read(replay.Write());
        Assert.Equal(stage, read.Stage);
        Assert.Null(read.Verify());
    }

    [Fact]
    public void TheStageIsInTheChecksum()
    {
        // The same inputs on another stage give another final checksum, so a replay can't quietly
        // pass on a stage it wasn't recorded on.
        var inputs = Recorded(10).Inputs;
        var wider = Replay.Record(11, new Stage(0, 2560, 600, 1281), inputs);
        Assert.NotEqual(Replay.Record(11, Stage.Training, inputs).FinalChecksum, wider.FinalChecksum);
    }

    [Fact]
    public void Version2IsRefusedButRerecordReadsItOnTheTrainingStage()
    {
        const string v2 = "stick-fighter-replay 2\nseed 1\ndata 00\nframes 1\ninput 0000 0000 1\nchecksum 00\nfinal 0000000000000000\n";
        var error = Assert.Throws<FormatException>(() => Replay.Read(v2));
        Assert.Contains("rerecord", error.Message, StringComparison.Ordinal);
        Assert.Equal(Stage.Training, Replay.Read(v2, allowOldVersions: true).Stage);
    }

    [Fact]
    public void ReplaysRecordTheCharacterDataHash()
    {
        var replay = Recorded(30);
        Assert.Equal(Characters.Characters.Placeholder.DataHash, replay.DataHash);
        Assert.True(replay.MatchesLoadedData);
        Assert.Equal(replay.DataHash, Replay.Read(replay.Write()).DataHash);
    }

    [Fact]
    public void AReplayFromOtherDataIsFlagged()
    {
        var replay = Recorded(30);
        var other = new Replay(replay.Seed, replay.DataHash ^ 1, replay.Stage, replay.Inputs, replay.Checkpoints, replay.FinalChecksum);
        Assert.False(other.MatchesLoadedData);
    }

    [Fact]
    public void Version1IsRefusedWithAClearMessageButRerecordCanReadIt()
    {
        const string v1 = "stick-fighter-replay 1\nseed 1\nframes 1\ninput 0000 0000 1\nchecksum 00\nfinal 0000000000000000\n";
        var error = Assert.Throws<FormatException>(() => Replay.Read(v1));
        Assert.Contains("rerecord", error.Message, StringComparison.Ordinal);

        var old = Replay.Read(v1, allowOldVersions: true);
        Assert.Equal(0UL, old.DataHash);
        Assert.Equal(1, old.Frames);
    }

    // The committed scripted replays show what their names say.
    [Theory]
    [InlineData("hit", false, true)]
    [InlineData("whiff", false, false)]
    [InlineData("trade", true, true)]
    [InlineData("block-stand", false, false)]
    [InlineData("block-crouch-low", false, false)]
    [InlineData("low-hits-standing-block", false, true)]
    [InlineData("proximity-guard", false, false)]
    [InlineData("plink-link", false, true)]
    [InlineData("counter-hit", false, true)]
    [InlineData("combo-scaling", false, true)]
    [InlineData("crouch-hitstun", false, true)]
    [InlineData("air-no-juggle", true, false)]
    [InlineData("sweep-knockdown", false, true)]
    [InlineData("quick-rise", true, false)]
    [InlineData("dizzy", false, true)]
    [InlineData("throw", false, true)]
    [InlineData("throw-back", false, true)]
    [InlineData("throw-meaty", false, true)]
    [InlineData("throw-tech-first", false, false)]
    [InlineData("throw-tech-last", false, false)]
    [InlineData("throw-whiff-punished", true, false)]
    [InlineData("throw-hitstun", false, true)]
    [InlineData("throw-same-frame", false, false)]
    [InlineData("throw-counterhit", false, true)]
    public void ScriptedReplaysCoverTheirCase(string name, bool p1Hurt, bool p2Hurt)
    {
        var replay = Replay.Read(System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "replays", name + ".sfr")));
        var state = GameState.NewMatch(replay.Seed, replay.Stage);
        foreach (var input in replay.Inputs)
            Simulation.Step(ref state, input);

        var full = Characters.Characters.Placeholder.Stats.Health;
        Assert.Equal(p1Hurt, state.Fighters[0].Health < full);
        Assert.Equal(p2Hurt, state.Fighters[1].Health < full);
    }

    [Fact]
    public void TheCornerPushbackReplayEndsWithTheDefenderPinned()
    {
        var replay = Replay.Read(System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "replays", "pushback-corner.sfr")));
        var state = GameState.NewMatch(replay.Seed, replay.Stage);
        foreach (var input in replay.Inputs)
            Simulation.Step(ref state, input);

        Assert.Equal(Collision.Pushing.MaxX(state.Stage), state.Fighters[1].Position.X);
        Assert.True(state.Fighters[1].Health < Characters.Characters.Placeholder.Stats.Health);
        Assert.True(state.Fighters[1].Position.X - state.Fighters[0].Position.X > Collision.Body.PushHalfWidth * 2);
    }

    // Every frame of a committed replay: the state after each step, and the inputs.
    private static (System.Collections.Generic.List<GameState> States, Replay Replay) Play(string name)
    {
        var replay = Replay.Read(System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "replays", name + ".sfr")));
        var state = GameState.NewMatch(replay.Seed, replay.Stage);
        var states = new System.Collections.Generic.List<GameState>();
        foreach (var input in replay.Inputs)
        {
            Simulation.Step(ref state, input);
            states.Add(state);
        }
        return (states, replay);
    }

    private static int ThrowDamage => Characters.Characters.Placeholder.FindMove("LP+LK")!.Damage;
    private static int FullHealth => Characters.Characters.Placeholder.Stats.Health;

    // The first frame P2 is held in a throw, or -1.
    private static int GrabFrame(System.Collections.Generic.List<GameState> states) =>
        states.FindIndex(s => s.Fighters[1].Action == (int)Characters.FighterAction.Thrown);

    [Theory]
    [InlineData("throw", false)]
    [InlineData("throw-back", true)]
    [InlineData("throw-meaty", false)]
    public void TheThrowReplaysLand(string name, bool behind)
    {
        var (states, _) = Play(name);
        Assert.True(GrabFrame(states) >= 0);
        Assert.Contains(states, s => s.Fighters[1].KnockedDown);
        var end = states[^1];
        Assert.Equal(FullHealth - ThrowDamage, end.Fighters[1].Health);
        Assert.False(end.Fighters[1].KnockedDown); // up again by the end
        Assert.Equal(behind, end.Fighters[1].Position.X < end.Fighters[0].Position.X);
    }

    // The meaty throw is active on P2's last blockstun frame and doesn't grab; it grabs the next.
    [Fact]
    public void TheMeatyThrowFailsInBlockstunThenGrabs()
    {
        var (states, _) = Play("throw-meaty");
        var grab = GrabFrame(states);
        var throwMove = Characters.Characters.Placeholder.FindMove("LP+LK")!;
        var before = states[grab - 1];
        Assert.True(states[grab - 2].Fighters[1].Blockstun > 0); // in blockstun as that frame began
        Assert.True(throwMove.IsActive(before.Fighters[0].ActionFrame));
        Assert.Equal(-1, before.Fighters[1].Grab);
    }

    [Theory]
    [InlineData("throw-tech-first", -4)]
    [InlineData("throw-tech-last", 7)]
    public void TheTechReplaysTechAtTheEdgesOfTheWindow(string name, int pressRelativeToGrab)
    {
        var (states, replay) = Play(name);
        var grab = GrabFrame(states);
        Assert.True(grab >= 0);
        var press = System.Linq.Enumerable.ToList(replay.Inputs).FindIndex(i => i.Player2.Has(Buttons.LightPunch | Buttons.LightKick));
        Assert.Equal(pressRelativeToGrab, press - grab);
        Assert.DoesNotContain(states, s => s.Fighters[1].KnockedDown);
        Assert.Equal(FullHealth, states[^1].Fighters[1].Health);
    }

    // Grabbed in a heavy kick's startup, P2 presses LP+LK inside the window, and the throw lands anyway.
    [Fact]
    public void ACounterHitThrowCantBeTeched()
    {
        var (states, replay) = Play("throw-counterhit");
        var grab = GrabFrame(states);
        Assert.Equal(Characters.Characters.Placeholder.FindMove("5HK"), Characters.Characters.Placeholder.Moves[states[grab - 1].Fighters[1].Move]);
        var press = System.Linq.Enumerable.ToList(replay.Inputs).FindIndex(i => i.Player2.Has(Buttons.LightPunch | Buttons.LightKick));
        var stats = Characters.Characters.Placeholder.Stats;
        Assert.InRange(press - grab, stats.TechFrame + 1 - stats.TechWindow, stats.TechFrame);
        Assert.Contains(states, s => s.Fighters[1].KnockedDown);
        Assert.Equal(FullHealth - ThrowDamage, states[^1].Fighters[1].Health);
    }

    // The plinked jab comes out as a jab (not MP) and lands before P2 leaves hitstun: a combo.
    [Fact]
    public void APlinkedJabLinks()
    {
        var (states, _) = Play("plink-link");
        var jab = Characters.Characters.Placeholder.FindMove("5LP")!;
        var hits = 0;
        for (var i = 1; i < states.Count; i++)
        {
            if (states[i].Fighters[1].Health >= states[i - 1].Fighters[1].Health)
                continue;
            hits++;
            if (hits == 2)
                Assert.True(states[i - 1].Fighters[1].Hitstun > 0, "P2 was free before the second jab landed");
        }
        Assert.Equal(2, hits);
        Assert.Equal(FullHealth - 2 * jab.Damage, states[^1].Fighters[1].Health);
    }

    // Hit states. The frames on which P2 (or P1) lost health, with how much.
    private static System.Collections.Generic.List<(int Frame, int Damage)> Hits(System.Collections.Generic.List<GameState> states, int defender)
    {
        var hits = new System.Collections.Generic.List<(int, int)>();
        var health = FullHealth;
        for (var i = 0; i < states.Count; i++)
        {
            var now = states[i].Fighters[defender].Health;
            if (now < health)
                hits.Add((i, health - now));
            health = now;
        }
        return hits;
    }

    [Fact]
    public void TheCounterHitReplayDoesMoreDamageAndHitstun()
    {
        var (states, _) = Play("counter-hit");
        var jab = Characters.Characters.Placeholder.FindMove("5LP")!;
        var (frame, damage) = Assert.Single(Hits(states, 1));
        Assert.Equal(jab.Damage * 125 / 100, damage);
        Assert.True(states[frame].Fighters[1].CounterHit);
        Assert.Equal(jab.Hitstun - 1 + 1, states[frame].Fighters[1].Hitstun);
    }

    [Fact]
    public void TheComboReplayScalesItsThirdHit()
    {
        var (states, _) = Play("combo-scaling");
        var hits = Hits(states, 1);
        Assert.Equal(new[] { 20, 20, 16 }, hits.ConvertAll(h => h.Damage));
        Assert.Equal(3, states[hits[2].Frame].Fighters[1].ComboHits);
        Assert.Equal(56, states[hits[2].Frame].Fighters[1].ComboDamage);
    }

    [Fact]
    public void TheCrouchReplayAddsTheCrouchHitstunBonus()
    {
        var (states, _) = Play("crouch-hitstun");
        var crJab = Characters.Characters.Placeholder.FindMove("2LP")!;
        var (frame, _) = Assert.Single(Hits(states, 1));
        Assert.Equal(crJab.Hitstun - 1 + Characters.Characters.Placeholder.Stats.CrouchHitstunBonus, states[frame].Fighters[1].Hitstun);
    }

    // P1 is hit rising and falls with juggle count 1. P2's next jab is active, its hitbox on P1's
    // body, while P1 falls, and does nothing; P1 lands knocked down, and nothing hits it there.
    [Fact]
    public void TheAirHitReplayCantBeJuggled()
    {
        var (states, _) = Play("air-no-juggle");
        var hits = Hits(states, 0);
        var (first, _) = Assert.Single(hits);
        Assert.True(states[first].Fighters[0].Airborne);
        Assert.Equal(1, states[first].Fighters[0].Juggle);
        var landed = states.FindIndex(first, s => !s.Fighters[0].Airborne);
        Assert.True(states[landed].Fighters[0].KnockedDown);

        var jab = Characters.Characters.Placeholder.FindMove("5LP")!;
        var passedThrough = 0;
        for (var i = first + 1; i < landed; i++)
        {
            var s = states[i];
            var p2 = s.Fighters[1];
            if (s.Fighters[0].Juggle == 0 || p2.Move < 0 || !jab.IsActive(p2.ActionFrame) || p2.MoveHit)
                continue;
            var contacts = Collision.Contacts.Find(s, Collision.Body.Shapes(s.Fighters[0]), Collision.Body.Shapes(p2));
            if (contacts.Count > 0)
                passedThrough++;
        }
        Assert.True(passedThrough > 0, "P2's jab should overlap the falling P1 at least once");
    }

    // Unfrozen frames from `from` while `down` holds for the fighter: how long it lay down.
    private static int UnfrozenWhile(System.Collections.Generic.List<GameState> states, int from, Func<GameState, bool> down)
    {
        var frames = 0;
        for (var i = from; i < states.Count && down(states[i]); i++)
        {
            if (!Hitstop.Frozen(states[i], states[i].Frame - 1))
                frames++;
        }
        return frames;
    }

    [Fact]
    public void TheSweepReplayIsAHardKnockdownDespiteMashing()
    {
        var (states, _) = Play("sweep-knockdown");
        var (frame, damage) = Assert.Single(Hits(states, 1));
        Assert.Equal(Characters.Characters.Placeholder.FindMove("2HK")!.Damage, damage);
        Assert.True(states[frame].Fighters[1].KnockedDown);
        var stats = Characters.Characters.Placeholder.Stats;
        Assert.Equal(stats.KnockdownFrames, UnfrozenWhile(states, frame, s => s.Fighters[1].KnockedDown));
    }

    [Fact]
    public void TheQuickRiseReplayGetsUpSooner()
    {
        var (states, _) = Play("quick-rise");
        var (hit, _) = Assert.Single(Hits(states, 0));
        Assert.Equal(1, states[hit].Fighters[0].Juggle);
        var landed = states.FindIndex(hit, s => s.Fighters[0].KnockedDown);
        var stats = Characters.Characters.Placeholder.Stats;
        Assert.Equal(stats.QuickRiseFrames, UnfrozenWhile(states, landed, s => s.Fighters[0].KnockedDown));
    }

    [Fact]
    public void TheDizzyReplayFillsTheGauge()
    {
        var (states, _) = Play("dizzy");
        var hits = Hits(states, 1);
        Assert.Equal(5, hits.Count);
        Assert.All(hits, h => Assert.Equal(100, h.Damage)); // five separate combos, none scaled
        var dizzy = states.FindIndex(s => s.Fighters[1].Action == (int)Characters.FighterAction.Dizzy);
        Assert.True(dizzy > hits[4].Frame);
        Assert.Equal(0, states[dizzy].Fighters[1].Stun);
        var stats = Characters.Characters.Placeholder.Stats;
        Assert.Equal(stats.DizzyFrames, UnfrozenWhile(states, dizzy, s => s.Fighters[1].Action == (int)Characters.FighterAction.Dizzy));
    }

    [Fact]
    public void TheWhiffedThrowIsPunishedInItsRecovery()
    {
        var (states, _) = Play("throw-whiff-punished");
        Assert.Equal(-1, GrabFrame(states));
        var hit = states.FindIndex(s => s.Fighters[0].Hitstun > 0);
        Assert.Equal(Normals.Throw, states[hit - 1].Fighters[0].Move);
        Assert.Equal(FullHealth - Characters.Characters.Placeholder.FindMove("5HK")!.Damage, states[^1].Fighters[0].Health);
    }

    [Fact]
    public void AThrowOnAFighterInHitstunWhiffs()
    {
        var (states, _) = Play("throw-hitstun");
        var throwMove = Characters.Characters.Placeholder.FindMove("LP+LK")!;
        Assert.Contains(states, s => s.Fighters[0].Move == Normals.Throw && throwMove.IsActive(s.Fighters[0].ActionFrame) && s.Fighters[1].Hitstun > 0);
        Assert.Equal(-1, GrabFrame(states));
        Assert.Equal(FullHealth - Characters.Characters.Placeholder.FindMove("5LP")!.Damage, states[^1].Fighters[1].Health);
    }

    [Fact]
    public void SameFrameThrowsTech()
    {
        var (states, _) = Play("throw-same-frame");
        Assert.Contains(states, s => s.Fighters[0].Move == Normals.Throw && s.Fighters[1].Move == Normals.Throw);
        Assert.DoesNotContain(states, s => s.Fighters[0].Grab >= 0 || s.Fighters[1].Grab >= 0);
        Assert.Equal(-1, states[^1].Fighters[0].Move);
        var center = states[^1].Stage.Center;
        Assert.Equal(center - states[^1].Fighters[0].Position.X, states[^1].Fighters[1].Position.X - center);
    }

    private static string RepoRoot()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "StickFighter.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new System.IO.DirectoryNotFoundException("repo root");
    }
}
