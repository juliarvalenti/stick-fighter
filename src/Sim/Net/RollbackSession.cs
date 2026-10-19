using System.Collections.Generic;
using StickFighter.Sim.Replays;

namespace StickFighter.Sim.Net;

// One peer of a two-player match played over a network, GGPO style. Each peer runs its own copy of
// the match. Its own input is known at once (after the input delay); the other's arrives late, so
// the peer predicts it (the last input it has, held) and carries on. When the real input arrives
// and differs from the prediction, the peer rolls back to that frame, resimulates to the present
// with the right input, and carries on. It never predicts more than MaxRollback frames: past that
// it stalls (doesn't advance) until the other catches up. Peers compare checksums of confirmed
// frames, so a desync is caught where it happens. Nothing here knows about sockets or clocks: it's
// given a transport, and ticked once a frame.
//
// Time sync: each peer tells the other how far ahead it thinks it is. The two views, averaged,
// cancel the network's latency out and give the frame advantage: how many frames this peer is
// ahead of the other. A peer that's ahead skips a tick now and then (at most one in
// MinFramesBetweenSkips), so a peer whose clock runs fast settles back level instead of running
// into the rollback cap and stalling.
public sealed class RollbackSession
{
    public const int MaxRollback = 8;
    public const int MaxInputDelay = 4;

    // Confirmed frames whose checksums are compared: every this many.
    public const int ChecksumInterval = 30;

    // Time sync skips a tick at most once in this many frames.
    public const int MinFramesBetweenSkips = 10;

    // The advantage is averaged over this many ticks, so a late message doesn't cause a skip.
    private const int AdvantageWindow = 16;

    // How far back inputs, states and per-frame checksums are kept: comfortably more than the
    // rollback cap, the delay and the lag before a checksum frame is confirmed on both sides.
    private const int History = 128;

    // At most this many inputs in one message (more would only resend what's long acknowledged).
    private const int MaxInputsPerMessage = 32;

    private readonly int _local;
    private readonly ITransport _transport;
    private readonly Stepper _step;
    private readonly StateBuffer _saved = new(History);

    private readonly Buttons[] _localInputs = new Buttons[History];
    private readonly Buttons[] _remoteInputs = new Buttons[History];
    private readonly Buttons[] _usedRemote = new Buttons[History];

    // The last frame whose input is known: ours (Frame + delay - 1 once running), and theirs, every
    // frame up to it.
    private int _localKnown;
    private int _remoteConfirmed = -1;

    // The last of our inputs the other has confirmed it has.
    private int _peerAck = -1;

    // The checksum of every confirmed frame (the state on that frame, before it's simulated), by
    // frame, for the last History frames: to compare, and to find where a desync began.
    private readonly ulong[] _frameChecksums = new ulong[History];
    private int _lastChecksummed;

    private readonly List<(int Frame, ulong Checksum, GameState State)> _ownChecksums = new();
    private readonly List<(int Frame, ulong Checksum)> _theirChecksums = new();
    private int _lastChecksumFrame;

    // The other peer's latest frame and its view of its advantage, and this peer's averaged view.
    private int _remoteFrame;
    private int _remoteAdvantage;
    private readonly int[] _advantageSamples = new int[AdvantageWindow];
    private int _samples;
    private int _framesSinceSkip;

    // For the ping: the tick each of our inputs was first sent, and the last one acknowledged.
    private readonly int[] _sentAtTick = new int[History];
    private int _ticks;
    private int _pingFrame = -1;
    private int _pingSixteenths = -1;

    private GameState _state;

    // `localPlayer`: 0 for P1, 1 for P2. `step` is the simulation (a test can swap in a broken one).
    // `timeSync`: whether a peer that's ahead slows down (on unless a test wants to see without).
    public RollbackSession(int localPlayer, ulong seed, in Stage stage, MatchMode mode, int inputDelay, ITransport transport,
        Stepper? step = null, bool timeSync = true)
    {
        if (inputDelay < 0 || inputDelay > MaxInputDelay)
            throw new System.ArgumentOutOfRangeException(nameof(inputDelay), $"Input delay must be 0 to {MaxInputDelay} frames");
        _local = localPlayer;
        _transport = transport;
        _step = step ?? Simulation.Step;
        _state = GameState.NewMatch(seed, stage, mode);
        InputDelay = inputDelay;
        TimeSync = timeSync;

        // The first `inputDelay` frames have no input from us: nothing was pressed that long ago.
        _localKnown = inputDelay - 1;
        _frameChecksums[0] = _state.Checksum();
    }

    public int LocalPlayer => _local;
    public int InputDelay { get; }
    public bool TimeSync { get; }

    // The match as this peer has it now (the last frames may still rest on a prediction).
    public GameState State => _state;
    public int Frame => _state.Frame;

    // Every frame before this is settled: both players' inputs are known for it.
    public int ConfirmedFrame => Min(_remoteConfirmed + 1, _state.Frame);

    public int Rollbacks { get; private set; }
    public int DeepestRollback { get; private set; }
    public int ResimulatedFrames { get; private set; }
    public int StalledTicks { get; private set; }
    public int SkippedTicks { get; private set; }

    // How many frames ahead of the other peer this one is (negative: behind), averaged over the
    // last few ticks, with latency cancelled out: half the difference between the two views.
    public int FrameAdvantage
    {
        get
        {
            var count = Min(_samples, AdvantageWindow);
            if (count == 0)
                return 0;
            var sum = 0;
            for (var i = 0; i < count; i++)
                sum += _advantageSamples[i];
            return sum / (2 * count);
        }
    }

    // The largest advantage, either way, seen so far.
    public int WidestAdvantage { get; private set; }

    // The round trip, in milliseconds (-1 until measured): from sending an input to the other
    // acknowledging it, smoothed. Whole ticks, so to within a frame (about 17 ms).
    public int PingMs => _pingSixteenths < 0 ? -1 : _pingSixteenths * 1000 / (16 * GameInfo.TicksPerSecond);

    // The first checksum frame that didn't match the other peer's, if any; and, once the two have
    // swapped their per-frame checksums, the first frame that differed.
    public Desync? Desynced { get; private set; }

    public readonly record struct Desync(int Frame, ulong Local, ulong Remote, int FirstDiverged = -1);

    // One frame of play: takes in what's arrived (rolling back if a prediction was wrong), then
    // advances a frame with `local` as this player's input, unless that would mean predicting more
    // than MaxRollback frames (it stalls), or time sync has it wait a tick for the other to catch up.
    // Sends either way. True if it advanced.
    public bool Tick(Buttons local) => Update(local, advance: true);

    // Takes in what's arrived and sends, without advancing: for a peer that has reached the end of
    // a scripted match and is waiting for the other's last inputs.
    public void Poll() => Update(Buttons.None, advance: false);

    private bool Update(Buttons local, bool advance)
    {
        _ticks++;
        var rollbackFrom = Receive();
        if (rollbackFrom < _state.Frame)
            Resimulate(rollbackFrom);
        SampleAdvantage();

        var advanced = false;
        if (advance)
        {
            _framesSinceSkip++;
            if (_state.Frame - _remoteConfirmed > MaxRollback)
            {
                StalledTicks++;
            }
            else if (TimeSync && FrameAdvantage >= 1 && _framesSinceSkip >= MinFramesBetweenSkips)
            {
                SkippedTicks++;
                _framesSinceSkip = 0;
            }
            else
            {
                _localKnown = _state.Frame + InputDelay;
                _localInputs[_localKnown % History] = local;
                _sentAtTick[_localKnown % History] = _ticks;
                Advance();
                advanced = true;
            }
        }

        CompareChecksums();
        Send();
        return advanced;
    }

    // Takes in every message that's arrived. Returns the earliest frame already simulated with a
    // remote input that turned out wrong (or Frame if none).
    private int Receive()
    {
        var rollbackFrom = _state.Frame;
        while (_transport.TryReceive(out var message))
        {
            _peerAck = Max(_peerAck, message.Ack);
            MeasurePing(message.Ack);
            if (message.Frame >= _remoteFrame)
            {
                _remoteFrame = message.Frame;
                _remoteAdvantage = message.Advantage;
            }
            for (var i = 0; i < message.Inputs.Length; i++)
            {
                var frame = message.FirstFrame + i;
                if (frame != _remoteConfirmed + 1)
                    continue; // already have it, or a gap before it (it comes again: we haven't acked it)
                var input = message.Inputs[i];
                _remoteInputs[frame % History] = input;
                _remoteConfirmed = frame;
                if (frame < _state.Frame && _usedRemote[frame % History] != input && frame < rollbackFrom)
                    rollbackFrom = frame;
            }
            if (message.ChecksumFrame >= 0)
                _theirChecksums.Add((message.ChecksumFrame, message.Checksum));
            if (message.Trail is { } trail)
                FindFirstDivergence(message.TrailFirst, trail);
        }
        return rollbackFrom;
    }

    // An acknowledgement of our input for a frame newer than any before: how long it took, from the
    // tick that input was sent, averaged in (an eighth of each new sample, kept in sixteenths of a
    // tick). Inputs from before the first send (the delay's empty frames) don't count.
    private void MeasurePing(int ack)
    {
        if (ack <= _pingFrame || ack < InputDelay || ack > _localKnown || _localKnown - ack >= History)
            return;
        _pingFrame = ack;
        var sample = (_ticks - _sentAtTick[ack % History]) * 16;
        _pingSixteenths = _pingSixteenths < 0 ? sample : _pingSixteenths + (sample - _pingSixteenths) / 8;
    }

    // This peer's view (its frame less the other's latest), against the other's view of itself.
    private void SampleAdvantage()
    {
        var local = _state.Frame - _remoteFrame;
        _advantageSamples[_samples % AdvantageWindow] = local - _remoteAdvantage;
        _samples++;
        var advantage = FrameAdvantage;
        WidestAdvantage = Max(WidestAdvantage, advantage < 0 ? -advantage : advantage);
    }

    // Back to `frame`, then forward again to where we were, with the inputs as now known.
    private void Resimulate(int frame)
    {
        var to = _state.Frame;
        if (!_saved.TryLoad(frame, ref _state))
            throw new System.InvalidOperationException($"No saved state for frame {frame} to roll back to (now {to})");
        Rollbacks++;
        DeepestRollback = Max(DeepestRollback, to - frame);
        while (_state.Frame < to)
        {
            Advance();
            ResimulatedFrames++;
        }
    }

    // Simulates the current frame: saves the state it starts from (to roll back to), then steps
    // with our input and the other's, known or predicted.
    private void Advance()
    {
        var frame = _state.Frame;
        _saved.Save(_state);
        var remote = RemoteInput(frame);
        _usedRemote[frame % History] = remote;
        var mine = frame <= _localKnown && frame >= InputDelay ? _localInputs[frame % History] : Buttons.None;
        var input = _local == 0 ? new FrameInput(mine, remote) : new FrameInput(remote, mine);
        _step(ref _state, input);
        RecordChecksums();
    }

    // Known, or predicted: the last known input held.
    private Buttons RemoteInput(int frame)
    {
        if (frame <= _remoteConfirmed)
            return _remoteInputs[frame % History];
        return _remoteConfirmed >= 0 ? _remoteInputs[_remoteConfirmed % History] : Buttons.None;
    }

    // Checksums each frame as it's confirmed (both inputs known for every frame before it); and at
    // each checksum frame keeps the checksum and state, to send and to compare.
    private void RecordChecksums()
    {
        var confirmed = Min(ConfirmedFrame, _state.Frame);
        while (_lastChecksummed < confirmed)
        {
            var frame = _lastChecksummed + 1;
            var state = _state;
            if (frame != _state.Frame && !_saved.TryLoad(frame, ref state))
                return;
            _lastChecksummed = frame;
            var checksum = state.Checksum();
            _frameChecksums[frame % History] = checksum;
            if (frame == _lastChecksumFrame + ChecksumInterval)
            {
                _lastChecksumFrame = frame;
                _ownChecksums.Add((frame, checksum, state));
                if (_ownChecksums.Count > 8)
                    _ownChecksums.RemoveAt(0);
            }
        }
    }

    private void CompareChecksums()
    {
        RecordChecksums();
        for (var i = _theirChecksums.Count - 1; i >= 0; i--)
        {
            var (frame, theirs) = _theirChecksums[i];
            if (frame > _lastChecksumFrame)
                continue; // not confirmed here yet
            foreach (var (ownFrame, own, _) in _ownChecksums)
            {
                if (ownFrame == frame && own != theirs && Desynced is null)
                    Desynced = new Desync(frame, own, theirs);
            }
            _theirChecksums.RemoveAt(i);
        }
    }

    // The other peer's checksums for the frames leading up to the mismatch, against ours: the first
    // that differs is where the desync began.
    private void FindFirstDivergence(int first, ulong[] trail)
    {
        if (Desynced is not { FirstDiverged: < 0 } desync)
            return;
        for (var i = 0; i < trail.Length; i++)
        {
            var frame = first + i;
            if (frame > _lastChecksummed || frame <= _lastChecksummed - History)
                continue;
            if (_frameChecksums[frame % History] != trail[i])
            {
                Desynced = desync with { FirstDiverged = frame };
                return;
            }
        }
    }

    // The state at a confirmed checksum frame, kept for comparing two peers after a desync.
    public bool TryGetChecksumState(int frame, out GameState state)
    {
        foreach (var (ownFrame, _, saved) in _ownChecksums)
        {
            if (ownFrame == frame)
            {
                state = saved;
                return true;
            }
        }
        state = default;
        return false;
    }

    private void Send()
    {
        var first = Max(_peerAck + 1, _localKnown - MaxInputsPerMessage + 1);
        var count = Max(0, _localKnown - first + 1);
        var inputs = new Buttons[count];
        for (var i = 0; i < count; i++)
        {
            var frame = first + i;
            inputs[i] = frame >= InputDelay ? _localInputs[frame % History] : Buttons.None;
        }
        var (checksumFrame, checksum) = _ownChecksums.Count > 0 ? (_ownChecksums[^1].Frame, _ownChecksums[^1].Checksum) : (-1, 0UL);

        // After a desync, the checksums of the frames since the last checksum frame that matched.
        var (trailFirst, trail) = (-1, (ulong[]?)null);
        if (Desynced is { } desync)
        {
            trailFirst = desync.Frame - ChecksumInterval + 1;
            trail = new ulong[ChecksumInterval];
            for (var i = 0; i < trail.Length; i++)
                trail[i] = _frameChecksums[(trailFirst + i) % History];
        }

        _transport.Send(new NetMessage(first, inputs, _remoteConfirmed, checksumFrame, checksum,
            _state.Frame, _state.Frame - _remoteFrame, trailFirst, trail));
    }

    // Integer min and max (System.Math is banned in the sim, for its floating-point members).
    private static int Min(int a, int b) => a < b ? a : b;

    private static int Max(int a, int b) => a > b ? a : b;
}
