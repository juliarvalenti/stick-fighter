using StickFighter.Sim.Numerics;

namespace StickFighter.Sim.Collision;

// A segment with a thickness: a limb, the torso, or (with A == B) a circle like a head or a fireball.
public readonly struct Capsule
{
    public readonly FixVec2 A;
    public readonly FixVec2 B;
    public readonly Fix Radius;

    public Capsule(FixVec2 a, FixVec2 b, Fix radius)
    {
        A = a;
        B = b;
        Radius = radius;
    }

    public static Capsule Circle(FixVec2 center, Fix radius) => new(center, center, radius);

    public Capsule Offset(FixVec2 by) => new(A + by, B + by, Radius);
}

// An axis-aligned box, used for pushboxes. Min is the top-left corner (+Y is down).
public readonly struct Box
{
    public readonly FixVec2 Min;
    public readonly FixVec2 Max;

    public Box(FixVec2 min, FixVec2 max)
    {
        Min = min;
        Max = max;
    }

    public Fix Width => Max.X - Min.X;
}
