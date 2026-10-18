using StickFighter.Sim.Numerics;

namespace StickFighter.Sim.Collision;

// Keeps fighters from overlapping and inside the stage walls.
public static class Pushing
{
    public static void Resolve(ref GameState state)
    {
        ref var p1 = ref state.Fighters[0];
        ref var p2 = ref state.Fighters[1];

        // Who's on the left. On an exact tie the facings decide, and they're always opposite,
        // so the answer doesn't depend on which player is which.
        var p1Left = p1.Position.X != p2.Position.X ? p1.Position.X < p2.Position.X : p1.Facing > 0;
        ref var left = ref p1Left ? ref p1 : ref p2;
        ref var right = ref p1Left ? ref p2 : ref p1;

        // Split the overlap evenly. An odd raw amount rounds up for both, so neither gets the extra unit.
        var overlap = Overlap.BoxesX(Body.PushBox(left.Position), Body.PushBox(right.Position));
        if (overlap > Fix.Zero)
        {
            var half = Fix.FromRaw((overlap.Raw + 1) / 2);
            left.Position = new FixVec2(left.Position.X - half, left.Position.Y);
            right.Position = new FixVec2(right.Position.X + half, right.Position.Y);
        }

        ClampToWalls(ref left);
        ClampToWalls(ref right);

        // Pinned against a wall, the other fighter takes the whole push.
        var width = Body.PushHalfWidth * 2;
        if (right.Position.X - left.Position.X < width)
        {
            if (left.Position.X <= MinX)
                right.Position = new FixVec2(left.Position.X + width, right.Position.Y);
            else
                left.Position = new FixVec2(right.Position.X - width, left.Position.Y);
        }
    }

    private static Fix MinX => Stage.LeftWall + Body.PushHalfWidth;
    private static Fix MaxX => Stage.RightWall - Body.PushHalfWidth;

    private static void ClampToWalls(ref FighterState fighter) =>
        fighter.Position = new FixVec2(Fix.Clamp(fighter.Position.X, MinX, MaxX), fighter.Position.Y);
}
