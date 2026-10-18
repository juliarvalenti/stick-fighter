using System;
using System.Collections.Generic;
using StickFighter.Sim.Numerics;

namespace StickFighter.MoveCompiler;

// What strikes in a move, and how hard. Read from the script's hitbox verb and its strength.
public sealed record Strike(string Limb, int Tier, bool Grounded, bool Crouching)
{
    public bool Kick => Limb is "front-foot" or "back-foot";
    public bool Hands => Limb is "front-hand" or "back-hand" or "both-hands";
}

// The animation principles every move gets by default, so a script only says what's particular to
// it (where the hit lands, how it winds up) and the whole body follows:
//
// - the whole body strikes: the hip shifts toward the target over planted feet (a small step on
//   heavies), the torso leans into a punch and away from a kick, the free arm pulls back to the
//   chest, and the legs react through the weight shift;
// - anticipation: startup winds up the other way first;
// - arcs: recovery comes back through a chamber (a kick's knee, a punch's elbow), not straight;
// - timing: a snap out and an eased, slower return that settles with a slight overshoot;
// - overlapping action: elbows, knees and the head trail their parent by a frame.
//
// None of it moves the hit: after the body moves, the striking hand or foot is put back on the
// exact spot the script aimed it at, so reach and frame data stay as written.
public sealed class Motion
{
    // By strength: light, medium, heavy.
    private static readonly double[] PunchShift = { 10, 16, 22 }; // px toward the target
    private static readonly double[] KickShift = { 8, 12, 16 };
    private static readonly double[] PunchLean = { 6, 9, 12 }; // degrees into the strike
    private static readonly double[] KickLean = { -5, -8, -12 }; // kicks lean away
    private const double HeavyStep = 10; // px the front foot slides forward on a heavy
    private const double CrouchScale = 0.6; // crouching moves shift and lean less
    private const double WindupShare = 0.35; // anticipation moves this much of the shift, the other way
    private const double WindupLean = 0.4; // and this much of the lean

    private readonly Rig _rig;

    public Motion(Rig rig) => _rig = rig;

    // The active pose: the body moves into the strike, then the striking end goes back where the
    // script put it.
    public void Into(FloatPose pose, FloatPose stance, Strike strike, bool scriptLeaned, bool scriptStepped)
    {
        var aim = Ends(pose, strike.Limb);
        var scale = strike.Crouching ? CrouchScale : 1;

        if (!scriptLeaned)
            pose.A[FloatPose.Spine] += Degrees((strike.Kick ? KickLean : PunchLean)[strike.Tier] * scale);

        // The shift is measured from the stance, whatever hip the script's wind-up pose has.
        if (strike.Grounded && !scriptStepped)
        {
            var shift = (strike.Kick ? KickShift : PunchShift)[strike.Tier] * scale;
            Shift(pose, stance.HipX + shift - pose.HipX, strike.Tier == 2 && !strike.Kick ? HeavyStep : 0);
        }

        CounterArms(pose, strike);
        Aim(pose, strike.Limb, aim);
    }

    // The wind-up: the body draws back the other way, and a striking limb that the script didn't
    // wind up is chambered (a fist drawn back, a knee lifted).
    public void Windup(FloatPose pose, Strike strike, bool scriptMoved, bool scriptLeaned)
    {
        var scale = strike.Crouching ? CrouchScale : 1;
        if (!scriptMoved)
            Chamber(pose, strike, from: null);
        if (!scriptLeaned)
            pose.A[FloatPose.Spine] -= Degrees((strike.Kick ? KickLean : PunchLean)[strike.Tier] * scale * WindupLean);
        if (strike.Grounded)
            Shift(pose, -(strike.Kick ? KickShift : PunchShift)[strike.Tier] * scale * WindupShare, 0);
    }

    // In the air the legs stay tucked as in the jump, whatever pose the script borrows, apart from
    // a kicking leg.
    public static void KeepTuck(FloatPose pose, FloatPose stance, Strike strike)
    {
        if (strike.Grounded)
            return;
        foreach (var (front, hip, knee) in new[] { (true, FloatPose.FrontHip, FloatPose.FrontKnee), (false, FloatPose.BackHip, FloatPose.BackKnee) })
        {
            if (strike.Limb == (front ? "front-foot" : "back-foot"))
                continue;
            pose.A[hip] = stance.A[hip];
            pose.A[knee] = stance.A[knee];
        }
    }

    // The way back's midpoint: the body halfway home and the striking limb pulled into its chamber,
    // so the hand or foot travels an arc back to stance instead of a line.
    public FloatPose Retract(FloatPose active, FloatPose stance, Strike strike)
    {
        var pose = Blend(active, stance, 0.5);
        Chamber(pose, strike, from: (active, stance));
        return pose;
    }

    // The striking hand or foot's way home: a curve from where it hit, bending through the chamber,
    // to its stance spot, relative to its shoulder or hip. A quadratic Bézier stays inside the
    // triangle of those three points, so the limb can't swing out past where it hit.
    public void ArcBack(List<FloatPose> frames, int first, int count, FloatPose active, FloatPose chamber, FloatPose stance, Strike strike)
    {
        foreach (var (arm, front) in Limbs(strike.Limb))
        {
            var a = End(active, arm, front) - Root(active, arm);
            var k = End(chamber, arm, front) - Root(chamber, arm);
            var h = End(stance, arm, front) - Root(stance, arm);
            for (var j = 0; j < count; j++)
            {
                var s = Compiler.Progress(j, count, 0, Ease.InOut);
                var path = a * ((1 - s) * (1 - s)) + k * (2 * (1 - s) * s) + h * (s * s);
                var pose = frames[first + j];
                Reach(pose, arm, front, Root(pose, arm) + path);
            }
        }
    }

    private Vec Root(FloatPose pose, bool arm) => arm ? _rig.Neck(pose) : _rig.Hip(pose);

    // Puts the striking end into its chamber: fists by the chest, a kicking knee up with the foot
    // under it. With `from`, the chamber sits between where the strike was and where it returns to.
    private void Chamber(FloatPose pose, Strike strike, (FloatPose Active, FloatPose Stance)? from)
    {
        foreach (var (arm, front) in Limbs(strike.Limb))
        {
            var root = arm ? _rig.Neck(pose) : _rig.Hip(pose);
            var end = End(pose, arm, front);
            Vec target;
            if (arm)
            {
                // Elbow drops and the fist comes back toward the chest.
                var home = from is { } f ? End(f.Stance, arm, front) - _rig.Neck(f.Stance) + root : end;
                var out_ = from is { } g ? End(g.Active, arm, front) - _rig.Neck(g.Active) + root : end;
                target = home + (out_ - home) * 0.35 + new Vec(from is null ? -14 : 0, 12);
            }
            else if (strike.Crouching || !strike.Grounded)
            {
                // Low and air kicks pull the foot back in toward the hip.
                var home = from is { } f ? End(f.Stance, arm, front) - _rig.Hip(f.Stance) + root : end;
                var out_ = from is { } g ? End(g.Active, arm, front) - _rig.Hip(g.Active) + root : end;
                target = home + (out_ - home) * 0.35 + new Vec(from is null ? -12 : 0, from is null ? -10 : 0);
            }
            else
            {
                // A standing kick chambers: knee up, foot tucked under it.
                target = root + new Vec(30, 0.65 * (_rig.Thigh + _rig.Shin));
            }
            Reach(pose, arm, front, target);
        }
    }

    // The arm that isn't striking pulls back to guard the chest (punches), or both arms balance the
    // kick: the back arm swings back and the front one stays up.
    private void CounterArms(FloatPose pose, Strike strike)
    {
        var neck = _rig.Neck(pose);
        var down = (_rig.Hip(pose) - neck).Unit;
        var chest = neck + down * (_rig.Spine * 0.3);
        switch (strike.Limb)
        {
            case "front-hand":
                Reach(pose, arm: true, front: false, chest + new Vec(10, 0));
                break;
            case "back-hand":
                Reach(pose, arm: true, front: true, chest + new Vec(14, -12));
                break;
            case "front-foot" or "back-foot":
                Reach(pose, arm: true, front: false, neck + new Vec(-44, 60));
                Reach(pose, arm: true, front: true, neck + new Vec(26, 30));
                break;
        }
    }

    // Moves the hip forward (or back) over the feet that are on the floor; `step` slides the front
    // foot forward too. Falls back to less if the legs can't reach.
    private void Shift(FloatPose pose, double px, double step)
    {
        var back = _rig.Leg(pose, front: false).End;
        var front = _rig.Leg(pose, front: true).End;
        var backPlanted = Math.Abs(back.Y) <= 1;
        var frontPlanted = Math.Abs(front.Y) <= 1;
        var original = pose.Clone();
        foreach (var share in new[] { 1.0, 0.5, 0.0 })
        {
            pose.HipX = original.HipX + px * share;
            Array.Copy(original.A, pose.A, pose.A.Length);
            var ok = (!backPlanted || _rig.Solve(pose, arm: false, front: false, back))
                && (!frontPlanted || _rig.Solve(pose, arm: false, front: true, front + new Vec(step * share, 0)));
            if (ok)
                return;
        }
    }

    public Vec[] Ends(FloatPose pose, string limb)
    {
        var ends = new List<Vec>();
        foreach (var (arm, front) in Limbs(limb))
            ends.Add(End(pose, arm, front));
        return ends.ToArray();
    }

    // Puts the striking ends back on `aim`, as close as the limb reaches.
    public void Aim(FloatPose pose, string limb, Vec[] aim)
    {
        var i = 0;
        foreach (var (arm, front) in Limbs(limb))
            Reach(pose, arm, front, aim[i++]);
    }

    private void Reach(FloatPose pose, bool arm, bool front, Vec target)
    {
        if (_rig.Solve(pose, arm, front, target))
            return;
        var root = arm ? _rig.Neck(pose) : _rig.Hip(pose);
        var length = arm ? _rig.UpperArm + _rig.Forearm : _rig.Thigh + _rig.Shin;
        _rig.Solve(pose, arm, front, root + (target - root).Unit * (length - 1));
    }

    private Vec End(FloatPose pose, bool arm, bool front) => (arm ? _rig.Arm(pose, front) : _rig.Leg(pose, front)).End;

    public static IEnumerable<(bool Arm, bool Front)> Limbs(string limb) => limb switch
    {
        "front-hand" => new[] { (true, true) },
        "back-hand" => new[] { (true, false) },
        "both-hands" => new[] { (true, true), (true, false) },
        "front-foot" => new[] { (false, true) },
        "back-foot" => new[] { (false, false) },
        _ => Array.Empty<(bool, bool)>(),
    };

    public static double Degrees(double degrees) => degrees * Angle.UnitsPerTurn / 360.0;

    public static FloatPose Blend(FloatPose from, FloatPose to, double t) => Blend(from, to, t, t);

    // Blends with the children (elbows, knees, the head) at their own progress, for overlap.
    public static FloatPose Blend(FloatPose from, FloatPose to, double t, double children)
    {
        var p = new FloatPose
        {
            HipX = from.HipX + (to.HipX - from.HipX) * t,
            HipY = from.HipY + (to.HipY - from.HipY) * t,
        };
        for (var i = 0; i < 10; i++)
        {
            var share = IsChild(i) ? children : t;
            p.A[i] = from.A[i] + Rig.Wrap(to.A[i] - from.A[i]) * share; // the short way round
        }
        return p;
    }

    private static bool IsChild(int joint) => joint is FloatPose.Neck or FloatPose.BackElbow or FloatPose.FrontElbow
        or FloatPose.BackKnee or FloatPose.FrontKnee;
}
