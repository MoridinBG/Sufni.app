using Sufni.Telemetry;

namespace Sufni.Telemetry.Tests;

public class RawTelemetryDataTests
{
    [Fact]
    public void FromStream_V3File_RoutesToV3Parser()
    {
        using var ms = SstTestFiles.CreateV3Stream(timestamp: 99999, samples: [(100, 200)]);

        // Act
        var result = RawTelemetryData.FromStream(ms);

        // Assert
        Assert.Equal(3, result.Version);
        Assert.Equal(1000, result.SampleRate);
        Assert.Equal(99999, result.Timestamp);
        Assert.Single(result.Front);
        Assert.Equal(100, result.Front[0]);
        Assert.Empty(result.Markers);
    }

    [Fact]
    public void FromStream_V4File_RoutesToTlvParser()
    {
        using var ms = SstTestFiles.CreateV4Stream(
            88888,
            SstTestFiles.Rates((TlvChunkType.Telemetry, 1000)),
            SstTestFiles.Telemetry((123, 456)));

        // Act
        var result = RawTelemetryData.FromStream(ms);

        // Assert
        Assert.Equal(4, result.Version);
        Assert.Equal(1000, result.SampleRate);
        Assert.Equal(88888, result.Timestamp);
    }

    [Fact]
    public void FromStream_V3File_Preserves64BitTimestamp()
    {
        using var ms = SstTestFiles.CreateV3Stream(timestamp: 3_000_000_000L, samples: [(100, 200)]);

        var result = RawTelemetryData.FromStream(ms);

        Assert.Equal(3_000_000_000L, result.Timestamp);
    }

    [Fact]
    public void FromStream_V4File_Preserves64BitTimestamp()
    {
        using var ms = SstTestFiles.CreateV4Stream(
            3_000_000_000L,
            SstTestFiles.Rates((TlvChunkType.Telemetry, 1000)),
            SstTestFiles.Telemetry((123, 456)));

        var result = RawTelemetryData.FromStream(ms);

        Assert.Equal(3_000_000_000L, result.Timestamp);
    }

    [Fact]
    public void FromStream_V3File_PreservesSamplesForProcessingStage()
    {
        // Spike: 100, 100, 100, 2000, 2000, 2000, 100, 100, 100...
        // The detector looks for sudden jumps.
        var samples = Enumerable.Repeat(((ushort)100, (ushort)100), 100)
            .Append(((ushort)2000, (ushort)2000))
            .Concat(Enumerable.Repeat(((ushort)100, (ushort)100), 100))
            .ToArray();
        using var ms = SstTestFiles.CreateV3Stream(timestamp: 0, samples: samples);

        // Act
        var result = RawTelemetryData.FromStream(ms);

        Assert.Contains((ushort)2000, result.Front);
        Assert.Equal(0, result.FrontAnomalyRate);
    }
}
