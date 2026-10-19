using System.Threading;
using StickFighter.Net;
using StickFighter.Sim;
using StickFighter.Sim.Net;
using StickFighter.Sim.Replays;
using Xunit;

namespace StickFighter.Presentation.Tests;

// Online matches over real UDP sockets on localhost: the handshake, a match ending in sync with the
// offline replay of it, a refusal, and a forfeit when one side's socket goes away. The pure rules
// are tested in Sim.Tests over an in-memory link; this is the same over the real thing.
public class UdpMatchTests
{
    private const int Frames = 600;
    private const int Delay = 2;

    private static NetMatch.Settings Settings(ulong? hash = null) =>
        new(42, Stage.Training, MatchMode.Versus, Delay, hash ?? Replay.LoadedDataHash);

    private static FrameInput Script(int f) => new(
        f % 40 < 20 ? Buttons.Right : f % 40 == 25 ? Buttons.MediumPunch : Buttons.None,
        f % 30 < 10 ? Buttons.Left : f % 30 == 15 ? Buttons.LightKick : Buttons.None);

    // One tick for each, a millisecond apart so datagrams have time to land.
    private static void Tick(NetMatch m)
    {
        var s = m.Session;
        var frame = s is null ? 0 : s.Frame + s.InputDelay;
        m.Tick(frame < Frames ? Script(frame).For(m.LocalPlayer) : Buttons.None, advance: s is null || s.Frame < Frames);
    }

    [Fact]
    public void TwoSocketsOnLocalhostPlayAMatchInSync()
    {
        using var hostLink = UdpLink.Host(0);
        using var joinLink = UdpLink.Join($"127.0.0.1:{hostLink.LocalPort}");
        var host = new NetMatch(hostLink, true, Settings());
        var joiner = new NetMatch(joinLink, false, Settings());

        for (var t = 0; t < 20_000; t++)
        {
            Tick(host);
            Tick(joiner);
            if (host.Session?.ConfirmedFrame == Frames && joiner.Session?.ConfirmedFrame == Frames)
                break;
            Thread.Sleep(t % 10 == 0 ? 1 : 0);
        }

        var inputs = new FrameInput[Frames];
        for (var f = Delay; f < Frames; f++)
            inputs[f] = Script(f);
        var offline = Replay.Record(42, Stage.Training, inputs, MatchMode.Versus).FinalChecksum;
        Assert.Equal(NetPhase.Playing, host.Phase);
        Assert.Equal(Frames, host.Session!.ConfirmedFrame);
        Assert.Equal(Frames, joiner.Session!.ConfirmedFrame);
        Assert.Equal(offline, host.Session.State.Checksum());
        Assert.Equal(offline, joiner.Session.State.Checksum());
    }

    [Fact]
    public void DifferentCharacterDataIsRefusedOverUdp()
    {
        using var hostLink = UdpLink.Host(0);
        using var joinLink = UdpLink.Join($"127.0.0.1:{hostLink.LocalPort}");
        var host = new NetMatch(hostLink, true, Settings());
        var joiner = new NetMatch(joinLink, false, Settings(hash: Replay.LoadedDataHash ^ 1));

        for (var t = 0; t < 2000 && joiner.Phase != NetPhase.Refused; t++)
        {
            Tick(host);
            Tick(joiner);
            Thread.Sleep(1);
        }
        Assert.Equal(NetPhase.Refused, host.Phase);
        Assert.Equal(NetPhase.Refused, joiner.Phase);
        Assert.Contains("character data differs", joiner.RefusedBecause);
    }

    // The joiner's socket closes mid-match (a crash, a pulled cable): the host counts the grace
    // period down and wins by forfeit.
    [Fact]
    public void AVanishedJoinerForfeits()
    {
        using var hostLink = UdpLink.Host(0);
        var joinLink = UdpLink.Join($"127.0.0.1:{hostLink.LocalPort}");
        var host = new NetMatch(hostLink, true, Settings());
        var joiner = new NetMatch(joinLink, false, Settings());
        for (var t = 0; t < 2000 && (host.Session?.Frame ?? 0) < 120; t++)
        {
            Tick(host);
            Tick(joiner);
            Thread.Sleep(t % 10 == 0 ? 1 : 0);
        }
        Assert.Equal(NetPhase.Playing, host.Phase);

        joinLink.Dispose();
        var sawLost = false;
        for (var t = 0; t < NetMatch.LostAfterTicks + NetMatch.GraceTicks + 50 && host.Phase != NetPhase.Forfeited; t++)
        {
            Tick(host);
            sawLost |= host.Phase == NetPhase.ConnectionLost;
        }
        Assert.True(sawLost, "connection lost first");
        Assert.Equal(NetPhase.Forfeited, host.Phase);
        Assert.Equal(0, host.ForfeitWinner);
    }
}
