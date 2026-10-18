using System;

namespace StickFighter;

// The camera's arithmetic, as pure functions of their arguments: no Godot, no clock, no hidden
// state. StageCamera keeps the little state there is (where it is now, when an effect started),
// and tests/Presentation.Tests checks these directly.
//
// Presentation only: floats are fine here, and nothing computed here flows back into the game.
public static class CameraMath
{
    // How much of the remaining distance the camera closes each tick; under half a pixel it snaps.
    public const float FollowRate = 0.2f;
    private const float SnapDistance = 0.5f;

    // Room kept between a fighter's drawn edge and the screen edge.
    public const float FramePad = 16f;

    // The zoom that fits both fighters' drawn extents (plus FramePad each side) in the view: 1
    // unless they're drawn wider than the screen, which only happens near the separation limit
    // (their pushboxes span one screen there, and arms and leans reach past them). Then the camera
    // zooms out just enough rather than cutting anyone off.
    public static float FrameZoom(float drawnLeft, float drawnRight, float viewWidth)
    {
        var needed = drawnRight - drawnLeft + 2 * FramePad;
        return needed <= viewWidth ? 1f : viewWidth / needed;
    }

    // Where the camera wants to be: the fighters' midpoint, but no closer to a wall than half the
    // visible width. Follow may still take it past a wall, just far enough to keep a cornered
    // fighter's drawn body in view (it's drawn past the wall its pushbox stops at).
    public static float TargetX(float p1X, float p2X, float leftWall, float rightWall, float viewWidth, float zoom = 1f) =>
        ClampToStage((p1X + p2X) / 2, leftWall, rightWall, viewWidth / zoom);

    // One tick of lag toward the target, then kept where both fighters' outer edges (their drawn
    // extents, padded) are in view, past a wall if need be. At FrameZoom's zoom that range is never
    // empty, so the lag can never cut a fighter off.
    public static float Follow(float current, float target, float leftEdge, float rightEdge, float viewWidth)
    {
        var next = MathF.Abs(target - current) < SnapDistance ? target : current + (target - current) * FollowRate;
        var half = viewWidth / 2;
        var min = rightEdge - half;
        var max = leftEdge + half;
        return min <= max ? Math.Clamp(next, min, max) : (leftEdge + rightEdge) / 2;
    }

    // One display frame of framing: the zoom that fits what's drawn, and the camera's x after
    // `ticks` ticks of lag from `current` (null to cut straight to the target), then kept where
    // everything drawn, padded, is in view.
    public static (float X, float Zoom) Frame(float? current, int ticks, float p1X, float p2X, float drawnLeft, float drawnRight,
        float leftWall, float rightWall, float viewWidth)
    {
        var zoom = FrameZoom(drawnLeft, drawnRight, viewWidth);
        var visible = viewWidth / zoom;
        var target = TargetX(p1X, p2X, leftWall, rightWall, visible);
        var (left, right) = (drawnLeft - FramePad, drawnRight + FramePad);
        var x = current ?? target;
        for (var i = 0; i < ticks; i++)
            x = Follow(x, target, left, right, visible);
        return (Follow(x, x, left, right, visible), zoom);
    }

    public static float ClampToStage(float centerX, float leftWall, float rightWall, float visibleWidth)
    {
        var half = visibleWidth / 2;
        if (rightWall - leftWall <= visibleWidth)
            return (leftWall + rightWall) / 2;
        return Math.Clamp(centerX, leftWall + half, rightWall - half);
    }

    // Where to draw, in stage coordinates, a backdrop shape placed at `layerX` on a layer at
    // `depth`: 1 moves with the stage, 0 stays fixed to the screen, and in between scrolls slower
    // than the stage, so it reads as further away.
    public static float ParallaxX(float layerX, float cameraX, float depth) => layerX + cameraX * (1f - depth);

    // Screen shake: a jolt that fades linearly to nothing over `duration` frames. The direction
    // each frame comes from an integer hash of (seed, frame), so the same inputs always shake the
    // same way.
    public static (float X, float Y) Shake(float amplitude, int duration, int elapsed, int seed = 0)
    {
        if (duration <= 0 || elapsed < 0 || elapsed >= duration)
            return (0f, 0f);
        var strength = amplitude * (duration - elapsed) / duration;
        return (strength * Unit(Hash(seed, elapsed, 0)), strength * Unit(Hash(seed, elapsed, 1)));
    }

    // How far into a push-in the camera is, from 0 (normal) to 1 (fully pushed in): eased in over
    // `inFrames`, held for `holdFrames`, eased back out over `outFrames`.
    public static float PushInAmount(int elapsed, int inFrames, int holdFrames, int outFrames)
    {
        if (elapsed < 0)
            return 0f;
        if (elapsed < inFrames)
            return Ease((float)elapsed / inFrames);
        elapsed -= inFrames;
        if (elapsed < holdFrames)
            return 1f;
        elapsed -= holdFrames;
        return elapsed < outFrames ? 1f - Ease((float)elapsed / outFrames) : 0f;
    }

    // The view during a push-in: zoom and center blend from the normal view toward the point, and
    // the center is kept inside the stage at the new zoom.
    public static (float X, float Y, float Zoom) PushIn(
        float baseX, float baseY, float pointX, float pointY, float zoom, float amount,
        float leftWall, float rightWall, float stageHeight, float viewWidth, float viewHeight)
    {
        // No push-in: the normal view as it is (which may be past a wall to frame a cornered fighter).
        if (amount <= 0f)
            return (baseX, baseY, 1f);
        var z = 1f + (zoom - 1f) * amount;
        var x = ClampToStage(baseX + (pointX - baseX) * amount, leftWall, rightWall, viewWidth / z);
        var y = ClampToStage(baseY + (pointY - baseY) * amount, 0f, stageHeight, viewHeight / z);
        return (x, y, z);
    }

    // Smoothstep: starts and ends gently.
    private static float Ease(float t) => t * t * (3f - 2f * t);

    // -1 to 1 from the hash's top bits.
    private static float Unit(uint hash) => (hash >> 8) / (float)(1 << 23) - 1f;

    // A small fixed integer mix (lowbias32), the same on every machine.
    private static uint Hash(int seed, int frame, int axis)
    {
        var x = (uint)seed * 0x9E3779B9u ^ (uint)frame * 0x85EBCA6Bu ^ (uint)axis * 0xC2B2AE35u;
        x ^= x >> 16;
        x *= 0x7FEB352Du;
        x ^= x >> 15;
        x *= 0x846CA68Bu;
        x ^= x >> 16;
        return x;
    }
}
