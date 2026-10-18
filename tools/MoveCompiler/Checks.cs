using System;
using System.Collections.Generic;
using StickFighter.Sim.Characters;
using StickFighter.Sim.Numerics;

namespace StickFighter.MoveCompiler;

// Body sanity for compiled moves, run on the integer poses through the sim's own fixed-point
// skeleton, so it checks exactly what the game will draw and hit with.
public static class Checks
{
    // A little give, so a straight limb that rounds a hair past straight isn't an error.
    private const int BendTolerance = 24; // Angle units, about 2°
    private const int FloorTolerance = 3; // px

    public static List<string> Run(CompiledMove move, IReadOnlyList<PoseKey> keys, Character character)
    {
        var problems = new List<string>();
        var airborne = move.Input.StartsWith("j.", StringComparison.Ordinal);
        var floor = Fix.Zero;

        if (keys.Count != move.Startup - 1 + move.Active + move.Recovery)
            problems.Add($"compiled to {keys.Count} frames, but the frame data says {move.Startup - 1 + move.Active + move.Recovery}");

        for (var frame = 0; frame < keys.Count; frame++)
        {
            var key = keys[frame];
            void Problem(string what) => problems.Add($"move {move.Input} frame {frame}: {what}");

            // Elbows only bend one way (negative), knees the other (positive), as in the stance poses.
            Check(key.FrontElbow, elbow: true, "front elbow", Problem);
            Check(key.BackElbow, elbow: true, "back elbow", Problem);
            Check(key.FrontKnee, elbow: false, "front knee", Problem);
            Check(key.BackKnee, elbow: false, "back knee", Problem);

            var joints = Skeleton.Solve(character.Skeleton, key, FixVec2.Zero, 1);
            var back = joints[Joint.BackFoot].Y;
            var front = joints[Joint.FrontFoot].Y;
            if (!airborne)
            {
                // A planted foot on the floor, and no foot through it.
                var lowest = Fix.Max(back, front);
                if (Fix.Abs(lowest - floor) > Fix.FromInt(FloorTolerance))
                    Problem($"no foot on the floor (lowest is {(-lowest).RoundToInt()} px up)");
            }
            if (back > Fix.FromInt(FloorTolerance) || front > Fix.FromInt(FloorTolerance))
                Problem("a foot is below the floor");

            // No leg passes through the torso.
            var hip = joints[Joint.Hip];
            var neck = joints[Joint.Neck];
            foreach (var (from, to, name) in new[]
            {
                (Joint.BackKnee, Joint.BackFoot, "back shin"),
                (Joint.FrontKnee, Joint.FrontFoot, "front shin"),
            })
            {
                if (Crosses(joints[from], joints[to], hip, neck))
                    Problem($"the {name} passes through the torso");
            }
        }

        // In recovery, the striking hand or foot comes straight back: relative to its shoulder or hip,
        // it stays on the line from where it was on the last active frame to its stance spot. A limb
        // that swings out past that line, with no hitbox, reads as a late hit or a second one.
        // (Distance from the shoulder or hip alone can't show this: a straight limb is already at
        // full length, and a kick sweeping round stays at full length the whole way.)
        var (root, end) = Ends(Enum.Parse<Limb>(move.HitboxLimb));
        var lastActive = move.Startup - 2 + move.Active;
        if (move.Recovery > 0 && lastActive >= 0 && keys.Count > lastActive + 1)
        {
            var from = Relative(keys[lastActive], root, end, character);
            var to = Relative(keys[^1], root, end, character);
            for (var frame = lastActive + 1; frame < keys.Count; frame++)
            {
                var off = DistanceToSegment(Relative(keys[frame], root, end, character), from, to);
                if (off > RecoverySlack)
                    problems.Add($"move {move.Input} frame {frame}: in recovery the {Name(end)} swings {off.RoundToInt()} px off the straight path back to stance");
            }
        }

        var hitboxFrames = 0;
        for (var frame = 0; frame < keys.Count; frame++)
        {
            if (frame >= move.Startup - 1 && frame < move.Startup - 1 + move.Active)
                hitboxFrames++;
        }
        if (hitboxFrames != move.Active)
            problems.Add($"move {move.Input}: the hitbox is out on {hitboxFrames} frames, but active is {move.Active}");

        return problems;
    }

    private static readonly Fix RecoverySlack = Fix.FromInt(4); // px

    // The joint a striking limb reaches from, and the joint at its end.
    private static (Joint Root, Joint End) Ends(Limb limb) => limb switch
    {
        Limb.FrontForearm or Limb.FrontUpperArm => (Joint.Neck, Joint.FrontHand),
        Limb.BackForearm or Limb.BackUpperArm => (Joint.Neck, Joint.BackHand),
        Limb.FrontShin or Limb.FrontThigh => (Joint.Hip, Joint.FrontFoot),
        Limb.BackShin or Limb.BackThigh => (Joint.Hip, Joint.BackFoot),
        _ => (Joint.Hip, Joint.Head),
    };

    private static FixVec2 Relative(PoseKey key, Joint root, Joint end, Character character)
    {
        var joints = Skeleton.Solve(character.Skeleton, key, FixVec2.Zero, 1);
        return joints[end] - joints[root];
    }

    private static Fix DistanceToSegment(FixVec2 p, FixVec2 a, FixVec2 b)
    {
        var ab = b - a;
        var lengthSquared = ab.LengthSquared();
        var t = lengthSquared == Fix.Zero ? Fix.Zero : Fix.Clamp(FixVec2.Dot(p - a, ab) / lengthSquared, Fix.Zero, Fix.One);
        return (p - (a + ab * t)).Length();
    }

    private static string Name(Joint joint) => joint switch
    {
        Joint.FrontHand => "front hand",
        Joint.BackHand => "back hand",
        Joint.FrontFoot => "front foot",
        Joint.BackFoot => "back foot",
        _ => joint.ToString().ToLowerInvariant(),
    };

    private static void Check(Angle bend, bool elbow, string name, Action<string> problem)
    {
        var units = bend.Units > Angle.UnitsPerTurn / 2 ? bend.Units - Angle.UnitsPerTurn : bend.Units;
        if (elbow ? units > BendTolerance : units < -BendTolerance)
            problem($"the {name} bends backwards ({units * 360 / Angle.UnitsPerTurn}°)");
    }

    // Proper segment intersection (touching at an endpoint doesn't count).
    private static bool Crosses(FixVec2 a, FixVec2 b, FixVec2 c, FixVec2 d)
    {
        var d1 = Side(c, d, a);
        var d2 = Side(c, d, b);
        var d3 = Side(a, b, c);
        var d4 = Side(a, b, d);
        return d1 * d2 < 0 && d3 * d4 < 0;
    }

    private static int Side(FixVec2 p, FixVec2 q, FixVec2 r) => Fix.Sign(FixVec2.Cross(q - p, r - p));
}
