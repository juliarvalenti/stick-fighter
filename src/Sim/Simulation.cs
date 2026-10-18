using StickFighter.Sim.Characters;
using StickFighter.Sim.Collision;
using StickFighter.Sim.Combat;
using StickFighter.Sim.Numerics;

namespace StickFighter.Sim;

public static class Simulation
{
    // Advances the match by one frame. Must not allocate.
    public static void Step(ref GameState state, in FrameInput input)
    {
        // The input layer: raw buttons are cleaned and recorded, and from here on
        // game logic reads input only through the history.
        for (var i = 0; i < GameState.PlayerCount; i++)
            state.Inputs[i].Record(state.Frame, input.For(i).Cleaned());

        // Where both stood when the frame began: Pushing measures movement from here, so walking and
        // hit pushback both count as moving away when the separation limit applies.
        var p1Before = state.Fighters[0].Position.X;
        var p2Before = state.Fighters[1].Position.X;

        // Decided for both before either acts, so neither player sees the other's move a frame ahead.
        var p1Proximity = Blocking.ProximityGuard(state, 0);
        var p2Proximity = Blocking.ProximityGuard(state, 1);
        Act(ref state, 0, p1Proximity);
        Act(ref state, 1, p2Proximity);

        Pushing.Resolve(ref state, p1Before, p2Before);
        FaceEachOther(ref state);

        // Every contact is found from both fighters' poses before any is applied, so trades hit both.
        var contacts = Contacts.Find(state, Body.Shapes(state.Fighters[0]), Body.Shapes(state.Fighters[1]));
        Hits.Apply(ref state, contacts);
        Pushing.Resolve(ref state, p1Before, p2Before);

        // Placeholder: draw from the RNG every frame so the rollback tests prove its state restores.
        state.Rng.NextUInt();

        state.Frame++;
    }

    // One fighter's frame: reeling in hitstun, guarding in blockstun, carrying on with a move,
    // starting a normal from a (possibly buffered) press, guarding by proximity, or moving.
    private static void Act(ref GameState state, int player, bool proximityGuard)
    {
        ref var fighter = ref state.Fighters[player];
        var character = Characters.Characters.Placeholder;
        var history = state.Inputs[player];
        var now = state.Frame;

        if (fighter.Hitstun > 0)
        {
            fighter.Hitstun--;
            Continue(ref fighter, FighterAction.Hitstun);
            return;
        }

        // In blockstun the fighter keeps guarding and can switch between standing and crouching.
        if (fighter.Blockstun > 0)
        {
            fighter.Blockstun--;
            fighter.Velocity = FixVec2.Zero;
            Continue(ref fighter, Blocking.HoldingDown(state, player) ? FighterAction.BlockCrouch : FighterAction.BlockStand);
            return;
        }

        if (fighter.Action == (int)FighterAction.Attack && fighter.ActionFrame + 1 < character.Moves[fighter.Move].TotalFrames)
        {
            fighter.ActionFrame++;
            fighter.Velocity = FixVec2.Zero;
            return;
        }

        // Free to act. Normals don't cancel into each other yet (Specials, cancels, links).
        var direction = Numpad.Direction(history.At(now, now), fighter.Facing);
        var crouching = Numpad.Vertical(direction) < 0;
        var wasLow = IsLow(fighter, character);
        var move = BufferedNormal(history, now, fighter.LastMoveFrame, crouching);
        if (move >= 0)
        {
            fighter.Action = (int)FighterAction.Attack;
            fighter.ActionFrame = 0;
            fighter.Move = move;
            fighter.MoveHit = false;
            fighter.LastMoveFrame = now;
            fighter.Velocity = FixVec2.Zero;
            return;
        }

        fighter.Move = -1;

        // Holding back near an incoming attack: guard in place rather than walk away.
        if (proximityGuard)
        {
            fighter.Velocity = FixVec2.Zero;
            Continue(ref fighter, crouching ? FighterAction.BlockCrouch : FighterAction.BlockStand);
            return;
        }

        // Down (1, 2 or 3) crouches, through the crouch transition unless already low. Otherwise
        // forward and back walk at their own speeds.
        var forward = Numpad.Horizontal(direction);
        var action = crouching ? (wasLow || CrouchedDown(fighter, character) ? FighterAction.Crouch : FighterAction.CrouchDown)
            : forward > 0 ? FighterAction.WalkForward
            : forward < 0 ? FighterAction.WalkBack
            : FighterAction.Stand;
        var speed = action switch
        {
            FighterAction.WalkForward => character.Stats.WalkForward,
            FighterAction.WalkBack => -character.Stats.WalkBack,
            _ => Fix.Zero,
        };

        fighter.Velocity = new FixVec2(speed * fighter.Facing, Fix.Zero);
        fighter.Position += fighter.Velocity;
        Continue(ref fighter, action);
    }

    // The newest unused attack press within the buffer window, as a move index, or -1.
    private static int BufferedNormal(in InputHistory history, int now, int lastMoveFrame, bool crouching)
    {
        for (var frame = now; frame > now - Normals.BufferWindow && frame > lastMoveFrame; frame--)
        {
            var move = Normals.Pick(history.PressedOn(frame, now) & Buttons.Attacks, crouching);
            if (move >= 0)
                return move;
        }
        return -1;
    }

    private static void Continue(ref FighterState fighter, FighterAction action)
    {
        fighter.ActionFrame = (int)action == fighter.Action ? fighter.ActionFrame + 1 : 0;
        fighter.Action = (int)action;
        if (action != FighterAction.Attack)
            fighter.Move = -1;
    }

    // Already down low: crouching, guarding low, or finishing a crouching normal. Down from here
    // goes straight to the crouch pose, with no transition.
    private static bool IsLow(in FighterState fighter, Character character) =>
        fighter.Action is (int)FighterAction.Crouch or (int)FighterAction.BlockCrouch
        || (fighter.Action == (int)FighterAction.Attack && fighter.Move >= 0 && character.Moves[fighter.Move].Crouching);

    // The crouch transition has run its frames.
    private static bool CrouchedDown(in FighterState fighter, Character character) =>
        fighter.Action == (int)FighterAction.CrouchDown && fighter.ActionFrame + 1 >= character.Stats.CrouchTransitionFrames;

    // Free to turn around: on the ground (always, until jumps) and not mid-move, reeling or guarding
    // in blockstun. A fighter who isn't free keeps facing the way it was until it is.
    public static bool CanTurn(in FighterState fighter) =>
        fighter.Hitstun == 0 && fighter.Blockstun == 0 && fighter.Action != (int)FighterAction.Attack && !fighter.KnockedDown;

    // Facings come from positions after movement, and each free fighter turns toward the other on
    // the same frame, so neither player is processed first.
    private static void FaceEachOther(ref GameState state)
    {
        var p1 = state.Fighters[0].Position.X;
        var p2 = state.Fighters[1].Position.X;
        if (p1 == p2)
            return;
        var p1Facing = p1 < p2 ? 1 : -1;
        if (CanTurn(state.Fighters[0]))
            state.Fighters[0].Facing = p1Facing;
        if (CanTurn(state.Fighters[1]))
            state.Fighters[1].Facing = -p1Facing;
    }
}
