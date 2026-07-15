using System.Reactive.Linq;
using Avalonia;
using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.ExtensionHost.TestSupport.Async;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Insights.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Extensions;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Persistence;
using Sufni.Telemetry;

namespace Sufni.App.Tests.TestSupport.Sessions;

internal sealed class SessionDetailHarness
{
    private readonly ITrackCoordinator trackCoordinator = TestCoordinatorSubstitutes.Track();
    private readonly ITileLayerService tileLayerService = Substitute.For<ITileLayerService>().WithDefaultSelectedLayerChanges();

    public SessionDetailHarness()
    {
        tileLayerService.AvailableLayers.Returns([]);
        tileLayerService.InitializeAsync().Returns(Task.CompletedTask);
        SessionPreferences.WithDefaultObserveRecorded();
        SessionPreferences.GetRecordedAsync(Arg.Any<Guid>()).Returns(Task.FromResult(global::Sufni.App.Infrastructure.SessionPreferences.Default));
        SessionPreferences.UpdateRecordedAsync(Arg.Any<Guid>(), Arg.Any<Func<global::Sufni.App.Infrastructure.SessionPreferences, global::Sufni.App.Infrastructure.SessionPreferences>>())
            .Returns(Task.CompletedTask);
        SessionPresentationService.CalculateDampingPercentages(
                Arg.Any<TelemetryData>(),
                Arg.Any<TelemetryTimeRange?>(),
                Arg.Any<VelocityAverageMode>(),
                Arg.Any<DampingSpeedCutoffs?>())
            .Returns(SessionDampingPercentages.Empty);
        SessionInsightsService.Analyze(Arg.Any<SessionInsightsRequest>()).Returns(SessionInsightsResult.Hidden);
    }

    public ISessionCoordinator SessionCoordinator { get; } = TestCoordinatorSubstitutes.Session();
    public ISessionStore SessionStore { get; } = Substitute.For<ISessionStore>();
    public IRecordedSessionProjection RecordedSessionProjection { get; } = Substitute.For<IRecordedSessionProjection>();
    public ISessionPresentationService SessionPresentationService { get; } = Substitute.For<ISessionPresentationService>();
    public ISessionInsightsService SessionInsightsService { get; } = Substitute.For<ISessionInsightsService>();
    public IShellCoordinator Shell { get; } = Substitute.For<IShellCoordinator>();
    public IDialogService DialogService { get; } = Substitute.For<IDialogService>();
    public ISessionPreferences SessionPreferences { get; } = Substitute.For<ISessionPreferences>();
    public TestSessionProcessedTelemetryReader ProcessedTelemetryReader { get; } = new();
    public IRecordedSessionDerivationWindowCache DerivationWindowCache { get; } = Substitute.For<IRecordedSessionDerivationWindowCache>();
    public IEditorFactory EditorFactory { get; } = Substitute.For<IEditorFactory>();
    public IUiThreadDispatcher UiThreadDispatcher { get; set; } = new InlineUiThreadDispatcher();
    public IRecordedSessionProcessingOptionCache ProcessingOptionCache { get; set; } = new InMemoryRecordedSessionProcessingOptionCache();
    public IObservable<RecordedSessionDomainSnapshot> ProjectionChanges { get; private set; } = Observable.Empty<RecordedSessionDomainSnapshot>();

    public SessionSnapshot CreateRecordedSession(
        string name = "Recorded Session 01",
        string description = "Suspension notes",
        bool hasProcessedData = true,
        long updated = 1) =>
        TestSnapshots.Session(name: name, description: description, hasProcessedData: hasProcessedData, updated: updated);

    public SessionDetailViewModel CreateLoadedRecordedSession(
        SessionSnapshot? snapshot = null,
        SessionDetailLoadResult? loadResult = null,
        IObservable<RecordedSessionDomainSnapshot>? projectionChanges = null,
        bool isDesktop = true,
        IReadOnlyList<IRecordedSessionExtensionFactory>? recordedSessionExtensionFactories = null)
    {
        snapshot ??= CreateRecordedSession();
        ConfigureSnapshot(snapshot, projectionChanges);
        ConfigureLoad(snapshot, loadResult ?? CreateLoadedResult());
        return CreateEditor(snapshot, isDesktop, recordedSessionExtensionFactories);
    }

    public Task LoadAsync(SessionDetailViewModel editor, Rect? bounds = null) =>
        editor.LoadedCommand.ExecuteAsync(bounds);

    public Task SaveAsync(SessionDetailViewModel editor) =>
        editor.SaveCommand.ExecuteAsync(null);

    public Task DeleteAsync(SessionDetailViewModel editor, bool navigateBack = true) =>
        editor.DeleteCommand.ExecuteAsync(navigateBack);

    public Task UnloadAsync(SessionDetailViewModel editor) =>
        editor.UnloadedCommand.ExecuteAsync(null);

    public void PublishProjectionChange(IObserver<RecordedSessionDomainSnapshot> publisher, RecordedSessionDomainSnapshot snapshot) =>
        publisher.OnNext(snapshot);

    public void PublishPreferenceChange(IObserver<global::Sufni.App.Infrastructure.SessionPreferences> publisher, global::Sufni.App.Infrastructure.SessionPreferences preferences) =>
        publisher.OnNext(preferences);

    public void ConfigureSnapshot(SessionSnapshot snapshot, IObservable<RecordedSessionDomainSnapshot>? projectionChanges = null)
    {
        ProjectionChanges = projectionChanges ?? Observable.Empty<RecordedSessionDomainSnapshot>();
        RecordedSessionProjection.WatchSession(snapshot.Id).Returns(ProjectionChanges);
        SessionStore.Get(snapshot.Id).Returns(snapshot);
    }

    public void ConfigureLoad(SessionSnapshot snapshot, SessionDetailLoadResult result) =>
        SessionCoordinator.LoadDetailAsync(
                snapshot.Id,
                Arg.Any<SessionPresentationDimensions>(),
                Arg.Any<IProgress<SessionDetailLoadProgress>>(),
                Arg.Any<CancellationToken>())
            .Returns(result);

    public SessionDetailLoadResult.Loaded CreateLoadedResult(
        TelemetryData? telemetry = null,
        bool includeBalance = true,
        IReadOnlyList<TrackPoint>? trackPoints = null,
        double? mediaColumnWidth = null)
    {
        var data = telemetry ?? TestTelemetryData.CreateProcessed(rearPresent: includeBalance);
        return new SessionDetailLoadResult.Loaded(new SessionDetailData(
            new SessionTelemetryPresentationData(
                data,
                FullTrackId: null,
                FullTrackPoints: null,
                TrackPoints: trackPoints?.ToList(),
                MediaColumnWidth: mediaColumnWidth,
                DampingSpeedCutoffs: DampingSpeedCutoffs.Default,
                DampingSpeedCutoffOwner: null)));
    }

    private SessionDetailViewModel CreateEditor(
        SessionSnapshot snapshot,
        bool isDesktop,
        IReadOnlyList<IRecordedSessionExtensionFactory>? recordedSessionExtensionFactories)
    {
        TestApp.SetIsDesktop(isDesktop);
        var analysisResultStateFactory = new RecordedSessionAnalysisResultStateFactory(
            new RecordedSessionAnalysisComputer(SessionInsightsService),
            new InlineBackgroundTaskRunner(),
            UiThreadDispatcher);
        return new SessionDetailViewModel(
            snapshot,
            SessionCoordinator,
            trackCoordinator,
            SessionStore,
            RecordedSessionProjection,
            new TestMapViewModelFactory(tileLayerService),
            Shell,
            DialogService,
            SessionPreferences,
            UiThreadDispatcher,
            deferDomainHandlingWhenInactive: isDesktop,
            ProcessingOptionCache,
            ProcessedTelemetryReader,
            analysisResultStateFactory,
            DerivationWindowCache,
            () => EditorFactory,
            extensionHost: new ExtensionHostDependencies(
                recordedSessionExtensionFactories ?? [],
                Substitute.For<IExtensionDatabaseConnection>(),
                Substitute.For<IRecordedSessionDataReader>(),
                new InlineBackgroundTaskRunner()));
    }
}
