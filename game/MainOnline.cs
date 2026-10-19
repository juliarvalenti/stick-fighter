using System;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using StickFighter.Net;
using StickFighter.Sim;
using StickFighter.Sim.Net;
using StickFighter.Sim.Replays;
using StickFighter.UI.Screens;

namespace StickFighter;

// Online play's part of the game loop, direct connect: hosting on a port or joining by IP:port, the
// handshake while the online screen shows what's happening, then the match, ticked through the
// NetMatch (and its RollbackSession) instead of the local driver. A lost connection shows a
// countdown; after the grace period it's a forfeit, and the results say so. Online play can't stop
// the other player's clock, so pause doesn't freeze an online match: it plays on underneath.
// Kept apart from Main.cs so the menus and online play don't edit the same lines.
public partial class Main
{
    // The port a host listens on (forwarded on the host's router to play over the internet).
    public const int OnlinePort = 7000;

    private UdpLink? _link;
    private NetMatch? _online;
    private OnlineScreen? _onlineScreen;
    private TickDriver _netClock = new();
    private bool _onlineStarted;
    private string _lastJoinAddress = "";
    private int _lostSecondShown = -1;

    // For the net overlay: rollbacks over the last second.
    private int _netTicks;
    private int _rollbacksAtSecond;
    private int _rollbacksPerSecond;

    // Host / Join / Back, with `status` saying why we're back here (a refusal, a failed port).
    private void ShowOnline(string status = "")
    {
        CloseOnline();
        _flow = Flow.Menu;
        var screen = new OnlineScreen(OnlineScreen.Page.Choose, status: status);
        screen.Chosen += item =>
        {
            switch (item)
            {
                case OnlineScreen.HostItem:
                    StartHosting();
                    break;
                case OnlineScreen.JoinItem:
                    ShowJoin();
                    break;
                default:
                    ShowMenu();
                    break;
            }
        };
        ShowScreen(screen, ShowMenu);
    }

    private void StartHosting()
    {
        try
        {
            _link = UdpLink.Host(OnlinePort);
        }
        catch (SocketException e)
        {
            ShowOnline($"Can't host on port {OnlinePort}: {e.Message}");
            return;
        }
        BeginConnecting(host: true);
        _onlineScreen = new OnlineScreen(OnlineScreen.Page.Host, $"{LanAddress()}:{OnlinePort}", "Waiting for a player…");
        _onlineScreen.Chosen += _ => ShowOnline();
        ShowScreen(_onlineScreen, () => ShowOnline());
    }

    private void ShowJoin(string status = "")
    {
        CloseOnline();
        _flow = Flow.Menu;
        var screen = new OnlineScreen(OnlineScreen.Page.Join, _lastJoinAddress, status);
        screen.Chosen += item =>
        {
            if (item == OnlineScreen.ConnectItem)
                Connect(screen.Address);
            else
                ShowOnline();
        };
        _onlineScreen = screen;
        ShowScreen(screen, () => ShowOnline());
    }

    private void Connect(string address)
    {
        CloseOnline();
        _lastJoinAddress = address;
        try
        {
            _link = UdpLink.Join(address);
        }
        catch (Exception e) when (e is FormatException or SocketException)
        {
            if (_onlineScreen is not null)
                _onlineScreen.Status = e.Message;
            return;
        }
        BeginConnecting(host: false);
        if (_onlineScreen is not null)
            _onlineScreen.Status = $"Connecting to {address}…";
    }

    private void BeginConnecting(bool host)
    {
        // The host decides the match; the joiner's settings only give its own input delay and data.
        var settings = new NetMatch.Settings(NextSeed, Stage.Training, MatchMode.Versus, DeviceInput.Settings.InputDelay, Replay.LoadedDataHash);
        _online = new NetMatch(_link!, host, settings);
        _netClock = new TickDriver();
        _onlineStarted = false;
        _lostSecondShown = -1;
        _netTicks = 0;
        _rollbacksAtSecond = 0;
        _rollbacksPerSecond = 0;
    }

    // Instead of the local driver while online: runs the ticks due through the NetMatch, and acts on
    // what happened (connected, refused, lost, forfeited). Returns the ticks run.
    private int TickOnline(long elapsedMicroseconds)
    {
        var online = _online!;
        var ticks = _netClock.Due(elapsedMicroseconds);
        for (var i = 0; i < ticks && online.Phase is not (NetPhase.Refused or NetPhase.Forfeited); i++)
        {
            online.Tick(DeviceInput.Read().Player1);
            _netTicks++;
        }

        switch (online.Phase)
        {
            case NetPhase.Refused:
                var reason = online.RefusedBecause ?? "Refused";
                CloseOnline();
                if (_onlineScreen is not null)
                    _onlineScreen.Status = reason;
                return ticks;
            case NetPhase.Forfeited:
                ShowOnlineResults(forfeit: true);
                return ticks;
            case NetPhase.Playing or NetPhase.ConnectionLost when !_onlineStarted:
                BeginOnlineMatch();
                break;
        }

        if (online.Phase == NetPhase.ConnectionLost)
        {
            var seconds = (online.GraceLeft + GameInfo.TicksPerSecond - 1) / GameInfo.TicksPerSecond;
            if (seconds != _lostSecondShown && seconds % 3 == 1)
                ShowToast($"Connection lost: the other player forfeits in {seconds} s", UI.Toast.Kind.Warning);
            _lostSecondShown = seconds;
        }
        else if (_lostSecondShown >= 0)
        {
            _lostSecondShown = -1;
            ShowToast("Connection back", UI.Toast.Kind.Success);
        }

        if (online.Session is { } session)
        {
            if (_onlineStarted)
                _state = session.State;
            if (_netTicks >= GameInfo.TicksPerSecond)
            {
                _rollbacksPerSecond = session.Rollbacks - _rollbacksAtSecond;
                _rollbacksAtSecond = session.Rollbacks;
                _netTicks = 0;
            }
        }
        return ticks;
    }

    // Connected: the online match starts, from the state both sides agreed on.
    private void BeginOnlineMatch()
    {
        var session = _online!.Session!;
        _onlineStarted = true;
        _matchNumber++;
        _settings = new Menus.MatchSettings(MatchMode.Versus);
        _playerColors = _settings.Colors(DefaultP1, DefaultP2);
        _state = session.State;
        _training = null;
        _replayFromMatchStart = false; // the local recorder doesn't see the other player's inputs
        _camera.Cut();
        _effects.Clear();
        _sound.Director.Clear();
        _voice.Begin();
        _presenter.Begin(new[] { _settings.P1Color, _settings.P2Color });
        _flow = Flow.Match;
        _paused = null;
        _onlineScreen = null;
        ShowScreen(null);
        ShowToast($"Connected: you're player {_online.LocalPlayer + 1}", UI.Toast.Kind.Success);
    }

    // The match is over, decided or by forfeit. Online there's no rematch yet (both would have to
    // agree): back to the title.
    private void ShowOnlineResults(bool forfeit)
    {
        var online = _online;
        var results = _presenter.Results(_state);
        if (forfeit && online is not null)
        {
            results = results with { Winner = online.LocalPlayer == 0 ? MatchWinner.P1 : MatchWinner.P2 };
            ShowToast("The other player left: you win by forfeit", UI.Toast.Kind.Success);
        }
        CloseOnline();
        _flow = Flow.Results;
        _paused = null;
        var screen = new ResultsScreen(results, new[] { _settings.P1Color, _settings.P2Color }, online: true);
        screen.Chosen += _ => ShowPressStart();
        ShowScreen(screen);
    }

    // Hangs up. The other side sees the connection go, and wins by forfeit after the grace period.
    private void CloseOnline()
    {
        _link?.Dispose();
        _link = null;
        _online = null;
        _onlineStarted = false;
    }

    // For the F3 overlay: ping, frame advantage, rollbacks a second and stalls.
    private string? NetStats() => _online?.Session is { } s
        ? $"net  ping {(s.PingMs < 0 ? "-" : s.PingMs.ToString())} ms   advantage {s.FrameAdvantage:+0;-0;0}   rollbacks {_rollbacksPerSecond}/s   stalls {s.StalledTicks}"
        : null;

    // This machine's address on the local network, for a host to read out (127.0.0.1 if none).
    private static string LanAddress()
    {
        foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (network.OperationalStatus != OperationalStatus.Up || network.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;
            foreach (var address in network.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily == AddressFamily.InterNetwork)
                    return address.Address.ToString();
            }
        }
        return "127.0.0.1";
    }
}
