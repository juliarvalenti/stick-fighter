using System;

namespace StickFighter.Sim;

// Turns the display's frame deltas into the elapsed microseconds TickDriver expects, without the
// jitter that makes a vsync'd game stutter.
//
// - Snapping: a delta within SnapTolerance of a whole number of refresh periods counts as exactly
//   that many periods, so ±0.1 ms of vsync noise doesn't push a frame to 0 or 2 ticks.
// - No drift: microseconds are carried as an exact fraction, never truncated away.
//
// Integer-only. The caller converts Godot's delta to nanoseconds and the refresh rate to mHz.
public sealed class FramePacer
{
    // A delta within 2% of a whole number of periods is snapped to it.
    public const int SnapTolerancePercent = 2;

    // Beyond this many periods it's a hitch, not vsync; pass it through unsnapped.
    public const int MaxSnapPeriods = 8;

    private const long NanosPerSecond = 1_000_000_000;

    // Refresh rate in millihertz (60 Hz = 60_000). With no usable rate, nothing snaps.
    private readonly long _refreshMilliHz;

    // Carried time, in units of 1 / _refreshMilliHz microseconds.
    private Int128 _carry;

    public FramePacer(long refreshMilliHz)
    {
        _refreshMilliHz = refreshMilliHz;
    }

    public bool Snapping => _refreshMilliHz > 0;

    public long ToMicroseconds(long deltaNanos)
    {
        if (deltaNanos <= 0)
            return 0;

        var rate = Snapping ? _refreshMilliHz : 1_000;
        _carry += Snapping && TrySnap(deltaNanos, out var periods)
            // k periods = k / (rate / 1000) s = k × 10^9 / rate µs, i.e. k × 10^9 carry units.
            ? (Int128)periods * NanosPerSecond
            : (Int128)deltaNanos * rate / 1_000;

        var micros = _carry / rate;
        _carry -= micros * rate;
        return (long)micros;
    }

    private bool TrySnap(long deltaNanos, out long periods)
    {
        // In units of 10^-12 s × mHz: one period is exactly 10^12.
        const long OnePeriod = 1_000_000_000_000;
        var scaled = (Int128)deltaNanos * _refreshMilliHz;
        periods = (long)((scaled + OnePeriod / 2) / OnePeriod);
        if (periods < 1 || periods > MaxSnapPeriods)
            return false;

        var error = scaled - (Int128)periods * OnePeriod;
        if (error < 0)
            error = -error;
        return error * 100 <= (Int128)OnePeriod * SnapTolerancePercent;
    }
}
