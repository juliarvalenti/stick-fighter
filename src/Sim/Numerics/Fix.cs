using System;

namespace StickFighter.Sim.Numerics;

// A signed fixed-point number with 16 fractional bits, stored in a long.
//
// Why 16.16 in 64 bits:
// - 1/65536 px is far finer than anything visible, so movement never stutters from rounding.
// - The 48 integer bits mean positions, velocities and products of them can't overflow in a match.
// - Multiply and divide go through Int128, so even large intermediates are exact.
// - Shifts by 16 are cheap, and the raw value is what gets saved and checksummed.
//
// Every operation is integer-only, so results are bit-identical on every platform.
// Division and shifts round toward negative infinity (floor), consistently.
public readonly struct Fix : IEquatable<Fix>, IComparable<Fix>
{
    public const int FractionBits = 16;
    public const long OneRaw = 1L << FractionBits;

    public readonly long Raw;

    private Fix(long raw) => Raw = raw;

    public static Fix FromRaw(long raw) => new(raw);

    public static Fix FromInt(int value) => new((long)value << FractionBits);

    // An exact ratio, e.g. FromRatio(3, 2) for 1.5. Floors.
    public static Fix FromRatio(long numerator, long denominator) =>
        new(FloorDiv((Int128)numerator << FractionBits, denominator));

    public static readonly Fix Zero = new(0);
    public static readonly Fix One = new(OneRaw);
    public static readonly Fix Half = new(OneRaw / 2);
    public static readonly Fix MinValue = new(long.MinValue);
    public static readonly Fix MaxValue = new(long.MaxValue);

    public static implicit operator Fix(int value) => FromInt(value);

    public static Fix operator +(Fix a, Fix b) => new(a.Raw + b.Raw);
    public static Fix operator -(Fix a, Fix b) => new(a.Raw - b.Raw);
    public static Fix operator -(Fix a) => new(-a.Raw);
    public static Fix operator *(Fix a, Fix b) => new((long)(((Int128)a.Raw * b.Raw) >> FractionBits));
    public static Fix operator /(Fix a, Fix b) => new(FloorDiv((Int128)a.Raw << FractionBits, b.Raw));
    public static Fix operator *(Fix a, int b) => new(a.Raw * b);
    public static Fix operator /(Fix a, int b) => new(FloorDiv(a.Raw, b));

    public static bool operator ==(Fix a, Fix b) => a.Raw == b.Raw;
    public static bool operator !=(Fix a, Fix b) => a.Raw != b.Raw;
    public static bool operator <(Fix a, Fix b) => a.Raw < b.Raw;
    public static bool operator >(Fix a, Fix b) => a.Raw > b.Raw;
    public static bool operator <=(Fix a, Fix b) => a.Raw <= b.Raw;
    public static bool operator >=(Fix a, Fix b) => a.Raw >= b.Raw;

    // The largest integer not greater than this value.
    public int FloorToInt() => (int)(Raw >> FractionBits);

    // The nearest integer; halves round up.
    public int RoundToInt() => (int)((Raw + OneRaw / 2) >> FractionBits);

    public static Fix Abs(Fix a) => a.Raw < 0 ? new(-a.Raw) : a;
    public static Fix Min(Fix a, Fix b) => a.Raw <= b.Raw ? a : b;
    public static Fix Max(Fix a, Fix b) => a.Raw >= b.Raw ? a : b;
    public static Fix Clamp(Fix value, Fix min, Fix max) => Max(min, Min(max, value));
    public static int Sign(Fix a) => a.Raw > 0 ? 1 : a.Raw < 0 ? -1 : 0;

    // Floor of the exact square root. Negative input is an error in game logic.
    public static Fix Sqrt(Fix a)
    {
        if (a.Raw < 0)
            throw new ArgumentOutOfRangeException(nameof(a), "Square root of a negative number");
        return new((long)ISqrt((UInt128)(ulong)a.Raw << FractionBits));
    }

    public bool Equals(Fix other) => Raw == other.Raw;
    public override bool Equals(object? obj) => obj is Fix other && Equals(other);
    public override int GetHashCode() => (int)Raw ^ (int)(Raw >> 32);
    public int CompareTo(Fix other) => Raw.CompareTo(other.Raw);

    // Exact decimal text, for logs and tests (e.g. "-1.5", "0.0000152587890625").
    public override string ToString()
    {
        var negative = Raw < 0;
        var magnitude = negative ? -(Int128)Raw : Raw;
        var whole = magnitude >> FractionBits;
        var fraction = magnitude & (OneRaw - 1);
        var text = (negative ? "-" : "") + whole.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (fraction == 0)
            return text;

        // 1/65536 has exactly 16 decimal digits, so this terminates.
        var digits = new System.Text.StringBuilder(".");
        while (fraction != 0)
        {
            fraction *= 10;
            digits.Append((char)('0' + (int)(fraction >> FractionBits)));
            fraction &= OneRaw - 1;
        }
        return text + digits;
    }

    private static long FloorDiv(Int128 numerator, long denominator)
    {
        if (denominator == 0)
            throw new DivideByZeroException();
        var quotient = numerator / denominator;
        if ((numerator % denominator != 0) && ((numerator < 0) != (denominator < 0)))
            quotient--;
        return (long)quotient;
    }

    private static UInt128 ISqrt(UInt128 n)
    {
        if (n < 2)
            return n;
        // Newton's method from an over-estimate converges down to the floor root.
        var x = n;
        var y = (x + 1) >> 1;
        while (y < x)
        {
            x = y;
            y = (x + n / x) >> 1;
        }
        return x;
    }
}
