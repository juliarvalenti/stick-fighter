using StickFighter.Sim.Collision;
using StickFighter.Sim.Numerics;

namespace StickFighter.Sim;

public static class Simulation
{
    // Placeholder walk speed in px per frame, until characters have their own data.
    private static readonly Fix WalkSpeed = Fix.FromInt(4);

    // Advances the match by one frame. Must not allocate.
    public static void Step(ref GameState state, in FrameInput input)
    {
        // The input layer: raw buttons are cleaned and recorded, and from here on
        // game logic reads input only through the history.
        for (var i = 0; i < GameState.PlayerCount; i++)
            state.Inputs[i].Record(state.Frame, input.For(i).Cleaned());

        for (var i = 0; i < GameState.PlayerCount; i++)
        {
            ref var fighter = ref state.Fighters[i];
            var buttons = state.Inputs[i].At(state.Frame, state.Frame);
            var forward = Numpad.Horizontal(Numpad.Direction(buttons, fighter.Facing));
            fighter.Velocity = new FixVec2(WalkSpeed * (forward * fighter.Facing), Fix.Zero);
            fighter.Position += fighter.Velocity;
            fighter.ActionFrame++;
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
