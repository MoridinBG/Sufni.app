using SQLite;
using Sufni.App.ExtensionHost.Contracts.Database;

using Sufni.App.Infrastructure;
using Sufni.App.Extensibility.Database;
using Sufni.App.Tests.TestSupport.Persistence;
namespace Sufni.App.Tests.Extensibility.Database;

public class ExtensionCascadeServiceTests
{
    [Fact]
    public async Task ApplyRulesForDeletedCoreEntityInTransaction_SoftDeletesRows_AndRefreshesParticipantsAfterCommit()
    {
        using var tempDirectory = new TempDirectory("sufni-cascade-test");
        var databasePath = Path.Combine(tempDirectory.Path, "soft-cascade.db");
        var sessionId = Guid.NewGuid();
        var migrator = CreateMigrator([typeof(SoftCascadeRow)]);
        var provider = CreateProvider(new ExtensionCascadeRule(
            "test",
            ExtensionCoreEntityKind.Session,
            "soft_cascade_row",
            "session_id",
            ExtensionCascadeAction.SoftDelete));
        var refresh = new RecordingRefreshParticipant();

        var context = CreateConnectionContext(databasePath, [migrator]);
        var connection = await context.GetInitializedConnectionAsync();
        await connection.InsertAsync(new SoftCascadeRow
        {
            Id = "soft",
            SessionId = sessionId,
        });
        var service = new ExtensionCascadeService(context, [migrator], [provider], [refresh]);

        var applied = false;
        await context.RunInTransactionAsync(transaction =>
        {
            applied = service.ApplyRulesForDeletedCoreEntityInTransaction(
                transaction,
                ExtensionCoreEntityKind.Session,
                sessionId);
        });
        await service.RefreshExtensionStateAsync();

        Assert.True(applied);
        var row = await connection.GetAsync<SoftCascadeRow>("soft");
        Assert.NotNull(row.Deleted);
        Assert.Equal(row.Deleted, row.Updated);
        Assert.Equal(1, refresh.RefreshCount);
    }

    [Fact]
    public async Task ApplyRulesForDeletedCoreEntityInTransaction_HardDeletesRows()
    {
        using var tempDirectory = new TempDirectory("sufni-cascade-test");
        var databasePath = Path.Combine(tempDirectory.Path, "hard-cascade.db");
        var trackId = Guid.NewGuid();
        var migrator = CreateMigrator([typeof(HardCascadeRow)]);
        var provider = CreateProvider(new ExtensionCascadeRule(
            "test",
            ExtensionCoreEntityKind.Track,
            "hard_cascade_row",
            "track_id",
            ExtensionCascadeAction.HardDelete));

        var context = CreateConnectionContext(databasePath, [migrator]);
        var connection = await context.GetInitializedConnectionAsync();
        await connection.InsertAsync(new HardCascadeRow
        {
            Id = "hard",
            TrackId = trackId,
        });
        var service = new ExtensionCascadeService(context, [migrator], [provider], []);

        var applied = false;
        await context.RunInTransactionAsync(transaction =>
        {
            applied = service.ApplyRulesForDeletedCoreEntityInTransaction(
                transaction,
                ExtensionCoreEntityKind.Track,
                trackId);
        });

        Assert.True(applied);
        Assert.Empty(await connection.Table<HardCascadeRow>().ToListAsync());
    }

    [Fact]
    public async Task Initialization_RepairsExtensionOrphansAfterStartupCleanupWithoutRefreshingParticipants()
    {
        using var tempDirectory = new TempDirectory("sufni-cascade-test");
        var databasePath = Path.Combine(tempDirectory.Path, "orphan-repair.db");
        var missingSessionId = Guid.NewGuid();
        var refreshProviderWasResolved = false;
        var migrator = CreateMigrator(
            [typeof(SoftCascadeRow)],
            [
                new ExtensionDatabaseMigrationStep(1, async (context, _) =>
                {
                    await context.Database.InsertAsync(new SoftCascadeRow
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

        var context = CreateConnectionContext(
            databasePath,
            [migrator],
            [provider],
            () =>
            {
                refreshProviderWasResolved = true;
                return [new RecordingRefreshParticipant()];
            });
        var connection = await context.GetInitializedConnectionAsync();

        var row = Assert.Single(await connection.Table<SoftCascadeRow>().ToListAsync());
        Assert.NotNull(row.Deleted);
        Assert.Equal(row.Deleted, row.Updated);
        Assert.False(refreshProviderWasResolved);
    }

    [Fact]
    public async Task Constructor_RejectsRulesForUndeclaredTables()
    {
        using var tempDirectory = new TempDirectory("sufni-cascade-test");
        var databasePath = Path.Combine(tempDirectory.Path, "invalid-cascade.db");
        var migrator = CreateMigrator([typeof(SoftCascadeRow)]);
        var provider = CreateProvider(new ExtensionCascadeRule(
            "test",
            ExtensionCoreEntityKind.Session,
            "not_owned",
            "session_id",
            ExtensionCascadeAction.SoftDelete));

        var context = CreateConnectionContext(databasePath, [migrator]);
        _ = await context.GetInitializedConnectionAsync();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ExtensionCascadeService(context, [migrator], [provider], []));

        Assert.Contains("not_owned", exception.Message);
    }

    [Fact]
    public async Task Constructor_RejectsRulesForTablesOwnedByAnotherExtension()
    {
        using var tempDirectory = new TempDirectory("sufni-cascade-test");
        var databasePath = Path.Combine(tempDirectory.Path, "wrong-owner-cascade.db");
        var migrator = new TestExtensionMigrator(
            "owner",
            targetVersion: 0,
            [typeof(SoftCascadeRow)],
            []);
        var provider = CreateProvider(new ExtensionCascadeRule(
            "other",
            ExtensionCoreEntityKind.Session,
            "soft_cascade_row",
            "session_id",
            ExtensionCascadeAction.SoftDelete));

        var context = CreateConnectionContext(databasePath, [migrator]);
        _ = await context.GetInitializedConnectionAsync();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ExtensionCascadeService(context, [migrator], [provider], []));

        Assert.Contains("other", exception.Message);
        Assert.Contains("owner", exception.Message);
    }

    private static TestExtensionMigrator CreateMigrator(
        IReadOnlyList<Type> tableTypes,
        IReadOnlyList<ExtensionDatabaseMigrationStep>? steps = null,
        int targetVersion = 0) =>
        new("test", targetVersion, tableTypes, steps ?? []);

    private static TestCascadeRuleProvider CreateProvider(params ExtensionCascadeRule[] rules) =>
        new(rules);

    private static SqliteConnectionContext CreateConnectionContext(
        string databasePath,
        IEnumerable<IExtensionDatabaseMigrator> migrators,
        IEnumerable<IExtensionCascadeRuleProvider>? ruleProviders = null,
        Func<IReadOnlyList<IExtensionStateRefreshParticipant>>? refreshParticipantsProvider = null) =>
        new(
            databasePath,
            createAppDirectories: false,
            migrators,
            ruleProviders ?? [],
            refreshParticipantsProvider ?? (() => []));

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
