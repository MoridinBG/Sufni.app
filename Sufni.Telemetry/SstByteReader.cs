using System.Buffers.Binary;

namespace Sufni.Telemetry;

// Cursor-style little-endian reader for SST payloads. The parser intentionally
// fails fast on short reads so corrupt files stop at the field boundary.
internal ref struct SstByteReader(ReadOnlySpan<byte> bytes)
{
    private readonly ReadOnlySpan<byte> bytes = bytes;

    public int Length => bytes.Length;
    public int Position { get; private set; }
    public int Remaining => bytes.Length - Position;

    public byte ReadByte()
    {
        EnsureAvailable(sizeof(byte));
        return bytes[Position++];
    }

    public ushort ReadUInt16()
    {
        var value = BinaryPrimitives.ReadUInt16LittleEndian(ReadBytes(sizeof(ushort)));
        return value;
    }

    public short ReadInt16()
    {
        var value = BinaryPrimitives.ReadInt16LittleEndian(ReadBytes(sizeof(short)));
        return value;
    }

    public uint ReadUInt32()
    {
        var value = BinaryPrimitives.ReadUInt32LittleEndian(ReadBytes(sizeof(uint)));
        return value;
    }

    public long ReadInt64()
    {
        var value = BinaryPrimitives.ReadInt64LittleEndian(ReadBytes(sizeof(long)));
        return value;
    }

    public float ReadSingle()
    {
        var raw = BinaryPrimitives.ReadInt32LittleEndian(ReadBytes(sizeof(float)));
        return BitConverter.Int32BitsToSingle(raw);
    }

    public double ReadDouble()
    {
        var raw = BinaryPrimitives.ReadInt64LittleEndian(ReadBytes(sizeof(double)));
        return BitConverter.Int64BitsToDouble(raw);
    }

    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        EnsureAvailable(count);
        var value = bytes.Slice(Position, count);
        Position += count;
        return value;
    }

    public void MoveTo(int position)
    {
        if ((uint)position > (uint)bytes.Length)
            throw new ArgumentOutOfRangeException(nameof(position));

        Position = position;
    }

    private void EnsureAvailable(int count)
    {
        if (count < 0 || Remaining < count)
            throw new FormatException("SST data ended before the expected field could be read.");
    }
}

internal static class SstParserBytes
{
    public static byte[] ReadRemainingBytes(BinaryReader reader)
    {
        var stream = reader.BaseStream;
        var remaining = checked((int)(stream.Length - stream.Position));
        var bytes = reader.ReadBytes(remaining);
        if (bytes.Length != remaining)
            throw new FormatException("SST stream ended before the expected bytes could be read.");

        return bytes;
    }
}
