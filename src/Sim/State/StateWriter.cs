using System;
using System.Buffers.Binary;
using StickFighter.Sim.Numerics;

namespace StickFighter.Sim;

// Writes state fields into a canonical little-endian byte encoding, in the order they're called.
// The checksum and equality both use this encoding, so the field order lives in one place.
public ref struct StateWriter
{
    private readonly Span<byte> _buffer;

    public StateWriter(Span<byte> buffer)
    {
        _buffer = buffer;
        Length = 0;
    }

    public int Length { get; private set; }

    public void Write(int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(_buffer[Length..], value);
        Length += 4;
    }

    public void Write(long value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(_buffer[Length..], value);
        Length += 8;
    }

    public void Write(ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(_buffer[Length..], value);
        Length += 8;
    }

    public void Write(bool value)
    {
        _buffer[Length] = value ? (byte)1 : (byte)0;
        Length += 1;
    }

    public void Write(Fix value) => Write(value.Raw);

    public void Write(FixVec2 value)
    {
        Write(value.X);
        Write(value.Y);
    }

    public void Write(Angle value) => Write(value.Units);
}
