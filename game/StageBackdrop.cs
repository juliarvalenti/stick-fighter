using Godot;
using StickFighter.Sim;
using StickFighter.Style;

namespace StickFighter;

// The stage behind the fighters: flat paper, and two rows of distant blocks that scroll slower
// than the stage (parallax), so the camera's movement reads as depth. Flat fills in two faint
// paper tones only, so nothing competes with the players' colors (Art.Tests checks every player
// color against both at 4.5:1). Presentation only: reads the stage and the camera, writes nothing.
public partial class StageBackdrop : Node2D
{
    // How fast each row scrolls relative to the stage.
    private const float FarDepth = 0.25f;
    private const float NearDepth = 0.5f;

    // Block widths and heights above the floor, laid left to right and repeated. Fixed lists, not
    // random, so the stage looks the same every run.
    private static readonly Vector2[] FarBlocks =
    {
        new(140, 150), new(90, 215), new(170, 120), new(110, 185), new(200, 140), new(80, 240), new(150, 165),
    };

    private static readonly Vector2[] NearBlocks =
    {
        new(220, 70), new(160, 110), new(260, 55), new(140, 95), new(200, 80),
    };

    private const float FarGap = 14;
    private const float NearGap = 48;

    private Stage _stage;
    private float _cameraX;
    private float _cameraY;
    private float _zoom = 1f;

    public override void _Ready() => ZIndex = -10;

    // `zoom` below 1 shows more than the viewport (the camera zooming out at the separation limit).
    public void Present(in Stage stage, Vector2 camera, float zoom)
    {
        _stage = stage;
        (_cameraX, _cameraY) = (camera.X, camera.Y);
        _zoom = zoom;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var view = GetViewportRect().Size / _zoom;
        var left = _cameraX - view.X / 2;
        var top = _cameraY - view.Y / 2;
        var floor = _stage.Floor.ToFloat();

        // The paper itself, over the whole view (the clear color is only a fallback).
        DrawRect(new Rect2(left, top, view.X, view.Y), Look.Paper);

        DrawRow(FarBlocks, FarGap, FarDepth, Look.BackdropFar, floor, left, view.X);
        DrawRow(NearBlocks, NearGap, NearDepth, Look.BackdropNear, floor, left, view.X);
    }

    // Lays the row's blocks along the floor from a screen left of the stage to a screen past it (a
    // fixed layout, whatever the view), and draws the ones in view.
    private void DrawRow(Vector2[] blocks, float gap, float depth, Color color, float floor, float left, float width)
    {
        var x = -(float)Stage.ScreenWidth;
        for (var i = 0; x < _stage.RightWall.ToFloat() + Stage.ScreenWidth; i++)
        {
            var size = blocks[i % blocks.Length];
            var at = CameraMath.ParallaxX(x, _cameraX, depth);
            if (at + size.X >= left && at <= left + width)
                DrawRect(new Rect2(at, floor - size.Y, size.X, size.Y), color);
            x += size.X + gap;
        }
    }
}
