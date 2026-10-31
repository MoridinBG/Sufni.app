using Sufni.App.MapsAndTracks.Coordinators;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Sufni.App.Acquisition.Coordinators;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Shell.ViewModels;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Tests.TestSupport.Doubles;

namespace Sufni.App.Tests.Shell.Coordinators;

public class AppShutdownTests
{
    [Fact]
    public async Task Cancel_KeepsDirtyTabOpenAndLeavesWorkAndPendingDeletesRunning()
    {
        var dialogs = Substitute.For<IDialogService>();
        dialogs.ShowCloseConfirmationAsync(Arg.Any<bool>()).Returns(PromptResult.Cancel);
        var tab = new TestTabPageViewModel(dialogs) { IsDirty = true };
        var workspace = CreateWorkspace();
        workspace.OpenOrFocus(tab);
        using var pending = TestPendingDeletes.Create();
        var key = new PendingDeleteKey.Session(Guid.NewGuid());
        pending.Schedule(key, "ride");
        var imports = TestCoordinatorSubstitutes.ImportSessions();
        var disposed = false;
        var shutdown = Create(workspace, pending, imports);

        var accepted = await shutdown.RequestAsync(() => { disposed = true; return ValueTask.CompletedTask; }, () => { });

        Assert.False(accepted);
        Assert.Same(tab, Assert.Single(workspace.Tabs));
        Assert.True(tab.IsDirty);
        Assert.False(tab.WasClosed);
        Assert.True(pending.IsPending(key));
        Assert.False(disposed);
        Assert.False(shutdown.IsRunning);
        await imports.DidNotReceive().StopAsync();
    }

    [Fact]
    public async Task Shutdown_ClosesTabs_WaitsForWork_ThenFinalizesDeletesAndStopsServerBeforeDisposal()
    {
        var dialogs = Substitute.For<IDialogService>();
        dialogs.ShowCloseConfirmationAsync(Arg.Any<bool>()).Returns(PromptResult.No);
        var background = new TestTabPageViewModel(dialogs) { IsDirty = true };
        var current = new TestTabPageViewModel(dialogs) { IsDirty = true };
        var workspace = CreateWorkspace();
        workspace.OpenOrFocus(background);
        workspace.OpenOrFocus(current);
        var drain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var imports = TestCoordinatorSubstitutes.ImportSessions();
        imports.StopAsync().Returns(_ =>
        {
            Assert.True(background.WasClosed);
            Assert.True(current.WasClosed);
            return drain.Task;
        });
        var key = new PendingDeleteKey.Session(Guid.NewGuid());
        var sessions = TestCoordinatorSubstitutes.Session();
        var persisted = true;
        sessions.DeleteAsync(key.Id).Returns(_ =>
        {
            persisted = false;
            return new SessionDeleteResult.Deleted();
        });
        using var pending = TestPendingDeletes.Create(sessions: sessions);
        pending.Schedule(key, "ride");
        var server = Substitute.For<ISynchronizationServerService>();
        var serverStopped = false;
        server.StopAsync().Returns(_ =>
        {
            Assert.False(persisted);
            Assert.False(pending.IsPending(key));
            serverStopped = true;
            return Task.CompletedTask;
        });
        var metricsDrain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tracks = TestCoordinatorSubstitutes.Track();
        tracks.StopAsync().Returns(metricsDrain.Task);
        var shutdown = Create(workspace, pending, imports, server, tracks);
        var disposed = false;
        var flushed = false;

        var run = shutdown.RequestAsync(() =>
        {
            Assert.True(serverStopped);
            disposed = true;
            return ValueTask.CompletedTask;
        }, () => { Assert.True(disposed); flushed = true; });

        Assert.True(shutdown.IsRunning);
        Assert.Empty(workspace.Tabs);
        Assert.True(pending.IsPending(key));
        Assert.False(serverStopped);
        drain.SetResult();
        Assert.False(run.IsCompleted);
        Assert.True(pending.IsPending(key));
        Assert.False(serverStopped);
        metricsDrain.SetResult();
        Assert.True(await run.AwaitBoundedAsync(TimeSpan.FromSeconds(5)));
        Assert.True(disposed);
        Assert.True(flushed);
    }

    [Fact]
    public async Task Background_FinalizesDeletesWithoutClosingTabsOrStoppingWork()
    {
        var workspace = CreateWorkspace();
        var tab = new TestTabPageViewModel { IsDirty = true };
        workspace.OpenOrFocus(tab);
        var sessions = TestCoordinatorSubstitutes.Session();
        var key = new PendingDeleteKey.Session(Guid.NewGuid());
        var persisted = true;
        sessions.DeleteAsync(key.Id).Returns(_ =>
        {
            persisted = false;
            return new SessionDeleteResult.Deleted();
        });
        using var pending = TestPendingDeletes.Create(sessions: sessions);
        pending.Schedule(key, "ride");
        var imports = TestCoordinatorSubstitutes.ImportSessions();
        var shutdown = Create(workspace, pending, imports);
        var flushed = false;

        await shutdown.OnBackgroundAsync(() => { Assert.False(persisted); flushed = true; });

        Assert.False(pending.IsPending(key));
        Assert.True(flushed);
        Assert.Same(tab, workspace.CurrentTab);
        Assert.True(tab.IsDirty);
        await imports.DidNotReceive().StopAsync();
    }

    private static ShellWorkspaceViewModel CreateWorkspace() => new(new InlineUiThreadDispatcher(), new FakeTimeProvider());

    private static AppShutdown Create(ShellWorkspaceViewModel workspace, IPendingDeletes pending,
        IImportSessionsCoordinator imports, ISynchronizationServerService? server = null, ITrackCoordinator? tracks = null) =>
        new(workspace, Substitute.For<IAppStartup>(), imports, TestCoordinatorSubstitutes.Sync(),
            Substitute.For<ISessionRecomputeEngine>(), pending, new InlineBackgroundTaskRunner(), tracks ?? TestCoordinatorSubstitutes.Track(), server);
}
