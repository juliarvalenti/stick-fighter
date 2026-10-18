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
        state.Fighters[0].Position = new FixVec2(Fix.FromInt(p1X), Stage.Floor);
        state.Fighters[1].Position = new FixVec2(Fix.FromInt(p2X), Stage.Floor);
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
        state.Fighters[1].Position = new FixVec2(Fix.FromInt(600) + Fix.FromRaw(1), Stage.Floor);
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
    [InlineData(1210, 1240)] // P2 pinned in the right corner
    public void AFighterInTheCornerPushesTheOtherOut(int p1X, int p2X)
    {
        var state = Apart(p1X, p2X);
        Pushing.Resolve(ref state);
        Assert.True(state.Fighters[0].Position.X >= Body.PushHalfWidth);
        Assert.True(state.Fighters[1].Position.X <= Stage.RightWall - Body.PushHalfWidth);
        Assert.Equal(Body.PushHalfWidth * 2, Gap(state));
    }

    [Fact]
    public void WalkingIntoTheWallStops()
    {
        var state = GameState.NewMatch(1);
        var retreat = new FrameInput(Buttons.Left, Buttons.Right);
        for (var i = 0; i < 300; i++)
            Simulation.Step(ref state, retreat);
        Assert.Equal(Body.PushHalfWidth, state.Fighters[0].Position.X);
        Assert.Equal(Stage.RightWall - Body.PushHalfWidth, state.Fighters[1].Position.X);
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
        var shoulder = Body.Joint(fighter, Body.Neck);
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
    public void TheAttackersOutstretchedLimbIsHittable()
    {
        var state = Close();
        var hit = Contacts.Find(state, WithJab(state.Fighters[0], 30), Body.Shapes(state.Fighters[1]));
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

        var p2 = new FighterShapes { Push = Body.PushBox(state.Fighters[1].Position) };
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
