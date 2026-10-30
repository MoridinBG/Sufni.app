using System.Reactive.Linq;
using NSubstitute;
using SQLite;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Shell.Coordinators;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.Tests.TestSupport.Persistence;

namespace Sufni.App.Tests.Shell.Coordinators;

public class AppStartupTests
{
    [Fact]
    public async Task Start_WaitsForDatabaseAndRefresh_BeforeCreatingPagesAndBecomingReady()
    {
        var migration = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var database = Substitute.For<IStartupDatabase>();
        database.Initialization.Returns(migration.Task);
        var refresher = Substitute.For<IAppDataRefresher>();
        refresher.RefreshAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            refreshStarted.SetResult();
            return refresh.Task;
        });
        using var startup = Create(database, refresher);
        var created = 0;
        var states = new List<ShellStartupState>();
        using var subscription = startup.Observe().Subscribe(states.Add);

        var run = startup.StartAsync(() => created++);
        Assert.Same(run, startup.StartAsync(() => created++));
        Assert.Equal(0, created);
        migration.SetResult();
        await refreshStarted.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, created);
        Assert.IsType<ShellStartupState.Starting>(startup.State);

        refresh.SetResult();
        await run.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, created);
        Assert.Collection(states,
            value => Assert.IsType<ShellStartupState.Starting>(value),
            value => Assert.IsType<ShellStartupState.Ready>(value));
    }

    [Fact]
    public async Task MigrationFailure_BlocksPagesAndServer_AndResetRequiresRelaunchWithNewIdentity()
    {
        using var temp = new TempDatabase();
        string originalIdentity;
        await using (var original = PersistenceTestData.CreateConnectionContext(temp.DatabasePath))
        {
            await original.Initialization;
            originalIdentity = await original.Connection.ExecuteScalarAsync<string>("SELECT db_incarnation FROM database_identity");
        }

        using (var damaged = new SQLiteConnection(temp.DatabasePath))
        {
            damaged.Execute("DROP TABLE core_migration");
            damaged.Execute("CREATE VIEW core_migration AS SELECT 'broken' AS id");
        }

        await using var context = PersistenceTestData.CreateConnectionContext(temp.DatabasePath);
        var server = Substitute.For<IPairingServerCoordinator>();
        using var startup = Create(context, server: server);
        var pagesCreated = 0;
        await startup.StartAsync(() => pagesCreated++);
        Assert.Equal(StartupFailureReason.Database, Assert.IsType<ShellStartupState.Failed>(startup.State).Reason);
        Assert.Equal(0, pagesCreated);
        await server.DidNotReceive().StartServerAsync();

        var reset = Assert.IsType<DatabaseResetResult.Reset>(await startup.ResetDatabaseAsync());

        Assert.IsType<ShellStartupState.RelaunchRequired>(startup.State);
        Assert.Equal(0, pagesCreated);
        Assert.True(context.Initialization.IsFaulted);
        using var archive = new SQLiteConnection(Path.Combine(reset.ArchiveDirectory, Path.GetFileName(temp.DatabasePath)));
        using var fresh = new SQLiteConnection(temp.DatabasePath);
        Assert.Equal(originalIdentity, archive.ExecuteScalar<string>("SELECT db_incarnation FROM database_identity"));
        Assert.NotEqual(originalIdentity, fresh.ExecuteScalar<string>("SELECT db_incarnation FROM database_identity"));
    }

    [Fact]
    public async Task Stop_CancelsRefreshWithoutCreatingPagesOrPublishingFailure()
    {
        var enteredRefresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refresher = Substitute.For<IAppDataRefresher>();
        refresher.RefreshAsync(Arg.Any<CancellationToken>()).Returns(call =>
        {
            enteredRefresh.SetResult();
            return new TaskCompletionSource().Task.WaitAsync(call.Arg<CancellationToken>());
        });
        using var startup = Create(refresher: refresher);
        var created = false;
        var run = startup.StartAsync(() => created = true);
        await enteredRefresh.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        await startup.StopAsync().AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        await run;

        Assert.False(created);
        Assert.IsType<ShellStartupState.Starting>(startup.State);
    }

    [Fact]
    public async Task Ready_StartsOwnedRepairJobs_AndStopWaitsForCancellation()
    {
        var job = new CancellableJob();
        using var startup = Create(jobs: [job]);
        await startup.StartAsync(() => { });
        Assert.IsType<ShellStartupState.Ready>(startup.State);
        Assert.True(job.Started);

        await startup.StopAsync().AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.True(job.Stopped);
    }

    [Fact]
    public async Task ResetFailure_RemainsBlockedAndAllowsAnotherReset()
    {
        var database = Substitute.For<IStartupDatabase>();
        database.Initialization.Returns(Task.FromException(new IOException()));
        database.ArchiveAndResetAsync().Returns(Task.FromException<string>(new IOException()), Task.FromResult("archive"));
        using var startup = Create(database);
        await startup.StartAsync(() => throw new InvalidOperationException());

        Assert.IsType<DatabaseResetResult.Failed>(await startup.ResetDatabaseAsync());
        Assert.Equal(StartupFailureReason.Reset, Assert.IsType<ShellStartupState.Failed>(startup.State).Reason);
        Assert.IsType<DatabaseResetResult.Reset>(await startup.ResetDatabaseAsync());
        Assert.IsType<ShellStartupState.RelaunchRequired>(startup.State);
    }

    private static AppStartup Create(
        IStartupDatabase? database = null,
        IAppDataRefresher? refresher = null,
        IPairingServerCoordinator? server = null,
        IStartupJob[]? jobs = null)
    {
        if (database is null)
        {
            database = Substitute.For<IStartupDatabase>();
            database.Initialization.Returns(Task.CompletedTask);
        }

        return new AppStartup(database, refresher ?? Substitute.For<IAppDataRefresher>(),
            Substitute.For<ITileLayerService>(), new InlineUiThreadDispatcher(), new InlineBackgroundTaskRunner(),
            Substitute.For<IAppPaths>(), jobs ?? [], server);
    }

    private sealed class CancellableJob : IStartupJob
    {
        public bool Started { get; private set; }
        public bool Stopped { get; private set; }
        public async Task RunAsync(CancellationToken cancellationToken)
        {
            Started = true;
            try { await new TaskCompletionSource().Task.WaitAsync(cancellationToken); }
            finally { Stopped = true; }
        }
    }
}
