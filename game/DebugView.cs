using Godot;
using StickFighter.Sim;
using StickFighter.Sim.Characters;
using StickFighter.Sim.Collision;

namespace StickFighter;

// Draws what the simulation sees, over the fighters: skeleton joints, hurtboxes, hitboxes,
// pushboxes and projectile shapes, the walls and a ruler along the floor, plus the frame number
// and state checksum. Toggled with F2.
// Read only: it gets a copy of the state each frame and never writes back.
// Training mode's hitbox view will reuse it.
public partial class DebugView : Node2D
{
    private static readonly Color JointColor = new(0.1f, 0.1f, 0.12f);
    private static readonly Color HurtColor = new(0.1f, 0.7f, 0.3f);
    private static readonly Color HitColor = new(0.9f, 0.1f, 0.1f);
    private static readonly Color SpentHitColor = new(0.5f, 0.05f, 0.1f);
    private static readonly Color PushColor = new(0.2f, 0.4f, 0.95f);
    private static readonly Color ProjectileColor = new(0.95f, 0.55f, 0.1f);
    private static readonly Color RulerColor = new(0.1f, 0.1f, 0.12f, 0.5f);
    private const float Line = 2f;
    private const int RulerStep = 160;

    private GameState _state;
    private Label _info = null!;

    // Screen-anchored: the frame and checksum stay put while the camera moves the stage.
    private CanvasLayer _hud = null!;

    // Off in scenario captures: the checksum changes with any new state field, and golden images
    // should change only when what's drawn does. The live F2 view keeps it.
    public bool ShowChecksum { get; set; } = true;

    public override void _Ready()
    {
        ZIndex = 10;
        _hud = new CanvasLayer { Layer = 10 };
        _info = new Label { Position = new Vector2(1280 - 330, 12), Size = new Vector2(318, 40) };
        _info.HorizontalAlignment = HorizontalAlignment.Right;
        _info.AddThemeFontSizeOverride("font_size", 16);
        _info.AddThemeColorOverride("font_color", JointColor);
        _hud.AddChild(_info);
        AddChild(_hud);

        // `-- --debug` starts with it on.
        Visible = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--debug") >= 0;
        _hud.Visible = Visible;
    }

    // A CanvasLayer doesn't inherit visibility, so the label follows this node by hand.
    public override void _Notification(int what)
    {
        if (what == NotificationVisibilityChanged && _hud is not null)
            _hud.Visible = Visible;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.F2 })
            Visible = !Visible;
    }

    // Hands the view this frame's state. (Not Show(): CanvasItem.Show() means "make visible".)
    public void Present(in GameState state)
    {
        _state = state;
        if (!Visible)
            return;
        _info.Text = ShowChecksum ? $"frame {state.Frame}\nchecksum {state.Checksum():x16}" : $"frame {state.Frame}";
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawStage(_state.Stage);
        for (var i = 0; i < GameState.PlayerCount; i++)
            DrawFighter(_state.Fighters[i]);

        for (var slot = 0; slot < GameState.MaxProjectiles; slot++)
        {
            var projectile = _state.Projectiles[slot];
            if (projectile.Active)
                DrawCapsule(Contacts.ProjectileShape(projectile), ProjectileColor);
        }
    }

    // The walls, and a tick every RulerStep px under the floor labeled with its stage x, so
    // captures show where on the stage the camera is.
    private void DrawStage(in Stage stage)
    {
        var left = stage.LeftWall.ToFloat();
        var right = stage.RightWall.ToFloat();
        var floor = stage.Floor.ToFloat();
        DrawLine(new Vector2(left, 0), new Vector2(left, floor), PushColor, Line);
        DrawLine(new Vector2(right, 0), new Vector2(right, floor), PushColor, Line);

        var font = ThemeDB.FallbackFont;
        for (var x = stage.LeftWall.FloorToInt(); x <= stage.RightWall.FloorToInt(); x += RulerStep)
        {
            DrawLine(new Vector2(x, floor), new Vector2(x, floor + 12), RulerColor, Line);
            DrawString(font, new Vector2(x + 4, floor + 30), x.ToString(System.Globalization.CultureInfo.InvariantCulture),
                HorizontalAlignment.Left, -1, 14, RulerColor);
        }
    }

    private void DrawFighter(FighterState fighter)
    {
        var shapes = Body.Shapes(fighter);

        var push = shapes.Push;
        DrawRect(new Rect2(push.Min.ToVector2(), (push.Max - push.Min).ToVector2()), PushColor, false, Line);

        for (var i = 0; i < shapes.HurtCount; i++)
            DrawCapsule(shapes.Hurt[i].Shape, HurtColor);
        for (var i = 0; i < shapes.HitCount; i++)
            DrawCapsule(shapes.Hit[i], HitColor);

        // A hitbox that has already connected no longer collides, but it still shows, darker, so
        // you can see where the hit landed.
        if (Body.MoveOf(fighter) is { } move && move.IsActive(fighter.ActionFrame) && fighter.MoveHit)
            DrawCapsule(Body.Segment(Body.Pose(fighter), move.HitboxLimb, move.HitboxRadius + move.ActiveBonus), SpentHitColor);

        var joints = Body.Pose(fighter);
        for (var i = 0; i < Skeleton.JointCount; i++)
            DrawCircle(joints[i].ToVector2(), 4, JointColor);
        DrawCircle(fighter.Position.ToVector2(), 5, PushColor); // origin: the feet
    }

    // A capsule outline: the two end circles and the two sides between them.
    private void DrawCapsule(in Capsule capsule, Color color)
    {
        var a = capsule.A.ToVector2();
        var b = capsule.B.ToVector2();
        var r = capsule.Radius.ToFloat();
        DrawArc(a, r, 0, Mathf.Tau, 24, color, Line);
        if (a == b)
            return;
        DrawArc(b, r, 0, Mathf.Tau, 24, color, Line);
        var side = (b - a).Normalized().Orthogonal() * r;
        DrawLine(a + side, b + side, color, Line);
        DrawLine(a - side, b - side, color, Line);
    }
}
