using NSubstitute;
using Sufni.App.Models;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Infrastructure;

public static class TestTelemetryFactories
{
    public static ITelemetryDataStore CreateDataStore(string name = "store", Guid? boardId = null)
    {
        var dataStore = Substitute.For<ITelemetryDataStore>();
        dataStore.Name.Returns(name);
        dataStore.BoardId.Returns(boardId);
        return dataStore;
    }

    public static ITelemetryFile CreateTelemetryFile(
        string name = "file",
        string description = "",
        DateTime? startTime = null,
        string duration = "1s",
        bool? shouldBeImported = true,
        byte version = 4,
        string? malformedMessage = null,
        bool? canImport = null,
        bool hasUnknown = false,
        byte[]? sourceBytes = null)
    {
        var telemetryFile = Substitute.For<ITelemetryFile>();
        telemetryFile.Name.Returns(name);
        telemetryFile.FileName.Returns($"{name}.SST");
        telemetryFile.Description.Returns(description);
        telemetryFile.Version.Returns(version);
        telemetryFile.StartTime.Returns(startTime ?? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        telemetryFile.Duration.Returns(duration);
        telemetryFile.ShouldBeImported.Returns(shouldBeImported);
        telemetryFile.MalformedMessage.Returns(malformedMessage);
        telemetryFile.CanImport.Returns(canImport ?? string.IsNullOrWhiteSpace(malformedMessage));
        telemetryFile.HasUnknown.Returns(hasUnknown);
        telemetryFile.ReadSourceAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new TelemetryFileSource($"{name}.SST", sourceBytes ?? [1, 2, 3])));
        return telemetryFile;
    }

    public static TelemetryData CreateTelemetryDataWithImu(
        IReadOnlyList<byte>? activeLocations = null,
        IReadOnlyList<ImuMetaEntry>? meta = null,
        IReadOnlyList<ImuRecord>? records = null,
        double duration = 2.0,
        int sampleRate = 100)
    {
        return new TelemetryData
        {
            Metadata = new Metadata
            {
                Duration = duration,
                SampleRate = sampleRate,
            },
            Front = new Suspension
            {
                Present = true,
                Travel = [0.0],
                Velocity = [0.0],
                Strokes = new Strokes()
            },
            Rear = new Suspension
            {
                Present = true,
                Travel = [0.0],
                Velocity = [0.0],
                Strokes = new Strokes()
            },
            Airtimes = [],
            ImuData = new RawImuData
            {
                SampleRate = sampleRate,
                ActiveLocations = activeLocations?.ToList() ?? [0, 1],
                Meta = meta?.ToList() ??
                [
                    new ImuMetaEntry(0, 1.0f, 1.0f),
                    new ImuMetaEntry(1, 1.0f, 1.0f)
                ],
                Records = records?.ToList() ??
                [
                    new ImuRecord(1, 0, 1, 0, 0, 0),
                    new ImuRecord(2, 0, 1, 0, 0, 0),
                    new ImuRecord(3, 0, 1, 0, 0, 0),
                    new ImuRecord(4, 0, 1, 0, 0, 0)
                ]
            }
        };
    }

    public static TelemetryData CreateTelemetryData(
        double duration = 2.0,
        int sampleRate = 2)
    {
        return new TelemetryData
        {
            Metadata = new Metadata
            {
                Duration = duration,
                SampleRate = sampleRate,
            },
            Front = new Suspension
            {
                Present = true,
                MaxTravel = 170.0,
                Travel = [0.0, 25.0, 50.0, 75.0],
                Velocity = [100.0, -50.0, 25.0, 0.0],
                Strokes = new Strokes()
            },
            Rear = new Suspension
            {
                Present = true,
                MaxTravel = 160.0,
                Travel = [0.0, 20.0, 40.0, 60.0],
                Velocity = [80.0, -40.0, 20.0, 0.0],
                Strokes = new Strokes()
            },
            Airtimes = []
        };
    }
}
