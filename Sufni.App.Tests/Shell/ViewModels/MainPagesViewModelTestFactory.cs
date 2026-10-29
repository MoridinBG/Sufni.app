using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DynamicData;
using NSubstitute;

using Sufni.App.Acquisition.Coordinators;
using Sufni.App.Acquisition.ViewModels;
using Sufni.App.Bikes.ViewModels.ItemLists;
using Sufni.App.Infrastructure.Theming;
using Sufni.App.LiveDaq.ViewModels.ItemLists;
using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.Sessions.Lists.ViewModels.ItemLists;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Setups.ViewModels.ItemLists;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Shell.ViewModels;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.ViewModels;
using Sufni.App.SyncAndPairing.ViewModels.ItemLists;
using Sufni.App.Acquisition.Models;
using Sufni.App.Acquisition.Services;
using Sufni.App.Bikes.Queries;
using Sufni.App.Infrastructure;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.LiveDaq.Stores;
using Sufni.App.Setups.Stores;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.SyncAndPairing.Stores;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Video;
using Sufni.App.Video.Coordinators;
namespace Sufni.App.Tests.Shell.ViewModels;

internal static class MainPagesViewModelTestFactory
{
    private static readonly IUiThreadDispatcher UiThreadDispatcher = new InlineUiThreadDispatcher();

    public static MainPagesViewModel Create(
        LiveDaqListViewModel? liveDaqsPage = null,
        ITrackCoordinator? trackCoordinator = null,
        IAppDataRefresher? appDataRefresher = null,
        IThemeService? themeService = null,
        ISyncCoordinator? syncCoordinator = null,
        IShellCoordinator? shell = null,
        ShellWorkspaceViewModel? workspace = null,
        IAppEnvironment? appEnvironment = null,
        ILayoutProfileTransitionState? layoutProfileTransitionState = null,
        IUiPreferences? uiPreferences = null,
        IVideoCoordinator? videoCoordinator = null,
        PairingClientViewModel? pairingClientPage = null,
        PairingServerViewModel? pairingServerViewModel = null)
    {
        appDataRefresher ??= Substitute.For<IAppDataRefresher>();
        var importSessionsCoordinator = TestCoordinatorSubstitutes.ImportSessions();
        trackCoordinator ??= TestCoordinatorSubstitutes.Track();
        syncCoordinator ??= TestCoordinatorSubstitutes.Sync();
        shell ??= Substitute.For<IShellCoordinator>();
        workspace ??= new ShellWorkspaceViewModel(UiThreadDispatcher);

        appDataRefresher.RefreshAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        if (themeService is null)
        {
            themeService = Substitute.For<IThemeService>();
            themeService.Mode.Returns(SufniThemeMode.Dark);
            themeService.EffectiveMode.Returns(SufniThemeMode.Dark);
            themeService.IsSystemThemeAvailable.Returns(false);
        }
        appEnvironment ??= CreateAppEnvironment();
        if (uiPreferences is null)
        {
            uiPreferences = Substitute.For<IUiPreferences>();
            uiPreferences.SetLayoutProfileAsync(Arg.Any<UiLayoutProfile?>()).Returns(Task.CompletedTask);
        }

        return new MainPagesViewModel(
            appDataRefresher,
            importSessionsCoordinator,
            trackCoordinator,
            syncCoordinator,
            shell,
            workspace,
            themeService,
            appEnvironment,
            layoutProfileTransitionState ?? new LayoutProfileTransitionState(),
            uiPreferences,
            CreateBikeListPage(),
            CreateSessionListPage(),
            CreateSetupListPage(),
            liveDaqsPage ?? CreateLiveDaqListPage(),
            CreateImportSessionsPage(shell, importSessionsCoordinator),
            CreatePairedDeviceListPage(),
            UiThreadDispatcher,
            videoCoordinator ?? Substitute.For<IVideoCoordinator>(),
            pairingClientPage: pairingClientPage,
            pairingServerViewModel: pairingServerViewModel);
    }

    public static IAppEnvironment CreateAppEnvironment(
        UiLayoutProfile layoutProfile = UiLayoutProfile.Workspace,
        AppCapabilities? capabilities = null,
        InputCapabilities? input = null) =>
        TestAppEnvironment.Create(layoutProfile, capabilities, input);

    private static BikeListViewModel CreateBikeListPage() =>
        new(
            new BikeStoreStub(),
            TestCoordinatorSubstitutes.Bike(),
            Substitute.For<IBikeDependencyQuery>(),
            UiThreadDispatcher);

    private static SessionListViewModel CreateSessionListPage() =>
        new(
            CreateRecordedSessionProjection(),
            TestCoordinatorSubstitutes.Session(),
            VideoFixture.CreateEmptyStore(),
            UiThreadDispatcher);

    private static IRecordedSessionProjection CreateRecordedSessionProjection()
    {
        var projection = Substitute.For<IRecordedSessionProjection>();
        var cache = new SourceCache<RecordedSessionSummary, Guid>(summary => summary.Id);
        projection.ConnectSessions().Returns(cache.Connect());
        return projection;
    }

    private static SetupListViewModel CreateSetupListPage() =>
        new(new SetupStoreStub(), TestCoordinatorSubstitutes.Setup(), UiThreadDispatcher);

    private static LiveDaqListViewModel CreateLiveDaqListPage() =>
        new(new LiveDaqStore(), TestCoordinatorSubstitutes.LiveDaq(), UiThreadDispatcher);

    private static PairedDeviceListViewModel CreatePairedDeviceListPage() =>
        new(new PairedDeviceStoreStub(), CreatePairedDeviceCoordinator(), UiThreadDispatcher);

    private static PairedDeviceCoordinator CreatePairedDeviceCoordinator() =>
        new(Substitute.For<IPairedDeviceStoreWriter>());

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
