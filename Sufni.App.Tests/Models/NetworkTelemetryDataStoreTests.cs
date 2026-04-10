using System.Net;
using System.Text;
using Sufni.App.Models;

namespace Sufni.App.Tests.Models;

public class NetworkTelemetryDataStoreTests
{
    [Fact]
    public void ParseDirectoryListing_ParsesDurationMillisecondsFromEntries()
    {
        var boardBytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var expectedBoardId = UuidUtil.CreateDeviceUuid(boardBytes);

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write(boardBytes);
        writer.Write((ushort)1000);

        writer.Write(Encoding.ASCII.GetBytes("00001.SST"));
        writer.Write((ulong)1234);
        writer.Write((ulong)111);
        writer.Write((uint)6000);
        writer.Write((byte)4);

        writer.Write(Encoding.ASCII.GetBytes("00002.SST"));
        writer.Write((ulong)5678);
        writer.Write((ulong)222);
        writer.Write((uint)3000);
        writer.Write((byte)3);

        var listing = NetworkTelemetryDataStore.ParseDirectoryListing(ms.ToArray());

        Assert.Equal(expectedBoardId, listing.BoardId);
        Assert.Collection(
            listing.Entries,
            first =>
            {
                Assert.Equal("00001.SST", first.Name);
                Assert.Equal((ulong)1234, first.Size);
                Assert.Equal((ulong)111, first.Timestamp);
                Assert.Equal((uint)6000, first.DurationMilliseconds);
                Assert.Equal((byte)4, first.Version);
            },
            second =>
            {
                Assert.Equal("00002.SST", second.Name);
                Assert.Equal((ulong)5678, second.Size);
                Assert.Equal((ulong)222, second.Timestamp);
                Assert.Equal((uint)3000, second.DurationMilliseconds);
                Assert.Equal((byte)3, second.Version);
            });
    }

    [Fact]
    public void NetworkTelemetryFile_UsesProvidedDurationForPreviewState()
    {
        var endpoint = new IPEndPoint(IPAddress.Loopback, 5555);

        var importable = new NetworkTelemetryFile(endpoint, "00001.SST", 4, 111, TimeSpan.FromSeconds(6));
        var shortFile = new NetworkTelemetryFile(endpoint, "00002.SST", 3, 222, TimeSpan.FromSeconds(3));

        Assert.True(importable.ShouldBeImported);
        Assert.Equal((byte)4, importable.Version);
        Assert.Equal("00:00:06", importable.Duration);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(111).LocalDateTime, importable.StartTime);

        Assert.Null(shortFile.ShouldBeImported);
        Assert.Equal((byte)3, shortFile.Version);
        Assert.Equal("00:00:03", shortFile.Duration);
    }
}