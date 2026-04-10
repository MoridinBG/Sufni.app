using System.Text;
using Sufni.Telemetry;

namespace Sufni.Telemetry.Tests;

public class SstV4TlvParserTests
{
    [Fact]
    public void Parse_ValidV4File_AllChunks_ReturnsCorrectData()
    {
        // Arrange
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // Header
        writer.Write(Encoding.ASCII.GetBytes("SST"));
        writer.Write((byte)4);
        writer.Write((uint)0); // Padding
        writer.Write((long)123456789); // Timestamp

        // Rates Chunk (0x00)
        writer.Write((byte)0x00);
        writer.Write((ushort)6); // Length
        writer.Write((byte)TlvChunkType.Telemetry);
        writer.Write((ushort)1000);
        writer.Write((byte)TlvChunkType.Imu);
        writer.Write((ushort)100);

        // Telemetry Chunk (0x01)
        writer.Write((byte)0x01);
        writer.Write((ushort)8); // 2 records
        writer.Write((ushort)1000); // Front 1
        writer.Write((ushort)2000); // Rear 1
        writer.Write((ushort)1100); // Front 2
        writer.Write((ushort)2100); // Rear 2

        // Marker Chunk (0x02) - at sample 2
        writer.Write((byte)0x02);
        writer.Write((ushort)0);

        // IMU Meta Chunk (0x04)
        writer.Write((byte)0x04);
        writer.Write((ushort)10); // 1 + (1 * 9)
        writer.Write((byte)1); // 1 entry
        writer.Write((byte)0); // Location: Frame
        writer.Write(16384.0f); // Accel LSB
        writer.Write(16.4f); // Gyro LSB

        // IMU Chunk (0x03)
        writer.Write((byte)0x03);
        writer.Write((ushort)12); // 1 record
        writer.Write((short)100); // ax
        writer.Write((short)200); // ay
        writer.Write((short)300); // az
        writer.Write((short)10);  // gx
        writer.Write((short)20);  // gy
        writer.Write((short)30);  // gz

        ms.Position = 0;
        using var reader = new BinaryReader(ms);
        reader.ReadBytes(4); // Skip magic and version

        var parser = new SstV4TlvParser();

        // Act
        var result = parser.Parse(reader, 4);

        // Assert
        Assert.Equal(4, result.Version);
        Assert.Equal(1000, result.SampleRate);
        Assert.Equal(123456789, result.Timestamp);
        Assert.Equal(2, result.Front.Length);
        Assert.Equal(2, result.Rear.Length);
        Assert.Single(result.Markers);
        Assert.Equal(2.0 / 1000.0, result.Markers[0].TimestampOffset);
        Assert.NotNull(result.ImuData);
        Assert.Single(result.ImuData.Meta);
        Assert.Single(result.ImuData.Records);
        Assert.Equal(100, result.ImuData.Records[0].Ax);
    }

    [Fact]
    public void Parse_GpsChunk_ReturnsCorrectData()
    {
        // Arrange
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(Encoding.ASCII.GetBytes("SST"));
        writer.Write((byte)4);
        writer.Write((uint)0); // Padding
        writer.Write((long)0); // Timestamp

        // Rates Chunk
        writer.Write((byte)0x00);
        writer.Write((ushort)3);
        writer.Write((byte)TlvChunkType.Telemetry);
        writer.Write((ushort)1000);

        // Telemetry Chunk (minimal)
        writer.Write((byte)0x01);
        writer.Write((ushort)4);
        writer.Write((ushort)500);
        writer.Write((ushort)600);

        // GPS Chunk (0x05) - 1 record = 46 bytes
        writer.Write((byte)0x05);
        writer.Write((ushort)46);
        writer.Write((uint)20250106);  // Date: 2025-01-06
        writer.Write((uint)43200000);  // Time: 12:00:00.000 (noon in ms)
        writer.Write(47.123456);       // Latitude
        writer.Write(19.654321);       // Longitude
        writer.Write(150.5f);          // Altitude
        writer.Write(25.3f);           // Speed
        writer.Write(180.0f);          // Heading
        writer.Write((byte)2);         // Fix mode (3D)
        writer.Write((byte)12);        // Satellites
        writer.Write(2.5f);            // EPE 2D
        writer.Write(3.5f);            // EPE 3D

        ms.Position = 0;
        using var reader = new BinaryReader(ms);
        reader.ReadBytes(4);

        var parser = new SstV4TlvParser();

        // Act
        var result = parser.Parse(reader, 4);

        // Assert
        Assert.NotNull(result.GpsData);
        Assert.Single(result.GpsData);
        var gps = result.GpsData[0];
        Assert.Equal(new DateTime(2025, 1, 6, 12, 0, 0, DateTimeKind.Utc), gps.Timestamp);
        Assert.Equal(47.123456, gps.Latitude, 6);
        Assert.Equal(19.654321, gps.Longitude, 6);
        Assert.Equal(150.5f, gps.Altitude);
        Assert.Equal(25.3f, gps.Speed);
        Assert.Equal(180.0f, gps.Heading);
        Assert.Equal(2, gps.FixMode);
        Assert.Equal(12, gps.Satellites);
        Assert.Equal(2.5f, gps.Epe2d);
        Assert.Equal(3.5f, gps.Epe3d);
    }

    [Fact]
    public void Parse_UnknownChunkType_SkipsGracefully()
    {
        // Arrange
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(Encoding.ASCII.GetBytes("SST"));
        writer.Write((byte)4);
        writer.Write((uint)0); // Padding
        writer.Write((long)0); // Timestamp

        // Unknown Chunk (0xFF)
        writer.Write((byte)0xFF);
        writer.Write((ushort)5);
        writer.Write(new byte[] { 1, 2, 3, 4, 5 });

        // Rates Chunk
        writer.Write((byte)TlvChunkType.Rates);
        writer.Write((ushort)3);
        writer.Write((byte)TlvChunkType.Telemetry);
        writer.Write((ushort)1000);

        // Telemetry Chunk
        writer.Write((byte)0x01);
        writer.Write((ushort)4);
        writer.Write((ushort)500);
        writer.Write((ushort)600);

        ms.Position = 0;
        using var reader = new BinaryReader(ms);
        reader.ReadBytes(4);

        var parser = new SstV4TlvParser();

        // Act
        var result = parser.Parse(reader, 4);

        // Assert
        Assert.Single(result.Front);
        Assert.Equal(500, result.Front[0]);
    }

    [Fact]
    public void Inspect_UnknownChunkType_ReturnsValidInspectionWithHasUnknown()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(Encoding.ASCII.GetBytes("SST"));
        writer.Write((byte)4);
        writer.Write((uint)0);
        writer.Write((long)123456789);

        writer.Write((byte)TlvChunkType.Rates);
        writer.Write((ushort)3);
        writer.Write((byte)TlvChunkType.Telemetry);
        writer.Write((ushort)1000);

        writer.Write((byte)0xFF);
        writer.Write((ushort)5);
        writer.Write(new byte[] { 1, 2, 3, 4, 5 });

        writer.Write((byte)TlvChunkType.Telemetry);
        writer.Write((ushort)4);
        writer.Write((ushort)500);
        writer.Write((ushort)600);

        ms.Position = 0;

        var result = RawTelemetryData.InspectStream(ms);

        var inspection = Assert.IsType<ValidSstFileInspection>(result);
        Assert.True(inspection.HasUnknown);
        Assert.Equal(4, inspection.Version);
        Assert.Equal(1000, inspection.TelemetrySampleRate);
        Assert.Equal(TimeSpan.FromSeconds(1.0 / 1000.0), inspection.Duration);
    }

    [Fact]
    public void Inspect_InvalidTelemetryChunkLength_ReturnsMalformedInspection()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(Encoding.ASCII.GetBytes("SST"));
        writer.Write((byte)4);
        writer.Write((uint)0);
        writer.Write((long)123456789);

        writer.Write((byte)TlvChunkType.Rates);
        writer.Write((ushort)3);
        writer.Write((byte)TlvChunkType.Telemetry);
        writer.Write((ushort)1000);

        writer.Write((byte)TlvChunkType.Telemetry);
        writer.Write((ushort)5);
        writer.Write(new byte[] { 1, 2, 3, 4, 5 });

        ms.Position = 0;

        var result = RawTelemetryData.InspectStream(ms);

        var inspection = Assert.IsType<MalformedSstFileInspection>(result);
        Assert.Equal((byte)4, inspection.Version);
        Assert.NotNull(inspection.StartTime);
        Assert.NotEmpty(inspection.Message);
    }

    [Fact]
    public void Parse_InvalidTelemetryChunkLength_ThrowsFormatException()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(Encoding.ASCII.GetBytes("SST"));
        writer.Write((byte)4);
        writer.Write((uint)0);
        writer.Write((long)123456789);

        writer.Write((byte)TlvChunkType.Rates);
        writer.Write((ushort)3);
        writer.Write((byte)TlvChunkType.Telemetry);
        writer.Write((ushort)1000);

        writer.Write((byte)TlvChunkType.Telemetry);
        writer.Write((ushort)5);
        writer.Write(new byte[] { 1, 2, 3, 4, 5 });

        ms.Position = 0;

        Assert.Throws<FormatException>(() => RawTelemetryData.FromStream(ms));
    }
}
