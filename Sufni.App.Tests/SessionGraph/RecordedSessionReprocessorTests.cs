using Sufni.App.Models;
using Sufni.App.Models.SensorConfigurations;
using Sufni.App.SessionGraph;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;

namespace Sufni.App.Tests.SessionGraph;

public class RecordedSessionReprocessorTests
{
    [Fact]
    public async Task ReprocessAsync_ImportedSst_DecompressesStoredPayloadBeforeParsing()
    {
        var session = TestSnapshots.Session(id: Guid.NewGuid(), setupId: Guid.NewGuid());
        var bike = TestSnapshots.Bike(id: Guid.NewGuid());
        var setup = TestSnapshots.Setup(id: session.SetupId!.Value, bikeId: bike.Id) with
        {
            FrontSensorConfigurationJson = SensorConfiguration.ToJson(new LinearForkSensorConfiguration
            {
                Length = 10,
                Resolution = 12
            })
        };
        var sstBytes = CreateSstV3Bytes();
        var payload = RecordedSessionSourcePayloadCodec.CompressImportedSst(sstBytes);
        var source = new RecordedSessionSource
        {
            SessionId = session.Id,
            SourceKind = RecordedSessionSourceKind.ImportedSst,
            SourceName = "compressed-source.SST",
            SchemaVersion = 1,
            SourceHash = RecordedSessionSourceHash.Compute(
                RecordedSessionSourceKind.ImportedSst,
                "compressed-source.SST",
                1,
                payload),
            Payload = payload
        };
        var domain = new RecordedSessionDomainSnapshot(
            session,
            setup,
            bike,
            null,
            null,
            RecordedSessionSourceSnapshot.From(source),
            new SessionStaleness.MissingProcessedData(),
            DerivedChangeKind.None);
        var reprocessor = new RecordedSessionReprocessor(new ProcessingFingerprintService());

        var result = await reprocessor.ReprocessAsync(domain, source);

        Assert.Equal("compressed-source.SST", result.TelemetryData.Metadata.SourceName);
        Assert.Equal(3, result.TelemetryData.Metadata.Version);
        Assert.NotEmpty(result.TelemetryData.Front.Travel);
    }

    private static byte[] CreateSstV3Bytes()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write("SST"u8);
        writer.Write((byte)3);
        writer.Write((ushort)100);
        writer.Write((ushort)0);
        writer.Write(1_700_000_000L);

        for (ushort sample = 0; sample < 64; sample++)
        {
            writer.Write((ushort)(1200 + sample));
            writer.Write(ushort.MaxValue);
        }

        return stream.ToArray();
    }
}
