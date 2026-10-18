using StickFighter.Sim.Characters;
using StickFighter.Sim.Collision;
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

        var stats = Characters.Characters.Placeholder.Stats;
        for (var i = 0; i < GameState.PlayerCount; i++)
        {
            ref var fighter = ref state.Fighters[i];
            var direction = Numpad.Direction(state.Inputs[i].At(state.Frame, state.Frame), fighter.Facing);

            // Down crouches (no walking while crouched); otherwise forward and back walk at their own speeds.
            var forward = Numpad.Horizontal(direction);
            var action = Numpad.Vertical(direction) < 0 ? FighterAction.Crouch
                : forward > 0 ? FighterAction.WalkForward
                : forward < 0 ? FighterAction.WalkBack
                : FighterAction.Stand;
            var speed = action switch
            {
                FighterAction.WalkForward => stats.WalkForward,
                FighterAction.WalkBack => -stats.WalkBack,
                _ => Fix.Zero,
            };

            fighter.Velocity = new FixVec2(speed * fighter.Facing, Fix.Zero);
            fighter.Position += fighter.Velocity;
            fighter.ActionFrame = (int)action == fighter.Action ? fighter.ActionFrame + 1 : 0;
            fighter.Action = (int)action;
        }

        Pushing.Resolve(ref state);
        FaceEachOther(ref state);

        // Placeholder: draw from the RNG every frame so the rollback tests prove its state restores.
        state.Rng.NextUInt();

        state.Frame++;
    }

    // Both facings come from positions after movement, so neither player is processed first.
    private static void FaceEachOther(ref GameState state)
    {
        var p1 = state.Fighters[0].Position.X;
        var p2 = state.Fighters[1].Position.X;
        if (p1 == p2)
            return;
        state.Fighters[0].Facing = p1 < p2 ? 1 : -1;
        state.Fighters[1].Facing = -state.Fighters[0].Facing;
    }
}
