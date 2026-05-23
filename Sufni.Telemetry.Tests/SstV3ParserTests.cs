using Sufni.Telemetry;

namespace Sufni.Telemetry.Tests;

public class SstV3ParserTests
{
    [Fact]
    public void Parse_ValidV3File_ReturnsCorrectData()
    {
        using var ms = SstTestFiles.CreateV3ParserStream(samples: [(100, 200), (110, 210)]);
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
        using var ms = SstTestFiles.CreateV3ParserStream(timestamp: 0);
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
        using var ms = SstTestFiles.CreateV3ParserStream(samples: [(ushort.MaxValue, 200), (ushort.MaxValue, 210)]);
        using var reader = new BinaryReader(ms);

        var parser = new SstV3Parser();

        var result = parser.Parse(reader, 3);

        Assert.Empty(result.Front);
        Assert.Equal([200, 210], result.Rear);
    }

    [Fact]
    public void Parse_WithAbsentRearChannel_LeavesRearEmpty()
    {
        using var ms = SstTestFiles.CreateV3ParserStream(samples: [(100, ushort.MaxValue), (110, ushort.MaxValue)]);
        using var reader = new BinaryReader(ms);

        var parser = new SstV3Parser();

        var result = parser.Parse(reader, 3);

        Assert.Equal([100, 110], result.Front);
        Assert.Empty(result.Rear);
    }

    [Fact]
    public void Parse_PreservesHighAdcSamples_WithoutSignedWrap()
    {
        ushort[] front = [2500, 2510, 2520, 2530, 2540];
        ushort[] rear = [3000, 3010, 3020, 3030, 3040];

        using var ms = SstTestFiles.CreateV3ParserStream(
            samples: front.Zip(rear, (frontSample, rearSample) => (frontSample, rearSample)).ToArray());
        using var reader = new BinaryReader(ms);

        var result = new SstV3Parser().Parse(reader, 3);

        Assert.Equal(front, result.Front);
        Assert.Equal(rear, result.Rear);
    }

    [Fact]
    public void Parse_PreservesOutOfRangePayloadSamples_ForProcessingStage()
    {
        ushort[] front = [1500, 1500, 1500, 0xFFFE, 1500, 1500, 1500];
        ushort[] rear = [1600, 1600, 1600, 0x8000, 1600, 1600, 1600];

        using var ms = SstTestFiles.CreateV3ParserStream(
            samples: front.Zip(rear, (frontSample, rearSample) => (frontSample, rearSample)).ToArray());
        using var reader = new BinaryReader(ms);

        var result = new SstV3Parser().Parse(reader, 3);

        Assert.Equal(front, result.Front);
        Assert.Equal(rear, result.Rear);
    }
}
