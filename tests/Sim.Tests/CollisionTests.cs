using StickFighter.Sim.Collision;
using StickFighter.Sim.Numerics;
using Xunit;

namespace StickFighter.Sim.Tests;

public class OverlapTests
{
    private static FixVec2 V(int x, int y) => new(Fix.FromInt(x), Fix.FromInt(y));
    private static Capsule C(int ax, int ay, int bx, int by, int r) => new(V(ax, ay), V(bx, by), Fix.FromInt(r));

    [Fact]
    public void CrossingSegmentsOverlapAtTheCrossing()
    {
        Assert.True(Overlap.Capsules(C(0, 0, 10, 10, 1), C(0, 10, 10, 0, 1), out var contact));
        Assert.Equal(V(5, 5), contact);
    }

    [Fact]
    public void ParallelSegmentsWithinReachOverlap()
    {
        Assert.True(Overlap.Capsules(C(0, 0, 10, 0, 2), C(0, 3, 10, 3, 2), out _));
        Assert.False(Overlap.Capsules(C(0, 0, 10, 0, 1), C(0, 3, 10, 3, 1), out _));
    }

    [Fact]
    public void ParallelOffsetSegmentsMeasureEndToEnd()
    {
        // Collinear, end to end with a 4 px gap.
        Assert.False(Overlap.Capsules(C(0, 0, 10, 0, 1), C(14, 0, 20, 0, 1), out _));
        Assert.True(Overlap.Capsules(C(0, 0, 10, 0, 2), C(14, 0, 20, 0, 2), out var contact));
        Assert.Equal(V(12, 0), contact);
    }

    [Fact]
    public void TouchingCountsAsOverlap()
    {
        // Circles 10 px apart with radii summing to exactly 10.
        Assert.True(Overlap.Capsules(Capsule.Circle(V(0, 0), Fix.FromInt(4)), Capsule.Circle(V(10, 0), Fix.FromInt(6)), out _));
        Assert.False(Overlap.Capsules(Capsule.Circle(V(0, 0), Fix.FromInt(4)), Capsule.Circle(V(10, 0), Fix.FromRatio(23, 4)), out _));
    }

    [Fact]
    public void CircleAgainstTheMiddleOfASegment()
    {
        Assert.True(Overlap.Capsules(Capsule.Circle(V(5, 5), Fix.FromInt(5)), C(0, 0, 10, 0, 0), out var contact));
        Assert.Equal(V(5, 0), new FixVec2(contact.X, Fix.Zero));
        Assert.False(Overlap.Capsules(Capsule.Circle(V(5, 6), Fix.FromInt(5)), C(0, 0, 10, 0, 0), out _));
    }

    [Fact]
    public void ClosestPointsClampToSegmentEnds()
    {
        Overlap.ClosestPoints(V(0, 0), V(10, 0), V(20, 5), V(30, 5), out var c1, out var c2);
        Assert.Equal(V(10, 0), c1);
        Assert.Equal(V(20, 5), c2);
    }

    [Fact]
    public void OverlapIsSymmetric()
    {
        var a = C(0, 0, 40, 30, 6);
        var b = C(35, 0, 10, 50, 6);
        Assert.Equal(Overlap.Capsules(a, b, out var ab), Overlap.Capsules(b, a, out var ba));
        Assert.Equal(ab, ba);
    }

    [Fact]
    public void BoxesOverlapButTouchingEdgesDoNot()
    {
        var a = new Box(V(0, 0), V(10, 10));
        Assert.True(Overlap.Boxes(a, new Box(V(9, 9), V(20, 20))));
        Assert.False(Overlap.Boxes(a, new Box(V(10, 0), V(20, 10))));
        Assert.False(Overlap.Boxes(a, new Box(V(0, 10), V(10, 20))));
        Assert.Equal(Fix.FromInt(3), Overlap.BoxesX(a, new Box(V(7, 0), V(20, 10))));
    }
}

public class PushingTests
{
    private static GameState Apart(int p1X, int p2X)
    {
        var state = GameState.NewMatch(1);
        state.Fighters[0].Position = new FixVec2(Fix.FromInt(p1X), Stage.Training.Floor);
        state.Fighters[1].Position = new FixVec2(Fix.FromInt(p2X), Stage.Training.Floor);
        return state;
    }

    private static Fix Gap(in GameState s) => s.Fighters[1].Position.X - s.Fighters[0].Position.X;

    [Fact]
    public void WalkingIntoEachOtherStopsAtThePushboxes()
    {
        var state = GameState.NewMatch(1);
        var walkIn = new FrameInput(Buttons.Right, Buttons.Left);
        for (var i = 0; i < 300; i++)
        {
            Simulation.Step(ref state, walkIn);
            Assert.True(Gap(state) >= Body.PushHalfWidth * 2, $"overlapping on frame {state.Frame}");
        }
        Assert.Equal(Body.PushHalfWidth * 2, Gap(state));
    }

    [Fact]
    public void OverlapIsSplitEvenly()
    {
        var state = Apart(600, 640); // 20 px overlap
        Pushing.Resolve(ref state);
        Assert.Equal(Fix.FromInt(590), state.Fighters[0].Position.X);
        Assert.Equal(Fix.FromInt(650), state.Fighters[1].Position.X);
    }

    [Fact]
    public void SwappingPlayersGivesTheMirroredResult()
    {
        var a = Apart(600, 641);
        var b = Apart(641, 600);
        b.Fighters[0].Facing = -1;
        b.Fighters[1].Facing = 1;
        Pushing.Resolve(ref a);
        Pushing.Resolve(ref b);
        Assert.Equal(a.Fighters[0].Position, b.Fighters[1].Position);
        Assert.Equal(a.Fighters[1].Position, b.Fighters[0].Position);
    }

    [Fact]
    public void OddOverlapIsNotGivenToEitherPlayer()
    {
        var state = Apart(600, 600);
        state.Fighters[1].Position = new FixVec2(Fix.FromInt(600) + Fix.FromRaw(1), Stage.Training.Floor);
        var midpoint = (state.Fighters[0].Position.X + state.Fighters[1].Position.X).Raw;
        Pushing.Resolve(ref state);
        Assert.Equal(midpoint, (state.Fighters[0].Position.X + state.Fighters[1].Position.X).Raw);
        Assert.True(Gap(state) >= Body.PushHalfWidth * 2);
    }

    [Fact]
    public void ExactlySameSpotSeparatesByFacing()
    {
        var state = Apart(640, 640); // P1 faces right, so P1 goes left
        Pushing.Resolve(ref state);
        Assert.Equal(Fix.FromInt(610), state.Fighters[0].Position.X);
        Assert.Equal(Fix.FromInt(670), state.Fighters[1].Position.X);
    }

    [Theory]
    [InlineData(40, 70)]     // P1 pinned in the left corner
    [InlineData(2490, 2520)] // P2 pinned in the right corner
    public void AFighterInTheCornerPushesTheOtherOut(int p1X, int p2X)
    {
        var state = Apart(p1X, p2X);
        Pushing.Resolve(ref state);
        Assert.True(state.Fighters[0].Position.X >= Body.PushHalfWidth);
        Assert.True(state.Fighters[1].Position.X <= Stage.Training.RightWall - Body.PushHalfWidth);
        Assert.Equal(Body.PushHalfWidth * 2, Gap(state));
    }

    [Fact]
    public void WalkingIntoEachWallStops()
    {
        var state = GameState.NewMatch(1);
        Run(ref state, 400, new FrameInput(Buttons.Left, Buttons.Left));
        Assert.Equal(Body.PushHalfWidth, state.Fighters[0].Position.X);
        Assert.Equal(Body.PushHalfWidth * 3, state.Fighters[1].Position.X);

        Run(ref state, 600, new FrameInput(Buttons.Right, Buttons.Right));
        Assert.Equal(Stage.Training.RightWall - Body.PushHalfWidth, state.Fighters[1].Position.X);
        Assert.Equal(Stage.Training.RightWall - Body.PushHalfWidth * 3, state.Fighters[0].Position.X);
    }

    [Fact]
    public void FightersStartInTheMiddleOfAStageWiderThanTheLimit()
    {
        var stage = Stage.Training;
        var state = GameState.NewMatch(1);
        Assert.Equal(stage.Center * 2, state.Fighters[0].Position.X + state.Fighters[1].Position.X);
        Assert.True(stage.RightWall - stage.LeftWall >= stage.MaxSeparation * 2);
        Assert.Equal(Fix.FromInt(Stage.ScreenWidth), stage.MaxSeparation);
    }

    [Fact]
    public void WalkingApartStopsBothAtTheLimit()
    {
        var state = GameState.NewMatch(1);
        var apart = new FrameInput(Buttons.Left, Buttons.Right);
        for (var i = 0; i < 200; i++)
        {
            Simulation.Step(ref state, apart);
            Assert.True(Gap(state) <= Pushing.MaxGap(state.Stage), $"too far apart on frame {state.Frame}");
        }
        Assert.Equal(Pushing.MaxGap(state.Stage), Gap(state));

        // Both walked back at the same speed, so they stay centered where they started.
        Assert.Equal(Stage.Training.Center * 2, state.Fighters[0].Position.X + state.Fighters[1].Position.X);

        // Pushboxes' outer edges span exactly one screen.
        var outer = state.Fighters[1].Position.X + Body.PushHalfWidth - (state.Fighters[0].Position.X - Body.PushHalfWidth);
        Assert.Equal(Fix.FromInt(Stage.ScreenWidth), outer);
    }

    [Fact]
    public void WalkingBackFromAStandingOpponentStopsAtTheLimit()
    {
        var state = GameState.NewMatch(1);
        var p2 = state.Fighters[1].Position.X;
        Run(ref state, 300, new FrameInput(Buttons.Left, Buttons.None));
        Assert.Equal(p2, state.Fighters[1].Position.X); // the standing fighter isn't dragged
        Assert.Equal(p2 - Pushing.MaxGap(state.Stage), state.Fighters[0].Position.X);
    }

    [Fact]
    public void TheLimitMovesWithTheOpponent()
    {
        var state = GameState.NewMatch(1);
        Run(ref state, 300, new FrameInput(Buttons.Left, Buttons.None));
        var p1 = state.Fighters[0].Position.X;

        // P2 walks forward and P1 keeps walking back: P1 moves again at full speed, because P2
        // closes the gap faster than P1 opens it.
        Simulation.Step(ref state, new FrameInput(Buttons.Left, Buttons.Left));
        Assert.Equal(p1 - Characters.Characters.Placeholder.Stats.WalkBack, state.Fighters[0].Position.X);

        // Now P2 walks back from a standing P1, and it's P2 that stops at the limit.
        p1 = state.Fighters[0].Position.X;
        Run(ref state, 300, new FrameInput(Buttons.None, Buttons.Right));
        Assert.Equal(p1, state.Fighters[0].Position.X);
        Assert.Equal(Pushing.MaxGap(state.Stage), Gap(state));
    }

    [Fact]
    public void BackingOffFromACorneredOpponentStopsAtTheLimit()
    {
        var state = GameState.NewMatch(1);
        Run(ref state, 300, new FrameInput(Buttons.Left, Buttons.Left));
        Run(ref state, 300, new FrameInput(Buttons.Left, Buttons.Right));
        Assert.Equal(Body.PushHalfWidth, state.Fighters[0].Position.X);
        Assert.Equal(Body.PushHalfWidth + Pushing.MaxGap(state.Stage), state.Fighters[1].Position.X);
    }

    [Fact]
    public void TheLimitDoesNotFavorEitherPlayer()
    {
        // The same walk-apart with the players swapped (P2 on the left) ends in the same places.
        var a = GameState.NewMatch(1);
        var b = GameState.NewMatch(1);
        (b.Fighters[0], b.Fighters[1]) = (a.Fighters[1], a.Fighters[0]);

        for (var i = 0; i < 200; i++)
        {
            // The left fighter walks back alone at first and the right one joins later, so the
            // limit pulls unevenly; then the left one turns around.
            var left = i < 150 ? Buttons.Left : Buttons.Right;
            var right = i < 50 ? Buttons.None : Buttons.Right;
            Simulation.Step(ref a, new FrameInput(left, right));
            Simulation.Step(ref b, new FrameInput(right, left));
        }
        Assert.Equal(a.Fighters[0].Position, b.Fighters[1].Position);
        Assert.Equal(a.Fighters[1].Position, b.Fighters[0].Position);
    }

    [Fact]
    public void ASetupTooFarApartIsPulledInEvenly()
    {
        var state = Apart(500, 2000);
        Pushing.Resolve(ref state);
        Assert.Equal(Pushing.MaxGap(state.Stage), Gap(state));
        Assert.Equal(Fix.FromInt(2500), state.Fighters[0].Position.X + state.Fighters[1].Position.X);
    }

    [Fact]
    public void TheStageComesFromTheMatch()
    {
        var narrow = new Stage(100, 900, 500, 600);
        var state = GameState.NewMatch(1, narrow);
        Assert.Equal(Fix.FromInt(500), state.Fighters[0].Position.Y);
        Assert.Equal(Fix.FromInt(1000), state.Fighters[0].Position.X + state.Fighters[1].Position.X);

        Run(ref state, 200, new FrameInput(Buttons.Left, Buttons.Right));
        Assert.Equal(Fix.FromInt(600) - Body.PushHalfWidth * 2, Gap(state));

        Run(ref state, 200, new FrameInput(Buttons.Left, Buttons.Left));
        Assert.Equal(Fix.FromInt(100) + Body.PushHalfWidth, state.Fighters[0].Position.X);
    }

    private static void Run(ref GameState state, int frames, FrameInput input)
    {
        for (var i = 0; i < frames; i++)
            Simulation.Step(ref state, input);
    }
}

public class ContactTests
{
    private static FixVec2 V(int x, int y) => new(Fix.FromInt(x), Fix.FromInt(y));

    private static GameState Close()
    {
        var state = GameState.NewMatch(1);
        state.Fighters[0].Position = V(600, 600);
        state.Fighters[1].Position = V(680, 600);
        return state;
    }

    // P1's front hand reaches toward P2's chest when they stand 80 px apart.
    private static FighterShapes WithJab(in FighterState fighter, int reach)
    {
        var shapes = Body.Shapes(fighter);
        var shoulder = Body.Pose(fighter)[Characters.Joint.Neck];
        shapes.AddHit(new Capsule(shoulder, shoulder + new FixVec2(Fix.FromInt(reach * fighter.Facing), Fix.Zero), Fix.FromInt(6)));
        return shapes;
    }

    [Fact]
    public void PlaceholderFightersHaveNoHitboxes()
    {
        var state = Close();
        var found = Contacts.Find(state, Body.Shapes(state.Fighters[0]), Body.Shapes(state.Fighters[1]));
        Assert.Equal(0, found.Count);
    }

    [Fact]
    public void AJabHitsAndAShortJabMisses()
    {
        var state = Close();
        var p2 = Body.Shapes(state.Fighters[1]);

        var hit = Contacts.Find(state, WithJab(state.Fighters[0], 80), p2);
        Assert.Equal(1, hit.Count);
        Assert.Equal(ContactKind.Hit, hit[0].Kind);
        Assert.Equal(0, hit[0].Attacker);
        Assert.Equal(1, hit[0].Defender);

        // Out of range. (Up close even a short jab connects, with P2's outstretched arm.)
        state.Fighters[1].Position = V(900, 600);
        var miss = Contacts.Find(state, WithJab(state.Fighters[0], 80), Body.Shapes(state.Fighters[1]));
        Assert.Equal(0, miss.Count);
    }

    [Fact]
    public void ArmsAreHittable()
    {
        // A small hit right on P2's guard hand connects: every limb is a hurtbox, including the
        // one a move would strike with.
        var state = Close();
        var hand = Body.Pose(state.Fighters[1])[Characters.Joint.FrontHand];
        var p1 = Body.Shapes(state.Fighters[0]);
        p1.AddHit(Capsule.Circle(hand, Fix.FromInt(2)));

        var hit = Contacts.Find(state, p1, Body.Shapes(state.Fighters[1]));
        Assert.Equal(1, hit.Count);
    }

    [Fact]
    public void TradesFindBothHitsInPlayerOrder()
    {
        var state = Close();
        var found = Contacts.Find(state, WithJab(state.Fighters[0], 80), WithJab(state.Fighters[1], 80));
        Assert.Equal(2, found.Count);
        Assert.Equal(0, found[0].Attacker);
        Assert.Equal(1, found[1].Attacker);
    }

    [Fact]
    public void ProjectileHitsTheOpponentNotItsOwner()
    {
        var state = Close();
        state.Projectiles[0] = new ProjectileState { Active = true, Owner = 0, Position = V(680, 450) };
        state.Projectiles[1] = new ProjectileState { Active = true, Owner = 1, Position = V(680, 450) }; // inside its own owner

        var found = Contacts.Find(state, Body.Shapes(state.Fighters[0]), Body.Shapes(state.Fighters[1]));
        Assert.Contains(found.Items[..found.Count].ToArray(), c => c.Kind == ContactKind.ProjectileHit && c.Attacker == 0 && c.Defender == 1);
        Assert.DoesNotContain(found.Items[..found.Count].ToArray(), c => c.Kind == ContactKind.ProjectileHit && c.Attacker == 1);
    }

    [Fact]
    public void HurtboxesCanIgnoreProjectiles()
    {
        var state = Close();
        state.Projectiles[0] = new ProjectileState { Active = true, Owner = 0, Position = V(680, 450) };

        var p2 = new FighterShapes { Push = Body.PushBox(state.Fighters[1]) };
        var full = Body.Shapes(state.Fighters[1]);
        for (var i = 0; i < full.HurtCount; i++)
            p2.AddHurt(full.Hurt[i].Shape, ignoresProjectiles: true);

        var found = Contacts.Find(state, Body.Shapes(state.Fighters[0]), p2);
        Assert.Equal(0, found.Count);
    }

    [Fact]
    public void OpposingProjectilesClash()
    {
        var state = GameState.NewMatch(1);
        state.Projectiles[0] = new ProjectileState { Active = true, Owner = 0, Position = V(630, 450) };
        state.Projectiles[1] = new ProjectileState { Active = true, Owner = 1, Position = V(670, 450) };

        var found = Contacts.Find(state, Body.Shapes(state.Fighters[0]), Body.Shapes(state.Fighters[1]));
        Assert.Equal(1, found.Count);
        Assert.Equal(ContactKind.ProjectileClash, found[0].Kind);
        Assert.Equal(0, found[0].Attacker);
        Assert.Equal(1, found[0].Defender);
        Assert.Equal(V(650, 450), found[0].Point);
    }

    [Fact]
    public void ProjectilesFarApartOrInactiveDoNotClash()
    {
        var state = GameState.NewMatch(1);
        state.Projectiles[0] = new ProjectileState { Active = true, Owner = 0, Position = V(500, 450) };
        state.Projectiles[1] = new ProjectileState { Active = true, Owner = 1, Position = V(600, 450) };
        Assert.Equal(0, Contacts.Find(state, Body.Shapes(state.Fighters[0]), Body.Shapes(state.Fighters[1])).Count);

        state.Projectiles[1].Position = V(510, 450);
        state.Projectiles[1].Active = false;
        Assert.Equal(0, Contacts.Find(state, Body.Shapes(state.Fighters[0]), Body.Shapes(state.Fighters[1])).Count);
    }
}
