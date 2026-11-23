
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.Telemetry.Tests;
namespace Sufni.App.Tests.Sessions.Models;

public class RecordedSessionSourcePayloadCodecTests
{
    // The golden SST v5 fixture as ZstdSharp.Port 0.8.8 compressed it at level 3,
    // the format that earlier app versions stored and synchronized.
    private const string EarlierVersionSstV5Payload =
        "KLUv/WCEAK0FADLIGSaQRWkN6/2X8znPzIRDIo+8uuovAChQ8+3iYaVSGCzfljxpZEuZAtvI4rNXoYeGhU9C52BzvSYRvBxo+Rh/HmuaIBNo5+xusxZNFIxeMwoGetZcuhCO1zNzNnfPtAqAgCTgikZFUQEjIKCCISQ7EMC88gYO124eEL+xKQatJBuZQMlu5DWz5EEDdwEvgkro754WDmxZ5piwyLJHg3FGaMeNL8wubdTJieW70Ah53gJFqAM=";

    [Fact]
    public async Task LiveCapturePayload_PreservesFrozenJsonBytes()
    {
        var expected = await File.ReadAllBytesAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Codec", "live-capture-v1.json"),
            TestContext.Current.CancellationToken);
        var source = RecordedSessionSourceFactory.CreateLiveCapture(TelemetryCodecFixtureData.LiveCapture());

        Assert.Equal(expected, source.Payload);
        Assert.Equal("8d48096e0b4ef640b5d38f908494629944ee9bb64cefbce12a36828dcaba563a", source.SourceHash);
    }

    [Fact]
    public void ImportedSstPayload_CompressesOnlyTheLogicalMemoryRange()
    {
        var backingBytes = new byte[] { 99, 1, 2, 3, 88 };

        var compressed = RecordedSessionSourcePayloadCodec.CompressImportedSst(
            backingBytes.AsMemory(1, 3));
        var decompressed = RecordedSessionSourcePayloadCodec.DecompressImportedSst(compressed);

        Assert.Equal(new byte[] { 1, 2, 3 }, decompressed);
    }

    [Fact]
    public void ImportedSstPayload_DecodesPayloadStoredByEarlierVersions()
    {
        var decompressed = RecordedSessionSourcePayloadCodec.DecompressImportedSst(
            Convert.FromBase64String(EarlierVersionSstV5Payload));

        Assert.Equal(GoldenTelemetryCompatibilityFixtures.SstV5.GetBytes(), decompressed);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(100)]
    public void ImportedSstPayload_RejectsTruncatedPayload(int keptBytes)
    {
        var payload = Convert.FromBase64String(EarlierVersionSstV5Payload)[..keptBytes];

        Assert.Throws<InvalidDataException>(() => RecordedSessionSourcePayloadCodec.DecompressImportedSst(payload));
    }
}
