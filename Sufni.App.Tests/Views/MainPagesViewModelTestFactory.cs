using System.Threading.Tasks;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Stores;
using Sufni.App.ViewModels;
using Sufni.App.ViewModels.ItemLists;

namespace Sufni.App.Tests.Views;

internal static class MainPagesViewModelTestFactory
{
    public static MainPagesViewModel Create()
    {
        var bikeStore = Substitute.For<IBikeStore>();
        var setupStore = Substitute.For<ISetupStore>();
        var sessionStore = Substitute.For<ISessionStore>();
        var pairedDeviceStore = Substitute.For<IPairedDeviceStore>();
        var importSessionsCoordinator = Substitute.For<IImportSessionsCoordinator>();
        var trackCoordinator = Substitute.For<ITrackCoordinator>();
        var syncCoordinator = Substitute.For<ISyncCoordinator>();
        var shell = Substitute.For<IShellCoordinator>();

        bikeStore.RefreshAsync().Returns(Task.CompletedTask);
        setupStore.RefreshAsync().Returns(Task.CompletedTask);
        sessionStore.RefreshAsync().Returns(Task.CompletedTask);
        pairedDeviceStore.RefreshAsync().Returns(Task.CompletedTask);
        importSessionsCoordinator.OpenAsync().Returns(Task.CompletedTask);
        trackCoordinator.ImportGpxAsync().Returns(Task.CompletedTask);
        syncCoordinator.SyncAllAsync().Returns(Task.CompletedTask);
        syncCoordinator.IsRunning.Returns(false);
        syncCoordinator.IsPaired.Returns(false);
        syncCoordinator.CanSync.Returns(false);

        return new MainPagesViewModel(
            bikeStore,
            setupStore,
            sessionStore,
            pairedDeviceStore,
            importSessionsCoordinator,
            trackCoordinator,
            syncCoordinator,
            shell,
            new BikeListViewModel(),
            new SessionListViewModel(),
            new SetupListViewModel(),
            new LiveDaqListViewModel(),
            new ImportSessionsViewModel(),
            new PairedDeviceListViewModel());
    }
}