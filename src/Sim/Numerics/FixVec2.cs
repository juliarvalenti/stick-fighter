using System;

namespace StickFighter.Sim.Numerics;

// A 2D point or vector in fixed-point, in pixels. +X is right, +Y is down, like Godot.
public readonly struct FixVec2 : IEquatable<FixVec2>
{
    public readonly Fix X;
    public readonly Fix Y;

    public FixVec2(Fix x, Fix y)
    {
        X = x;
        Y = y;
    }

    public static readonly FixVec2 Zero = new(Fix.Zero, Fix.Zero);

    public static FixVec2 operator +(FixVec2 a, FixVec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static FixVec2 operator -(FixVec2 a, FixVec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static FixVec2 operator -(FixVec2 a) => new(-a.X, -a.Y);
    public static FixVec2 operator *(FixVec2 a, Fix s) => new(a.X * s, a.Y * s);
    public static FixVec2 operator *(FixVec2 a, int s) => new(a.X * s, a.Y * s);

    public static bool operator ==(FixVec2 a, FixVec2 b) => a.X == b.X && a.Y == b.Y;
    public static bool operator !=(FixVec2 a, FixVec2 b) => !(a == b);

    public static Fix Dot(FixVec2 a, FixVec2 b) => a.X * b.X + a.Y * b.Y;
    public static Fix Cross(FixVec2 a, FixVec2 b) => a.X * b.Y - a.Y * b.X;
    public Fix LengthSquared() => Dot(this, this);
    public Fix Length() => Fix.Sqrt(LengthSquared());

    // The point at this angle and distance from the origin.
    public static FixVec2 FromAngle(Angle angle, Fix length) =>
        new(Trig.Cos(angle) * length, Trig.Sin(angle) * length);

    public bool Equals(FixVec2 other) => this == other;
    public override bool Equals(object? obj) => obj is FixVec2 other && Equals(other);
    public override int GetHashCode() => (int)(X.Raw * 31 + Y.Raw);
    public override string ToString() => $"({X}, {Y})";
}
