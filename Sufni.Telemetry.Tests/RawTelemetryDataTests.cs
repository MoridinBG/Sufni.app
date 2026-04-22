using System.Text;
using Sufni.Telemetry;

namespace Sufni.Telemetry.Tests;

public class RawTelemetryDataTests
{
    [Fact]
    public void FromStream_V3File_RoutesToV3Parser()
    {
        // Arrange
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write(Encoding.ASCII.GetBytes("SST"));
        writer.Write((byte)3);
        writer.Write((ushort)1000);
        writer.Write((ushort)0); // padding
        writer.Write((long)99999);
        writer.Write((ushort)100); // Front
        writer.Write((ushort)200); // Rear

        ms.Position = 0;

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
        // Arrange
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write(Encoding.ASCII.GetBytes("SST"));
        writer.Write((byte)4);
        writer.Write((uint)0); // padding
        writer.Write((long)88888);

        // Telemetry rate
        writer.Write((byte)0x00);
        writer.Write((ushort)3);
        writer.Write((byte)0x01);
        writer.Write((ushort)1000);

        // Minimal telemetry chunk
        writer.Write((byte)0x01);
        writer.Write((ushort)4);
        writer.Write((ushort)123);
        writer.Write((ushort)456);

        ms.Position = 0;

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
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write(Encoding.ASCII.GetBytes("SST"));
        writer.Write((byte)3);
        writer.Write((ushort)1000);
        writer.Write((ushort)0);
        writer.Write(3_000_000_000L);
        writer.Write((ushort)100);
        writer.Write((ushort)200);

        ms.Position = 0;

        var result = RawTelemetryData.FromStream(ms);

        Assert.Equal(3_000_000_000L, result.Timestamp);
    }

    [Fact]
    public void FromStream_V4File_Preserves64BitTimestamp()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write(Encoding.ASCII.GetBytes("SST"));
        writer.Write((byte)4);
        writer.Write((uint)0);
        writer.Write(3_000_000_000L);
        writer.Write((byte)0x00);
        writer.Write((ushort)3);
        writer.Write((byte)0x01);
        writer.Write((ushort)1000);
        writer.Write((byte)0x01);
        writer.Write((ushort)4);
        writer.Write((ushort)123);
        writer.Write((ushort)456);

        ms.Position = 0;

        var result = RawTelemetryData.FromStream(ms);

        Assert.Equal(3_000_000_000L, result.Timestamp);
    }

    [Fact]
    public void SpikeElimination_AppliedDuringParsing()
    {
        // Arrange
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write(Encoding.ASCII.GetBytes("SST"));
        writer.Write((byte)3);
        writer.Write((ushort)1000);
        writer.Write((ushort)0); // padding
        writer.Write((long)0);

        // Spike: 100, 100, 100, 2000, 2000, 2000, 100, 100, 100...
        // The detector looks for sudden jumps.
        for (int i = 0; i < 100; i++)
        {
            writer.Write((ushort)100); // Stable baseline
            writer.Write((ushort)100);
        }

        // Sudden jump (Spike)
        writer.Write((ushort)2000);
        writer.Write((ushort)2000);

        for (int i = 0; i < 100; i++)
        {
            writer.Write((ushort)100);
            writer.Write((ushort)100);
        }

        ms.Position = 0;

        // Act
        var result = RawTelemetryData.FromStream(ms);

        // Assert
        // If spike elimination worked, the 2000 should be smoothed out or identified as anomaly
        Assert.True(result.FrontAnomalyRate > 0);
        // Note: SpikeElimination behavior depends on thresholds, but here we just verify it was CALLED.
    }
}
