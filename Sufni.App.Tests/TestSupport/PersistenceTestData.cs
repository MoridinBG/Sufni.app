using SQLite;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.Telemetry;

using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
namespace Sufni.App.Tests.TestSupport;

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

    public static SqliteConnectionContext CreateConnectionContext(
        string databasePath,
        IEnumerable<IExtensionDatabaseMigrator> extensionMigrators) =>
        new(
            databasePath,
            createAppDirectories: false,
            extensionMigrators,
            extensionCascadeRuleProviders: [],
            extensionStateRefreshParticipantsProvider: () => []);
}

internal sealed class TableColumnInfo
{
    public string Name { get; set; } = string.Empty;
}

internal sealed class SqliteMasterRow
{
    public string Name { get; set; } = string.Empty;
}

[Table("test_extension_row")]
internal sealed class TestExtensionRow
{
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = string.Empty;

    [Column("value")]
    public int Value { get; set; }
}

[Table("second_test_extension_row")]
internal sealed class SecondTestExtensionRow
{
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = string.Empty;
}

[Table("test_extension_row")]
internal sealed class DuplicateNamedExtensionRow
{
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = string.Empty;
}

[Table("session")]
internal sealed class CoreNamedExtensionRow
{
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = string.Empty;
}
