using Xunit;

namespace StickFighter.Presentation.Tests;

public class CameraMathTests
{
    private const float Left = 0f;
    private const float Right = 2560f;
    private const float Width = 1280f;

    [Fact]
    public void TargetIsTheMidpointAwayFromTheWalls()
    {
        Assert.Equal(1280f, CameraMath.TargetX(1080f, 1480f, Left, Right, Width));
        Assert.Equal(640f, CameraMath.TargetX(30f, 90f, Left, Right, Width));     // left corner
        Assert.Equal(1920f, CameraMath.TargetX(2470f, 2530f, Left, Right, Width)); // right corner
    }

    [Fact]
    public void ZoomingInLetsTheCenterGetCloserToTheWall()
    {
        Assert.Equal(320f, CameraMath.TargetX(30f, 90f, Left, Right, Width, zoom: 2f));
    }

    [Fact]
    public void FollowLagsThenSettlesOnTheTarget()
    {
        var x = 1000f;
        x = CameraMath.Follow(x, 1100f, 900f, 1200f, Width);
        Assert.Equal(1020f, x); // a fifth of the way
        for (var i = 0; i < 60; i++)
            x = CameraMath.Follow(x, 1100f, 900f, 1200f, Width);
        Assert.Equal(1100f, x); // snapped exactly, no endless creep
    }

    [Fact]
    public void FollowNeverCutsOffAFighter()
    {
        // At the separation limit (outer edges exactly one screen apart) the lagging camera is held
        // at the only center that shows both.
        var x = CameraMath.Follow(500f, 1280f, 640f, 1920f, Width);
        Assert.Equal(1280f, x);

        // With room to spare it may lag, but each edge stays on screen.
        x = CameraMath.Follow(500f, 1280f, 1000f, 1400f, Width);
        Assert.True(x - Width / 2 <= 1000f && x + Width / 2 >= 1400f);
    }

    [Fact]
    public void ShakeIsAPureFunctionOfItsInputs()
    {
        for (var frame = 0; frame < 20; frame++)
            Assert.Equal(CameraMath.Shake(8f, 20, frame, seed: 3), CameraMath.Shake(8f, 20, frame, seed: 3));
        Assert.NotEqual(CameraMath.Shake(8f, 20, 1, seed: 3), CameraMath.Shake(8f, 20, 2, seed: 3));
        Assert.NotEqual(CameraMath.Shake(8f, 20, 1, seed: 3), CameraMath.Shake(8f, 20, 1, seed: 4));
    }

    [Fact]
    public void ShakeFadesAndStops()
    {
        for (var frame = 0; frame < 20; frame++)
        {
            var (x, y) = CameraMath.Shake(8f, 20, frame);
            var limit = 8f * (20 - frame) / 20;
            Assert.InRange(x, -limit, limit);
            Assert.InRange(y, -limit, limit);
        }
        Assert.Equal((0f, 0f), CameraMath.Shake(8f, 20, 20));
        Assert.Equal((0f, 0f), CameraMath.Shake(8f, 20, -1));
        Assert.Equal((0f, 0f), CameraMath.Shake(8f, 0, 0));
    }

    [Fact]
    public void PushInIsAPureFunctionOfItsInputs()
    {
        for (var frame = -2; frame < 40; frame++)
        {
            var a = CameraMath.PushInAmount(frame, 10, 15, 10);
            Assert.Equal(a, CameraMath.PushInAmount(frame, 10, 15, 10));
            Assert.Equal(
                CameraMath.PushIn(1280f, 360f, 1400f, 400f, 1.5f, a, Left, Right, 720f, Width, 720f),
                CameraMath.PushIn(1280f, 360f, 1400f, 400f, 1.5f, a, Left, Right, 720f, Width, 720f));
        }
    }

    [Fact]
    public void PushInEasesInHoldsAndEasesOut()
    {
        Assert.Equal(0f, CameraMath.PushInAmount(0, 10, 15, 10));
        Assert.Equal(0.5f, CameraMath.PushInAmount(5, 10, 15, 10));
        Assert.Equal(1f, CameraMath.PushInAmount(10, 10, 15, 10));
        Assert.Equal(1f, CameraMath.PushInAmount(24, 10, 15, 10));
        Assert.Equal(0.5f, CameraMath.PushInAmount(30, 10, 15, 10));
        Assert.Equal(0f, CameraMath.PushInAmount(35, 10, 15, 10));
        Assert.Equal(0f, CameraMath.PushInAmount(-1, 10, 15, 10));
    }

    [Fact]
    public void PushInZoomsTowardThePointAndStaysOnTheStage()
    {
        Assert.Equal((1280f, 360f, 1f), CameraMath.PushIn(1280f, 360f, 1400f, 400f, 2f, 0f, Left, Right, 720f, Width, 720f));
        Assert.Equal((1400f, 400f, 2f), CameraMath.PushIn(1280f, 360f, 1400f, 400f, 2f, 1f, Left, Right, 720f, Width, 720f));

        // Toward a point near the left wall and the top: the view stops at the stage's edges.
        var (x, y, zoom) = CameraMath.PushIn(640f, 360f, 50f, 20f, 2f, 1f, Left, Right, 720f, Width, 720f);
        Assert.Equal((320f, 180f, 2f), (x, y, zoom));
    }
}
