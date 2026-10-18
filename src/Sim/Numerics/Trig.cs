using System;

namespace StickFighter.Sim.Numerics;

// Sine and cosine by table lookup, exact on every platform.
//
// The quarter-wave table is built at startup with integer arithmetic only (a Taylor series in
// 2^48 fixed-point through Int128), never Math.Sin, so no platform's libm can change a value.
// Each entry is sin rounded to the nearest 1/65536. A known-answer test pins the table.
public static class Trig
{
    private const int QuarterSteps = Angle.QuarterTurn;   // 1024 steps from 0 to 90°
    private const int Scale = 48;
    // π/2 × 2^48, rounded.
    private const long HalfPiScaled = 442_139_859_501_778;

    private static readonly int[] QuarterSine = BuildQuarterSine();

    public static Fix Sin(Angle angle)
    {
        var units = angle.Units;
        var quadrant = units / QuarterSteps;
        var offset = units % QuarterSteps;
        return quadrant switch
        {
            0 => Fix.FromRaw(QuarterSine[offset]),
            1 => Fix.FromRaw(QuarterSine[QuarterSteps - offset]),
            2 => Fix.FromRaw(-QuarterSine[offset]),
            _ => Fix.FromRaw(-QuarterSine[QuarterSteps - offset]),
        };
    }

    public static Fix Cos(Angle angle) => Sin(angle + Angle.FromUnits(Angle.QuarterTurn));

    // Rotates a vector by an angle (positive turns +X toward +Y).
    public static FixVec2 Rotate(FixVec2 v, Angle angle)
    {
        var s = Sin(angle);
        var c = Cos(angle);
        return new FixVec2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    private static int[] BuildQuarterSine()
    {
        var table = new int[QuarterSteps + 1];
        for (var i = 0; i <= QuarterSteps; i++)
        {
            var x = (Int128)HalfPiScaled * i / QuarterSteps;
            table[i] = (int)((SinScaled(x) + ((Int128)1 << (Scale - Fix.FractionBits - 1))) >> (Scale - Fix.FractionBits));
        }
        return table;
    }

    // sin(x) for 0 ≤ x ≤ π/2, with x and the result scaled by 2^48.
    // Terms through x^17/17! leave an error under 2^-40, far below the table's 2^-16.
    private static Int128 SinScaled(Int128 x)
    {
        var x2 = (x * x) >> Scale;
        var term = x;
        var sum = x;
        for (var n = 1; n <= 8; n++)
        {
            term = -((term * x2) >> Scale) / ((2 * n) * (2 * n + 1));
            sum += term;
        }
        return sum;
    }
}
