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
    public void ARecordedMatchReplaysToTheSameChecksums()
    {
        var replay = Recorded(240);
        Assert.Equal(-1, replay.FirstDivergence(out _, out _));
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
        Assert.Equal(replay.Checksums, read.Checksums);
        Assert.Equal(text, read.Write());
    }

    [Fact]
    public void InputsAreRunLengthEncoded()
    {
        var text = Recorded(60).Write();
        Assert.Contains("input 0008 0014 10\n", text); // p1 Right, p2 Left + LP, for 10 frames
    }

    [Fact]
    public void AChangedInputShowsTheFirstDivergingFrame()
    {
        var replay = Recorded(100);
        var inputs = new FrameInput[replay.Frames];
        for (var i = 0; i < inputs.Length; i++)
            inputs[i] = replay.Inputs[i];
        inputs[42] = new FrameInput(Buttons.Left, Buttons.Right);

        var tampered = new Replay(replay.Seed, replay.DataHash, replay.Stage, inputs, replay.Checksums);
        Assert.Equal(42, tampered.FirstDivergence(out var expected, out var actual));
        Assert.NotEqual(expected, actual);
    }

    [Theory]
    [InlineData("not-a-replay 1\nseed 1\nframes 0\nfinal 0\n")]
    [InlineData("stick-fighter-replay 99\nseed 1\ndata 00\nframes 0\nfinal 0\n")]
    [InlineData("stick-fighter-replay 3\nseed 1\ndata 00\nstage 0 2560 600 1280\nframes 2\ninput 0000 0000 1\nchecksum 00\nfinal 00\n")]
    [InlineData("stick-fighter-replay 3\nseed 1\ndata 00\nstage 0 2560 600 1280\nframes 0\nbogus line\n")]
    [InlineData("stick-fighter-replay 3\nseed 1\nstage 0 2560 600 1280\nframes 0\nfinal 0\n")] // no data line
    [InlineData("stick-fighter-replay 3\nseed 1\ndata 00\nframes 0\nfinal 0\n")] // no stage line
    [InlineData("stick-fighter-replay 3\nseed 1\ndata 00\nstage 0 2560 600\nframes 0\nfinal 0\n")] // short stage line
    public void MalformedReplaysAreRejected(string text)
    {
        Assert.Throws<FormatException>(() => Replay.Read(text));
    }

    [Fact]
    public void ReplaysRecordTheStage()
    {
        var stage = new Stage(100, 1900, 580, 900);
        var replay = Replay.Record(5, stage, Recorded(120).Inputs);
        Assert.Contains("stage 100 1900 580 900\n", replay.Write(), StringComparison.Ordinal);

        var read = Replay.Read(replay.Write());
        Assert.Equal(stage, read.Stage);
        Assert.Equal(-1, read.FirstDivergence(out _, out _));
    }

    [Fact]
    public void TheStageIsInTheChecksum()
    {
        // The same inputs on another stage give other checksums from the very first frame, so a
        // replay can't quietly pass on a stage it wasn't recorded on.
        var inputs = Recorded(10).Inputs;
        var wider = Replay.Record(11, new Stage(0, 2560, 600, 1281), inputs);
        Assert.NotEqual(Replay.Record(11, Stage.Training, inputs).Checksums[0], wider.Checksums[0]);
    }

    [Fact]
    public void Version2IsRefusedButRerecordReadsItOnTheTrainingStage()
    {
        const string v2 = "stick-fighter-replay 2\nseed 1\ndata 00\nframes 1\ninput 0000 0000 1\nchecksum 00\nfinal 0000000000000000\n";
        var error = Assert.Throws<FormatException>(() => Replay.Read(v2));
        Assert.Contains("stage", error.Message, StringComparison.Ordinal);
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
        var other = new Replay(replay.Seed, replay.DataHash ^ 1, replay.Stage, replay.Inputs, replay.Checksums);
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
