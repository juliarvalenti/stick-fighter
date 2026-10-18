using System.Collections.Generic;
using Xunit;

namespace StickFighter.Sim.Tests;

public class TickDriverTests
{
    // Walk in, stop, back off, jab: a different input every few frames.
    private sealed class Script : IInputSource
    {
        public FrameInput Read(int frame) => (frame / 7 % 4) switch
        {
            0 => new FrameInput(Buttons.Right, Buttons.Left),
            1 => default,
            2 => new FrameInput(Buttons.Left | Buttons.LightPunch, Buttons.Right),
            _ => new FrameInput(Buttons.Down, Buttons.Down | Buttons.HeavyKick),
        };
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(144)]
    [InlineData(59)]
    [InlineData(240)]
    public void DisplayRateDoesNotChangeTheMatch(int hz)
    {
        Assert.Equal(Run(60), Run(hz));
    }

    [Theory]
    [InlineData(30, 2)]
    [InlineData(60, 1)]
    [InlineData(120, 0)]
    public void RunsSixtyTicksPerSecond(int hz, int expectedFirstUpdate)
    {
        var state = GameState.NewMatch(1);
        var driver = new TickDriver();
        var frameMicros = (1_000_000 + hz - 1) / hz; // whole microseconds, rounded up
        var first = driver.Update(ref state, frameMicros, new Script());
        Assert.Equal(expectedFirstUpdate, first);

        // The rest of one second of display frames.
        for (var i = 1; i < hz; i++)
            driver.Update(ref state, frameMicros, new Script());
        Assert.Equal(60, state.Frame);
    }

    [Fact]
    public void AHitchCatchesUpAtMostTheLimitAndDropsTheRest()
    {
        var state = GameState.NewMatch(1);
        var driver = new TickDriver();

        var ticks = driver.Update(ref state, 1_000_000, new Script()); // a one-second stall
        Assert.Equal(TickDriver.MaxTicksPerUpdate, ticks);
        Assert.Equal(60 - TickDriver.MaxTicksPerUpdate, driver.DroppedTicks);

        // Afterwards it's back to normal pacing, not still catching up.
        Assert.Equal(1, driver.Update(ref state, 16_667, new Script()));
    }

    [Fact]
    public void ZeroOrNegativeElapsedRunsNothing()
    {
        var state = GameState.NewMatch(1);
        var driver = new TickDriver();
        Assert.Equal(0, driver.Update(ref state, 0, new Script()));
        Assert.Equal(0, driver.Update(ref state, -5_000, new Script()));
        Assert.Equal(0, state.Frame);
    }

    // The checksum of every simulation frame up to 300, driven at this display rate.
    private static List<ulong> Run(int hz)
    {
        var recorder = new Recorder();
        var state = GameState.NewMatch(99);
        var driver = new TickDriver();
        var frameMicros = 1_000_000L / hz;

        var seen = new List<(int Frame, ulong Checksum)>();
        while (state.Frame < 300)
        {
            var before = state.Frame;
            var ticks = driver.Update(ref state, frameMicros, recorder);
            Assert.Equal(before + ticks, state.Frame);
            seen.Add((state.Frame, state.Checksum()));
        }

        // The driver asked for every frame's input once, in order, none skipped or repeated.
        Assert.Equal(state.Frame, recorder.Frames.Count);
        for (var frame = 0; frame < recorder.Frames.Count; frame++)
            Assert.Equal(frame, recorder.Frames[frame]);

        // Stepping frame by frame by hand gives the same state the driver showed at every display frame.
        var replay = GameState.NewMatch(99);
        var sums = new List<ulong> { replay.Checksum() };
        for (var frame = 0; frame < state.Frame; frame++)
        {
            Simulation.Step(ref replay, recorder.Script.Read(frame));
            sums.Add(replay.Checksum());
        }
        foreach (var (frame, checksum) in seen)
            Assert.Equal(sums[frame], checksum);

        return sums.GetRange(0, 301);
    }

    // Records which frame numbers the driver asked input for, in order.
    private sealed class Recorder : IInputSource
    {
        public readonly Script Script = new();
        public readonly List<int> Frames = new();

        public FrameInput Read(int frame)
        {
            Frames.Add(frame);
            return Script.Read(frame);
        }
    }
}
