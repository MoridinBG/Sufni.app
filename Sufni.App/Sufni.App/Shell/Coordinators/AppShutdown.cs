using System;
using System.Linq;
using System.Threading.Tasks;
using Sufni.App.Acquisition.Coordinators;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Shell.ViewModels;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Services;

namespace Sufni.App.Shell.Coordinators;

public interface IAppShutdown
{
    bool IsRunning { get; }
    event EventHandler? RunningChanged;
    Task<bool> RequestAsync(Func<ValueTask> disposeServices, Action flushLogs);
    Task OnBackgroundAsync(Action flushLogs);
}

public sealed class AppShutdown(
    ShellWorkspaceViewModel workspace,
    IAppStartup startup,
    IImportSessionsCoordinator imports,
    ISyncCoordinator sync,
    ISessionRecomputeEngine recompute,
    IPendingDeletes pendingDeletes,
    IBackgroundTaskRunner backgroundTaskRunner,
    ISynchronizationServerService? server = null) : IAppShutdown
{
    private Task<bool>? request;
    public bool IsRunning => request is { IsCompleted: false };
    public event EventHandler? RunningChanged;

    public Task<bool> RequestAsync(Func<ValueTask> disposeServices, Action flushLogs)
    {
        if (IsRunning) return request!;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        request = completion.Task;
        RunningChanged?.Invoke(this, EventArgs.Empty);
        _ = CompleteAsync();
        return request;

        async Task CompleteAsync()
        {
            try { completion.SetResult(await ShutdownAsync(disposeServices, flushLogs)); }
            catch (Exception exception) { completion.SetException(exception); }
            finally { RunningChanged?.Invoke(this, EventArgs.Empty); }
        }
    }

    private async Task<bool> ShutdownAsync(Func<ValueTask> disposeServices, Action flushLogs)
    {
        var current = workspace.CurrentTab;
        var tabs = workspace.Tabs.OrderBy(tab => ReferenceEquals(tab, current)).ToArray();
        foreach (var tab in tabs)
        {
            if (!await tab.TryPrepareCloseAsync()) return false;
            workspace.CloseTab(tab, rememberForRestore: false);
        }

        await Task.WhenAll(startup.StopAsync(), imports.StopAsync(), sync.StopAsync(), recompute.StopAsync());
        await pendingDeletes.FinalizeAllAsync();
        if (server is not null) await backgroundTaskRunner.RunAsync(server.StopAsync);
        await disposeServices();
        flushLogs();
        return true;
    }

    public async Task OnBackgroundAsync(Action flushLogs)
    {
        try { await pendingDeletes.FinalizeAllAsync(); }
        finally { flushLogs(); }
    }
}
