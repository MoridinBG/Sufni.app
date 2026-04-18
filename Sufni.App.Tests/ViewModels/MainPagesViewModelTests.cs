using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Stores;
using Sufni.App.ViewModels;
using Sufni.App.ViewModels.ItemLists;

namespace Sufni.App.Tests.ViewModels;

public class MainPagesViewModelTests
{
    [Fact]
    public void SelectedIndex_ActivatesLivePage_WhenSelected_AndDeactivatesIt_WhenLeft()
    {
        var bikeStore = Substitute.For<IBikeStore>();
        var setupStore = Substitute.For<ISetupStore>();
        var sessionStore = Substitute.For<ISessionStore>();
        var pairedDeviceStore = Substitute.For<IPairedDeviceStore>();
        bikeStore.RefreshAsync().Returns(Task.CompletedTask);
        setupStore.RefreshAsync().Returns(Task.CompletedTask);
        sessionStore.RefreshAsync().Returns(Task.CompletedTask);
        pairedDeviceStore.RefreshAsync().Returns(Task.CompletedTask);

        var importSessionsCoordinator = Substitute.For<IImportSessionsCoordinator>();
        var trackCoordinator = Substitute.For<ITrackCoordinator>();
        var syncCoordinator = Substitute.For<ISyncCoordinator>();
        var shell = Substitute.For<IShellCoordinator>();
        var liveCoordinator = Substitute.For<ILiveDaqCoordinator>();
        var livePage = new LiveDaqListViewModel(new LiveDaqStore(), liveCoordinator);

        var viewModel = new MainPagesViewModel(
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
            livePage,
            new ImportSessionsViewModel(),
            new PairedDeviceListViewModel());

        viewModel.SelectedIndex = 3;
        viewModel.SelectedIndex = 0;

        liveCoordinator.Received(1).Activate();
        liveCoordinator.Received(1).Deactivate();
    }
}