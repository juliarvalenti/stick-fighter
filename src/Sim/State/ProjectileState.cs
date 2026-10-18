using StickFighter.Sim.Numerics;

namespace StickFighter.Sim;

// One fireball slot: whose it is, which move threw it (its damage, stun, chip), where it is, how
// fast it travels, and how many frames it has flown. See Combat.Projectiles.
public struct ProjectileState
{
    public const int EncodedSize = 1 + 4 + 16 + 16 + 4 + 4;

    public bool Active;
    public int Owner;
    public FixVec2 Position;
    public FixVec2 Velocity;
    public int Frame;
    public int Move;

    public readonly void Encode(ref StateWriter writer)
    {
        writer.Write(Active);
        writer.Write(Owner);
        writer.Write(Position);
        writer.Write(Velocity);
        writer.Write(Frame);
        writer.Write(Move);
    }
}
