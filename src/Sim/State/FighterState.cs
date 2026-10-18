using StickFighter.Sim.Numerics;

namespace StickFighter.Sim;

public struct FighterState
{
    public const int EncodedSize = 16 + 16 + 4 + 4 + 4 + 4 + 4 + 1 + 4 + 1 + 4;

    public FixVec2 Position;
    public FixVec2 Velocity;

    // +1 facing right, -1 facing left.
    public int Facing;

    // What the fighter is doing (a FighterAction) and for how many frames.
    public int Action;
    public int ActionFrame;

    public int Health;

    // The move being performed (an index into the character's moves), or -1.
    public int Move;

    // The current move has already hit, so it can't hit again.
    public bool MoveHit;

    // Frames of hitstun left after this one. The fighter can't act while it's above zero.
    public int Hitstun;

    // Lying down: nothing hits a knocked-down fighter. Nothing knocks down yet (Knockdowns).
    public bool KnockedDown;

    // The frame the last move started, so a buffered press isn't used twice.
    public int LastMoveFrame;

    public readonly void Encode(ref StateWriter writer)
    {
        writer.Write(Position);
        writer.Write(Velocity);
        writer.Write(Facing);
        writer.Write(Action);
        writer.Write(ActionFrame);
        writer.Write(Health);
        writer.Write(Move);
        writer.Write(MoveHit);
        writer.Write(Hitstun);
        writer.Write(KnockedDown);
        writer.Write(LastMoveFrame);
    }
}
