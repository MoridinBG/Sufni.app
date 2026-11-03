using Avalonia.Platform.Storage;
using NSubstitute;
using static Sufni.App.Tests.TestSupport.Fixtures.TestStorageItems;

using Sufni.App.Acquisition.Models;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Acquisition.Models;

public class TelemetryFileInspectionMappingTests
{
    [Theory]
    [InlineData(TelemetryFileSourceKind.MassStorage, InspectionPayload.ValidV4WithUnknownChunk)]
    [InlineData(TelemetryFileSourceKind.StorageProvider, InspectionPayload.ValidV4WithUnknownChunk)]
    [InlineData(TelemetryFileSourceKind.MassStorage, InspectionPayload.MalformedV4)]
    [InlineData(TelemetryFileSourceKind.StorageProvider, InspectionPayload.MalformedV4)]
    [InlineData(TelemetryFileSourceKind.MassStorage, InspectionPayload.TrimmedV4)]
    [InlineData(TelemetryFileSourceKind.StorageProvider, InspectionPayload.TrimmedV4)]
    [InlineData(TelemetryFileSourceKind.MassStorage, InspectionPayload.InvalidMagic)]
    [InlineData(TelemetryFileSourceKind.StorageProvider, InspectionPayload.InvalidMagic)]
    [InlineData(TelemetryFileSourceKind.MassStorage, InspectionPayload.UnsupportedVersion)]
    [InlineData(TelemetryFileSourceKind.StorageProvider, InspectionPayload.UnsupportedVersion)]
    [InlineData(TelemetryFileSourceKind.MassStorage, InspectionPayload.InvalidTimestamp)]
    [InlineData(TelemetryFileSourceKind.StorageProvider, InspectionPayload.InvalidTimestamp)]
    public async Task TelemetryFile_MapsInspectionResult_ForSourceAndPayload(
        TelemetryFileSourceKind sourceKind,
        InspectionPayload payload)
    {
        var file = await CreateTelemetryFileAsync(sourceKind, payload);

        Assert.Equal(DateTimeKind.Utc, file.StartTime.Kind);

        switch (payload)
        {
            case InspectionPayload.ValidV4WithUnknownChunk:
                Assert.True(file.CanImport);
                Assert.Null(file.MalformedMessage);
                Assert.Equal("00:00:05", file.Duration);
                break;
            case InspectionPayload.MalformedV4:
            case InspectionPayload.InvalidMagic:
            case InspectionPayload.UnsupportedVersion:
            case InspectionPayload.InvalidTimestamp:
                Assert.False(file.CanImport);
                Assert.False(string.IsNullOrWhiteSpace(file.MalformedMessage));
                break;
            case InspectionPayload.TrimmedV4:
                Assert.True(file.CanImport);
                Assert.False(string.IsNullOrWhiteSpace(file.MalformedMessage));
                Assert.Equal("00:00:05", file.Duration);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(payload), payload, null);
        }
    }

    private static async Task<ITelemetryFile> CreateTelemetryFileAsync(
        TelemetryFileSourceKind sourceKind,
        InspectionPayload payload)
    {
        var bytes = payload switch
        {
            InspectionPayload.ValidV4WithUnknownChunk => TestSstFiles.CreateValidV4WithUnknownChunk(telemetrySampleCount: 5000),
            InspectionPayload.MalformedV4 => TestSstFiles.CreateMalformedV4WithInvalidTelemetryLength(),
            InspectionPayload.TrimmedV4 => TestSstFiles.CreateV4WithTelemetryChunkExtendingPastEnd(telemetrySampleCount: 5000),
            InspectionPayload.InvalidMagic => [0x01, 0x02],
            InspectionPayload.UnsupportedVersion => [(byte)'S', (byte)'S', (byte)'T', 99],
            InspectionPayload.InvalidTimestamp => TestSstFiles.CreateValidV3(timestamp: long.MaxValue),
            _ => throw new ArgumentOutOfRangeException(nameof(payload), payload, null)
        };
        var fileName = payload switch
        {
            InspectionPayload.MalformedV4 => "broken.SST",
            InspectionPayload.TrimmedV4 => "trimmed.SST",
            _ => "sample.SST"
        };

        if (sourceKind is TelemetryFileSourceKind.StorageProvider)
        {
            return await StorageProviderTelemetryFile.CreateAsync(BinaryFile(fileName, bytes));
        }

        using var tempDirectory = new TempDirectory("sufni-inspection-test");
        var path = Path.Combine(tempDirectory.Path, fileName);
        File.WriteAllBytes(path, bytes);
        return new MassStorageTelemetryFile(new FileInfo(path));
    }


    public enum TelemetryFileSourceKind
    {
        MassStorage,
        StorageProvider
    }

    public enum InspectionPayload
    {
        ValidV4WithUnknownChunk,
        MalformedV4,
        TrimmedV4,
        InvalidMagic,
        UnsupportedVersion,
        InvalidTimestamp
    }
}
