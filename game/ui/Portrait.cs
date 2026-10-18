using Godot;
using StickFighter.Sim.Characters;
using StickFighter.Sim.Numerics;
using StickFighter.Style;

namespace StickFighter.UI;

// A character drawn as itself: the real skeleton, posed, in one flat color with round-capped
// limbs, scaled to fit. Used for select-screen portraits and the results screen's winner.
//
// Ready is the character's own guard (its stand pose). Victory is a presentation-only pose: it
// never fights, so it's art here rather than a move script.
public partial class Portrait : Control
{
    public enum Pose
    {
        Ready,
        Victory,
    }

    private readonly Character _character;
    private readonly Pose _pose;
    private Color _color;

    public Portrait(Character character, Color color, Pose pose = Pose.Ready, bool facingLeft = false)
    {
        _character = character;
        _color = color;
        _pose = pose;
        FacingLeft = facingLeft;
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(120, 160);
    }

    public bool FacingLeft { get; }

    public Color Color
    {
        get => _color;
        set
        {
            _color = value;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var key = _pose == Pose.Victory ? Victory(_character) : _character.Pose(FighterAction.Stand).Keys[0];
        var joints = Skeleton.Solve(_character.Skeleton, key, FixVec2.Zero, FacingLeft ? -1 : 1);
        var head = _character.Skeleton.HeadRadius.Raw / (float)Fix.OneRaw;

        // Fit the figure (head top to feet) into the control, feet on its bottom edge.
        var top = float.MaxValue;
        var bottom = float.MinValue;
        var left = float.MaxValue;
        var right = float.MinValue;
        for (var i = 0; i < Skeleton.JointCount; i++)
        {
            var p = Point(joints[(Joint)i]);
            var r = i == (int)Joint.Head ? head : 0;
            top = Mathf.Min(top, p.Y - r);
            bottom = Mathf.Max(bottom, p.Y + r);
            left = Mathf.Min(left, p.X - r);
            right = Mathf.Max(right, p.X + r);
        }
        var scale = Mathf.Min(Size.Y * 0.92f / (bottom - top), Size.X * 0.9f / (right - left));
        var origin = new Vector2(Size.X / 2 - (left + right) / 2 * scale, Size.Y - bottom * scale - Size.Y * 0.02f);
        Vector2 At(Joint j) => origin + Point(joints[j]) * scale;

        var stroke = Mathf.Max(3, Look.FighterStroke * scale);
        for (var limb = Limb.Spine; limb <= Limb.FrontShin; limb++)
        {
            if (limb == Limb.Neck)
                continue;
            var (from, to) = Skeleton.Ends(limb);
            this.Stroke(At(from), At(to), _color, stroke);
        }
        DrawCircle(At(Joint.Head), head * scale, _color);
    }

    private static Vector2 Point(FixVec2 v) => new(v.X.Raw / (float)Fix.OneRaw, v.Y.Raw / (float)Fix.OneRaw);

    // The guard with the front fist thrown up, the back hand on the hip, feet set wide.
    private static PoseKey Victory(Character character)
    {
        var stand = character.Pose(FighterAction.Stand).Keys[0];
        return stand with
        {
            Spine = Angle.FromDegrees(-4),
            Neck = Angle.FromDegrees(-8),
            FrontShoulder = Angle.FromDegrees(-165),
            FrontElbow = Angle.FromDegrees(-25),
            BackShoulder = Angle.FromDegrees(40),
            BackElbow = Angle.FromDegrees(-120),
            BackHip = Angle.FromDegrees(14),
            BackKnee = Angle.FromDegrees(6),
            FrontHip = Angle.FromDegrees(-16),
            FrontKnee = Angle.FromDegrees(8),
        };
    }
}
