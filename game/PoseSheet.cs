using Godot;
using StickFighter.Sim.Characters;
using StickFighter.Sim.Collision;
using StickFighter.Sim.Numerics;

namespace StickFighter;

// One image for a whole move: every frame side by side at half size, with hurtboxes, the hitbox on
// active frames, and a bar per frame for its phase (startup yellow, active red, recovery blue).
// The author and the reviewer read this one picture; it's the move's golden image.
public partial class PoseSheet : Node2D
{
    private const float FighterScale = 0.5f;
    private const float CellWidth = 78f;
    private const float CellHeight = 190f;
    private const int Columns = 16;
    private const float Top = 70f;

    private static readonly Color Background = new(0.96f, 0.95f, 0.92f);
    private static readonly Color Ink = new(0.1f, 0.1f, 0.12f);
    private static readonly Color Body = new(0.85f, 0.2f, 0.25f);
    private static readonly Color Hurt = new(0.1f, 0.7f, 0.3f);
    private static readonly Color Hit = new(0.9f, 0.1f, 0.1f);
    private static readonly Color Startup = new(0.95f, 0.75f, 0.1f);
    private static readonly Color Recovery = new(0.25f, 0.45f, 0.9f);

    private readonly Move _move;

    public PoseSheet(Move move) => _move = move;

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        DrawRect(new Rect2(0, 0, 1280, 720), Background);
        DrawString(font, new Vector2(16, 34),
            $"{_move.Input}  {_move.Name}   startup {_move.Startup}  active {_move.Active}  recovery {_move.Recovery}   ({_move.TotalFrames} frames)",
            HorizontalAlignment.Left, -1, 20, Ink);

        var character = Characters.Placeholder;
        for (var frame = 0; frame < _move.TotalFrames; frame++)
        {
            var cell = new Vector2(8 + frame % Columns * CellWidth, Top + frame / Columns * CellHeight);
            var active = _move.IsActive(frame);
            var phase = frame < _move.FirstActiveFrame ? Startup : active ? Hit : Recovery;
            DrawRect(new Rect2(cell.X + 4, cell.Y + CellHeight - 22, CellWidth - 8, 6), phase);
            DrawString(font, new Vector2(cell.X + 4, cell.Y + CellHeight - 4), frame.ToString(), HorizontalAlignment.Left, -1, 12, Ink);

            // The fighter, facing right, feet at the bottom middle of the cell.
            DrawSetTransform(cell + new Vector2(CellWidth / 2, CellHeight - 30), 0, new Vector2(FighterScale, FighterScale));
            DrawLine(new Vector2(-70, 0), new Vector2(70, 0), Ink, 2);
            var joints = Skeleton.Solve(character.Skeleton, _move.Track.At(frame), FixVec2.Zero, 1);
            for (var limb = Limb.Spine; limb <= Limb.FrontShin; limb++)
            {
                if (limb == Limb.Neck)
                    continue;
                var (from, to) = Skeleton.Ends(limb);
                DrawLine(joints[from].ToVector2(), joints[to].ToVector2(), Body, 10);
            }
            DrawCircle(joints[Joint.Head].ToVector2(), character.Skeleton.HeadRadius.ToFloat(), Body);

            DrawCapsule(Capsule.Circle(joints[Joint.Head], character.Skeleton.HeadRadius), Hurt);
            DrawCapsule(Sim.Collision.Body.Segment(joints, Limb.Spine, character.Skeleton.TorsoRadius), Hurt);
            for (var limb = Limb.BackUpperArm; limb <= Limb.FrontShin; limb++)
                DrawCapsule(Sim.Collision.Body.Segment(joints, limb, character.Skeleton.LimbRadius), Hurt);
            if (active)
                DrawCapsule(Sim.Collision.Body.Segment(joints, _move.HitboxLimb, _move.HitboxRadius + _move.ActiveBonus), Hit, 4);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }

    private void DrawCapsule(in Capsule capsule, Color color, float width = 2)
    {
        var a = capsule.A.ToVector2();
        var b = capsule.B.ToVector2();
        var r = capsule.Radius.ToFloat();
        DrawArc(a, r, 0, Mathf.Tau, 20, color, width);
        if (a == b)
            return;
        DrawArc(b, r, 0, Mathf.Tau, 20, color, width);
        var side = (b - a).Normalized().Orthogonal() * r;
        DrawLine(a + side, b + side, color, width);
        DrawLine(a - side, b - side, color, width);
    }
}
