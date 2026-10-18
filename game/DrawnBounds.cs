using StickFighter.Sim;
using StickFighter.Sim.Characters;
using StickFighter.Sim.Collision;

namespace StickFighter;

// How far left and right a fighter is drawn this frame: every bone's round-capped stroke and the
// head, from the same posed joints Main draws. Wider than the pushbox: arms reach, a crouch-block
// leans back. The camera frames these, so nobody is drawn off-screen. No Godot here, so it's tested
// without the engine; presentation only.
public static class DrawnBounds
{
    // Main's bone width.
    public const float Stroke = 10f;

    public static (float Left, float Right) Of(in FighterState fighter)
    {
        var joints = Body.Pose(fighter);
        var left = float.MaxValue;
        var right = float.MinValue;
        for (var j = 0; j < Skeleton.JointCount; j++)
        {
            var x = Pixels(joints[(Joint)j].X.Raw);
            var reach = (Joint)j == Joint.Head ? Pixels(Body.HeadRadius.Raw) : Stroke / 2;
            left = System.Math.Min(left, x - reach);
            right = System.Math.Max(right, x + reach);
        }
        return (left, right);
    }

    // Both fighters together.
    public static (float Left, float Right) Of(in GameState state)
    {
        var (l1, r1) = Of(state.Fighters[0]);
        var (l2, r2) = Of(state.Fighters[1]);
        return (System.Math.Min(l1, l2), System.Math.Max(r1, r2));
    }

    private static float Pixels(long raw) => raw / (float)Sim.Numerics.Fix.OneRaw;
}
