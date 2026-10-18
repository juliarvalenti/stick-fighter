using System;

namespace StickFighter.Sim.Numerics;

// An angle in binary units: 4096 to a full turn, so 1 unit is about 0.088°.
// Wrapping is a mask, and joint angles stay exact integers in move data.
// 0 points along +X; positive turns from +X toward +Y (clockwise on screen, since +Y is down).
public readonly struct Angle : IEquatable<Angle>
{
    public const int UnitsPerTurn = 4096;
    public const int QuarterTurn = UnitsPerTurn / 4;
    private const int Mask = UnitsPerTurn - 1;

    // Always in [0, UnitsPerTurn).
    public readonly int Units;

    private Angle(int units) => Units = units & Mask;

    public static Angle FromUnits(int units) => new(units);

    // Whole degrees, for move data written by hand. Rounds to the nearest unit.
    public static Angle FromDegrees(int degrees)
    {
        var scaled = (long)degrees * UnitsPerTurn;
        var units = scaled >= 0 ? (scaled + 180) / 360 : -((-scaled + 180) / 360);
        return new((int)(units % UnitsPerTurn));
    }

    public static readonly Angle Zero = new(0);

    public static Angle operator +(Angle a, Angle b) => new(a.Units + b.Units);
    public static Angle operator -(Angle a, Angle b) => new(a.Units - b.Units);
    public static Angle operator -(Angle a) => new(-a.Units);

    public static bool operator ==(Angle a, Angle b) => a.Units == b.Units;
    public static bool operator !=(Angle a, Angle b) => a.Units != b.Units;

    // Mirrors across the vertical axis, for a fighter facing left.
    public Angle MirrorX() => new(UnitsPerTurn / 2 - Units);

    public bool Equals(Angle other) => Units == other.Units;
    public override bool Equals(object? obj) => obj is Angle other && Equals(other);
    public override int GetHashCode() => Units;
    public override string ToString() => $"{Units}/{UnitsPerTurn}";
}
