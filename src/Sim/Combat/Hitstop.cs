namespace StickFighter.Sim.Combat;

// Hitstop: when a hit or a block lands, both fighters freeze for a few frames (the move's
// HitstopOnHit or HitstopOnBlock; the larger of the two in a trade). While frozen nothing moves,
// no action frame advances and stun doesn't count down, so both fighters are delayed alike and
// frame advantage is unchanged. Input is still recorded, and the buffer counts only unfrozen
// frames, so a press made during the freeze comes out right after it.
//
// The freeze is GameState.FreezeFrom up to (not including) GameState.FreezeUntil, so it's saved,
// checksummed and rolled back with the match. Only the latest freeze is kept: a new one can only
// start when a hit lands, and nothing lands while frozen.
public static class Hitstop
{
    public static bool Frozen(in GameState state, int frame) =>
        frame >= state.FreezeFrom && frame < state.FreezeUntil;

    // Freeze the frames after this one. Called once per frame with that frame's longest hitstop.
    public static void Start(ref GameState state, int frames)
    {
        if (frames <= 0)
            return;
        state.FreezeFrom = state.Frame + 1;
        state.FreezeUntil = state.Frame + 1 + frames;
    }

    // The first frame of a window of `unfrozen` frames ending at `now`, skipping frozen frames, and
    // no further back than the input history can tell a press from a held button.
    public static int WindowStart(in GameState state, int now, int unfrozen)
    {
        var counted = 0;
        var frame = now;
        for (; frame > now - InputHistory.MaxWindow + 1; frame--)
        {
            if (!Frozen(state, frame) && ++counted == unfrozen)
                return frame;
        }
        return frame;
    }
}
