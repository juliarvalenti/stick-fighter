using System.Collections.Generic;
using StickFighter.Sim.Numerics;

namespace StickFighter.Sim.Net;

// Two peers in one process, joined by a fake network with latency, jitter, loss and reordering, all
// from a seeded RNG, so a test over it plays out the same every run. Time is in frames: Tick moves
// it on one frame, and a message arrives on the frame it's due. Jitter alone reorders messages;
// ReorderPercent holds a message back a few frames more on top.
public sealed class SimulatedNetwork
{
    private readonly List<(int Due, long Order, NetMessage Message)>[] _inFlight = { new(), new() };
    private Rng _rng;
    private long _sent;

    // `pingMs`: the round trip, so each way is half. `jitterPercent` of the one-way time, either way.
    public SimulatedNetwork(ulong seed, int pingMs, int jitterPercent = 0, int lossPercent = 0, int reorderPercent = 0)
    {
        _rng = Rng.Create(seed, 99);
        OneWayFrames = pingMs * GameInfo.TicksPerSecond / 2000;
        JitterFrames = (OneWayFrames * jitterPercent + 99) / 100;
        LossPercent = lossPercent;
        ReorderPercent = reorderPercent;
        Ends = new ITransport[] { new End(this, 0), new End(this, 1) };
    }

    public int OneWayFrames { get; }
    public int JitterFrames { get; }
    public int LossPercent { get; set; }
    public int ReorderPercent { get; }
    public int Now { get; private set; }
    public int Delivered { get; private set; }
    public int Dropped { get; private set; }

    // Player p's end: what it sends goes to the other.
    public IReadOnlyList<ITransport> Ends { get; }

    public void Tick() => Now++;

    private void Send(int from, NetMessage message)
    {
        if (_rng.Chance(LossPercent, 100))
        {
            Dropped++;
            return;
        }
        var delay = OneWayFrames + (JitterFrames > 0 ? _rng.NextInt(-JitterFrames, JitterFrames) : 0);
        if (_rng.Chance(ReorderPercent, 100))
            delay += _rng.NextInt(1, 3);
        _inFlight[1 - from].Add((Now + (delay > 0 ? delay : 0), _sent++, message));
    }

    // The earliest-due message for `to` that has arrived (ties in the order sent).
    private bool Receive(int to, out NetMessage message)
    {
        var queue = _inFlight[to];
        var best = -1;
        for (var i = 0; i < queue.Count; i++)
        {
            var (due, order, _) = queue[i];
            if (due > Now)
                continue;
            if (best < 0 || due < queue[best].Due || (due == queue[best].Due && order < queue[best].Order))
                best = i;
        }
        if (best < 0)
        {
            message = null!;
            return false;
        }
        message = queue[best].Message;
        queue.RemoveAt(best);
        Delivered++;
        return true;
    }

    private sealed class End : ITransport
    {
        private readonly SimulatedNetwork _network;
        private readonly int _player;

        public End(SimulatedNetwork network, int player) => (_network, _player) = (network, player);

        public void Send(NetMessage message) => _network.Send(_player, message);

        public bool TryReceive(out NetMessage message) => _network.Receive(_player, out message);
    }
}
