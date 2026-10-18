using StickFighter.Sim.Numerics;

namespace StickFighter.Sim;

// Placeholder until the game state task fills it in. Everything the match needs
// lives in this one value type, so saving a frame is a copy.
public struct GameState
{
    public int Tick;

    // The match's only source of randomness, seeded when the match starts.
    public Rng Rng;
}
