using Sufni.App.Acquisition.Coordinators;
using NSubstitute;
using Sufni.App.Bikes.Models;
using Sufni.Telemetry;

using Sufni.App.LiveDaq.Queries;
using Sufni.App.LiveDaq.Stores;
using Sufni.App.Shared.Base;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Acquisition.Services;
using Sufni.App.Acquisition.ViewModels;
using Sufni.App.Bikes.Queries;
using Sufni.App.Bikes.Stores;
using Sufni.App.Bikes.ViewModels.Editors;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.LiveDaq.ViewModels.Editors;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Insights.Services;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Queries;
using Sufni.App.Setups.ViewModels.Editors;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Extensions;
using Sufni.App.Video.ViewModels;
namespace Sufni.App.Tests.TestSupport.Doubles;

internal static class TestEditorFactories
{
    public static IEditorFactory Create(
        IBikeStore? bikeStore = null,
        ISetupBoardQuery? setupQuery = null,
        ISessionStore? sessionStore = null,
        ILiveDaqStore? liveDaqStore = null,
        ILiveDaqKnownBoardsQuery? knownBoards = null,
        ILiveDaqSharedStreamRegistry? streamRegistry = null,
        ILiveSessionServiceFactory? liveSessionFactory = null,
        IImportSessionsCoordinator? importCoordinator = null)
    {
        var sessionPresentationService = Substitute.For<ISessionPresentationService>();
        var sessionAnalysisService = Substitute.For<ISessionInsightsService>();
        var uiThreadDispatcher = new InlineUiThreadDispatcher();
        var backgroundTaskRunner = new InlineBackgroundTaskRunner();
        var analysisResultStateFactory = new RecordedSessionAnalysisResultStateFactory(
            new RecordedSessionAnalysisComputer(sessionAnalysisService),
            backgroundTaskRunner,
            uiThreadDispatcher);

        return new EditorFactory(
            TestCoordinatorSubstitutes.Bike(),
            Substitute.For<IBikeDependencyQuery>(),
            bikeStore ?? Substitute.For<IBikeStore>(),
            TestCoordinatorSubstitutes.Setup(),
            setupQuery ?? Substitute.For<ISetupBoardQuery>(),
            TestCoordinatorSubstitutes.Session(),
            TestCoordinatorSubstitutes.LiveDaq(),
            TestCoordinatorSubstitutes.SessionLoader(),
            TestCoordinatorSubstitutes.RecomputeEngine(),
            sessionStore ?? Substitute.For<ISessionStore>(),
            Substitute.For<IRecordedSessionProjection>(),
            sessionPresentationService,
            analysisResultStateFactory,
            new TestMapViewModelFactory(Substitute.For<ITileLayerService>().WithDefaultLayerState()),
            Substitute.For<ISessionPreferences>(),
            new TestSessionProcessedTelemetryReader(),
            knownBoards ?? Substitute.For<ILiveDaqKnownBoardsQuery>(),
            liveDaqStore ?? Substitute.For<ILiveDaqStore>(),
            Substitute.For<IDialogService>(),
            uiThreadDispatcher,
            backgroundTaskRunner,
            importCoordinator ?? TestCoordinatorSubstitutes.ImportSessions(),
            streamRegistry ?? Substitute.For<ILiveDaqSharedStreamRegistry>(),
            liveSessionFactory ?? Substitute.For<ILiveSessionServiceFactory>(),
            Substitute.For<IVideoPaneFactory>(),
            Substitute.For<ISessionEditingCoordinator>(),
            TestSegmentPickers.CreateFactory(),
            TestSegmentComparisons.CreateFactory(),
            new SessionOperationRunner(),
            new Microsoft.Extensions.Time.Testing.FakeTimeProvider(),
            new Sufni.App.Tests.TestSupport.Async.ManualUiTimerScheduler(),
            Substitute.For<IRecordedSessionDomainQuery>());
    }

}
