using System.Runtime.CompilerServices;

namespace StickFighter.Sim.Collision;

public struct Hurtbox
{
    public Capsule Shape;

    // Set by moves that pass through fireballs: projectiles don't hit this part of the body.
    public bool IgnoresProjectiles;
}

// One fighter's collision shapes for one frame, derived from its pose. Not part of GameState:
// it's rebuilt from the state whenever it's needed, so it never needs saving or checksumming.
public struct FighterShapes
{
    public const int MaxHurtboxes = 8;
    public const int MaxHitboxes = 4;

    public HurtboxList Hurt;
    public int HurtCount;

    public HitboxList Hit;
    public int HitCount;

    public Box Push;

    public void AddHurt(in Capsule shape, bool ignoresProjectiles = false) =>
        Hurt[HurtCount++] = new Hurtbox { Shape = shape, IgnoresProjectiles = ignoresProjectiles };

    public void AddHit(in Capsule shape) => Hit[HitCount++] = shape;
}

[InlineArray(FighterShapes.MaxHurtboxes)]
public struct HurtboxList
{
    private Hurtbox _element;
}

[InlineArray(FighterShapes.MaxHitboxes)]
public struct HitboxList
{
    private Capsule _element;
}
