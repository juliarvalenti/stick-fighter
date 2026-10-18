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

    public override void _Ready() => ZIndex = -10;

    public void Present(in Stage stage, float cameraX)
    {
        _stage = stage;
        _cameraX = cameraX;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var view = GetViewportRect().Size;
        var left = _cameraX - view.X / 2;
        var floor = _stage.Floor.ToFloat();

        // The paper itself, over the whole view (the clear color is only a fallback).
        DrawRect(new Rect2(left, 0, view.X, view.Y), Look.Paper);

        DrawRow(FarBlocks, FarGap, FarDepth, Look.BackdropFar, floor, left, view.X);
        DrawRow(NearBlocks, NearGap, NearDepth, Look.BackdropNear, floor, left, view.X);
    }

    // Lays the row's blocks along the floor from well left of anything the camera can show, and
    // draws the ones in view.
    private void DrawRow(Vector2[] blocks, float gap, float depth, Color color, float floor, float left, float width)
    {
        var x = -width;
        for (var i = 0; x < _stage.RightWall.ToFloat() + width; i++)
        {
            var size = blocks[i % blocks.Length];
            var at = CameraMath.ParallaxX(x, _cameraX, depth);
            if (at + size.X >= left && at <= left + width)
                DrawRect(new Rect2(at, floor - size.Y, size.X, size.Y), color);
            x += size.X + gap;
        }
    }
}
