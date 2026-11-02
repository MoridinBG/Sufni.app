using Microsoft.Extensions.Time.Testing;
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
        IThemeService? themeService = null,
        ISyncCoordinator? syncCoordinator = null,
        IShellCoordinator? shell = null,
        IAppEnvironment? appEnvironment = null,
        IVideoCoordinator? videoCoordinator = null,
        PairingClientViewModel? pairingClientPage = null,
        PairingServerViewModel? pairingServerViewModel = null)
    {
        trackCoordinator ??= TestCoordinatorSubstitutes.Track();
        syncCoordinator ??= TestCoordinatorSubstitutes.Sync();
        shell ??= Substitute.For<IShellCoordinator>();

        if (themeService is null)
        {
            themeService = Substitute.For<IThemeService>();
            themeService.Mode.Returns(SufniThemeMode.Dark);
            themeService.EffectiveMode.Returns(SufniThemeMode.Dark);
            themeService.IsSystemThemeAvailable.Returns(false);
        }
        appEnvironment ??= CreateAppEnvironment();
        var feedback = TestShellFeedback.Create(syncCoordinator, appEnvironment);
        return new MainPagesViewModel(
            trackCoordinator,
            feedback,
            shell,
            themeService,
            appEnvironment,
            CreateBikeListPage(shell, feedback),
            CreateSessionListPage(shell, feedback),
            CreateSetupListPage(shell, feedback),
            liveDaqsPage ?? CreateLiveDaqListPage(shell),
            CreatePairedDeviceListPage(),
            UiThreadDispatcher,
            videoCoordinator ?? Substitute.For<IVideoCoordinator>(),
            pairingClientPage: pairingClientPage,
            pairingServerViewModel: pairingServerViewModel,
            timeProvider: new FakeTimeProvider());
    }

    public static IAppEnvironment CreateAppEnvironment(
        UiLayoutProfile layoutProfile = UiLayoutProfile.Workspace,
        AppCapabilities? capabilities = null,
        InputCapabilities? input = null) =>
        TestAppEnvironment.Create(layoutProfile, capabilities, input);

    private static BikeListViewModel CreateBikeListPage(IShellCoordinator shell, IShellActions feedback) =>
        new(
            new BikeStoreStub(),
            Substitute.For<IBikeDependencyQuery>(),
            TestCoordinatorSubstitutes.Bike(),
            UiThreadDispatcher,
            pendingDeletes: TestPendingDeletes.Create(shell: shell), timeProvider: new FakeTimeProvider(), shell: shell, shellActions: feedback);

    private static SessionListViewModel CreateSessionListPage(IShellCoordinator shell, IShellActions feedback) =>
        new(
            CreateRecordedSessionProjection(),
            TestCoordinatorSubstitutes.RecomputeEngine(),
            UiThreadDispatcher,
            pendingDeletes: TestPendingDeletes.Create(shell: shell), timeProvider: new FakeTimeProvider(), shell: shell, shellActions: feedback);

    private static IRecordedSessionProjection CreateRecordedSessionProjection()
    {
        var projection = Substitute.For<IRecordedSessionProjection>();
        var cache = new SourceCache<RecordedSessionSummary, Guid>(summary => summary.Id);
        projection.ConnectSessions().Returns(cache.Connect());
        return projection;
    }

    private static SetupListViewModel CreateSetupListPage(IShellCoordinator shell, IShellActions feedback) =>
        new(new SetupBoardQueryStub(), TestCoordinatorSubstitutes.Setup(), UiThreadDispatcher, pendingDeletes: TestPendingDeletes.Create(shell: shell), timeProvider: new FakeTimeProvider(), shell: shell, shellActions: feedback);

    private static LiveDaqListViewModel CreateLiveDaqListPage(IShellCoordinator shell) =>
        new(new LiveDaqStore(new InlineUiThreadDispatcher()), TestCoordinatorSubstitutes.LiveDaq(), UiThreadDispatcher, timeProvider: new FakeTimeProvider(), shell: shell);

    private static PairedDeviceListViewModel CreatePairedDeviceListPage() =>
        new(new PairedDeviceStoreStub(), UiThreadDispatcher, pendingDeletes: TestPendingDeletes.Create(), timeProvider: new FakeTimeProvider());


}
