using System.Runtime.CompilerServices;
using StickFighter.Sim.Numerics;

namespace StickFighter.Sim.Characters;

public enum Joint
{
    Hip,
    Neck,
    Head,
    BackElbow,
    BackHand,
    FrontElbow,
    FrontHand,
    BackKnee,
    BackFoot,
    FrontKnee,
    FrontFoot,
}

// World positions of every joint for one frame.
[InlineArray(Skeleton.JointCount)]
public struct Joints
{
    private FixVec2 _element;

    public readonly FixVec2 this[Joint joint] => this[(int)joint];
}

// Forward kinematics from the hip, in fixed-point through Trig, so a pose lands on the same
// pixel on every machine. Solved for facing right, then mirrored in X for facing left.
public static class Skeleton
{
    public const int JointCount = 11;

    private static readonly Angle Up = Angle.FromUnits(Angle.UnitsPerTurn * 3 / 4);
    private static readonly Angle HalfTurn = Angle.FromUnits(Angle.UnitsPerTurn / 2);
    private static readonly Angle Down = Angle.FromUnits(Angle.UnitsPerTurn / 4);

    // Each limb's two joints, so a segment can become a hurt or hit capsule.
    public static (Joint From, Joint To) Ends(Limb limb) => limb switch
    {
        Limb.Spine => (Joint.Hip, Joint.Neck),
        Limb.Neck => (Joint.Neck, Joint.Head),
        Limb.BackUpperArm => (Joint.Neck, Joint.BackElbow),
        Limb.BackForearm => (Joint.BackElbow, Joint.BackHand),
        Limb.FrontUpperArm => (Joint.Neck, Joint.FrontElbow),
        Limb.FrontForearm => (Joint.FrontElbow, Joint.FrontHand),
        Limb.BackThigh => (Joint.Hip, Joint.BackKnee),
        Limb.BackShin => (Joint.BackKnee, Joint.BackFoot),
        Limb.FrontThigh => (Joint.Hip, Joint.FrontKnee),
        _ => (Joint.FrontKnee, Joint.FrontFoot),
    };

    // Joint positions for a pose, with the fighter's feet origin at `origin`.
    public static Joints Solve(SkeletonSize size, PoseKey pose, FixVec2 origin, int facing)
    {
        var local = new Joints();
        var spine = Up + pose.Spine;
        var down = spine + HalfTurn;

        var hip = pose.Hip;
        var neck = hip + FixVec2.FromAngle(spine, size.Spine);
        Set(ref local, Joint.Hip, hip);
        Set(ref local, Joint.Neck, neck);
        Set(ref local, Joint.Head, neck + FixVec2.FromAngle(spine + pose.Neck, size.Neck));

        Chain(ref local, neck, down + pose.BackShoulder, pose.BackElbow, size.UpperArm, size.Forearm, Joint.BackElbow, Joint.BackHand);
        Chain(ref local, neck, down + pose.FrontShoulder, pose.FrontElbow, size.UpperArm, size.Forearm, Joint.FrontElbow, Joint.FrontHand);
        // Legs hang from straight down, not from the spine, so leaning the torso keeps the feet planted.
        Chain(ref local, hip, Down + pose.BackHip, pose.BackKnee, size.Thigh, size.Shin, Joint.BackKnee, Joint.BackFoot);
        Chain(ref local, hip, Down + pose.FrontHip, pose.FrontKnee, size.Thigh, size.Shin, Joint.FrontKnee, Joint.FrontFoot);

        var world = new Joints();
        for (var i = 0; i < JointCount; i++)
            world[i] = origin + new FixVec2(local[i].X * facing, local[i].Y);
        return world;
    }

    private static void Chain(ref Joints joints, FixVec2 root, Angle first, Angle bend, Fix firstLength, Fix secondLength, Joint middle, Joint end)
    {
        var mid = root + FixVec2.FromAngle(first, firstLength);
        Set(ref joints, middle, mid);
        Set(ref joints, end, mid + FixVec2.FromAngle(first + bend, secondLength));
    }

    private static void Set(ref Joints joints, Joint joint, FixVec2 position) => joints[(int)joint] = position;
}
