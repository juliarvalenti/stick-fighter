using System.Collections.Generic;

namespace StickFighter.Sim.Replays;

// Wraps an input source and records every frame's input as the match runs. Building the replay
// re-simulates from the seed to fill in the per-frame checksums, so it needs no hook in the driver.
public sealed class ReplayRecorder : IInputSource
{
    private readonly IInputSource _source;
    private readonly List<FrameInput> _inputs = new();

    public ReplayRecorder(ulong seed, IInputSource source)
    {
        Seed = seed;
        _source = source;
    }

    public ulong Seed { get; }

    public int Frames => _inputs.Count;

    public FrameInput Read(int frame)
    {
        var input = _source.Read(frame);
        _inputs.Add(input);
        return input;
    }

    public Replay Build() => Replay.Record(Seed, _inputs.ToArray());
}
