using System.Runtime.CompilerServices;
using StickFighter.Sim.Numerics;

namespace StickFighter.Sim.Collision;

public enum ContactKind
{
    // A fighter's hitbox touched the other fighter's hurtbox. Attacker and Defender are players.
    Hit,

    // A projectile touched a fighter. Attacker is the projectile slot, Defender the player.
    ProjectileHit,

    // Two projectiles met. Attacker and Defender are the two slots, lower slot first.
    ProjectileClash,
}

public struct Contact
{
    public ContactKind Kind;
    public int Attacker;
    public int Defender;

    // Where to put the spark.
    public FixVec2 Point;
}

[InlineArray(MaxCount)]
public struct ContactList
{
    // 2 fighter hits + 1 per projectile + 1 clash per projectile pair.
    public const int MaxCount = 8;

    private Contact _element;
}

// Every overlap this frame, found before any is applied, in a fixed order:
// player 1's hit on player 2, player 2's hit on player 1, projectiles by slot, then clashes.
// Applying them all after the search is what makes trades work and ties symmetric.
public struct Contacts
{
    public ContactList Items;
    public int Count;

    public static readonly Fix ProjectileRadius = Fix.FromInt(24);

    public static Contacts Find(in GameState state, in FighterShapes p1, in FighterShapes p2)
    {
        var found = new Contacts();

        found.FighterHit(0, p1, p2);
        found.FighterHit(1, p2, p1);

        for (var slot = 0; slot < GameState.MaxProjectiles; slot++)
        {
            ref readonly var projectile = ref state.Projectiles[slot];
            if (!projectile.Active)
                continue;
            var target = 1 - projectile.Owner;
            found.ProjectileHit(slot, ProjectileShape(projectile), target, target == 0 ? p1 : p2);
        }

        for (var a = 0; a < GameState.MaxProjectiles; a++)
        {
            for (var b = a + 1; b < GameState.MaxProjectiles; b++)
            {
                ref readonly var pa = ref state.Projectiles[a];
                ref readonly var pb = ref state.Projectiles[b];
                if (pa.Active && pb.Active && pa.Owner != pb.Owner
                    && Overlap.Capsules(ProjectileShape(pa), ProjectileShape(pb), out var point))
                {
                    found.Add(ContactKind.ProjectileClash, a, b, point);
                }
            }
        }

        return found;
    }

    public static Capsule ProjectileShape(in ProjectileState projectile) =>
        Capsule.Circle(projectile.Position, ProjectileRadius);

    public readonly Contact this[int index] => Items[index];

    // A hit lands once per frame: the first overlapping hitbox and hurtbox pair, in list order.
    private void FighterHit(int attacker, in FighterShapes attackerShapes, in FighterShapes defenderShapes)
    {
        for (var h = 0; h < attackerShapes.HitCount; h++)
        {
            for (var u = 0; u < defenderShapes.HurtCount; u++)
            {
                if (Overlap.Capsules(attackerShapes.Hit[h], defenderShapes.Hurt[u].Shape, out var point))
                {
                    Add(ContactKind.Hit, attacker, 1 - attacker, point);
                    return;
                }
            }
        }
    }

    private void ProjectileHit(int slot, in Capsule shape, int target, in FighterShapes targetShapes)
    {
        for (var u = 0; u < targetShapes.HurtCount; u++)
        {
            ref readonly var hurt = ref targetShapes.Hurt[u];
            if (!hurt.IgnoresProjectiles && Overlap.Capsules(shape, hurt.Shape, out var point))
            {
                Add(ContactKind.ProjectileHit, slot, target, point);
                return;
            }
        }
    }

    private void Add(ContactKind kind, int attacker, int defender, FixVec2 point) =>
        Items[Count++] = new Contact { Kind = kind, Attacker = attacker, Defender = defender, Point = point };
}
