namespace StickFighter.Sim;

// Supplies the input for each simulation frame. The game reads devices; tests and replays script it.
public interface IInputSource
{
    FrameInput Read(int frame);
}

// Runs Simulation.Step exactly GameInfo.TicksPerSecond times per second of elapsed time,
// whatever the display rate. The caller passes elapsed time in whole microseconds; the driver
// never reads a clock itself.
//
// Time is kept in units of 1/TicksPerSecond µs, so a 60 Hz tick is exactly 1,000,000 units
// and no rounding drift builds up over a match.
public sealed class TickDriver
{
    // Catch-up limit: after a hitch, run at most this many ticks in one update and drop the rest,
    // so a slow frame can't snowball into ever more simulation work.
    public const int MaxTicksPerUpdate = 4;

    private const long UnitsPerTick = 1_000_000;

    // Starts a quarter tick in, so the remainder sits away from the tick boundary and small timing
    // noise never adds or skips a tick. A quarter rather than a half: at 120 Hz each frame adds
    // half a tick, and a half-tick start would land the remainder exactly on the boundary.
    private long _accumulator = UnitsPerTick / 4;

    // Ticks dropped by the catch-up limit since the start, for the debug overlay.
    public long DroppedTicks { get; private set; }

    // Advances the state by however many whole ticks fit in the elapsed time. Returns that count.
    public int Update(ref GameState state, long elapsedMicroseconds, IInputSource input)
    {
        var ticks = Due(elapsedMicroseconds);
        for (var i = 0; i < ticks; i++)
            Simulation.Step(ref state, input.Read(state.Frame));
        return ticks;
    }

    // How many whole ticks are due for the elapsed time (at most MaxTicksPerUpdate, the rest
    // dropped), for a caller that runs the ticks itself: an online match, ticking its session.
    public int Due(long elapsedMicroseconds)
    {
        if (elapsedMicroseconds > 0)
            _accumulator += elapsedMicroseconds * GameInfo.TicksPerSecond;

        var ticks = 0;
        while (_accumulator >= UnitsPerTick && ticks < MaxTicksPerUpdate)
        {
            _accumulator -= UnitsPerTick;
            ticks++;
        }

        if (_accumulator >= UnitsPerTick)
        {
            DroppedTicks += _accumulator / UnitsPerTick;
            _accumulator %= UnitsPerTick;
        }

        return ticks;
    }
}
