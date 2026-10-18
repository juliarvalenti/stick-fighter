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

        // In recovery, the striking hand or foot never gets farther from the body than it was while
        // the hitbox was out. Further, with no hitbox, it reads as a late hit or a second one. Measured
        // from the torso (the neck-to-hip line, carried on down to the floor, so a foot back under
        // the body counts as home), not from the shoulder or hip: a limb folding back in an arc
        // passes, and one that sweeps round at full stretch fails.
        var end = End(Enum.Parse<Limb>(move.HitboxLimb));
        var firstActive = move.Startup - 1;
        var lastActive = firstActive + move.Active - 1;
        var reach = Fix.Zero;
        for (var frame = firstActive; frame <= lastActive && frame < keys.Count; frame++)
            reach = Fix.Max(reach, FromTorso(keys[frame], end, character));
        for (var frame = lastActive + 1; frame < keys.Count; frame++)
        {
            var distance = FromTorso(keys[frame], end, character);
            if (distance > reach + ReachSlack)
                problems.Add($"move {move.Input} frame {frame}: in recovery the {Name(end)} is {distance.RoundToInt()} px from the torso, past its active reach of {reach.RoundToInt()} px");
        }

        // The whole body strikes: between the stance and the first active frame the hip and the
        // torso both move, not just the striking limb. (In the air there's no stance to shift from,
        // so only the torso is checked, against the move's first frame.)
        if (firstActive < keys.Count)
        {
            var before = airborne ? keys[0] : Stance(move, character);
            var hit = keys[firstActive];
            var hipMoved = (hit.Hip - before.Hip).Length();
            var torsoTurned = Math.Abs(Wrap(hit.Spine.Units - before.Spine.Units));
            if (!airborne && hipMoved < Fix.FromInt(MinHipShift))
                problems.Add($"move {move.Input}: the hip moves {hipMoved.RoundToInt()} px into the strike (at least {MinHipShift}); the whole body should strike, not just the limb");
            if (torsoTurned < MinTorsoTurn)
                problems.Add($"move {move.Input}: the torso turns {torsoTurned * 360 / Angle.UnitsPerTurn}° into the strike (at least {MinTorsoTurn * 360 / Angle.UnitsPerTurn}°); the whole body should strike, not just the limb");
        }

        // Smooth: outside the snap into the strike, no joint turns more than a set amount in one
        // frame. On the ground the move starts from the stance, so its first frame counts too.
        var previous = airborne ? null : Stance(move, character);
        for (var frame = 0; frame < keys.Count; frame++)
        {
            var key = keys[frame];
            if (previous is not null && (frame < firstActive || frame > lastActive))
            {
                var (joint, turn) = LargestTurn(previous, key);
                if (turn > MaxTurnPerFrame)
                    problems.Add($"move {move.Input} frame {frame}: the {joint} turns {turn * 360 / Angle.UnitsPerTurn}° in one frame (at most {MaxTurnPerFrame * 360 / Angle.UnitsPerTurn}° outside the strike)");
            }
            previous = key;
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

    private static readonly Fix ReachSlack = Fix.FromInt(3); // px
    private const int MinHipShift = 4; // px
    private const int MinTorsoTurn = 34; // Angle units, about 3°
    private const int MaxTurnPerFrame = 512; // Angle units, 45°

    // The joint at the end of a striking limb.
    private static Joint End(Limb limb) => limb switch
    {
        Limb.FrontForearm or Limb.FrontUpperArm => Joint.FrontHand,
        Limb.BackForearm or Limb.BackUpperArm => Joint.BackHand,
        Limb.FrontShin or Limb.FrontThigh => Joint.FrontFoot,
        Limb.BackShin or Limb.BackThigh => Joint.BackFoot,
        _ => Joint.Head,
    };

    private static Fix FromTorso(PoseKey key, Joint end, Character character)
    {
        var joints = Skeleton.Solve(character.Skeleton, key, FixVec2.Zero, 1);
        var hip = joints[Joint.Hip];
        var below = new FixVec2(hip.X, Fix.Zero);
        return Fix.Min(DistanceToSegment(joints[end], hip, joints[Joint.Neck]), DistanceToSegment(joints[end], hip, below));
    }

    // The pose a ground move starts from: crouching moves from the crouch, the rest standing.
    private static PoseKey Stance(CompiledMove move, Character character) =>
        character.Pose(Compiler.DefaultStance(move.Input) == "crouch-guard" ? FighterAction.Crouch : FighterAction.Stand).Keys[0];

    private static readonly string[] JointNames =
    {
        "spine", "neck", "back shoulder", "back elbow", "front shoulder", "front elbow", "back hip", "back knee", "front hip", "front knee",
    };

    // The joint that turns most between two poses, and by how much.
    private static (string Joint, int Turn) LargestTurn(PoseKey a, PoseKey b)
    {
        int[] from = { a.Spine.Units, a.Neck.Units, a.BackShoulder.Units, a.BackElbow.Units, a.FrontShoulder.Units, a.FrontElbow.Units, a.BackHip.Units, a.BackKnee.Units, a.FrontHip.Units, a.FrontKnee.Units };
        int[] to = { b.Spine.Units, b.Neck.Units, b.BackShoulder.Units, b.BackElbow.Units, b.FrontShoulder.Units, b.FrontElbow.Units, b.BackHip.Units, b.BackKnee.Units, b.FrontHip.Units, b.FrontKnee.Units };
        var worst = (JointNames[0], 0);
        for (var i = 0; i < from.Length; i++)
        {
            var turn = Math.Abs(Wrap(to[i] - from[i]));
            if (turn > worst.Item2)
                worst = (JointNames[i], turn);
        }
        return worst;
    }

    private static int Wrap(int units)
    {
        var u = units % Angle.UnitsPerTurn;
        if (u > Angle.UnitsPerTurn / 2)
            u -= Angle.UnitsPerTurn;
        if (u <= -Angle.UnitsPerTurn / 2)
            u += Angle.UnitsPerTurn;
        return u;
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
