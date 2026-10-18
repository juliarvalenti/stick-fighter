using StickFighter.Sim.Numerics;

namespace StickFighter.Sim;

public struct FighterState
{
    public const int EncodedSize = 16 + 16 + 4 + 4 + 4 + 4 + 4 + 1 + 4 + 4 + 1 + 4 + 4 + 4 + 4 + 1 + 1 + 1 + 1 + 4 + 4 + 1 + 4 + 4;

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

    // Frames of blockstun left after this one. Like hitstun, the fighter can't act, but stays guarding.
    public int Blockstun;

    // Lying down: nothing hits a knocked-down fighter. Nothing knocks down yet (Knockdowns).
    public bool KnockedDown;

    // The frame the last move started, so a buffered press isn't used twice.
    public int LastMoveFrame;

    // Frames since a throw connected, for both the thrower and the thrown; -1 when not in a grab.
    public int Grab;

    // Frames left lying down (KnockedDown is true while this is above zero).
    public int Knockdown;

    // Frames of throw invulnerability left after getting up.
    public int Wakeup;

    // The throw this fighter is holding throws the opponent behind it (chosen by holding back).
    public bool BackThrow;

    // Grabbed in neutral or in the startup of their own throw, so LP+LK can tech it. Grabbed in the
    // startup or recovery of anything else, it's a counter-hit throw and can't be teched.
    public bool Techable;

    // Off the ground, on a jump's arc (from takeoff to touchdown). Gravity applies, and the fighter
    // can't guard, turn or walk.
    public bool Airborne;

    // This jump's one air attack has been used (and, on landing, that it was: the landing then has
    // recovery instead of being free).
    public bool AirAttackUsed;

    // The combo this fighter is taking: hits so far and the damage they did. A hit landing while
    // the last one's hitstun (or juggle fall) is still running adds to it; both reset to 0 when the
    // fighter recovers. The HUD's combo counter reads them.
    public int ComboHits;
    public int ComboDamage;

    // The last hit this fighter took was a counter hit (landed in the startup of its own attack).
    // The HUD's COUNTER callout reads it.
    public bool CounterHit;

    // Juggle count while falling from an air hit (0 when not juggled). See Move.JuggleLimit.
    public int Juggle;

    // Stun damage taken, scaled like damage. The stun meter (dizzy, recovery) is the knockdowns task.
    public int Stun;

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
        writer.Write(Blockstun);
        writer.Write(KnockedDown);
        writer.Write(LastMoveFrame);
        writer.Write(Grab);
        writer.Write(Knockdown);
        writer.Write(Wakeup);
        writer.Write(BackThrow);
        writer.Write(Techable);
        writer.Write(Airborne);
        writer.Write(AirAttackUsed);
        writer.Write(ComboHits);
        writer.Write(ComboDamage);
        writer.Write(CounterHit);
        writer.Write(Juggle);
        writer.Write(Stun);
    }
}
