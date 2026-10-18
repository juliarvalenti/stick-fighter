using Godot;
using StickFighter.Sim;
using StickFighter.Sim.Collision;
using StickFighter.Sim.Numerics;

namespace StickFighter;

// Draws what the simulation sees, over the fighters: skeleton joints, hurtboxes, hitboxes,
// pushboxes and projectile shapes, plus the frame number and state checksum. Toggled with F2.
// Read only: it gets a copy of the state each frame and never writes back.
// Training mode's hitbox view will reuse it.
public partial class DebugView : Node2D
{
    private static readonly Color JointColor = new(0.1f, 0.1f, 0.12f);
    private static readonly Color HurtColor = new(0.1f, 0.7f, 0.3f);
    private static readonly Color HitColor = new(0.9f, 0.1f, 0.1f);
    private static readonly Color PushColor = new(0.2f, 0.4f, 0.95f);
    private static readonly Color ProjectileColor = new(0.95f, 0.55f, 0.1f);
    private const float Line = 2f;

    private GameState _state;
    private Label _info = null!;

    public override void _Ready()
    {
        ZIndex = 10;
        _info = new Label { Position = new Vector2(1280 - 330, 12), Size = new Vector2(318, 40) };
        _info.HorizontalAlignment = HorizontalAlignment.Right;
        _info.AddThemeFontSizeOverride("font_size", 16);
        _info.AddThemeColorOverride("font_color", JointColor);
        AddChild(_info);

        // `-- --debug` starts with it on.
        Visible = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--debug") >= 0;
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
        _info.Text = $"frame {state.Frame}\nchecksum {state.Checksum():x16}";
        QueueRedraw();
    }

    public override void _Draw()
    {
        for (var i = 0; i < GameState.PlayerCount; i++)
            DrawFighter(_state.Fighters[i]);

        for (var slot = 0; slot < GameState.MaxProjectiles; slot++)
        {
            var projectile = _state.Projectiles[slot];
            if (projectile.Active)
                DrawCapsule(Contacts.ProjectileShape(projectile), ProjectileColor);
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

        foreach (var joint in Joints)
            DrawCircle(Body.Joint(fighter, joint).ToVector2(), 4, JointColor);
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

    private static readonly FixVec2[] Joints =
    {
        Body.Hip, Body.Neck, Body.Head, Body.BackFoot, Body.FrontFoot, Body.BackHand, Body.FrontHand,
    };
}
