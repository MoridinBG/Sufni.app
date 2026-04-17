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
        var bikeStoreWriter = Substitute.For<IBikeStoreWriter>();
        var setupStoreWriter = Substitute.For<ISetupStoreWriter>();
        var sessionStoreWriter = Substitute.For<ISessionStoreWriter>();
        var pairedDeviceStoreWriter = Substitute.For<IPairedDeviceStoreWriter>();
        bikeStoreWriter.RefreshAsync().Returns(Task.CompletedTask);
        setupStoreWriter.RefreshAsync().Returns(Task.CompletedTask);
        sessionStoreWriter.RefreshAsync().Returns(Task.CompletedTask);
        pairedDeviceStoreWriter.RefreshAsync().Returns(Task.CompletedTask);

        var importSessionsCoordinator = Substitute.For<IImportSessionsCoordinator>();
        var trackCoordinator = Substitute.For<ITrackCoordinator>();
        var syncCoordinator = Substitute.For<ISyncCoordinator>();
        var shell = Substitute.For<IShellCoordinator>();
        var liveCoordinator = Substitute.For<ILiveDaqCoordinator>();
        var livePage = new LiveDaqListViewModel(new LiveDaqStore(), liveCoordinator);

        var viewModel = new MainPagesViewModel(
            bikeStoreWriter,
            setupStoreWriter,
            sessionStoreWriter,
            pairedDeviceStoreWriter,
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