using System;
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
}
