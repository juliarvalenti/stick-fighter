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
        var recorder = new ReplayRecorder(11, new Pattern());
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
        var recorder = new ReplayRecorder(11, new Pattern());
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

        var tampered = new Replay(replay.Seed, inputs, replay.Checksums);
        Assert.Equal(42, tampered.FirstDivergence(out var expected, out var actual));
        Assert.NotEqual(expected, actual);
    }

    [Theory]
    [InlineData("not-a-replay 1\nseed 1\nframes 0\nfinal 0\n")]
    [InlineData("stick-fighter-replay 99\nseed 1\nframes 0\nfinal 0\n")]
    [InlineData("stick-fighter-replay 1\nseed 1\nframes 2\ninput 0000 0000 1\nchecksum 00\nfinal 00\n")]
    [InlineData("stick-fighter-replay 1\nseed 1\nframes 0\nbogus line\n")]
    public void MalformedReplaysAreRejected(string text)
    {
        Assert.Throws<FormatException>(() => Replay.Read(text));
    }
}
