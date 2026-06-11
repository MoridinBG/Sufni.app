using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Threading.Tasks;
using DynamicData;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.ExtensionHost.Contracts;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.Models;
using Sufni.App.Queries;
using Sufni.App.SessionGraph;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.Tests.Views.ItemLists;
using Sufni.App.Theming;
using Sufni.App.ViewModels;
using Sufni.App.ViewModels.ItemLists;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Contracts.SessionGraph;

namespace Sufni.App.Tests.Views;

internal static class MainPagesViewModelTestFactory
{
    private static readonly IUiThreadDispatcher UiThreadDispatcher = new InlineUiThreadDispatcher();

    public static MainPagesViewModel Create(
        LiveDaqListViewModel? liveDaqsPage = null,
        ITrackCoordinator? trackCoordinator = null,
        IAppDataRefresher? appDataRefresher = null,
        IThemeService? themeService = null,
        ISyncCoordinator? syncCoordinator = null,
        IEnumerable<IAppToolbarContributionProvider>? appToolbarContributionProviders = null,
        PairingServerViewModel? pairingServerViewModel = null,
        IEnumerable<IExtensionStateRefreshParticipant>? extensionStateRefreshParticipants = null)
    {
        appDataRefresher ??= Substitute.For<IAppDataRefresher>();
        var importSessionsCoordinator = TestCoordinatorSubstitutes.ImportSessions();
        trackCoordinator ??= TestCoordinatorSubstitutes.Track();
        syncCoordinator ??= TestCoordinatorSubstitutes.Sync();
        var shell = Substitute.For<IShellCoordinator>();

        appDataRefresher.RefreshAsync().Returns(Task.CompletedTask);
        if (themeService is null)
        {
            themeService = Substitute.For<IThemeService>();
            themeService.Mode.Returns(SufniThemeMode.Dark);
            themeService.EffectiveMode.Returns(SufniThemeMode.Dark);
            themeService.IsSystemThemeAvailable.Returns(false);
        }

        return new MainPagesViewModel(
            appDataRefresher,
            importSessionsCoordinator,
            trackCoordinator,
            syncCoordinator,
            shell,
            themeService,
            CreateBikeListPage(),
            CreateSessionListPage(),
            CreateSetupListPage(),
            liveDaqsPage ?? CreateLiveDaqListPage(),
            CreateImportSessionsPage(shell, importSessionsCoordinator),
            CreatePairedDeviceListPage(),
            UiThreadDispatcher,
            appToolbarContributionProviders,
            pairingServerViewModel: pairingServerViewModel,
            extensionStateRefreshParticipants: extensionStateRefreshParticipants);
    }

    public static WelcomeScreenViewModel CreateWelcomeScreen()
    {
        return new WelcomeScreenViewModel(
            Substitute.For<IShellCoordinator>(),
            Substitute.For<IDialogService>(),
            TestCoordinatorSubstitutes.Bike(),
            TestCoordinatorSubstitutes.Setup(),
            TestCoordinatorSubstitutes.ImportSessions(),
            Substitute.For<IFilesService>(),
            UiThreadDispatcher);
    }

    private static BikeListViewModel CreateBikeListPage() =>
        new(
            new BikeStoreStub(),
            TestCoordinatorSubstitutes.Bike(),
            Substitute.For<IBikeDependencyQuery>(),
            UiThreadDispatcher);

    private static SessionListViewModel CreateSessionListPage() =>
        new(CreateRecordedSessionGraph(), TestCoordinatorSubstitutes.Session(), UiThreadDispatcher);

    private static IRecordedSessionGraph CreateRecordedSessionGraph()
    {
        var graph = Substitute.For<IRecordedSessionGraph>();
        var cache = new SourceCache<RecordedSessionSummary, Guid>(summary => summary.Id);
        graph.ConnectSessions().Returns(cache.Connect());
        return graph;
    }

    private static SetupListViewModel CreateSetupListPage() =>
        new(new SetupStoreStub(), TestCoordinatorSubstitutes.Setup(), UiThreadDispatcher);

    private static LiveDaqListViewModel CreateLiveDaqListPage() =>
        new(new LiveDaqStore(), TestCoordinatorSubstitutes.LiveDaq(), UiThreadDispatcher);

    private static PairedDeviceListViewModel CreatePairedDeviceListPage() =>
        new(new PairedDeviceStoreStub(), CreatePairedDeviceCoordinator(), UiThreadDispatcher);

    private static PairedDeviceCoordinator CreatePairedDeviceCoordinator() =>
        new(Substitute.For<IPairedDeviceStoreWriter>(), Substitute.For<IPairedDeviceRepository>());

    private static ImportSessionsViewModel CreateImportSessionsPage(
        IShellCoordinator shell,
        IImportSessionsCoordinator importSessionsCoordinator)
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
            Substitute.For<ISetupStore>(),
            UiThreadDispatcher);
    }
}

internal sealed class TestAppToolbarContributionProvider(params AppToolbarContribution[] contributions)
    : IAppToolbarContributionProvider
{
    public string ExtensionId => contributions.Length > 0 ? contributions[0].ExtensionId : "test";

    public IReadOnlyList<AppToolbarContribution> CreateContributions()
    {
        return contributions;
    }
}
