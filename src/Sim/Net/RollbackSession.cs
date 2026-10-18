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
public sealed class RollbackSession
{
    public const int MaxRollback = 8;
    public const int MaxInputDelay = 4;

    // Confirmed frames whose checksums are compared: every this many.
    public const int ChecksumInterval = 30;

    // How far back inputs and states are kept: comfortably more than the rollback cap, the delay and
    // the lag before a checksum frame is confirmed on both sides.
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

    private readonly List<(int Frame, ulong Checksum, GameState State)> _ownChecksums = new();
    private readonly List<(int Frame, ulong Checksum)> _theirChecksums = new();
    private int _lastChecksumFrame;

    private GameState _state;

    // `localPlayer`: 0 for P1, 1 for P2. `step` is the simulation (a test can swap in a broken one).
    public RollbackSession(int localPlayer, ulong seed, in Stage stage, MatchMode mode, int inputDelay, ITransport transport,
        Stepper? step = null)
    {
        if (inputDelay < 0 || inputDelay > MaxInputDelay)
            throw new System.ArgumentOutOfRangeException(nameof(inputDelay), $"Input delay must be 0 to {MaxInputDelay} frames");
        _local = localPlayer;
        _transport = transport;
        _step = step ?? Simulation.Step;
        _state = GameState.NewMatch(seed, stage, mode);
        InputDelay = inputDelay;

        // The first `inputDelay` frames have no input from us: nothing was pressed that long ago.
        _localKnown = inputDelay - 1;
    }

    public int LocalPlayer => _local;
    public int InputDelay { get; }

    // The match as this peer has it now (the last frames may still rest on a prediction).
    public GameState State => _state;
    public int Frame => _state.Frame;

    // Every frame before this is settled: both players' inputs are known for it.
    public int ConfirmedFrame => Min(_remoteConfirmed + 1, _state.Frame);

    public int Rollbacks { get; private set; }
    public int DeepestRollback { get; private set; }
    public int ResimulatedFrames { get; private set; }
    public int StalledTicks { get; private set; }

    // The first frame whose checksum didn't match the other peer's, if any.
    public Desync? Desynced { get; private set; }

    public readonly record struct Desync(int Frame, ulong Local, ulong Remote);

    // One frame of play: takes in what's arrived (rolling back if a prediction was wrong), then
    // advances a frame with `local` as this player's input, unless that would mean predicting more
    // than MaxRollback frames, in which case it stalls. Sends either way. True if it advanced.
    public bool Tick(Buttons local) => Update(local, advance: true);

    // Takes in what's arrived and sends, without advancing: for a peer that has reached the end of
    // a scripted match and is waiting for the other's last inputs.
    public void Poll() => Update(Buttons.None, advance: false);

    private bool Update(Buttons local, bool advance)
    {
        var rollbackFrom = Receive();
        if (rollbackFrom < _state.Frame)
            Resimulate(rollbackFrom);

        var advanced = false;
        if (advance)
        {
            if (_state.Frame - _remoteConfirmed > MaxRollback)
            {
                StalledTicks++;
            }
            else
            {
                _localKnown = _state.Frame + InputDelay;
                _localInputs[_localKnown % History] = local;
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
        }
        return rollbackFrom;
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
        RecordChecksum();
    }

    // Known, or predicted: the last known input held.
    private Buttons RemoteInput(int frame)
    {
        if (frame <= _remoteConfirmed)
            return _remoteInputs[frame % History];
        return _remoteConfirmed >= 0 ? _remoteInputs[_remoteConfirmed % History] : Buttons.None;
    }

    // Once a checksum frame is confirmed (both inputs known for every frame before it), keep its
    // checksum and state, to send and to compare.
    private void RecordChecksum()
    {
        var next = _lastChecksumFrame + ChecksumInterval;
        if (next > ConfirmedFrame || next > _state.Frame)
            return;
        var state = _state;
        if (next != _state.Frame && !_saved.TryLoad(next, ref state))
            return;
        _lastChecksumFrame = next;
        _ownChecksums.Add((next, state.Checksum(), state));
        if (_ownChecksums.Count > 8)
            _ownChecksums.RemoveAt(0);
    }

    private void CompareChecksums()
    {
        RecordChecksum();
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
        _transport.Send(new NetMessage(first, inputs, _remoteConfirmed, checksumFrame, checksum));
    }

    // Integer min and max (System.Math is banned in the sim, for its floating-point members).
    private static int Min(int a, int b) => a < b ? a : b;

    private static int Max(int a, int b) => a > b ? a : b;
}
