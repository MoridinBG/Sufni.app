using System.Text;
using Sufni.Telemetry;

namespace Sufni.Telemetry.Tests;

public class SstV3ParserTests
{
    [Fact]
    public void Parse_ValidV3File_ReturnsCorrectData()
    {
        // Arrange
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // Header
        writer.Write(Encoding.ASCII.GetBytes("SST"));
        writer.Write((byte)3);
        writer.Write((ushort)1000); // Sample rate
        writer.Write((ushort)0); // Padding
        writer.Write((long)123456789); // Timestamp

        // Data (2 records)
        writer.Write((ushort)100); // Front 1
        writer.Write((ushort)200); // Rear 1
        writer.Write((ushort)110); // Front 2
        writer.Write((ushort)210); // Rear 2

        ms.Position = 4; // Skip Magic+Version
        using var reader = new BinaryReader(ms);

        var parser = new SstV3Parser();

        // Act
        var result = parser.Parse(reader, 3);

        // Assert
        Assert.Equal(3, result.Version);
        Assert.Equal(1000, result.SampleRate);
        Assert.Equal(123456789, result.Timestamp);
        Assert.Equal(2, result.Front.Length);
        Assert.Equal(2, result.Rear.Length);
        Assert.Empty(result.Markers);
        Assert.Null(result.ImuData);
    }
    
    [Fact]
    public void Parse_V3FileWithEmptyData_ReturnsEmptyArrays()
    {
        // Arrange
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // Header Prefix
        writer.Write(Encoding.ASCII.GetBytes("SST"));
        writer.Write((byte)3);

        // Header Remainder
        writer.Write((ushort)1000);
        writer.Write((ushort)0);
        writer.Write((long)0);

        ms.Position = 4;
        using var reader = new BinaryReader(ms);

        var parser = new SstV3Parser();

        // Act
        var result = parser.Parse(reader, 3);

        // Assert
        Assert.Empty(result.Front);
        Assert.Empty(result.Rear);
    }

    [Fact]
    public void Parse_WithAbsentFrontChannel_LeavesFrontEmpty()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(Encoding.ASCII.GetBytes("SST"));
        writer.Write((byte)3);
        writer.Write((ushort)1000);
        writer.Write((ushort)0);
        writer.Write((long)123456789);

        writer.Write(ushort.MaxValue);
        writer.Write((ushort)200);
        writer.Write(ushort.MaxValue);
        writer.Write((ushort)210);

        ms.Position = 4;
        using var reader = new BinaryReader(ms);

        var parser = new SstV3Parser();

        var result = parser.Parse(reader, 3);

        Assert.Empty(result.Front);
        Assert.Equal([200, 210], result.Rear);
    }

    [Fact]
    public void Parse_WithAbsentRearChannel_LeavesRearEmpty()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(Encoding.ASCII.GetBytes("SST"));
        writer.Write((byte)3);
        writer.Write((ushort)1000);
        writer.Write((ushort)0);
        writer.Write((long)123456789);

        writer.Write((ushort)100);
        writer.Write(ushort.MaxValue);
        writer.Write((ushort)110);
        writer.Write(ushort.MaxValue);

        ms.Position = 4;
        using var reader = new BinaryReader(ms);

        var parser = new SstV3Parser();

        var result = parser.Parse(reader, 3);

        Assert.Equal([100, 110], result.Front);
        Assert.Empty(result.Rear);
    }
}
