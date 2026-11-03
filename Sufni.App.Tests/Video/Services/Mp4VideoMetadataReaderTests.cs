using System.Buffers.Binary;
using System.Text;

using Sufni.App.Video.Services;

namespace Sufni.App.Tests.Video.Services;

public class Mp4VideoMetadataReaderTests
{
    private readonly Mp4VideoMetadataReader reader = new();

    [Fact]
    public async Task ReadAsync_ParsesVersionZeroMovieHeader()
    {
        var start = new DateTimeOffset(2026, 6, 3, 14, 15, 12, TimeSpan.Zero);
        await using var stream = CreateMovie(version: 0, start, timescale: 1000, durationUnits: 88088);

        var result = await reader.ReadAsync(stream, TestContext.Current.CancellationToken);

        var parsed = Assert.IsType<VideoMetadataReadResult.Parsed>(result);
        Assert.Equal(start, parsed.Metadata.StartUtc);
        Assert.Equal(TimeSpan.Zero, parsed.Metadata.StartUtc.Offset);
        Assert.Equal(88.088, parsed.Metadata.Duration.TotalSeconds, precision: 3);
    }

    [Fact]
    public async Task ReadAsync_ParsesVersionOneMovieHeader()
    {
        var start = new DateTimeOffset(2026, 6, 3, 14, 15, 12, TimeSpan.Zero);
        await using var stream = CreateMovie(version: 1, start, timescale: 10, durationUnits: 1234);

        var result = await reader.ReadAsync(stream, TestContext.Current.CancellationToken);

        var parsed = Assert.IsType<VideoMetadataReadResult.Parsed>(result);
        Assert.Equal(start, parsed.Metadata.StartUtc);
        Assert.Equal(123.4, parsed.Metadata.Duration.TotalSeconds, precision: 1);
    }

    [Fact]
    public async Task ReadAsync_ReturnsInvalid_WhenMovieHeaderIsMissing()
    {
        await using var stream = new MemoryStream(Box("ftyp", [0, 0, 0, 0]));

        var result = await reader.ReadAsync(stream, TestContext.Current.CancellationToken);

        Assert.IsType<VideoMetadataReadResult.Invalid>(result);
    }

    [Fact]
    public async Task ReadAsync_ReturnsInvalid_WhenDurationIsNotPositive()
    {
        var start = new DateTimeOffset(2026, 6, 3, 14, 15, 12, TimeSpan.Zero);
        await using var stream = CreateMovie(version: 0, start, timescale: 1000, durationUnits: 0);

        var result = await reader.ReadAsync(stream, TestContext.Current.CancellationToken);

        Assert.IsType<VideoMetadataReadResult.Invalid>(result);
    }

    private static MemoryStream CreateMovie(byte version, DateTimeOffset start, uint timescale, ulong durationUnits)
    {
        var movieHeader = Box("mvhd", MovieHeaderPayload(version, start, timescale, durationUnits));
        var movie = Box("moov", movieHeader);
        return new MemoryStream(Concat(Box("ftyp", [0, 0, 0, 0]), movie));
    }

    private static byte[] MovieHeaderPayload(byte version, DateTimeOffset start, uint timescale, ulong durationUnits)
    {
        return version switch
        {
            0 => MovieHeaderVersionZero(start, timescale, checked((uint)durationUnits)),
            1 => MovieHeaderVersionOne(start, timescale, durationUnits),
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, null)
        };
    }

    private static byte[] MovieHeaderVersionZero(DateTimeOffset start, uint timescale, uint durationUnits)
    {
        var payload = new byte[20];
        payload[0] = 0;
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(4, 4), checked((uint)ToQuickTimeSeconds(start)));
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(12, 4), timescale);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(16, 4), durationUnits);
        return payload;
    }

    private static byte[] MovieHeaderVersionOne(DateTimeOffset start, uint timescale, ulong durationUnits)
    {
        var payload = new byte[32];
        payload[0] = 1;
        BinaryPrimitives.WriteUInt64BigEndian(payload.AsSpan(4, 8), ToQuickTimeSeconds(start));
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(20, 4), timescale);
        BinaryPrimitives.WriteUInt64BigEndian(payload.AsSpan(24, 8), durationUnits);
        return payload;
    }

    private static ulong ToQuickTimeSeconds(DateTimeOffset value)
    {
        var quickTimeEpoch = new DateTimeOffset(1904, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return checked((ulong)(value - quickTimeEpoch).TotalSeconds);
    }

    private static byte[] Box(string type, params byte[][] payloadParts)
    {
        var payload = Concat(payloadParts);
        var box = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4), checked((uint)box.Length));
        Encoding.ASCII.GetBytes(type, box.AsSpan(4, 4));
        payload.CopyTo(box.AsSpan(8));
        return box;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var length = parts.Sum(part => part.Length);
        var result = new byte[length];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result.AsSpan(offset));
            offset += part.Length;
        }

        return result;
    }
}
