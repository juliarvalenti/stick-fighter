using System;

namespace StickFighter.Sim.Numerics;

// PCG32 (pcg-random.org, XSH RR variant): small, fast, and well understood.
// It's a plain value type, so it lives in GameState and saves, loads and checksums with it.
// Create one per match from the match seed; never share one between matches.
public struct Rng
{
    private const ulong Multiplier = 6364136223846793005UL;

    public ulong State;
    public ulong Increment;

    // Matches the reference pcg32_srandom_r(initstate, initseq).
    public static Rng Create(ulong seed, ulong stream = 54)
    {
        var rng = new Rng { State = 0, Increment = (stream << 1) | 1 };
        rng.NextUInt();
        rng.State += seed;
        rng.NextUInt();
        return rng;
    }

    public uint NextUInt()
    {
        var old = State;
        State = unchecked(old * Multiplier + Increment);
        var xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        var rotation = (int)(old >> 59);
        return (xorShifted >> rotation) | (xorShifted << (-rotation & 31));
    }

    // Uniform in [0, bound), without modulo bias (the reference pcg32_boundedrand_r).
    public int NextInt(int bound)
    {
        if (bound <= 0)
            throw new ArgumentOutOfRangeException(nameof(bound), "Bound must be positive");
        var threshold = (uint)(-bound) % (uint)bound;
        while (true)
        {
            var r = NextUInt();
            if (r >= threshold)
                return (int)(r % (uint)bound);
        }
    }

    // Uniform in [min, max], inclusive.
    public int NextInt(int min, int max) => min + NextInt(max - min + 1);

    // True with probability numerator / denominator.
    public bool Chance(int numerator, int denominator) => NextInt(denominator) < numerator;
}
