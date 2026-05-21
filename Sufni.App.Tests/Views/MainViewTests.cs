using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Services;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels;
using Sufni.App.Stores;
using Sufni.App.Views;
using Sufni.App.Views.Controls;

namespace Sufni.App.Tests.Views;

public class MainViewTests
{
    [AvaloniaFact]
    public async Task MainView_SwitchesMountedContent_WhenCurrentViewChanges()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: false);

        var mainPages = MainPagesViewModelTestFactory.Create();
        var viewModel = new MainViewModel(mainPages);
        var view = new MainView
        {
            DataContext = viewModel,
        };

        await using var mounted = await MountAsync(view);

        var host = mounted.View.FindControl<ContentControl>("CurrentViewHost");

        Assert.NotNull(host);
        Assert.Same(mainPages, host!.Content);
        Assert.Single(mounted.View.GetVisualDescendants().OfType<MainPagesView>());

        viewModel.OpenView(MainPagesViewModelTestFactory.CreateWelcomeScreen());
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.IsType<WelcomeScreenViewModel>(host.Content);
        Assert.Single(mounted.View.GetVisualDescendants().OfType<WelcomeScreenView>());

        viewModel.OpenPreviousView();
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Same(mainPages, host.Content);
        Assert.Single(mounted.View.GetVisualDescendants().OfType<MainPagesView>());
    }

    [AvaloniaFact]
    public async Task MainView_ShowsSyncBusyOverlay_WhenSyncRuns()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: false);

        var server = new TestSynchronizationServerService();
        var mainPages = MainPagesViewModelTestFactory.Create(syncCoordinator: CreateSyncCoordinator(server));
        var viewModel = new MainViewModel(mainPages);
        var view = new MainView
        {
            DataContext = viewModel,
        };

        await using var mounted = await MountAsync(view);

        var overlay = mounted.View.FindControl<BusyOverlay>("SyncBusyOverlay");
        Assert.NotNull(overlay);
        Assert.False(overlay!.IsVisible);

        var progress = new SynchronizationProgressSnapshot(
            SynchronizationPhase.PullingRemoteChanges,
            "Pulling remote changes",
            CurrentStep: 3,
            TotalSteps: 8,
            IsDeterminate: true);

        server.RaiseSyncActivityStarted(progress);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.True(overlay.IsVisible);
        Assert.True(overlay.IsActive);
        Assert.True(overlay.ShowProgress);
        Assert.False(overlay.IsProgressIndeterminate);
        Assert.Equal("Pulling remote changes", overlay.Message);
        Assert.Equal(0.375, overlay.ProgressValue);

        server.RaiseSyncActivityEnded(progress);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(overlay.IsVisible);
        Assert.False(overlay.IsActive);
    }

    private static async Task<MountedMainView> MountAsync(MainView view)
    {
        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedMainView(host, view);
    }

    private static SyncCoordinator CreateSyncCoordinator(ISynchronizationServerService server) =>
        new(
            Substitute.For<IBikeStoreWriter>(),
            Substitute.For<ISetupStoreWriter>(),
            Substitute.For<ISessionStoreWriter>(),
            Substitute.For<IRecordedSessionSourceStore>(),
            Substitute.For<IPairedDeviceStoreWriter>(),
            synchronizationClientService: null,
            pairingClientCoordinator: null,
            synchronizationServerService: server,
            backgroundTaskRunner: new InlineBackgroundTaskRunner(),
            inboundActivityIdleGrace: TimeSpan.Zero);
}

internal sealed class MountedMainView(Window host, MainView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public MainView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
