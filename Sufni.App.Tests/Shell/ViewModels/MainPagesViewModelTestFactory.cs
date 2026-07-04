using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DynamicData;
using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.Theming;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;

using Sufni.App.Acquisition.Coordinators;
using Sufni.App.Acquisition.ViewModels;
using Sufni.App.Bikes.ViewModels.ItemLists;
using Sufni.App.ExtensionHost.Contracts.Capabilities;
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
using Sufni.App.LiveDaq.Stores;
using Sufni.App.Setups.Stores;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.SyncAndPairing.Stores;
using Sufni.App.Tests.TestSupport.Doubles;
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
        IAppEnvironment? appEnvironment = null,
        IUiPreferences? uiPreferences = null,
        IEnumerable<IAppToolbarContributionProvider>? appToolbarContributionProviders = null,
        PairingServerViewModel? pairingServerViewModel = null,
        IEnumerable<IExtensionStateRefreshParticipant>? extensionStateRefreshParticipants = null)
    {
        appDataRefresher ??= Substitute.For<IAppDataRefresher>();
        var importSessionsCoordinator = TestCoordinatorSubstitutes.ImportSessions();
        trackCoordinator ??= TestCoordinatorSubstitutes.Track();
        syncCoordinator ??= TestCoordinatorSubstitutes.Sync();
        shell ??= Substitute.For<IShellCoordinator>();

        appDataRefresher.RefreshAsync().Returns(Task.CompletedTask);
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
            themeService,
            appEnvironment,
            uiPreferences,
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

    public static IAppEnvironment CreateAppEnvironment(
        UiLayoutProfile layoutProfile = UiLayoutProfile.Workspace,
        AppCapabilities? capabilities = null,
        InputCapabilities? input = null) =>
        new AppEnvironment(
            DefaultLayoutProfile: layoutProfile,
            LayoutProfile: layoutProfile,
            Capabilities: capabilities ?? new AppCapabilities(
                CanHostSyncServer: true,
                CanPairAsClient: false,
                HasHaptics: false,
                SupportsMassStorageImport: true,
                SupportsStorageProviderImport: true,
                SupportsNativeWindowing: true),
            Input: input ?? new InputCapabilities(
                HasPointer: true,
                HasTouch: false,
                HasKeyboard: true,
                SupportsPinch: false,
                SupportsLongPressContextMenu: false));

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
        new(CreateRecordedSessionProjection(), TestCoordinatorSubstitutes.Session(), UiThreadDispatcher);

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

internal sealed class TestAppToolbarContributionProvider : IAppToolbarContributionProvider
{
    private readonly IReadOnlyList<AppToolbarCommandContribution> commandContributions;
    private readonly IReadOnlyList<AppToolbarViewContribution> viewContributions;

    public TestAppToolbarContributionProvider(params AppToolbarViewContribution[] viewContributions)
        : this([], viewContributions)
    {
    }

    public TestAppToolbarContributionProvider(
        IReadOnlyList<AppToolbarCommandContribution> commandContributions,
        IReadOnlyList<AppToolbarViewContribution> viewContributions)
    {
        this.commandContributions = commandContributions;
        this.viewContributions = viewContributions;
    }

    public string ExtensionId =>
        commandContributions.FirstOrDefault()?.ExtensionId ??
        viewContributions.FirstOrDefault()?.ExtensionId ??
        "test";

    public IReadOnlyList<AppToolbarCommandContribution> CreateCommandContributions() => commandContributions;

    public IReadOnlyList<AppToolbarViewContribution> CreateViewContributions() => viewContributions;
}
