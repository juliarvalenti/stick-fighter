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
