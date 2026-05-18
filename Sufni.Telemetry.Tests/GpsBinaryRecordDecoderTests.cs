using System.Buffers.Binary;
using Sufni.Telemetry;

namespace Sufni.Telemetry.Tests;

public class GpsBinaryRecordDecoderTests
{
    [Fact]
    public void Decode_ReturnsGpsRecord_WhenRecordIsValid()
    {
        var bytes = CreateRecord(
            date: 20260518,
            timeMs: 45_296_789,
            latitude: 42.6977,
            longitude: 23.3219,
            altitude: 590.5f,
            speed: 8.25f,
            heading: 181.5f,
            fixMode: 2,
            satellites: 11,
            epe2d: 1.25f,
            epe3d: 2.5f);

        var record = GpsBinaryRecordDecoder.Decode(bytes);

        Assert.NotNull(record);
        Assert.Equal(new DateTime(2026, 5, 18, 12, 34, 56, 789, DateTimeKind.Utc), record.Timestamp);
        Assert.Equal(42.6977, record.Latitude);
        Assert.Equal(23.3219, record.Longitude);
        Assert.Equal(590.5f, record.Altitude);
        Assert.Equal(8.25f, record.Speed);
        Assert.Equal(181.5f, record.Heading);
        Assert.Equal(2, record.FixMode);
        Assert.Equal(11, record.Satellites);
        Assert.Equal(1.25f, record.Epe2d);
        Assert.Equal(2.5f, record.Epe3d);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(20260018u)]
    [InlineData(20261318u)]
    [InlineData(20260431u)]
    [InlineData(20250229u)]
    public void Decode_ReturnsNull_WhenDateIsInvalid(uint date)
    {
        var bytes = CreateRecord(date);

        var record = GpsBinaryRecordDecoder.Decode(bytes);

        Assert.Null(record);
    }

    [Fact]
    public void Decode_ReturnsGpsRecord_WhenDateIsLeapDayInLeapYear()
    {
        var bytes = CreateRecord(date: 20240229, timeMs: 1_000);

        var record = GpsBinaryRecordDecoder.Decode(bytes);

        Assert.NotNull(record);
        Assert.Equal(new DateTime(2024, 2, 29, 0, 0, 1, DateTimeKind.Utc), record.Timestamp);
    }

    [Fact]
    public void Decode_ThrowsArgumentException_WhenRecordLengthIsWrong()
    {
        Assert.Throws<ArgumentException>(() => GpsBinaryRecordDecoder.Decode(new byte[GpsBinaryRecordDecoder.RecordSize - 1]));
    }

    private static byte[] CreateRecord(
        uint date = 20260518,
        uint timeMs = 0,
        double latitude = 1.25,
        double longitude = 2.5,
        float altitude = 3.75f,
        float speed = 4.5f,
        float heading = 5.25f,
        byte fixMode = 1,
        byte satellites = 8,
        float epe2d = 6.25f,
        float epe3d = 7.5f)
    {
        var bytes = new byte[GpsBinaryRecordDecoder.RecordSize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), date);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), timeMs);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(8, 8), BitConverter.DoubleToInt64Bits(latitude));
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(16, 8), BitConverter.DoubleToInt64Bits(longitude));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24, 4), BitConverter.SingleToInt32Bits(altitude));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28, 4), BitConverter.SingleToInt32Bits(speed));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(32, 4), BitConverter.SingleToInt32Bits(heading));
        bytes[36] = fixMode;
        bytes[37] = satellites;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(38, 4), BitConverter.SingleToInt32Bits(epe2d));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(42, 4), BitConverter.SingleToInt32Bits(epe3d));
        return bytes;
    }
}
