using System.Collections.Generic;

namespace StickFighter.Sim.Net;

// Datagrams to and from the other player: what a UDP socket gives (game/net/UdpLink), or a test's
// in-memory pair. Unreliable and unordered; NetMatch and RollbackSession cope with that.
public interface IPacketLink
{
    void Send(byte[] packet);

    bool TryReceive(out byte[] packet);
}

public enum NetPhase
{
    // Handshaking: the joiner says hello until the host welcomes it.
    Connecting,
    Playing,

    // Nothing heard for LostAfterTicks: still playing (the session stalls at its cap), counting down.
    ConnectionLost,

    // Over: refused at the handshake, or the other player forfeited by staying gone.
    Refused,
    Forfeited,
}

// One player's end of an online match, over an IPacketLink: the handshake, then a RollbackSession
// carried in Data packets, and the disconnect rule (rules: a disconnect counts as a forfeit after
// a grace period). The host is P1 and decides the match (seed, stage, mode); the joiner is P2 and
// plays what it's welcomed with. Both must have the same protocol version and character data, or
// they'd simulate different games: a mismatch is refused, with the reason, on both sides.
//
// Time is in ticks (Tick is called once a frame), so the timeouts are testable without a clock.
public sealed class NetMatch
{
    // Resend the hello (or the welcome) this often while connecting.
    public const int HelloEveryTicks = 30;

    // No packet for this long: the connection is lost (a countdown shows).
    public const int LostAfterTicks = 1 * GameInfo.TicksPerSecond;

    // Then this long more and the other player forfeits (or, while connecting, the join fails).
    public const int GraceTicks = 10 * GameInfo.TicksPerSecond;

    // Everything a match starts from, decided by the host and adopted by the joiner, except the
    // input delay, which is each player's own. HostPlayer is the side the host plays (0: P1);
    // characters are roster indices and colors 0 (own) or 1 (the alternate).
    public readonly record struct Settings(ulong Seed, Stage Stage, MatchMode Mode, int InputDelay, ulong DataHash,
        int HostPlayer = 0, int P1Character = 0, int P2Character = 0, int P1Color = 0, int P2Color = 0);

    private readonly IPacketLink _link;
    private readonly Settings _mine;
    private readonly Queue<NetMessage> _inbox = new();
    private int _ticks;
    private int _lastHeard;

    // `host`: P1, deciding the match from `settings`; otherwise P2, playing what the host sends.
    // `settings.DataHash` is this build's character data (Replay.LoadedDataHash).
    public NetMatch(IPacketLink link, bool host, Settings settings)
    {
        _link = link;
        Host = host;
        _mine = settings;
        Phase = NetPhase.Connecting;
    }

    public bool Host { get; }

    // The side this player plays: the host's choice, and the other one for the joiner.
    public int LocalPlayer => Host ? _mine.HostPlayer : 1 - (Agreed?.HostPlayer ?? 0);
    public NetPhase Phase { get; private set; }

    // The match being played, once connected.
    public RollbackSession? Session { get; private set; }
    public Settings? Agreed { get; private set; }

    // Why the match was refused (Refused), or which player won by forfeit (Forfeited).
    public string? RefusedBecause { get; private set; }
    public int ForfeitWinner { get; private set; } = -1;

    // Ticks left before a lost connection is a forfeit.
    public int GraceLeft => Phase == NetPhase.ConnectionLost ? LostAfterTicks + GraceTicks - (_ticks - _lastHeard) : GraceTicks;

    // One frame: takes in what's arrived, handshakes or plays a frame with `local` as this player's
    // input, and checks the connection. `advance` false keeps the match where it is (still sending
    // and receiving): for a scripted match that has played all its frames.
    public void Tick(Buttons local, bool advance = true)
    {
        _ticks++;
        Pump();
        if (Phase is NetPhase.Refused or NetPhase.Forfeited)
            return;

        if (Phase == NetPhase.Connecting)
        {
            if (!Host && _ticks % HelloEveryTicks == 1)
                _link.Send(NetCodec.Encode(new HelloPacket(_mine.DataHash)));
        }
        else if (advance)
        {
            Session!.Tick(local);
        }
        else
        {
            Session!.Poll();
        }

        var silent = _ticks - _lastHeard;
        if (Phase == NetPhase.Connecting && !Host && silent > LostAfterTicks + GraceTicks)
        {
            RefuseLocally("No answer from the host");
        }
        else if (Phase is NetPhase.Playing or NetPhase.ConnectionLost)
        {
            if (silent > LostAfterTicks + GraceTicks)
            {
                Phase = NetPhase.Forfeited;
                ForfeitWinner = LocalPlayer;
            }
            else
            {
                Phase = silent > LostAfterTicks ? NetPhase.ConnectionLost : NetPhase.Playing;
            }
        }
    }

    // Drains the link: handshake packets are handled here, match data queued for the session.
    private void Pump()
    {
        while (_link.TryReceive(out var bytes))
        {
            var packet = NetCodec.Decode(bytes, out var version);
            if (packet is null)
            {
                if (version >= 0 && version != NetCodec.ProtocolVersion && Phase == NetPhase.Connecting)
                    Refuse($"The other player's game is a different version (protocol {version}, this is {NetCodec.ProtocolVersion})");
                continue;
            }
            _lastHeard = _ticks;

            switch (packet)
            {
                case HelloPacket hello when Host:
                    if (hello.DataHash != _mine.DataHash)
                        Refuse($"The other player's character data differs ({hello.DataHash:x16}, this is {_mine.DataHash:x16}): update both games to the same version");
                    else
                        Welcome();
                    break;
                case WelcomePacket welcome when !Host && Phase == NetPhase.Connecting:
                    if (welcome.DataHash != _mine.DataHash)
                    {
                        Refuse($"The host's character data differs ({welcome.DataHash:x16}, this is {_mine.DataHash:x16}): update both games to the same version");
                        break;
                    }
                    var adopted = new Settings(welcome.Seed, welcome.Stage, welcome.Mode, _mine.InputDelay, welcome.DataHash,
                        welcome.HostPlayer, welcome.P1Character, welcome.P2Character, welcome.P1Color, welcome.P2Color);
                    var start = StartChecksum(adopted);
                    if (start != welcome.StartChecksum)
                    {
                        Refuse($"The match would start differently on each side (start state {start:x16} here, {welcome.StartChecksum:x16} on the host)");
                        break;
                    }
                    Start(adopted);
                    break;
                case RefusePacket refuse:
                    if (Phase == NetPhase.Connecting)
                        RefuseLocally(refuse.Reason);
                    break;
                case DataPacket data when Session is not null:
                    _inbox.Enqueue(data.Message);
                    break;
            }
        }
    }

    // The host welcomes the joiner (again, if the first welcome was lost) and starts.
    private void Welcome()
    {
        var m = _mine;
        _link.Send(NetCodec.Encode(new WelcomePacket(m.DataHash, m.Seed, m.Stage, m.Mode, m.HostPlayer,
            m.P1Character, m.P2Character, m.P1Color, m.P2Color, StartChecksum(m))));
        if (Phase == NetPhase.Connecting)
            Start(m);
    }

    // The checksum of the state a match with these settings starts from: both sides must agree on
    // it before the first input, or they'd desync on frame 0.
    public static ulong StartChecksum(in Settings settings) =>
        GameState.NewMatch(settings.Seed, settings.Stage, settings.Mode).Checksum();

    private void Start(Settings settings)
    {
        Agreed = settings;
        Session = new RollbackSession(LocalPlayer, settings.Seed, settings.Stage, settings.Mode, settings.InputDelay, new Transport(this));
        Phase = NetPhase.Playing;
    }

    // Tells the other side why, and stops.
    private void Refuse(string reason)
    {
        _link.Send(NetCodec.Encode(new RefusePacket(reason)));
        RefuseLocally(reason);
    }

    private void RefuseLocally(string reason)
    {
        Phase = NetPhase.Refused;
        RefusedBecause = reason;
    }

    // The session's view of the connection: Data packets in and out.
    private sealed class Transport : ITransport
    {
        private readonly NetMatch _match;

        public Transport(NetMatch match) => _match = match;

        public void Send(NetMessage message) => _match._link.Send(NetCodec.Encode(new DataPacket(message)));

        public bool TryReceive(out NetMessage message) => _match._inbox.TryDequeue(out message!);
    }
}
