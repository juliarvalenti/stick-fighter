using StickFighter.Sim.Characters;
using StickFighter.Sim.Numerics;

namespace StickFighter.Sim.Combat;

// Who's blocking what. "Back" is away from the attacker, from positions on the frame it matters,
// never from facing: a crossup has to be blocked the other way.
public static class Blocking
{
    // Holding the direction that points away from the attacker this frame.
    public static bool HoldingBack(in GameState state, int defender)
    {
        ref readonly var me = ref state.Fighters[defender];
        ref readonly var them = ref state.Fighters[1 - defender];
        var buttons = state.Inputs[defender].At(state.Frame, state.Frame);
        var awayIsRight = me.Position.X != them.Position.X ? me.Position.X > them.Position.X : me.Facing < 0;
        return buttons.Has(awayIsRight ? Buttons.Right : Buttons.Left);
    }

    public static bool HoldingDown(in GameState state, int player) =>
        state.Inputs[player].At(state.Frame, state.Frame).Has(Buttons.Down);

    // Free to guard: not attacking or reeling. Already guarding (blockstun or proximity) counts.
    // Never in the air (no air blocking), in pre-jump frames (committed to the jump), mid-dash, or in
    // the true-recovery frames of a landing after an air attack.
    public static bool CanGuard(in FighterState fighter) =>
        fighter.Hitstun == 0 && fighter.Action != (int)FighterAction.Dizzy && fighter.Action != (int)FighterAction.Attack && !fighter.KnockedDown
        && !fighter.Airborne && fighter.Action != (int)FighterAction.PreJump && !Movement.Dashing.IsDashing(fighter)
        && !(fighter.Action == (int)FighterAction.Landing && fighter.AirAttackUsed && fighter.ActionFrame < Simulation.LandingLockFrames);

    // Highs must be blocked standing, lows crouching; mids either way.
    public static bool RightHeight(Characters.Guard height, bool crouching) => height switch
    {
        Characters.Guard.High => !crouching,
        Characters.Guard.Low => crouching,
        _ => true,
    };

    // The opponent's attack is coming (startup or active, not yet connected) and close enough that
    // holding back means guarding rather than walking back.
    public static bool ProximityGuard(in GameState state, int defender)
    {
        ref readonly var me = ref state.Fighters[defender];
        ref readonly var them = ref state.Fighters[1 - defender];
        if (them.Action != (int)FighterAction.Attack || them.MoveHit || them.Move < 0)
            return false;
        var move = Characters.Characters.Placeholder.Moves[them.Move];
        if (them.ActionFrame >= move.FirstActiveFrame + move.Active)
            return false;
        var range = Characters.Characters.Placeholder.Stats.ProximityGuard;
        return Fix.Abs(them.Position.X - me.Position.X) <= range && HoldingBack(state, defender);
    }
}
