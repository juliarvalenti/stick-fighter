using Godot;
using StickFighter.Sim;

namespace StickFighter;

// Placeholder scene: draws one stick figure in the house style until the real game exists.
public partial class Main : Node2D
{
    private static readonly Color Ink = new(0.1f, 0.1f, 0.12f);
    private static readonly Color Player1 = new(0.85f, 0.2f, 0.25f);
    private const float Stroke = 10f;

    public override void _Ready()
    {
        // `--smoke` (after `--`) runs one frame and exits 0: the headless check in CI.
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--smoke") >= 0)
        {
            GD.Print($"{GameInfo.Name}: smoke ok ({GameInfo.TicksPerSecond} ticks/s)");
            GetTree().CallDeferred(SceneTree.MethodName.Quit, 0);
        }
    }

    public override void _Draw()
    {
        var size = GetViewportRect().Size;
        var hip = new Vector2(size.X / 2, size.Y * 0.62f);
        var neck = hip + new Vector2(0, -130);

        Limb(hip, hip + new Vector2(-45, 120));
        Limb(hip, hip + new Vector2(45, 120));
        Limb(hip, neck);
        Limb(neck, neck + new Vector2(-70, 50));
        Limb(neck, neck + new Vector2(80, -20));
        DrawCircle(neck + new Vector2(0, -38), 30, Player1);

        var font = ThemeDB.FallbackFont;
        DrawString(font, new Vector2(0, size.Y * 0.18f), GameInfo.Name,
            HorizontalAlignment.Center, size.X, 64, Ink);
    }

    private void Limb(Vector2 from, Vector2 to)
    {
        DrawLine(from, to, Player1, Stroke);
        DrawCircle(from, Stroke / 2, Player1);
        DrawCircle(to, Stroke / 2, Player1);
    }
}
