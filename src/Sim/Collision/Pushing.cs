using StickFighter.Sim.Numerics;

namespace StickFighter.Sim.Collision;

// Keeps fighters from overlapping, within one screen of each other, and inside the stage walls.
public static class Pushing
{
    // Only from tests and tools that move fighters by velocity alone; Step passes real positions.
    public static void Resolve(ref GameState state) =>
        Resolve(ref state, state.Fighters[0].Position.X - state.Fighters[0].Velocity.X, state.Fighters[1].Position.X - state.Fighters[1].Velocity.X);

    // p1Before and p2Before are where each fighter stood at the start of the frame, so every
    // source of movement this frame (walking, pushback, ...) counts toward "moved away".
    public static void Resolve(ref GameState state, Fix p1Before, Fix p2Before)
    {
        ref var p1 = ref state.Fighters[0];
        ref var p2 = ref state.Fighters[1];
        var stage = state.Stage;

        // Who's on the left. On an exact tie, who was on the left when the frame began; failing
        // that, the facings (opposite whenever both are free to turn). None of it depends on which
        // player is which.
        var p1Left = p1.Position.X != p2.Position.X ? p1.Position.X < p2.Position.X
            : p1Before != p2Before ? p1Before < p2Before
            : p1.Facing > 0;
        ref var left = ref p1Left ? ref p1 : ref p2;
        ref var right = ref p1Left ? ref p2 : ref p1;
        var leftBefore = p1Left ? p1Before : p2Before;
        var rightBefore = p1Left ? p2Before : p1Before;

        var overlap = Overlap.BoxesX(Body.PushBox(left), Body.PushBox(right));
        if (overlap > Fix.Zero)
        {
            var (leftBack, rightBack) = SplitOverlap(
                overlap, left.Position.X - leftBefore, rightBefore - right.Position.X, Body.PushShare);
            left.Position = new FixVec2(left.Position.X - leftBack, left.Position.Y);
            right.Position = new FixVec2(right.Position.X + rightBack, right.Position.Y);
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

    // How far each fighter is pushed back out of an overlap, given how far each walked in this
    // frame. Each one's step in is shared: `share` of it pushes the other fighter along, and the
    // walker keeps the same share. So a fighter walking into one who isn't walking in moves the
    // pair together at `share` of its speed, two walking in alike stop each other, and with no
    // walking in (pushback, a setup) the overlap is split evenly. The same rule for both, so
    // swapping the players swaps the result; a raw unit short from rounding goes to both.
    public static (Fix LeftBack, Fix RightBack) SplitOverlap(Fix overlap, Fix leftIn, Fix rightIn, Fix share)
    {
        leftIn = Fix.Max(Fix.Zero, leftIn);
        rightIn = Fix.Max(Fix.Zero, rightIn);
        // Equal steps in (none, or both walking in alike) split it evenly, exactly. An odd raw
        // amount rounds up for both.
        var total = leftIn + rightIn;
        if (leftIn == rightIn)
        {
            var half = Fix.FromRaw((overlap.Raw + 1) / 2);
            return (half, half);
        }

        // Each is pushed back by the part of its own step it gives up, plus the part of the other's
        // step that pushes it, in proportion to the overlap.
        var leftBack = overlap * ((Fix.One - share) * leftIn + share * rightIn) / total;
        var rightBack = overlap * ((Fix.One - share) * rightIn + share * leftIn) / total;
        var shortfall = overlap - leftBack - rightBack;
        if (shortfall > Fix.Zero)
        {
            leftBack += shortfall;
            rightBack += shortfall;
        }
        return (leftBack, rightBack);
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

    // The furthest left and right a fighter's feet can be: its pushbox against the wall.
    public static Fix MinX(in Stage stage) => stage.LeftWall + Body.PushHalfWidth;
    public static Fix MaxX(in Stage stage) => stage.RightWall - Body.PushHalfWidth;

    private static void ClampToWalls(ref FighterState fighter, in Stage stage) =>
        fighter.Position = new FixVec2(Fix.Clamp(fighter.Position.X, MinX(stage), MaxX(stage)), fighter.Position.Y);
}
