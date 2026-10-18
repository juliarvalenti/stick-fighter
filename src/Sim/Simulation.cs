using StickFighter.Sim.Numerics;

namespace StickFighter.Sim;

public static class Simulation
{
    // Placeholder walk speed in px per frame, until characters have their own data.
    private static readonly Fix WalkSpeed = Fix.FromInt(4);

    // Advances the match by one frame. Must not allocate.
    public static void Step(ref GameState state, in FrameInput input)
    {
        for (var i = 0; i < GameState.PlayerCount; i++)
        {
            ref var fighter = ref state.Fighters[i];
            var buttons = input.For(i);
            var direction = ((buttons & FrameInput.Right) != 0 ? 1 : 0) - ((buttons & FrameInput.Left) != 0 ? 1 : 0);
            fighter.Velocity = new FixVec2(WalkSpeed * direction, Fix.Zero);
            fighter.Position += fighter.Velocity;
            fighter.ActionFrame++;
        }

        // Placeholder: draw from the RNG every frame so the rollback tests prove its state restores.
        state.Rng.NextUInt();

        state.Frame++;
    }
}
