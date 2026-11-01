using Sufni.App.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Services;

namespace Sufni.App.Shell.Coordinators;

internal sealed class AppStartup(
    IStartupDatabase database,
    IAppDataRefresher refresher,
    ITileLayerService tileLayers,
    IUiThreadDispatcher dispatcher,
    IBackgroundTaskRunner backgroundTaskRunner,
    IAppPaths paths,
    IAppPreferences preferences,
    IEnumerable<IStartupJob> jobs,
    IPairingServerCoordinator? server = null) : IAppStartup, IDisposable
{
    private static readonly ILogger logger = Log.ForContext<AppStartup>();
    private readonly BehaviorSubject<ShellStartupState> state = new(new ShellStartupState.Starting());
    private readonly CancellationTokenSource cancellation = new();
    private Task? startupTask;
    private Task<DatabaseResetResult>? resetTask;
    private Task repairTask = Task.CompletedTask;

    public ShellStartupState State => state.Value;
    public string LogPath => LoggingBootstrapper.CurrentLogFilePath ?? paths.LogsDirectory;
    public IObservable<ShellStartupState> Observe() => state.AsObservable();

    public Task StartAsync(Action activatePages) => startupTask ??= StartCoreAsync(activatePages);

    private async Task StartCoreAsync(Action activatePages)
    {
        var reason = StartupFailureReason.Database;
        try
        {
            await database.Initialization.WaitAsync(cancellation.Token);
            reason = StartupFailureReason.Startup;
            await preferences.InitializeAsync();
            reason = StartupFailureReason.Database;
            await refresher.RefreshAsync(cancellation.Token);
            reason = StartupFailureReason.Startup;
            await dispatcher.InvokeAsync(tileLayers.InitializeAsync);
            cancellation.Token.ThrowIfCancellationRequested();
            await dispatcher.InvokeAsync(activatePages);
            if (server is not null)
            {
                var result = await backgroundTaskRunner.RunAsync(server.StartServerAsync, cancellation.Token);
                if (result is ServerStartResult.Failed failed)
                {
                    await PublishAsync(new ShellStartupState.Failed(StartupFailureReason.Server, failed.Details));
                    return;
                }
            }

            cancellation.Token.ThrowIfCancellationRequested();
            await PublishAsync(new ShellStartupState.Ready());
            repairTask = Task.WhenAll(jobs.Select(job => job.RunAsync(cancellation.Token)));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.Error(exception, "Application startup failed during {Phase}", reason);
            await PublishAsync(new ShellStartupState.Failed(reason, exception.Message));
        }
    }

    public Task<DatabaseResetResult> ResetDatabaseAsync() =>
        resetTask is { IsCompleted: false } ? resetTask : resetTask = ResetCoreAsync();

    private async Task<DatabaseResetResult> ResetCoreAsync()
    {
        if (State is not ShellStartupState.Failed { Reason: StartupFailureReason.Database or StartupFailureReason.Reset })
        {
            throw new InvalidOperationException("Database reset requires a database startup failure.");
        }

        await PublishAsync(new ShellStartupState.Starting());
        try
        {
            var archive = await database.ArchiveAndResetAsync();
            await PublishAsync(new ShellStartupState.RelaunchRequired(archive));
            return new DatabaseResetResult.Reset(archive);
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Database reset failed");
            await PublishAsync(new ShellStartupState.Failed(StartupFailureReason.Reset, exception.Message));
            return new DatabaseResetResult.Failed();
        }
    }

    public async Task StopAsync()
    {
        await cancellation.CancelAsync();
        if (startupTask is not null)
        {
            await startupTask;
        }

        try { await repairTask; }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        if (resetTask is not null) await resetTask;
        // Schema work cannot be interrupted halfway through; shutdown waits for its connection.
        await database.Initialization.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    private Task PublishAsync(ShellStartupState value) => dispatcher.InvokeAsync(() => state.OnNext(value));

    public void Dispose()
    {
        cancellation.Dispose();
        state.Dispose();
    }
}
