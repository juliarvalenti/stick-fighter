using System;
using StickFighter.Sim.Numerics;
using Xunit;

namespace StickFighter.Sim.Tests;

public class FramePacerTests
{
    private sealed class Idle : IInputSource
    {
        public FrameInput Read(int frame) => default;
    }

    // ±jitter around a nominal period, deterministic from the seed.
    private static long Jittered(ref Rng rng, long periodNanos, long jitterNanos) =>
        periodNanos + rng.NextInt((int)-jitterNanos, (int)jitterNanos);

    [Theory]
    [InlineData(60_000, 300_000)]   // 60 Hz ± 0.3 ms
    [InlineData(59_940, 300_000)]   // 59.94 Hz, common on TVs
    [InlineData(120_000, 150_000)]  // 120 Hz ProMotion ± 0.15 ms
    [InlineData(144_000, 130_000)]  // 144 Hz ± 0.13 ms
    public void JitteredVsyncNeverSkipsOrDoublesATick(long refreshMilliHz, long jitterNanos)
    {
        var pacer = new FramePacer(refreshMilliHz);
        var driver = new TickDriver();
        var state = GameState.NewMatch(1);
        var rng = Rng.Create(5);
        var period = 1_000_000_000_000 / refreshMilliHz;

        const int DisplayFrames = 20_000;
        var doubles = 0;
        for (var i = 0; i < DisplayFrames; i++)
        {
            var micros = pacer.ToMicroseconds(Jittered(ref rng, period, jitterNanos));
            var ticks = driver.Update(ref state, micros, new Idle());
            if (refreshMilliHz >= 60_000)
            {
                // At or above 60 Hz: one tick or none, never two.
                Assert.InRange(ticks, 0, 1);
                if (refreshMilliHz == 60_000)
                    Assert.Equal(1, ticks);
            }
            else
            {
                // Slightly slower than 60 Hz: one tick, plus a second only as often as the rates differ.
                Assert.InRange(ticks, 1, 2);
                if (ticks == 2)
                    doubles++;
            }
        }

        // Exactly 60 ticks per second of snapped display time, give or take the head start.
        var expected = (long)DisplayFrames * 60_000 / refreshMilliHz;
        Assert.InRange(state.Frame, expected - 1, expected + 1);
        if (refreshMilliHz < 60_000)
            Assert.InRange(doubles, 0, DisplayFrames * (60_000 - refreshMilliHz) / refreshMilliHz + 1);
    }

    [Fact]
    public void ProMotionAlternatesZeroAndOneTicks()
    {
        var pacer = new FramePacer(120_000);
        var driver = new TickDriver();
        var state = GameState.NewMatch(1);
        var rng = Rng.Create(9);
        var previous = -1;

        for (var i = 0; i < 1_000; i++)
        {
            var ticks = driver.Update(ref state, pacer.ToMicroseconds(Jittered(ref rng, 8_333_333, 150_000)), new Idle());
            Assert.NotEqual(previous, ticks);
            previous = ticks;
        }
    }

    [Fact]
    public void ADroppedFrameSnapsToTwoPeriods()
    {
        var pacer = new FramePacer(60_000);
        Assert.Equal(33_333, pacer.ToMicroseconds(33_400_000)); // 2 periods, within 2%
    }

    [Fact]
    public void DeltasFarFromAPeriodPassThroughUnsnapped()
    {
        var pacer = new FramePacer(60_000);
        Assert.Equal(25_000, pacer.ToMicroseconds(25_000_000)); // 1.5 periods: not vsync
        Assert.Equal(1_000_000, pacer.ToMicroseconds(1_000_000_000)); // a hitch: 60 periods
    }

    [Fact]
    public void CarriedFractionsNeverDrift()
    {
        var pacer = new FramePacer(60_000);
        long total = 0;
        for (var i = 0; i < 60 * 60 * 10; i++) // ten minutes of exact 60 Hz frames
            total += pacer.ToMicroseconds(16_666_667);
        Assert.Equal(600_000_000, total);
    }

    [Fact]
    public void UnknownRefreshRatePassesThroughWithCarry()
    {
        var pacer = new FramePacer(0);
        Assert.False(pacer.Snapping);
        long total = 0;
        for (var i = 0; i < 3; i++)
            total += pacer.ToMicroseconds(333_333_333);
        Assert.Equal(999_999, total);
        Assert.Equal(0, pacer.ToMicroseconds(-5));
    }
}
