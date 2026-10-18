using StickFighter.Sim.Collision;
using StickFighter.Sim.Numerics;

namespace StickFighter.Sim.Training;

public enum ResetPosition
{
    Middle,

    // The dummy (P2) against the left wall, P1 in front of it.
    LeftCorner,

    // The dummy against the right wall, P1 in front of it.
    RightCorner,
}

// Training resets: a fresh state, as at the start of a match, with the fighters placed. A new
// state rather than an edit of the old one, so nothing from before the reset carries over.
public static class TrainingReset
{
    // The gap between the fighters after a corner reset: the same as at the start of a match.
    public static readonly Fix Gap = Fix.FromInt(2 * GameState.StartOffset);

    public static GameState Fresh(ulong seed, in Stage stage, MatchMode mode, ResetPosition position)
    {
        var state = GameState.NewMatch(seed, stage, mode);
        if (position == ResetPosition.Middle)
            return state;

        var left = position == ResetPosition.LeftCorner;
        var dummyX = left ? Pushing.MinX(stage) : Pushing.MaxX(stage);
        var playerX = left ? dummyX + Gap : dummyX - Gap;
        state.Fighters[1].Position = new FixVec2(dummyX, state.Fighters[1].Position.Y);
        state.Fighters[0].Position = new FixVec2(playerX, state.Fighters[0].Position.Y);
        state.Fighters[0].Facing = left ? -1 : 1;
        state.Fighters[1].Facing = left ? 1 : -1;
        return state;
    }
}
