using System;
using System.Net;
using System.Net.Sockets;
using StickFighter.Sim.Net;

namespace StickFighter.Net;

// An IPacketLink over a real UDP socket, for direct connect: the host binds a port and answers
// whoever writes to it first; the joiner sends to the host's IP:port. Non-blocking: Send fires a
// datagram, TryReceive takes one if any has arrived. No Godot here, so tests and tools/NetPlay use
// it as is.
public sealed class UdpLink : IPacketLink, IDisposable
{
    private readonly UdpClient _socket;
    private IPEndPoint? _remote;

    private UdpLink(UdpClient socket, IPEndPoint? remote)
    {
        _socket = socket;
        _remote = remote;
    }

    // Listens on `port` (0: any free port; see LocalPort) for a joiner.
    public static UdpLink Host(int port) => new(new UdpClient(new IPEndPoint(IPAddress.Any, port)), null);

    // Connects to a host at `address` ("192.168.1.20:7000").
    public static UdpLink Join(string address)
    {
        if (!IPEndPoint.TryParse(address, out var host) || host.Port == 0)
            throw new FormatException($"'{address}' isn't an IP address and port (like 192.168.1.20:7000)");
        return new UdpLink(new UdpClient(new IPEndPoint(IPAddress.Any, 0)), host);
    }

    public int LocalPort => ((IPEndPoint)_socket.Client.LocalEndPoint!).Port;

    // Whoever this link talks to: the host it joined, or the joiner the host has heard from.
    public IPEndPoint? Remote => _remote;

    public void Send(byte[] packet)
    {
        if (_remote is null)
            return; // a host with no joiner yet has no one to send to
        try
        {
            _socket.Send(packet, packet.Length, _remote);
        }
        catch (SocketException)
        {
            // An unreachable peer (ICMP "port unreachable" on some systems): the disconnect rule handles it.
        }
    }

    public bool TryReceive(out byte[] packet)
    {
        while (_socket.Available > 0)
        {
            var from = new IPEndPoint(IPAddress.Any, 0);
            try
            {
                packet = _socket.Receive(ref from);
            }
            catch (SocketException)
            {
                continue;
            }

            // The host takes the first sender as its joiner; after that, and for the joiner, packets
            // from anyone else are ignored.
            _remote ??= from;
            if (from.Equals(_remote))
                return true;
        }
        packet = Array.Empty<byte>();
        return false;
    }

    public void Dispose() => _socket.Dispose();
}
