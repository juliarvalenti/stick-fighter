using StickFighter.Sim.Characters;
using StickFighter.Sim.Collision;
using StickFighter.Sim.Numerics;

namespace StickFighter.Sim.Combat;

// Throws, after SF4. A throw connects on its active frames if the opponent is throwable and
// within range (feet to feet). Then both are held: the thrown animation plays TechFrame frames,
// and the game looks back TechWindow frames of the thrown fighter's input for LP+LK. Found, and the
// fighter was grabbed somewhere a tech is allowed (Techable), it's a tech: both break apart into neutral. Not found, the throw completes: damage, the opponent
// lands in front (or behind, for a back throw) and is knocked down.
//
// Everything is decided for both players before anything is applied, so two throws on the same
// frame both tech and neither player wins by being player 1.
public static class Throws
{
    // Run after both fighters have acted and been pushed apart, before strikes are applied:
    // a throw beats a strike on the same frame.
    // p1Stunned and p2Stunned: in hitstun or blockstun when the frame began (Stunned, read before
    // Act spends a stun frame). A fighter spending its last stun frame is still stunned this frame.
    public static void Resolve(ref GameState state, bool p1Stunned, bool p2Stunned)
    {
        var stats = Characters.Characters.Placeholder.Stats;

        // Grabs reaching the tech check.
        for (var p = 0; p < GameState.PlayerCount; p++)
        {
            ref var thrown = ref state.Fighters[p];
            if (thrown.Action != (int)FighterAction.Thrown || thrown.Grab != stats.TechFrame)
                continue;
            ref var thrower = ref state.Fighters[1 - p];
            if (thrown.Techable && Teched(state.Inputs[p], state.Frame, stats.TechWindow))
                BreakApart(ref thrower, ref thrown, stats.TechSeparation, state.Frame);
            else
                Complete(ref thrower, ref thrown, stats);
        }

        // Throwers finishing the follow-through.
        for (var p = 0; p < GameState.PlayerCount; p++)
        {
            ref var thrower = ref state.Fighters[p];
            if (thrower.Action == (int)FighterAction.Throwing && thrower.Grab >= stats.TechFrame + stats.ThrowFollowThrough)
                Release(ref thrower);
        }

        // New throws connecting, decided for both before either is applied.
        var p1Grabs = !p2Stunned && Connects(state, 0);
        var p2Grabs = !p1Stunned && Connects(state, 1);
        if (p1Grabs && p2Grabs)
            BreakApart(ref state.Fighters[0], ref state.Fighters[1], stats.TechSeparation, state.Frame);
        else if (p1Grabs)
            Grab(ref state, thrower: 0);
        else if (p2Grabs)
            Grab(ref state, thrower: 1);
    }

    public static bool Stunned(in FighterState f) => f.Hitstun > 0 || f.Blockstun > 0;

    // Apart from stun, which Resolve reads from the start of the frame.
    public static bool Throwable(in FighterState f) =>
        !f.KnockedDown
        && f.Wakeup == 0
        && f.Grab < 0;

    private static bool Connects(in GameState state, int player)
    {
        ref readonly var me = ref state.Fighters[player];
        ref readonly var them = ref state.Fighters[1 - player];
        if (Body.MoveOf(me) is not { Kind: MoveKind.Throw } move || !move.IsActive(me.ActionFrame) || me.MoveHit)
            return false;
        var range = Characters.Characters.Placeholder.Stats.ThrowRange;
        return Throwable(them) && Fix.Abs(them.Position.X - me.Position.X) <= range;
    }

    private static void Grab(ref GameState state, int thrower)
    {
        ref var me = ref state.Fighters[thrower];
        ref var them = ref state.Fighters[1 - thrower];
        me.MoveHit = true;
        // Back held when the throw started or when it connects: letting go of back in the two
        // startup frames still throws behind.
        me.BackThrow |= Blocking.HoldingBack(state, thrower);
        them.Techable = Techable(them);
        Hold(ref me, FighterAction.Throwing);
        Hold(ref them, FighterAction.Thrown);
    }

    // SF4: a throw can be teched from neutral (standing, crouching, walking or guarding) or from the
    // startup of your own throw. Grabbed in the startup or recovery of anything else, including a
    // whiffed throw's recovery, it's a counter-hit throw. (Landing joins the list with Jumps.)
    public static bool Techable(in FighterState f) => (FighterAction)f.Action switch
    {
        FighterAction.Stand or FighterAction.Crouch or FighterAction.CrouchDown or FighterAction.WalkForward
            or FighterAction.WalkBack or FighterAction.BlockStand or FighterAction.BlockCrouch => true,
        FighterAction.Attack => f.Move == Normals.Throw && f.ActionFrame < Characters.Characters.Placeholder.Moves[f.Move].FirstActiveFrame,
        _ => false,
    };

    private static void Hold(ref FighterState f, FighterAction action)
    {
        f.Action = (int)action;
        f.ActionFrame = 0;
        f.Grab = 0;
        f.Velocity = FixVec2.Zero;
        if (action == FighterAction.Thrown)
            f.Move = -1;
    }

    // LP and LK both pressed within two frames of each other, at some frame in the last `window`.
    public static bool Teched(in InputHistory history, int now, int window)
    {
        for (var frame = now; frame > now - window; frame--)
        {
            if (Normals.ThrowPressedOn(history, frame, now))
                return true;
        }
        return false;
    }

    // The tech uses up both players' LP+LK presses, so the buffer doesn't turn them into throws.
    private static void BreakApart(ref FighterState a, ref FighterState b, Fix separation, int now)
    {
        var aIsLeft = a.Position.X != b.Position.X ? a.Position.X < b.Position.X : a.Facing > 0;
        Release(ref a);
        Release(ref b);
        a.LastMoveFrame = now;
        b.LastMoveFrame = now;
        a.Position += new FixVec2(aIsLeft ? -separation : separation, Fix.Zero);
        b.Position += new FixVec2(aIsLeft ? separation : -separation, Fix.Zero);
    }

    private static void Complete(ref FighterState thrower, ref FighterState thrown, Stats stats)
    {
        var damage = Characters.Characters.Placeholder.Moves[thrower.Move].Damage;
        thrown.Health = thrown.Health > damage ? thrown.Health - damage : 0;

        // Lands in front, or behind for a back throw (the fighters swap sides).
        var toward = thrown.Position.X >= thrower.Position.X ? 1 : -1;
        var side = thrower.BackThrow ? -toward : toward;
        thrown.Position = new FixVec2(thrower.Position.X + stats.ThrowDistance * side, thrown.Position.Y);

        // Lands facing the thrower, so lying down the head is away from them either way. (A fighter
        // only turns when free, so after a back throw it would otherwise lie the wrong way round.)
        thrown.Facing = -side;

        thrown.Grab = -1;
        thrown.Techable = false;
        thrown.Knockdown = stats.KnockdownFrames;
        thrown.KnockedDown = true;
        thrown.Action = (int)FighterAction.Knockdown;
        thrown.ActionFrame = 0;
    }

    private static void Release(ref FighterState f)
    {
        f.Grab = -1;
        f.Techable = false;
        f.Move = -1;
        f.Action = (int)FighterAction.Stand;
        f.ActionFrame = 0;
        f.Velocity = FixVec2.Zero;
    }
}
