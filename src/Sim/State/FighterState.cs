using StickFighter.Sim.Numerics;

namespace StickFighter.Sim;

public struct FighterState
{
    public const int EncodedSize = 16 + 16 + 4 + 4 + 4;

    public FixVec2 Position;
    public FixVec2 Velocity;

    // +1 facing right, -1 facing left.
    public int Facing;

    // What the fighter is doing (an id into the character's action table) and for how many frames.
    public int Action;
    public int ActionFrame;

    public readonly void Encode(ref StateWriter writer)
    {
        writer.Write(Position);
        writer.Write(Velocity);
        writer.Write(Facing);
        writer.Write(Action);
        writer.Write(ActionFrame);
    }
}
