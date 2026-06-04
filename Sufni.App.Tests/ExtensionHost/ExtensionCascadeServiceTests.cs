using SQLite;
using Sufni.App.ExtensionHost.Database;
using Sufni.App.Services;

namespace Sufni.App.Tests.ExtensionHost;

public class ExtensionCascadeServiceTests
{
    [Fact]
    public async Task ApplyForDeletedCoreEntityAsync_SoftDeletesRowsAndRefreshesParticipants()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"sufni-cascade-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var databasePath = Path.Combine(tempDirectory, "soft-cascade.db");
        var sessionId = Guid.NewGuid();
        var migrator = CreateMigrator([typeof(SoftCascadeRow)]);
        var provider = CreateProvider(new ExtensionCascadeRule(
            "test",
            ExtensionCoreEntityKind.Session,
            "soft_cascade_row",
            "session_id",
            ExtensionCascadeAction.SoftDelete));
        var refresh = new RecordingRefreshParticipant();

        try
        {
            var database = new SqLiteDatabaseService(databasePath, [migrator]);
            var connection = await database.GetInitializedConnectionAsync();
            await connection.InsertAsync(new SoftCascadeRow
            {
                Id = "soft",
                SessionId = sessionId,
            });
            var service = new ExtensionCascadeService(database, [migrator], [provider], [refresh]);

            await service.ApplyForDeletedCoreEntityAsync(ExtensionCoreEntityKind.Session, sessionId);

            var row = await connection.GetAsync<SoftCascadeRow>("soft");
            Assert.NotNull(row.Deleted);
            Assert.Equal(row.Deleted, row.Updated);
            Assert.Equal(1, refresh.RefreshCount);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ApplyForDeletedCoreEntityAsync_HardDeletesRows()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"sufni-cascade-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var databasePath = Path.Combine(tempDirectory, "hard-cascade.db");
        var trackId = Guid.NewGuid();
        var migrator = CreateMigrator([typeof(HardCascadeRow)]);
        var provider = CreateProvider(new ExtensionCascadeRule(
            "test",
            ExtensionCoreEntityKind.Track,
            "hard_cascade_row",
            "track_id",
            ExtensionCascadeAction.HardDelete));

        try
        {
            var database = new SqLiteDatabaseService(databasePath, [migrator]);
            var connection = await database.GetInitializedConnectionAsync();
            await connection.InsertAsync(new HardCascadeRow
            {
                Id = "hard",
                TrackId = trackId,
            });
            var service = new ExtensionCascadeService(database, [migrator], [provider], []);

            await service.ApplyForDeletedCoreEntityAsync(ExtensionCoreEntityKind.Track, trackId);

            Assert.Empty(await connection.Table<HardCascadeRow>().ToListAsync());
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Initialization_RepairsExtensionOrphansAfterStartupCleanup()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"sufni-cascade-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var databasePath = Path.Combine(tempDirectory, "orphan-repair.db");
        var missingSessionId = Guid.NewGuid();
        var refresh = new RecordingRefreshParticipant();
        var migrator = CreateMigrator(
            [typeof(SoftCascadeRow)],
            [
                new ExtensionDatabaseMigrationStep(1, async (context, _) =>
                {
                    await context.Connection.InsertAsync(new SoftCascadeRow
                    {
                        Id = "orphan",
                        SessionId = missingSessionId,
                    });
                }),
            ],
            targetVersion: 1);
        var provider = CreateProvider(new ExtensionCascadeRule(
            "test",
            ExtensionCoreEntityKind.Session,
            "soft_cascade_row",
            "session_id",
            ExtensionCascadeAction.SoftDelete));

        try
        {
            var database = new SqLiteDatabaseService(databasePath, [migrator], [provider], [refresh]);
            var connection = await database.GetInitializedConnectionAsync();

            var row = Assert.Single(await connection.Table<SoftCascadeRow>().ToListAsync());
            Assert.NotNull(row.Deleted);
            Assert.Equal(row.Deleted, row.Updated);
            Assert.Equal(1, refresh.RefreshCount);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ApplyForDeletedCoreEntityAsync_RejectsRulesForUndeclaredTables()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"sufni-cascade-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var databasePath = Path.Combine(tempDirectory, "invalid-cascade.db");
        var migrator = CreateMigrator([typeof(SoftCascadeRow)]);
        var provider = CreateProvider(new ExtensionCascadeRule(
            "test",
            ExtensionCoreEntityKind.Session,
            "not_owned",
            "session_id",
            ExtensionCascadeAction.SoftDelete));

        try
        {
            var database = new SqLiteDatabaseService(databasePath, [migrator]);
            _ = await database.GetInitializedConnectionAsync();
            var service = new ExtensionCascadeService(database, [migrator], [provider], []);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ApplyForDeletedCoreEntityAsync(ExtensionCoreEntityKind.Session, Guid.NewGuid()));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    private static TestExtensionMigrator CreateMigrator(
        IReadOnlyList<Type> tableTypes,
        IReadOnlyList<ExtensionDatabaseMigrationStep>? steps = null,
        int targetVersion = 0) =>
        new("test", targetVersion, tableTypes, steps ?? []);

    private static TestCascadeRuleProvider CreateProvider(params ExtensionCascadeRule[] rules) =>
        new(rules);

    [Table("soft_cascade_row")]
    private sealed class SoftCascadeRow
    {
        [PrimaryKey]
        [Column("id")]
        public string Id { get; set; } = string.Empty;

        [Column("session_id")]
        public Guid SessionId { get; set; }

        [Column("deleted")]
        public long? Deleted { get; set; }

        [Column("updated")]
        public long Updated { get; set; }
    }

    [Table("hard_cascade_row")]
    private sealed class HardCascadeRow
    {
        [PrimaryKey]
        [Column("id")]
        public string Id { get; set; } = string.Empty;

        [Column("track_id")]
        public Guid TrackId { get; set; }
    }

    private sealed class TestExtensionMigrator(
        string extensionId,
        int targetVersion,
        IReadOnlyList<Type> tableTypes,
        IReadOnlyList<ExtensionDatabaseMigrationStep> steps) : IExtensionDatabaseMigrator
    {
        public string ExtensionId { get; } = extensionId;
        public int TargetVersion { get; } = targetVersion;
        public IReadOnlyList<Type> TableTypes { get; } = tableTypes;
        public IReadOnlyList<ExtensionDatabaseMigrationStep> Steps { get; } = steps;
    }

    private sealed class TestCascadeRuleProvider(IReadOnlyList<ExtensionCascadeRule> rules)
        : IExtensionCascadeRuleProvider
    {
        public IReadOnlyList<ExtensionCascadeRule> Rules { get; } = rules;
    }

    private sealed class RecordingRefreshParticipant : IExtensionStateRefreshParticipant
    {
        public int RefreshCount { get; private set; }

        public Task RefreshExtensionStateAsync(CancellationToken cancellationToken = default)
        {
            RefreshCount++;
            return Task.CompletedTask;
        }
    }
}

