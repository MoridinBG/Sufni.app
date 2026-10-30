using Microsoft.Extensions.Time.Testing;
using Sufni.App.Tests.TestSupport.Async;
using SQLite;
using Sufni.Telemetry;

using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
namespace Sufni.App.Tests.TestSupport.Persistence;

internal static class PersistenceTestData
{
    public static RecordedSessionSource CreateRecordedSessionSource(Guid sessionId) => new()
    {
        SessionId = sessionId,
        SourceKind = RecordedSessionSourceKind.ImportedSst,
        SourceName = "source.SST",
        SchemaVersion = 1,
        SourceHash = RecordedSessionSourceHash.Compute(
            RecordedSessionSourceKind.ImportedSst,
            "source.SST",
            1,
            [1, 2, 3]),
        Payload = [1, 2, 3]
    };

    public static Track CreateFullTrack() => new()
    {
        Id = Guid.NewGuid(),
        Points =
        [
            new TrackPoint(100, 1, 1, 10),
            new TrackPoint(101, 2, 2, 11)
        ]
    };

    public static byte[] CreateTelemetryBlob(double durationSeconds) => new TelemetryData
    {
        Metadata = new Metadata
        {
            SourceName = "source.SST",
            Version = 4,
            SampleRate = 100,
            Timestamp = 100,
            Duration = durationSeconds
        }
    }.BinaryForm;

    public static FakeTimeProvider CreateTimeProvider() =>
        new(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));

    public static SqliteConnectionContext CreateConnectionContext(string databasePath, TimeProvider? timeProvider = null,
        IReadOnlyDictionary<Guid, int>? legacyProcessingWindows = null) =>
        new(databasePath, timeProvider ?? CreateTimeProvider(), new InlineBackgroundTaskRunner(), legacyProcessingWindows ?? new Dictionary<Guid, int>());
}

internal sealed class TableColumnInfo
{
    public string Name { get; set; } = string.Empty;
}

internal sealed class SqliteMasterRow
{
    public string Name { get; set; } = string.Empty;
}
