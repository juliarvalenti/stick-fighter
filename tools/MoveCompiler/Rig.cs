using System;
using StickFighter.Sim.Characters;
using StickFighter.Sim.Numerics;

namespace StickFighter.MoveCompiler;

// A pose in compile-time floats: hip offset in px and the ten local joint angles in Angle units.
// Same conventions as PoseKey; rounded to integers on output.
public sealed class FloatPose
{
    public const int Spine = 0, Neck = 1, BackShoulder = 2, BackElbow = 3, FrontShoulder = 4,
        FrontElbow = 5, BackHip = 6, BackKnee = 7, FrontHip = 8, FrontKnee = 9;

    public static readonly string[] Names =
    {
        "spine", "neck", "backShoulder", "backElbow", "frontShoulder", "frontElbow",
        "backHip", "backKnee", "frontHip", "frontKnee",
    };

    public double HipX;
    public double HipY;
    public double[] A = new double[10];

    public FloatPose Clone() => new() { HipX = HipX, HipY = HipY, A = (double[])A.Clone() };

    public static FloatPose From(PoseKey key)
    {
        var p = new FloatPose { HipX = key.Hip.X.RoundToInt(), HipY = key.Hip.Y.RoundToInt() };
        int[] units =
        {
            key.Spine.Units, key.Neck.Units, key.BackShoulder.Units, key.BackElbow.Units, key.FrontShoulder.Units,
            key.FrontElbow.Units, key.BackHip.Units, key.BackKnee.Units, key.FrontHip.Units, key.FrontKnee.Units,
        };
        for (var i = 0; i < 10; i++)
            p.A[i] = Rig.Wrap(units[i]);
        return p;
    }
}

public readonly record struct Vec(double X, double Y)
{
    public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec operator *(Vec a, double s) => new(a.X * s, a.Y * s);
    public double Length => Math.Sqrt(X * X + Y * Y);
    public Vec Unit => Length == 0 ? new Vec(1, 0) : this * (1 / Length);
}

// Float forward and inverse kinematics on the character's skeleton, facing right, origin at the feet.
public sealed class Rig
{
    private const double Turn = Angle.UnitsPerTurn;
    private readonly SkeletonSize _size;

    public Rig(SkeletonSize size) => _size = size;

    public double Spine => _size.Spine.RoundToInt();
    public double NeckLength => _size.Neck.RoundToInt();
    public double UpperArm => _size.UpperArm.RoundToInt();
    public double Forearm => _size.Forearm.RoundToInt();
    public double Thigh => _size.Thigh.RoundToInt();
    public double Shin => _size.Shin.RoundToInt();

    public static double Wrap(double units)
    {
        var u = units % Turn;
        if (u > Turn / 2) u -= Turn;
        if (u <= -Turn / 2) u += Turn;
        return u;
    }

    private static Vec Dir(double units) => new(Math.Cos(units * 2 * Math.PI / Turn), Math.Sin(units * 2 * Math.PI / Turn));
    private static double ToUnits(double radians) => radians * Turn / (2 * Math.PI);

    public double SpineAbs(FloatPose p) => Turn * 3 / 4 + p.A[FloatPose.Spine];
    // Arms hang from the spine's down; legs from straight down, so leaning keeps the feet planted.
    public double DownAbs(FloatPose p) => SpineAbs(p) + Turn / 2;
    public const double LegDown = Turn / 4;

    public Vec Hip(FloatPose p) => new(p.HipX, p.HipY);
    public Vec Neck(FloatPose p) => Hip(p) + Dir(SpineAbs(p)) * Spine;
    public Vec Head(FloatPose p) => Neck(p) + Dir(SpineAbs(p) + p.A[FloatPose.Neck]) * NeckLength;

    public (Vec Mid, Vec End) Arm(FloatPose p, bool front)
    {
        var (s, e) = front ? (FloatPose.FrontShoulder, FloatPose.FrontElbow) : (FloatPose.BackShoulder, FloatPose.BackElbow);
        var upper = DownAbs(p) + p.A[s];
        var mid = Neck(p) + Dir(upper) * UpperArm;
        return (mid, mid + Dir(upper + p.A[e]) * Forearm);
    }

    public (Vec Mid, Vec End) Leg(FloatPose p, bool front)
    {
        var (h, k) = front ? (FloatPose.FrontHip, FloatPose.FrontKnee) : (FloatPose.BackHip, FloatPose.BackKnee);
        var thigh = LegDown + p.A[h];
        var mid = Hip(p) + Dir(thigh) * Thigh;
        return (mid, mid + Dir(thigh + p.A[k]) * Shin);
    }

    // Two-bone IK: puts an arm's hand or a leg's foot on `target`. Elbows bend down and back,
    // knees bend forward, the way the stance poses do. Returns false if the target is out of reach.
    public bool Solve(FloatPose p, bool arm, bool front, Vec target)
    {
        var root = arm ? Neck(p) : Hip(p);
        var (l1, l2) = arm ? (UpperArm, Forearm) : (Thigh, Shin);
        var d = target - root;
        var dist = d.Length;
        if (dist > l1 + l2 + 0.5)
            return false;
        dist = Math.Clamp(dist, Math.Abs(l1 - l2) + 0.001, l1 + l2 - 0.001);

        // Of the two elbow (or knee) positions that reach the target, take the one that bends the
        // way the joint actually bends: elbows negative, knees positive, as in the stance poses.
        var baseAngle = Math.Atan2(d.Y, d.X);
        var offset = Math.Acos(Math.Clamp((l1 * l1 + dist * dist - l2 * l2) / (2 * l1 * dist), -1, 1));
        var first = 0.0;
        var second = 0.0;
        var bestScore = double.NegativeInfinity;
        foreach (var a1 in new[] { baseAngle + offset, baseAngle - offset })
        {
            var mid = root + new Vec(Math.Cos(a1), Math.Sin(a1)) * l1;
            var end = target - mid;
            var bend = Wrap(ToUnits(Math.Atan2(end.Y, end.X)) - ToUnits(a1));
            var score = arm ? -bend : bend;
            if (score > bestScore)
            {
                bestScore = score;
                first = Wrap(ToUnits(a1) - (arm ? DownAbs(p) : LegDown));
                second = bend;
            }
        }

        var (j1, j2) = (arm, front) switch
        {
            (true, true) => (FloatPose.FrontShoulder, FloatPose.FrontElbow),
            (true, false) => (FloatPose.BackShoulder, FloatPose.BackElbow),
            (false, true) => (FloatPose.FrontHip, FloatPose.FrontKnee),
            _ => (FloatPose.BackHip, FloatPose.BackKnee),
        };
        p.A[j1] = first;
        p.A[j2] = second;
        return true;
    }
}
