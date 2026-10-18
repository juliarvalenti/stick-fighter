using StickFighter.Sim.Numerics;

namespace StickFighter.Sim.Collision;

// Keeps fighters from overlapping, within one screen of each other, and inside the stage walls.
public static class Pushing
{
    public static void Resolve(ref GameState state)
    {
        ref var p1 = ref state.Fighters[0];
        ref var p2 = ref state.Fighters[1];
        var stage = state.Stage;

        // Who's on the left. On an exact tie the facings decide, and they're always opposite,
        // so the answer doesn't depend on which player is which.
        var p1Left = p1.Position.X != p2.Position.X ? p1.Position.X < p2.Position.X : p1.Facing > 0;
        ref var left = ref p1Left ? ref p1 : ref p2;
        ref var right = ref p1Left ? ref p2 : ref p1;

        // Where each stood before this frame's move (Step adds the velocity just before this).
        var leftBefore = left.Position.X - left.Velocity.X;
        var rightBefore = right.Position.X - right.Velocity.X;

        // Split the overlap evenly. An odd raw amount rounds up for both, so neither gets the extra unit.
        var overlap = Overlap.BoxesX(Body.PushBox(left.Position), Body.PushBox(right.Position));
        if (overlap > Fix.Zero)
        {
            var half = Fix.FromRaw((overlap.Raw + 1) / 2);
            left.Position = new FixVec2(left.Position.X - half, left.Position.Y);
            right.Position = new FixVec2(right.Position.X + half, right.Position.Y);
        }

        // Walls first, so a fighter walking back into a wall doesn't count as moving away.
        // The separation limit only pulls fighters toward each other, never into a wall.
        ClampToWalls(ref left, stage);
        ClampToWalls(ref right, stage);
        LimitSeparation(ref left, ref right, leftBefore - left.Position.X, right.Position.X - rightBefore, stage);

        // Pinned against a wall, the other fighter takes the whole push.
        var width = Body.PushHalfWidth * 2;
        if (right.Position.X - left.Position.X < width)
        {
            if (left.Position.X <= MinX(stage))
                right.Position = new FixVec2(left.Position.X + width, right.Position.Y);
            else
                left.Position = new FixVec2(right.Position.X - width, left.Position.Y);
        }
    }

    // The furthest apart the fighters' feet can be: their pushboxes' outer edges then span the
    // stage's maximum separation.
    public static Fix MaxGap(in Stage stage) => stage.MaxSeparation - Body.PushHalfWidth * 2;

    // Too far apart, the fighters are pulled back toward each other, each by at most how far it
    // moved away this frame. That makes the limit an invisible wall that moves with the opponent:
    // walking back from a standing opponent stops there, and walking apart together stops both.
    //
    // When both moved away the pull is split evenly, so neither player is favored. One that moved
    // away less (or not at all) takes less, and the other the rest. Anything still left over (a
    // match set up too far apart) is split evenly. Odd raw amounts round up for both.
    private static void LimitSeparation(ref FighterState left, ref FighterState right, Fix leftMoved, Fix rightMoved, in Stage stage)
    {
        var excess = right.Position.X - left.Position.X - MaxGap(stage);
        if (excess <= Fix.Zero)
            return;

        var leftAway = Fix.Max(Fix.Zero, leftMoved);
        var rightAway = Fix.Max(Fix.Zero, rightMoved);
        var half = Fix.FromRaw((excess.Raw + 1) / 2);
        var leftBack = Fix.Min(leftAway, Fix.Max(half, excess - rightAway));
        var rightBack = Fix.Min(rightAway, Fix.Max(half, excess - leftAway));

        var rest = excess - leftBack - rightBack;
        if (rest > Fix.Zero)
        {
            var restHalf = Fix.FromRaw((rest.Raw + 1) / 2);
            leftBack += restHalf;
            rightBack += restHalf;
        }

        left.Position = new FixVec2(left.Position.X + leftBack, left.Position.Y);
        right.Position = new FixVec2(right.Position.X - rightBack, right.Position.Y);
    }

    private static Fix MinX(in Stage stage) => stage.LeftWall + Body.PushHalfWidth;
    private static Fix MaxX(in Stage stage) => stage.RightWall - Body.PushHalfWidth;

    private static void ClampToWalls(ref FighterState fighter, in Stage stage) =>
        fighter.Position = new FixVec2(Fix.Clamp(fighter.Position.X, MinX(stage), MaxX(stage)), fighter.Position.Y);
}
