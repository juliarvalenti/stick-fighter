using Godot;
using StickFighter.Sim;
using StickFighter.Sim.Collision;

namespace StickFighter;

// Keeps both fighters framed: centered on their midpoint with a little lag, clamped to the stage
// walls, at a fixed zoom. It reads game state and never writes it.
//
// Effects for later epics: Shake (Hitstop and sparks) and PushIn (Meter and supers). Each is a
// pure function (CameraMath) of its inputs and of how many frames have passed since it started,
// counted in simulation frames, so a scenario's captures come out the same every run.
public partial class StageCamera : Camera2D
{
    private float _x;
    private bool _placed;

    private float _shakeAmplitude;
    private int _shakeDuration;
    private int _shakeStart;

    private float _pushX;
    private float _pushY;
    private float _pushZoom = 1f;
    private int _pushIn;
    private int _pushHold;
    private int _pushOut;
    private int _pushStart;

    private int _frame;

    public override void _Ready()
    {
        PositionSmoothingEnabled = false;
        IgnoreRotation = true;
        MakeCurrent();
    }

    // Shakes the screen from now, fading over `frames` frames.
    public void Shake(float amplitude, int frames)
    {
        _shakeAmplitude = amplitude;
        _shakeDuration = frames;
        _shakeStart = _frame;
    }

    // Zooms toward a point over `inFrames`, holds, then eases back over `outFrames`.
    public void PushIn(Vector2 point, float zoom, int inFrames, int holdFrames, int outFrames)
    {
        (_pushX, _pushY, _pushZoom) = (point.X, point.Y, zoom);
        (_pushIn, _pushHold, _pushOut) = (inFrames, holdFrames, outFrames);
        _pushStart = _frame;
    }

    // Moves the camera for a new state, after `ticks` simulation steps. The lag advances once per
    // tick, so it doesn't depend on the display's frame rate.
    public void Present(in GameState state, int ticks)
    {
        _frame = state.Frame;
        var view = GetViewportRect().Size;
        var stage = state.Stage;
        var left = stage.LeftWall.ToFloat();
        var right = stage.RightWall.ToFloat();

        var p1 = state.Fighters[0].Position.X.ToFloat();
        var p2 = state.Fighters[1].Position.X.ToFloat();
        var half = Body.PushHalfWidth.ToFloat();
        var leftEdge = Mathf.Min(p1, p2) - half;
        var rightEdge = Mathf.Max(p1, p2) + half;
        var target = CameraMath.TargetX(p1, p2, left, right, view.X);

        if (!_placed)
        {
            _x = target;
            _placed = true;
        }
        for (var i = 0; i < ticks; i++)
            _x = CameraMath.Follow(_x, target, leftEdge, rightEdge, view.X);

        var amount = CameraMath.PushInAmount(_frame - _pushStart, _pushIn, _pushHold, _pushOut);
        var (x, y, zoom) = CameraMath.PushIn(_x, view.Y / 2, _pushX, _pushY, _pushZoom, amount, left, right, view.Y, view.X, view.Y);
        var (shakeX, shakeY) = CameraMath.Shake(_shakeAmplitude, _shakeDuration, _frame - _shakeStart);

        Position = new Vector2(x, y);
        Zoom = new Vector2(zoom, zoom);
        Offset = new Vector2(shakeX, shakeY);
        ForceUpdateScroll();
    }
}
