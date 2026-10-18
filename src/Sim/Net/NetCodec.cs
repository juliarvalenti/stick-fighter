using System;
using System.Buffers.Binary;
using System.Text;

namespace StickFighter.Sim.Net;

// What goes over the wire. Every packet starts with the protocol version and its kind:
// - Hello, from the joining player: its character data hash (it must match the host's);
// - Welcome, from the host: its data hash and everything the match starts from (seed, stage, mode,
//   which side the host plays, both players' characters and colors), which the joiner adopts, and
//   the checksum of the starting state, which the joiner's must equal;
// - Refuse, either way: why the match can't happen;
// - Data: a NetMessage, once the match is on.
// Little-endian, compact, and checked: anything malformed decodes as null and is dropped.
public abstract record Packet;

public sealed record HelloPacket(ulong DataHash) : Packet;

public sealed record WelcomePacket(ulong DataHash, ulong Seed, Stage Stage, MatchMode Mode, int HostPlayer,
    int P1Character, int P2Character, int P1Color, int P2Color, ulong StartChecksum) : Packet;

public sealed record RefusePacket(string Reason) : Packet;

public sealed record DataPacket(NetMessage Message) : Packet;

public static class NetCodec
{
    // Bumped whenever the wire format or what's exchanged changes, so two different builds refuse
    // each other cleanly instead of misreading packets.
    public const byte ProtocolVersion = 1;

    private const byte Hello = 1;
    private const byte Welcome = 2;
    private const byte Refuse = 3;
    private const byte Data = 4;

    public static byte[] Encode(Packet packet)
    {
        var w = new Writer();
        w.Byte(ProtocolVersion);
        switch (packet)
        {
            case HelloPacket hello:
                w.Byte(Hello);
                w.U64(hello.DataHash);
                break;
            case WelcomePacket welcome:
                w.Byte(Welcome);
                w.U64(welcome.DataHash);
                w.U64(welcome.Seed);
                w.I32(welcome.Stage.LeftWall.FloorToInt());
                w.I32(welcome.Stage.RightWall.FloorToInt());
                w.I32(welcome.Stage.Floor.FloorToInt());
                w.I32(welcome.Stage.MaxSeparation.FloorToInt());
                w.Byte((byte)welcome.Mode);
                w.Byte((byte)welcome.HostPlayer);
                w.Byte((byte)welcome.P1Character);
                w.Byte((byte)welcome.P2Character);
                w.Byte((byte)welcome.P1Color);
                w.Byte((byte)welcome.P2Color);
                w.U64(welcome.StartChecksum);
                break;
            case RefusePacket refuse:
                w.Byte(Refuse);
                w.Bytes(Encoding.UTF8.GetBytes(refuse.Reason));
                break;
            case DataPacket { Message: var m }:
                w.Byte(Data);
                w.I32(m.FirstFrame);
                w.U16((ushort)m.Inputs.Length);
                foreach (var input in m.Inputs)
                    w.U16((ushort)input);
                w.I32(m.Ack);
                w.I32(m.ChecksumFrame);
                w.U64(m.Checksum);
                w.I32(m.Frame);
                w.I32(m.Advantage);
                w.I32(m.TrailFirst);
                w.U16((ushort)(m.Trail?.Length ?? 0));
                foreach (var sum in m.Trail ?? Array.Empty<ulong>())
                    w.U64(sum);
                break;
            default:
                throw new ArgumentException($"Unknown packet {packet.GetType().Name}", nameof(packet));
        }
        return w.ToArray();
    }

    // The packet, or null if it's malformed. `version` is the protocol version it was sent with
    // (a packet from another version isn't decoded at all, only reported).
    public static Packet? Decode(ReadOnlySpan<byte> bytes, out int version)
    {
        version = bytes.Length > 0 ? bytes[0] : -1;
        if (bytes.Length < 2 || version != ProtocolVersion)
            return null;
        var r = new Reader(bytes[2..]);
        try
        {
            Packet packet = bytes[1] switch
            {
                Hello => new HelloPacket(r.U64()),
                Welcome => new WelcomePacket(r.U64(), r.U64(), new Stage(r.I32(), r.I32(), r.I32(), r.I32()), (MatchMode)r.Byte(),
                    r.Byte(), r.Byte(), r.Byte(), r.Byte(), r.Byte(), r.U64()),
                Refuse => new RefusePacket(Encoding.UTF8.GetString(r.Rest())),
                Data => DecodeData(ref r),
                _ => throw new FormatException(),
            };
            return r.Done ? packet : null;
        }
        catch (Exception e) when (e is FormatException or ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    private static DataPacket DecodeData(ref Reader r)
    {
        var first = r.I32();
        var inputs = new Buttons[r.U16()];
        for (var i = 0; i < inputs.Length; i++)
            inputs[i] = (Buttons)r.U16();
        var ack = r.I32();
        var checksumFrame = r.I32();
        var checksum = r.U64();
        var frame = r.I32();
        var advantage = r.I32();
        var trailFirst = r.I32();
        var trailLength = r.U16();
        ulong[]? trail = trailLength > 0 ? new ulong[trailLength] : null;
        for (var i = 0; i < trailLength; i++)
            trail![i] = r.U64();
        return new DataPacket(new NetMessage(first, inputs, ack, checksumFrame, checksum, frame, advantage, trailFirst, trail));
    }

    private sealed class Writer
    {
        private byte[] _bytes = new byte[64];
        private int _length;

        public void Byte(byte value) => Span(1)[0] = value;
        public void U16(ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(Span(2), value);
        public void I32(int value) => BinaryPrimitives.WriteInt32LittleEndian(Span(4), value);
        public void U64(ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(Span(8), value);
        public void Bytes(byte[] value) => value.CopyTo(Span(value.Length));

        public byte[] ToArray() => _bytes.AsSpan(0, _length).ToArray();

        private Span<byte> Span(int count)
        {
            if (_length + count > _bytes.Length)
                Array.Resize(ref _bytes, (_length + count) * 2);
            var span = _bytes.AsSpan(_length, count);
            _length += count;
            return span;
        }
    }

    private ref struct Reader
    {
        private readonly ReadOnlySpan<byte> _bytes;
        private int _at;

        public Reader(ReadOnlySpan<byte> bytes)
        {
            _bytes = bytes;
            _at = 0;
        }

        public readonly bool Done => _at == _bytes.Length;

        public byte Byte() => Take(1)[0];
        public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        public int I32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
        public ulong U64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));

        public ReadOnlySpan<byte> Rest() => Take(_bytes.Length - _at);

        private ReadOnlySpan<byte> Take(int count)
        {
            if (_at + count > _bytes.Length)
                throw new FormatException();
            var span = _bytes.Slice(_at, count);
            _at += count;
            return span;
        }
    }
}
