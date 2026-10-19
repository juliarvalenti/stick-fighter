using Godot;
using StickFighter.Sim.Characters;
using StickFighter.Sim.Collision;
using StickFighter.Sim.Numerics;
using StickFighter.Style;

namespace StickFighter.Editor;

// One frame of a move, large: the posed skeleton (the sim's own Skeleton.Solve) over the floor
// grid, with the hitbox view on: hurtboxes on every frame, the hitbox on active frames, colored as
// on the pose sheets and the debug view.
public partial class PoseView : Control
{
    private const float Zoom = 1.2f; // a standing fighter fills about two thirds of the view's height

    private static readonly Color Hurt = new(0.1f, 0.7f, 0.3f);
    private static readonly Color Hit = new(0.9f, 0.1f, 0.1f);

    public Character? Character { get; set; }
    public Move? Move { get; set; }
    public int Frame { get; set; }

    public PoseView()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;
    }

    public override void _Draw()
    {
        this.Card(new Rect2(Vector2.Zero, Size), Look.Paper);
        var feet = new Vector2(Size.X * 0.42f, Size.Y - 40);

        // The floor, with a tick every character width (a pushbox across), as training's grid.
        DrawLine(new Vector2(16, feet.Y), new Vector2(Size.X - 16, feet.Y), Look.Ink, Look.LineStroke);
        if (Character is { } c)
        {
            var step = 2 * c.Stats.PushHalfWidth.ToFloat() * Zoom;
            for (var x = feet.X - step * 6; x < Size.X; x += step)
            {
                if (x > 16)
                    DrawLine(new Vector2(x, feet.Y - 10), new Vector2(x, feet.Y + 10), new Color(Look.Ink, 0.35f), 2);
            }
        }
        if (Character is not { } character || Move is not { } move)
            return;

        DrawSetTransform(feet, 0, new Vector2(Zoom, Zoom));
        var frame = Mathf.Clamp(Frame, 0, move.TotalFrames - 1);
        var joints = Skeleton.Solve(character.Skeleton, move.Track.At(frame), FixVec2.Zero, 1);
        for (var limb = Limb.Spine; limb <= Limb.FrontShin; limb++)
        {
            if (limb == Limb.Neck)
                continue;
            var (from, to) = Skeleton.Ends(limb);
            this.Stroke(joints[from].ToVector2(), joints[to].ToVector2(), Look.P1, 10);
        }
        DrawCircle(joints[Joint.Head].ToVector2(), character.Skeleton.HeadRadius.ToFloat(), Look.P1);

        Capsule(Sim.Collision.Capsule.Circle(joints[Joint.Head], character.Skeleton.HeadRadius), Hurt);
        Capsule(Body.Segment(joints, Limb.Spine, character.Skeleton.TorsoRadius), Hurt);
        for (var limb = Limb.BackUpperArm; limb <= Limb.FrontShin; limb++)
            Capsule(Body.Segment(joints, limb, character.Skeleton.LimbRadius), Hurt);
        if (move.IsActive(frame))
            Capsule(Body.Segment(joints, move.HitboxLimb, move.HitboxRadius + move.ActiveBonus), Hit, 3);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    private void Capsule(in Capsule capsule, Color color, float width = 1.5f)
    {
        var a = capsule.A.ToVector2();
        var b = capsule.B.ToVector2();
        var r = capsule.Radius.ToFloat();
        DrawArc(a, r, 0, Mathf.Tau, 24, color, width, true);
        if (a == b)
            return;
        DrawArc(b, r, 0, Mathf.Tau, 24, color, width, true);
        var side = (b - a).Normalized().Orthogonal() * r;
        DrawLine(a + side, b + side, color, width, true);
        DrawLine(a - side, b - side, color, width, true);
    }
}
