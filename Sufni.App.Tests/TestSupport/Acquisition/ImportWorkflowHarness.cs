using Sufni.App.MapsAndTracks.Stores;
using System.Reactive.Subjects;
using System.Reactive.Linq;
using Sufni.App.Tests.TestSupport.Harness;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.Time.Testing;
using System.Collections.ObjectModel;
using DynamicData;
using Sufni.App.Shared.Stores;
using NSubstitute;
using Sufni.App.Acquisition.Coordinators;
using Sufni.App.Acquisition.Models;
using Sufni.App.Acquisition.Services;
using Sufni.App.Acquisition.Services.Management;
using Sufni.App.Acquisition.ViewModels;
using Sufni.App.Bikes.Models;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Services;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Coordinators;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.Stores;
using Sufni.App.Shell.Coordinators;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Tests.TestSupport.Async;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Persistence;
using Sufni.Telemetry;

namespace Sufni.App.Tests.TestSupport.Acquisition;

internal sealed class ImportWorkflowHarness
{
    public ImportWorkflowHarness()
    {
        TelemetryDataStoreService.DataStores.Returns(DataStores);
        TelemetryDataStoreService.LoadFilesAsync(Arg.Any<ITelemetryDataStore>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TelemetryFileListing>(new TelemetryFileListing.Listed([], null)));
        ImportSessionsCoordinatorSubstitute.State.Returns(_ => ImportState.Value);
        ImportSessionsCoordinatorSubstitute.Observe().Returns(ImportState);
        ImportSessionsCoordinatorSubstitute.Sources.Returns(new ReadOnlyObservableCollection<ITelemetryDataStore>(DataStores));
        ImportSessionsCoordinatorSubstitute.ObserveSources(Arg.Any<Action<DataStoreDiscoveryIssue>>())
            .Returns(call => SourceIssues.Subscribe(call.Arg<Action<DataStoreDiscoveryIssue>>()));
        FilesService.OpenDataStoreFolderAsync().Returns(Task.FromResult<IStorageFolder?>(null));
        ConfigureDefaultReprocessor();
        ConfigureDefaultWriter();
    }

    public Subject<DataStoreDiscoveryIssue> SourceIssues { get; } = new();

    public ITrackStoreWriter TrackStore { get; } = Substitute.For<ITrackStoreWriter>();

    public StorePublication Publication { get; } = new(new InlineUiThreadDispatcher());

    public BehaviorSubject<SessionImportState> ImportState { get; } = new(new SessionImportState.Idle());

    public ISessionRepository SessionRepository { get; } = Substitute.For<ISessionRepository>();
    public ITrackRepository TrackRepository { get; } = Substitute.For<ITrackRepository>();
    public ISynchronizableRepository<Setup> SetupRepository { get; } = Substitute.For<ISynchronizableRepository<Setup>>();
    public ISynchronizableRepository<Bike> BikeRepository { get; } = Substitute.For<ISynchronizableRepository<Bike>>();
    public ISessionStoreWriter SessionStore { get; } = Substitute.For<ISessionStoreWriter>();
    public IRecordedSessionSourceStoreWriter SourceStore { get; } = Substitute.For<IRecordedSessionSourceStoreWriter>();
    public RecordingBackgroundTaskRunner BackgroundTaskRunner { get; } = new();
    public IRecordedSessionReprocessor Reprocessor { get; } = Substitute.For<IRecordedSessionReprocessor>();
    public ITelemetryDataStoreService TelemetryDataStoreService { get; } = Substitute.For<ITelemetryDataStoreService>();
    public IFilesService FilesService { get; } = Substitute.For<IFilesService>();
    public IShellCoordinator Shell { get; } = Substitute.For<IShellCoordinator>();
    public IDialogService DialogService { get; } = Substitute.For<IDialogService>();
    public ISetupCoordinator SetupCoordinator { get; } = TestCoordinatorSubstitutes.Setup();
    public IImportSessionsCoordinator ImportSessionsCoordinatorSubstitute { get; } = TestCoordinatorSubstitutes.ImportSessions();
    public SetupBoardQueryStub SetupQuery { get; } = new();
    public ObservableCollection<ITelemetryDataStore> DataStores { get; } = [];

    public ImportSessionsCoordinator CreateCoordinator(
        ISessionRepository? repository = null,
        ISessionStoreWriter? sessionStore = null,
        IRecordedSessionSourceStoreWriter? sourceStore = null,
        ITrackStoreWriter? trackStore = null,
        IAppEnvironment? environment = null) => new(
        repository ?? SessionRepository,
        TrackRepository,
        SetupRepository,
        BikeRepository,
        sessionStore ?? SessionStore,
        sourceStore ?? SourceStore,
        BackgroundTaskRunner,
        Reprocessor,
        new InlineUiThreadDispatcher(),
        Publication,
        trackStore ?? TrackStore,
        TelemetryDataStoreService,
        FilesService,
        environment ?? TestAppEnvironment.Create(UiLayoutProfile.Workspace));

    public ImportSessionsViewModel CreateViewModel(IUiThreadDispatcher? dispatcher = null) => new(
        Shell,
        DialogService,
        ImportSessionsCoordinatorSubstitute,
        SetupQuery,
        dispatcher ?? new InlineUiThreadDispatcher(), timeProvider: new FakeTimeProvider());

    public (Setup Setup, Bike Bike) SeedSetupAndBike(double headAngle = 65.0)
    {
        var bikeId = Guid.NewGuid();
        var setupId = Guid.NewGuid();
        var bike = new Bike(bikeId, "test bike") { HeadAngle = headAngle, ForkStroke = 160 };
        var setup = new Setup(setupId, "test setup") { BikeId = bikeId };
        SetupRepository.GetAsync(setupId).Returns(Task.FromResult<Setup?>(setup));
        BikeRepository.GetAsync(bikeId).Returns(Task.FromResult<Bike?>(bike));
        SetupQuery.Add(TestSnapshots.Setup(id: setupId, bikeId: bikeId));
        return (setup, bike);
    }

    public static RecordedSessionReprocessResult CreateReprocessResult(
        RecordedSessionDerivationInput input,
        TelemetryData? telemetryData = null,
        Track? generatedFullTrack = null)
    {
        var data = telemetryData ?? TestTelemetryData.CreateMinimal();
        var fingerprint = new ProcessingFingerprint(
            SchemaVersion: 2,
            ProcessingVersion: 1,
            SetupId: input.Setup.Id,
            BikeId: input.Bike.Id,
            TrackProjectionVersion: 1,
            DependencyHash: "dependency",
            SourceHash: input.Source.SourceHash);
        return new RecordedSessionReprocessResult.Processed(
            ProcessedGeneration.Create(new ProcessedTelemetryPayload(
                data,
                data.BinaryForm,
                AppJson.Serialize(fingerprint)), generatedFullTrack?.Points),
            fingerprint);
    }

    private void ConfigureDefaultReprocessor()
    {
        Reprocessor
            .ReprocessAsync(Arg.Any<RecordedSessionDerivationInput>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var domain = callInfo.ArgAt<RecordedSessionDerivationInput>(0);
                return Task.FromResult(CreateReprocessResult(domain));
            });
    }

    private void ConfigureDefaultWriter()
    {
        SessionRepository
            .ImportProcessedSessionAsync(
                Arg.Any<Session>(),
                Arg.Any<Track?>(),
                Arg.Any<RecordedSessionSource>())
            .Returns(callInfo =>
            {
                var session = callInfo.ArgAt<Session>(0);
                var track = callInfo.ArgAt<Track?>(1);
                session.Updated = 10;
                return Task.FromResult<SessionImportCommitResult>(new SessionImportCommitResult.Inserted(
                    session, RecordedSessionSourceSnapshot.From(callInfo.Arg<RecordedSessionSource>())));
            });
    }
}
