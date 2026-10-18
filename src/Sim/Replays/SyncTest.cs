using System.Collections.Generic;
using System.Linq;

namespace StickFighter.Sim.Replays;

// One simulation step. Simulation.Step, or a stand-in when testing the checker itself.
public delegate void Stepper(ref GameState state, in FrameInput input);

// GGPO's SyncTest, offline: play a match and, after every frame, roll back 1 to MaxRollback frames
// and resimulate, requiring the same checksum on every resimulated frame. It's how a rollback bug
// shows up in CI instead of online: state the step reads but GameState doesn't hold (a static, a
// cache, anything a load doesn't restore) makes a resimulated frame come out different.
//
// Each depth is rolled back twice:
//  - Same inputs: load the saved frame and run it forward again.
//  - Mispredicted: first run forward on predicted inputs (each player's last input held, as a
//    rollback session predicts a remote player), then load and run forward on the real ones. This is
//    what an online correction does, so any trace the wrong prediction leaves behind is caught.
//
// The match carries on from the resimulated state, as a rollback session does.
//
// Depths are tiered by length, to keep the gate fast as replays grow: a replay under LongReplay
// frames (the combat ones) is rolled back at every depth 1 to 8 after every frame. A longer one is
// rolled back at depths 1, 4 and 8 after every frame, and at every depth after every
// FullSweepEvery-th frame, so a bug that only one depth shows is still found, a little later.
public static class SyncTest
{
    // GGPO's usual prediction limit; the rollback perf budget is sized for it.
    public const int MaxRollback = 8;

    public const int LongReplay = 600;
    public const int FullSweepEvery = 30;
    private static readonly int[] LongDepths = { 1, 4, 8 };

    // Whether a replay of `frames` frames rolls back `depth` frames after `frame`.
    public static bool Tests(int frames, int frame, int depth) =>
        frames < LongReplay || frame % FullSweepEvery == 0 || System.Array.IndexOf(LongDepths, depth) >= 0;

    // Differences: the GameState fields that came out different, first-run value then resimulated
    // ("Fighters[1].Hitstun: 7 vs 6"), from StateDiff.
    public readonly record struct Failure(int Frame, int Depth, bool Mispredicted, ulong Expected, ulong Actual, IReadOnlyList<string> Differences)
    {
        public override string ToString() =>
            $"frame {Frame} differs after rolling back {Depth} frame{(Depth == 1 ? "" : "s")}"
            + $"{(Mispredicted ? " from a misprediction" : "")}: expected {Expected:x16}, got {Actual:x16}"
            + string.Concat(Differences.Select(d => "\n  " + d));
    }

    public static Failure? Run(ulong seed, in Stage stage, IReadOnlyList<FrameInput> inputs, MatchMode mode = MatchMode.Sandbox) =>
        Run(seed, stage, inputs, Simulation.Step, mode);

    // The first frame that comes out different on a resimulation, or null if every one matches.
    public static Failure? Run(ulong seed, in Stage stage, IReadOnlyList<FrameInput> inputs, Stepper step, MatchMode mode = MatchMode.Sandbox)
    {
        var state = GameState.NewMatch(seed, stage, mode);
        var saved = new StateBuffer(MaxRollback + 1);
        var checksums = new ulong[inputs.Count];
        var firstRun = new GameState[inputs.Count]; // to say which fields differ on a failure

        for (var frame = 0; frame < inputs.Count; frame++)
        {
            saved.Save(state);
            step(ref state, inputs[frame]);
            checksums[frame] = state.Checksum();
            firstRun[frame] = state;

            for (var depth = 1; depth <= MaxRollback && depth <= frame + 1; depth++)
            {
                if (!Tests(inputs.Count, frame, depth))
                    continue;
                var from = frame + 1 - depth;
                for (var pass = 0; pass < 2; pass++)
                {
                    var mispredicted = pass == 1;
                    if (mispredicted)
                    {
                        Load(saved, from, ref state);
                        for (var f = from; f <= frame; f++)
                            step(ref state, Predicted(inputs, from));
                    }

                    Load(saved, from, ref state);
                    for (var f = from; f <= frame; f++)
                    {
                        step(ref state, inputs[f]);
                        var actual = state.Checksum();
                        if (actual != checksums[f])
                            return new Failure(f, depth, mispredicted, checksums[f], actual, StateDiff.Between(firstRun[f], state));
                    }
                }
            }
        }
        return null;
    }

    private static void Load(StateBuffer saved, int frame, ref GameState state)
    {
        if (!saved.TryLoad(frame, ref state))
            throw new System.InvalidOperationException($"frame {frame} is no longer saved");
    }

    // What a session rolling back to `from` would have guessed for every frame since: the last
    // inputs it had (frame from - 1), held. Nothing before the first frame.
    private static FrameInput Predicted(IReadOnlyList<FrameInput> inputs, int from) =>
        from == 0 ? default : inputs[from - 1];
}
