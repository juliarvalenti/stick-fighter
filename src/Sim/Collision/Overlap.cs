using StickFighter.Sim.Numerics;

namespace StickFighter.Sim.Collision;

// Overlap tests in fixed-point. Distances are compared squared, so there's no square root.
public static class Overlap
{
    // Capsules overlap when their segments come within the sum of the radii. Touching counts.
    // The contact point is halfway between the closest points of the two segments.
    public static bool Capsules(in Capsule a, in Capsule b, out FixVec2 contact)
    {
        ClosestPoints(a.A, a.B, b.A, b.B, out var onA, out var onB);
        var gap = onA - onB;
        var reach = a.Radius + b.Radius;
        contact = new FixVec2((onA.X + onB.X) / 2, (onA.Y + onB.Y) / 2);
        return gap.LengthSquared() <= reach * reach;
    }

    // Boxes overlap when they share area. Touching edges don't count, so fighters can stand flush.
    public static bool Boxes(in Box a, in Box b) =>
        a.Min.X < b.Max.X && b.Min.X < a.Max.X && a.Min.Y < b.Max.Y && b.Min.Y < a.Max.Y;

    // Horizontal overlap of two boxes, or zero if they don't overlap.
    public static Fix BoxesX(in Box a, in Box b) =>
        Boxes(a, b) ? Fix.Min(a.Max.X, b.Max.X) - Fix.Max(a.Min.X, b.Min.X) : Fix.Zero;

    // Closest points between segments p1-q1 and p2-q2 (Ericson, Real-Time Collision Detection 5.1.9),
    // with exact zero checks in place of epsilons, since everything is integer.
    public static void ClosestPoints(FixVec2 p1, FixVec2 q1, FixVec2 p2, FixVec2 q2, out FixVec2 c1, out FixVec2 c2)
    {
        var d1 = q1 - p1;
        var d2 = q2 - p2;
        var r = p1 - p2;
        var a = FixVec2.Dot(d1, d1);
        var e = FixVec2.Dot(d2, d2);
        var f = FixVec2.Dot(d2, r);
        Fix s, t;

        if (a == Fix.Zero && e == Fix.Zero)
        {
            s = Fix.Zero;
            t = Fix.Zero;
        }
        else if (a == Fix.Zero)
        {
            s = Fix.Zero;
            t = Fix.Clamp(f / e, Fix.Zero, Fix.One);
        }
        else
        {
            var c = FixVec2.Dot(d1, r);
            if (e == Fix.Zero)
            {
                t = Fix.Zero;
                s = Fix.Clamp(-c / a, Fix.Zero, Fix.One);
            }
            else
            {
                var b = FixVec2.Dot(d1, d2);
                var denom = a * e - b * b;

                // Parallel segments have no unique closest pair; start from s = 0 and let the clamps settle t.
                s = denom != Fix.Zero ? Fix.Clamp((b * f - c * e) / denom, Fix.Zero, Fix.One) : Fix.Zero;
                t = (b * s + f) / e;

                if (t < Fix.Zero)
                {
                    t = Fix.Zero;
                    s = Fix.Clamp(-c / a, Fix.Zero, Fix.One);
                }
                else if (t > Fix.One)
                {
                    t = Fix.One;
                    s = Fix.Clamp((b - c) / a, Fix.Zero, Fix.One);
                }
            }
        }

        c1 = p1 + d1 * s;
        c2 = p2 + d2 * t;
    }
}
