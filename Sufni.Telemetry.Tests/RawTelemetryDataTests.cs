using Sufni.Telemetry;

namespace Sufni.Telemetry.Tests;

public class RawTelemetryDataTests
{
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

    [Theory]
    [InlineData(3, new ushort[] { 100, 110 }, new ushort[] { 200, 210 })]
    [InlineData(4, new ushort[] { 120, 130 }, new ushort[] { 220, 230 })]
    public void FromMemory_ArraySlice_ParsesOnlyTheLogicalBytes(int version, ushort[] expectedFront, ushort[] expectedRear)
    {
        var bytes = (version == 3 ? GoldenTelemetryCompatibilityFixtures.SstV3 : GoldenTelemetryCompatibilityFixtures.SstV4).GetBytes();
        var backing = Enumerable.Repeat((byte)0xFF, bytes.Length + 16).ToArray();
        bytes.CopyTo(backing, 7);

        var result = RawTelemetryData.FromMemory(backing.AsMemory(7, bytes.Length));

        Assert.Equal(version, result.Version);
        Assert.Equal(expectedFront, result.Front);
        Assert.Equal(expectedRear, result.Rear);
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
    }
}
