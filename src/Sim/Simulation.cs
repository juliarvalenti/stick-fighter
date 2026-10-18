namespace StickFighter.Sim;

public static class Simulation
{
    // Advances the match by one tick. Must not allocate.
    public static void Step(ref GameState state)
    {
        state.Tick++;
    }
}
