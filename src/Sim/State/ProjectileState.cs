using StickFighter.Sim.Numerics;

namespace StickFighter.Sim;

// One fireball slot. Empty until Specials, cancels, links, but saved and checksummed from day one.
public struct ProjectileState
{
    public const int EncodedSize = 1 + 4 + 16 + 16 + 4;

    public bool Active;
    public int Owner;
    public FixVec2 Position;
    public FixVec2 Velocity;
    public int Frame;

    public readonly void Encode(ref StateWriter writer)
    {
        writer.Write(Active);
        writer.Write(Owner);
        writer.Write(Position);
        writer.Write(Velocity);
        writer.Write(Frame);
    }
}
