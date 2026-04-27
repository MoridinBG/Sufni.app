using System.Collections.ObjectModel;
using System.Threading.Tasks;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.Tests.Views.ItemLists;
using Sufni.App.ViewModels;
using Sufni.App.ViewModels.ItemLists;

namespace Sufni.App.Tests.Views;

internal static class MainPagesViewModelTestFactory
{
    public static MainPagesViewModel Create(
        LiveDaqListViewModel? liveDaqsPage = null,
        PairingServerViewModel? pairingServerViewModel = null)
    {
        var bikeStore = Substitute.For<IBikeStore>();
        var setupStore = Substitute.For<ISetupStore>();
        var sessionStore = Substitute.For<ISessionStore>();
        var pairedDeviceStore = Substitute.For<IPairedDeviceStore>();
        var importSessionsCoordinator = TestCoordinatorSubstitutes.ImportSessions();
        var trackCoordinator = TestCoordinatorSubstitutes.Track();
        var syncCoordinator = TestCoordinatorSubstitutes.Sync();
        var shell = Substitute.For<IShellCoordinator>();

        bikeStore.RefreshAsync().Returns(Task.CompletedTask);
        setupStore.RefreshAsync().Returns(Task.CompletedTask);
        sessionStore.RefreshAsync().Returns(Task.CompletedTask);
        pairedDeviceStore.RefreshAsync().Returns(Task.CompletedTask);
        return new MainPagesViewModel(
            bikeStore,
            setupStore,
            sessionStore,
            pairedDeviceStore,
            importSessionsCoordinator,
            trackCoordinator,
            syncCoordinator,
            shell,
            CreateBikeListPage(),
            CreateSessionListPage(),
            CreateSetupListPage(),
            liveDaqsPage ?? CreateLiveDaqListPage(),
            CreateImportSessionsPage(shell, importSessionsCoordinator),
            CreatePairedDeviceListPage(),
            pairingServerViewModel: pairingServerViewModel);
    }

    public static WelcomeScreenViewModel CreateWelcomeScreen()
    {
        return new WelcomeScreenViewModel(
            Substitute.For<IShellCoordinator>(),
            Substitute.For<IDialogService>(),
            TestCoordinatorSubstitutes.Bike(),
            TestCoordinatorSubstitutes.Setup(),
            TestCoordinatorSubstitutes.ImportSessions(),
            Substitute.For<IFilesService>());
    }

    private static BikeListViewModel CreateBikeListPage() =>
        new(
            new BikeStoreStub(),
            TestCoordinatorSubstitutes.Bike(),
            Substitute.For<IBikeDependencyQuery>());

    private static SessionListViewModel CreateSessionListPage() =>
        new(new SessionStoreStub(), TestCoordinatorSubstitutes.Session());

    private static SetupListViewModel CreateSetupListPage() =>
        new(new SetupStoreStub(), TestCoordinatorSubstitutes.Setup());

    private static LiveDaqListViewModel CreateLiveDaqListPage() =>
        new(new LiveDaqStore(), TestCoordinatorSubstitutes.LiveDaq());

    private static PairedDeviceListViewModel CreatePairedDeviceListPage() =>
        new(new PairedDeviceStoreStub(), CreatePairedDeviceCoordinator());

    private static PairedDeviceCoordinator CreatePairedDeviceCoordinator() =>
        new(Substitute.For<IPairedDeviceStoreWriter>(), Substitute.For<IDatabaseService>());

    private static ImportSessionsViewModel CreateImportSessionsPage(
        IShellCoordinator shell,
        ImportSessionsCoordinator importSessionsCoordinator)
    {
        var telemetryDataStoreService = Substitute.For<ITelemetryDataStoreService>();
        telemetryDataStoreService.DataStores.Returns(new ObservableCollection<ITelemetryDataStore>());

        return new ImportSessionsViewModel(
            telemetryDataStoreService,
            Substitute.For<IFilesService>(),
            shell,
            Substitute.For<IDialogService>(),
            TestCoordinatorSubstitutes.Setup(),
            importSessionsCoordinator,
            Substitute.For<ISetupStore>());
    }
}