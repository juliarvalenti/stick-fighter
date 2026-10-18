using StickFighter.Sim.Characters;
using StickFighter.Sim.Numerics;

namespace StickFighter.Sim.Collision;

// A fighter's pose and the collision shapes that come from it this frame. The pose is a pure
// function of (character, action, action frame), so none of this is stored in GameState.
public static class Body
{
    private static Character Data => Characters.Characters.Placeholder;

    public static Fix PushHalfWidth => Data.Stats.PushHalfWidth;
    public static Fix PushHeight => Data.Stats.PushHeight;
    public static Fix HeadRadius => Data.Skeleton.HeadRadius;

    public static PoseKey PoseOf(in FighterState fighter) =>
        Data.Pose((FighterAction)fighter.Action).At(fighter.ActionFrame);

    public static Joints Pose(in FighterState fighter) =>
        Skeleton.Solve(Data.Skeleton, PoseOf(fighter), fighter.Position, fighter.Facing);

    // Every bone is a hurtbox; the head is a circle. A move's active limb will become a hitbox here
    // once moves run (Normals and getting hit).
    public static FighterShapes Shapes(in FighterState fighter)
    {
        var joints = Pose(fighter);
        var size = Data.Skeleton;
        var shapes = new FighterShapes();

        shapes.AddHurt(Capsule.Circle(joints[Joint.Head], size.HeadRadius));
        shapes.AddHurt(Segment(joints, Limb.Spine, size.TorsoRadius));
        for (var limb = Limb.BackUpperArm; limb <= Limb.FrontShin; limb++)
            shapes.AddHurt(Segment(joints, limb, size.LimbRadius));

        shapes.Push = PushBox(fighter.Position);
        return shapes;
    }

    public static Capsule Segment(in Joints joints, Limb limb, Fix radius)
    {
        var (from, to) = Skeleton.Ends(limb);
        return new Capsule(joints[from], joints[to], radius);
    }

    public static Box PushBox(FixVec2 feet) => new(
        new FixVec2(feet.X - PushHalfWidth, feet.Y - PushHeight),
        new FixVec2(feet.X + PushHalfWidth, feet.Y));
}
