using System.Collections.Generic;
using StickFighter.Sim.Net;
using StickFighter.Sim.Replays;
using Xunit;

namespace StickFighter.Sim.Tests;

// The online match around a session: the handshake, refusals, and a disconnect becoming a forfeit.
// Over an in-memory link here (instant, lossless, and cuttable); tests/Presentation.Tests plays the
// same over real UDP sockets.
public class NetMatchTests
{
    private const int Frames = 600;

    private sealed class Wire
    {
        public readonly Queue<byte[]>[] Queues = { new(), new() };
        public bool Cut { get; set; }
    }

    private sealed class End : IPacketLink
    {
        private readonly Wire _wire;
        private readonly int _me;

        public End(Wire wire, int me) => (_wire, _me) = (wire, me);

        public void Send(byte[] packet)
        {
            if (!_wire.Cut)
                _wire.Queues[1 - _me].Enqueue(packet);
        }

        public bool TryReceive(out byte[] packet) => _wire.Queues[_me].TryDequeue(out packet!);
    }

    private static NetMatch.Settings Settings(ulong seed = 42, ulong? hash = null, MatchMode mode = MatchMode.Sandbox) =>
        new(seed, Stage.Training, mode, 2, hash ?? Replay.LoadedDataHash);

    private static FrameInput Script(int f) => new(
        f % 40 < 20 ? Buttons.Right : f % 40 == 25 ? Buttons.MediumPunch : Buttons.None,
        f % 30 < 10 ? Buttons.Left : f % 30 == 15 ? Buttons.LightKick : Buttons.None);

    // Ticks both until both have played `frames` frames and confirmed them (or `ticks` run out); a
    // side that has played them all waits there, still sending and receiving.
    private static void Play(NetMatch host, NetMatch joiner, int frames, int ticks = 4000, System.Action<int>? each = null)
    {
        for (var t = 0; t < ticks; t++)
        {
            each?.Invoke(t);
            foreach (var m in new[] { host, joiner })
            {
                var s = m.Session;
                var frame = s is null ? 0 : s.Frame + s.InputDelay;
                m.Tick(frame < frames ? Script(frame).For(m.LocalPlayer) : Buttons.None, advance: s is null || s.Frame < frames);
            }
            if (host.Session is { } h && joiner.Session is { } j && h.ConfirmedFrame == frames && j.ConfirmedFrame == frames)
                return;
        }
    }

    private static (NetMatch Host, NetMatch Joiner, Wire Wire) Pair(NetMatch.Settings host, NetMatch.Settings joiner)
    {
        var wire = new Wire();
        return (new NetMatch(new End(wire, 0), true, host), new NetMatch(new End(wire, 1), false, joiner), wire);
    }

    [Fact]
    public void AfterTheHandshakeTheJoinerPlaysTheHostsMatchInSync()
    {
        var (host, joiner, _) = Pair(Settings(seed: 42, mode: MatchMode.Versus), Settings(seed: 7));
        Play(host, joiner, Frames);

        Assert.Equal(NetPhase.Playing, host.Phase);
        Assert.Equal(NetPhase.Playing, joiner.Phase);
        Assert.Equal(42UL, joiner.Agreed!.Value.Seed); // the host decides the match
        Assert.Equal(MatchMode.Versus, joiner.Agreed!.Value.Mode);
        Assert.Equal(0, host.LocalPlayer);
        Assert.Equal(1, joiner.LocalPlayer);

        // Both end on the same frame, confirmed, and on what an offline replay of the match gives.
        Assert.Equal(Frames, host.Session!.ConfirmedFrame);
        Assert.Equal(Frames, joiner.Session!.ConfirmedFrame);
        Assert.Equal(Offline(42, MatchMode.Versus), host.Session.State.Checksum());
        Assert.Equal(Offline(42, MatchMode.Versus), joiner.Session.State.Checksum());
    }

    // Everything the match starts from comes from the host: the joiner adopts its seed, mode, side,
    // characters and colors (keeping only its own input delay), and both start from the same state.
    [Fact]
    public void TheJoinerAdoptsTheHostsChoicesEvenToPlayP1()
    {
        var hostSettings = Settings(seed: 9, mode: MatchMode.Versus) with { HostPlayer = 1, P1Color = 1, P2Color = 0, InputDelay = 3 };
        var (host, joiner, _) = Pair(hostSettings, Settings(seed: 1));
        Play(host, joiner, 120);

        Assert.Equal(1, host.LocalPlayer);
        Assert.Equal(0, joiner.LocalPlayer);
        var agreed = joiner.Agreed!.Value;
        Assert.Equal((9UL, MatchMode.Versus, 1, 1, 0), (agreed.Seed, agreed.Mode, agreed.HostPlayer, agreed.P1Color, agreed.P2Color));
        Assert.Equal(2, agreed.InputDelay); // its own
        Assert.Equal(NetMatch.StartChecksum(hostSettings), NetMatch.StartChecksum(agreed));
        Assert.Equal(120, host.Session!.ConfirmedFrame);
        Assert.Equal(host.Session.State.Checksum(), joiner.Session!.State.Checksum());
    }

    // A welcome whose start state the joiner can't reproduce (here, tampered with) is refused before
    // the first frame rather than desyncing on it.
    [Fact]
    public void AStartStateTheJoinerCantReproduceIsRefused()
    {
        var wire = new Wire();
        var joiner = new NetMatch(new End(wire, 1), false, Settings());
        var settings = Settings();
        wire.Queues[1].Enqueue(NetCodec.Encode(new WelcomePacket(settings.DataHash, settings.Seed, settings.Stage, settings.Mode,
            0, 0, 0, 0, 0, NetMatch.StartChecksum(settings) ^ 1)));
        joiner.Tick(Buttons.None);

        Assert.Equal(NetPhase.Refused, joiner.Phase);
        Assert.Contains("start differently", joiner.RefusedBecause);
    }

    [Fact]
    public void DifferentCharacterDataIsRefusedOnBothSides()
    {
        var (host, joiner, _) = Pair(Settings(), Settings(hash: Replay.LoadedDataHash ^ 1));
        Play(host, joiner, 60, ticks: 120);

        Assert.Equal(NetPhase.Refused, host.Phase);
        Assert.Equal(NetPhase.Refused, joiner.Phase);
        Assert.Contains("character data differs", host.RefusedBecause);
        Assert.Contains("character data differs", joiner.RefusedBecause);
        Assert.Null(host.Session);
        Assert.Null(joiner.Session);
    }

    [Fact]
    public void ADifferentProtocolVersionIsRefused()
    {
        var wire = new Wire();
        var host = new NetMatch(new End(wire, 0), true, Settings());
        var hello = NetCodec.Encode(new HelloPacket(Replay.LoadedDataHash));
        hello[0] = NetCodec.ProtocolVersion + 1;
        wire.Queues[0].Enqueue(hello);
        host.Tick(Buttons.None);

        Assert.Equal(NetPhase.Refused, host.Phase);
        Assert.Contains("different version", host.RefusedBecause);
    }

    [Fact]
    public void AJoinerNobodyAnswersGivesUp()
    {
        var wire = new Wire { Cut = true };
        var joiner = new NetMatch(new End(wire, 1), false, Settings());
        for (var t = 0; t <= NetMatch.LostAfterTicks + NetMatch.GraceTicks + 1; t++)
            joiner.Tick(Buttons.None);
        Assert.Equal(NetPhase.Refused, joiner.Phase);
        Assert.Contains("No answer", joiner.RefusedBecause);
    }

    // Mid-match the line goes dead: "connection lost" after a second, counting down the grace, then
    // a forfeit, won by the player still there (each side sees the other leave).
    [Fact]
    public void ADisconnectBecomesAForfeitAfterTheGracePeriod()
    {
        var (host, joiner, wire) = Pair(Settings(), Settings());
        Play(host, joiner, 120);
        Assert.Equal(NetPhase.Playing, host.Phase);

        wire.Cut = true;
        var lostAt = -1;
        var t = 0;
        for (; t < NetMatch.LostAfterTicks + NetMatch.GraceTicks + 10 && host.Phase != NetPhase.Forfeited; t++)
        {
            host.Tick(Buttons.None);
            if (lostAt < 0 && host.Phase == NetPhase.ConnectionLost)
            {
                lostAt = t;
                Assert.True(host.GraceLeft > 0 && host.GraceLeft <= NetMatch.GraceTicks);
            }
        }

        Assert.InRange(lostAt, NetMatch.LostAfterTicks - 1, NetMatch.LostAfterTicks + 1);
        Assert.Equal(NetPhase.Forfeited, host.Phase);
        Assert.Equal(0, host.ForfeitWinner); // the host stayed: P1 wins
        Assert.InRange(t, NetMatch.LostAfterTicks + NetMatch.GraceTicks - 1, NetMatch.LostAfterTicks + NetMatch.GraceTicks + 2);
    }

    // Back within the grace period: play resumes and the match ends in sync.
    [Fact]
    public void AReconnectWithinTheGraceCarriesOn()
    {
        var (host, joiner, wire) = Pair(Settings(), Settings());
        Play(host, joiner, Frames, each: t => wire.Cut = t is >= 200 and < 400);

        Assert.Equal(NetPhase.Playing, host.Phase);
        Assert.Equal(NetPhase.Playing, joiner.Phase);
        Assert.Equal(Frames, host.Session!.ConfirmedFrame);
        Assert.Equal(Offline(42, MatchMode.Sandbox), host.Session.State.Checksum());
        Assert.Equal(Offline(42, MatchMode.Sandbox), joiner.Session!.State.Checksum());
    }

    // The match offline: no input for the first 2 frames (the input delay), then the script.
    private static ulong Offline(ulong seed, MatchMode mode)
    {
        var inputs = new FrameInput[Frames];
        for (var f = 2; f < Frames; f++)
            inputs[f] = Script(f);
        return Replay.Record(seed, Stage.Training, inputs, mode).FinalChecksum;
    }

    [Fact]
    public void PacketsRoundTripAndJunkIsDropped()
    {
        var message = new NetMessage(17, new[] { Buttons.Right, Buttons.LightPunch | Buttons.Down }, 9, 30, 0xdeadbeefUL, 40, -2, 1, new[] { 1UL, 2UL });
        var data = Assert.IsType<DataPacket>(NetCodec.Decode(NetCodec.Encode(new DataPacket(message)), out _));
        Assert.Equal(message with { Inputs = data.Message.Inputs, Trail = data.Message.Trail }, data.Message);
        Assert.Equal(message.Inputs, data.Message.Inputs);
        Assert.Equal(message.Trail, data.Message.Trail);

        var welcome = new WelcomePacket(5, 6, Stage.Training, MatchMode.Training, 1, 0, 0, 1, 0, 0xabcUL);
        Assert.Equal(welcome, NetCodec.Decode(NetCodec.Encode(welcome), out _));
        Assert.Equal(new RefusePacket("nope"), NetCodec.Decode(NetCodec.Encode(new RefusePacket("nope")), out _));

        Assert.Null(NetCodec.Decode(new byte[] { NetCodec.ProtocolVersion, 4, 1 }, out _)); // truncated
        Assert.Null(NetCodec.Decode(new byte[] { NetCodec.ProtocolVersion, 99 }, out _)); // unknown kind
        Assert.Null(NetCodec.Decode(System.Array.Empty<byte>(), out _));
    }
}
