using StickFighter.Sim.Characters;
using StickFighter.Sim.Numerics;

namespace StickFighter.Sim.Collision;

// A fighter's pose and the collision shapes that come from it this frame. The pose is a pure
// function of (character, action, action frame), so none of this is stored in GameState.
public static class Body
{
    private static Character Data => Characters.Characters.Placeholder;

    public static Fix PushHalfWidth => Data.Stats.PushHalfWidth;
    public static Fix PushShare => Data.Stats.PushShare;
    public static Fix HeadRadius => Data.Skeleton.HeadRadius;

    // The pushbox's height in the fighter's stance; a move uses its stance's (crouching or standing).
    public static Fix PushHeight(in FighterState fighter) =>
        MoveOf(fighter) is { } move
            ? Data.PushHeight(move.Crouching ? FighterAction.Crouch : FighterAction.Stand)
            : Data.PushHeight((FighterAction)fighter.Action);

    // The move being performed, if the fighter is attacking.
    public static Move? MoveOf(in FighterState fighter) =>
        fighter.Action == (int)FighterAction.Attack && fighter.Move >= 0 ? Data.Moves[fighter.Move] : null;

    public static PoseKey PoseOf(in FighterState fighter) =>
        MoveOf(fighter) is { } move
            ? move.Track.At(fighter.ActionFrame)
            : Data.Pose((FighterAction)fighter.Action).At(fighter.ActionFrame);

    public static Joints Pose(in FighterState fighter) =>
        Skeleton.Solve(Data.Skeleton, PoseOf(fighter), fighter.Position, fighter.Facing);

    // Every bone is a hurtbox; the head is a circle. On a move's active frames its striking limb is
    // also a hitbox, until the move has hit once. A throw has no hitbox: Throws checks its range.
    // A back-dash's invincible frames have no hurtboxes at all.
    public static FighterShapes Shapes(in FighterState fighter)
    {
        var joints = Pose(fighter);
        var size = Data.Skeleton;
        var shapes = new FighterShapes();

        if (!Movement.Dashing.Invincible(fighter, Data.Stats))
        {
            shapes.AddHurt(Capsule.Circle(joints[Joint.Head], size.HeadRadius));
            shapes.AddHurt(Segment(joints, Limb.Spine, size.TorsoRadius));
            for (var limb = Limb.BackUpperArm; limb <= Limb.FrontShin; limb++)
                shapes.AddHurt(Segment(joints, limb, size.LimbRadius));
        }

        // A move that throws a projectile has no hitbox of its own: the projectile is what hits.
        if (MoveOf(fighter) is { Kind: MoveKind.Normal, Spawn: null } move && move.IsActive(fighter.ActionFrame) && !fighter.MoveHit)
            shapes.AddHit(Segment(joints, move.HitboxLimb, move.HitboxRadius + move.ActiveBonus));

        shapes.Push = PushBox(fighter);
        return shapes;
    }

    public static Capsule Segment(in Joints joints, Limb limb, Fix radius)
    {
        var (from, to) = Skeleton.Ends(limb);
        return new Capsule(joints[from], joints[to], radius);
    }

    public static Box PushBox(in FighterState fighter) => new(
        new FixVec2(fighter.Position.X - PushHalfWidth, fighter.Position.Y - PushHeight(fighter)),
        new FixVec2(fighter.Position.X + PushHalfWidth, fighter.Position.Y));
}
