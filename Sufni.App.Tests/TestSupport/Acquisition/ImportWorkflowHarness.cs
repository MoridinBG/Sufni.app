using System.Collections.ObjectModel;
using DynamicData;
using NSubstitute;
using Sufni.App.Acquisition.Coordinators;
using Sufni.App.Acquisition.Models;
using Sufni.App.Acquisition.Services;
using Sufni.App.Acquisition.Services.Management;
using Sufni.App.Acquisition.ViewModels;
using Sufni.App.Bikes.Models;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
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
            .Returns(Task.FromResult<IReadOnlyList<ITelemetryFile>>([]));
        SetupStore.Connect().Returns(SetupCache.Connect());
        SetupStore.FindByBoardId(Arg.Any<Guid>())
            .Returns(callInfo => SetupCache.Items.FirstOrDefault(s => s.BoardId == callInfo.Arg<Guid>()));
        ImportSessionsCoordinatorSubstitute.ImportAsync(
                Arg.Any<IReadOnlyList<ITelemetryFile>>(),
                Arg.Any<Guid>(),
                Arg.Any<IProgress<SessionImportEvent>?>())
            .Returns(Task.FromResult(new SessionImportResult([], [])));
        ConfigureDefaultReprocessor();
        ConfigureDefaultWriter();
    }

    public ISessionTelemetryWriter SessionTelemetryWriter { get; } = Substitute.For<ISessionTelemetryWriter>();
    public ISynchronizableRepository<Setup> SetupRepository { get; } = Substitute.For<ISynchronizableRepository<Setup>>();
    public ISynchronizableRepository<Bike> BikeRepository { get; } = Substitute.For<ISynchronizableRepository<Bike>>();
    public ISessionStoreWriter SessionStore { get; } = Substitute.For<ISessionStoreWriter>();
    public IRecordedSessionSourceStoreWriter SourceStore { get; } = Substitute.For<IRecordedSessionSourceStoreWriter>();
    public RecordingBackgroundTaskRunner BackgroundTaskRunner { get; } = new();
    public IDaqManagementService DaqManagementService { get; } = Substitute.For<IDaqManagementService>();
    public IRecordedSessionReprocessor Reprocessor { get; } = Substitute.For<IRecordedSessionReprocessor>();
    public IEditorFactory EditorFactory { get; } = Substitute.For<IEditorFactory>();
    public ITelemetryDataStoreService TelemetryDataStoreService { get; } = Substitute.For<ITelemetryDataStoreService>();
    public IFilesService FilesService { get; } = Substitute.For<IFilesService>();
    public IShellCoordinator Shell { get; } = Substitute.For<IShellCoordinator>();
    public IDialogService DialogService { get; } = Substitute.For<IDialogService>();
    public ISetupCoordinator SetupCoordinator { get; } = TestCoordinatorSubstitutes.Setup();
    public IImportSessionsCoordinator ImportSessionsCoordinatorSubstitute { get; } = TestCoordinatorSubstitutes.ImportSessions();
    public ISetupStore SetupStore { get; } = Substitute.For<ISetupStore>();
    public ObservableCollection<ITelemetryDataStore> DataStores { get; } = [];
    public SourceCache<SetupSnapshot, Guid> SetupCache { get; } = new(s => s.Id);

    public ImportSessionsCoordinator CreateCoordinator() => new(
        SessionTelemetryWriter,
        SetupRepository,
        BikeRepository,
        SessionStore,
        SourceStore,
        BackgroundTaskRunner,
        DaqManagementService,
        Reprocessor,
        EditorFactory);

    public ImportSessionsViewModel CreateViewModel() => new(
        TelemetryDataStoreService,
        FilesService,
        Shell,
        DialogService,
        SetupCoordinator,
        ImportSessionsCoordinatorSubstitute,
        SetupStore,
        new InlineUiThreadDispatcher());

    public (Setup Setup, Bike Bike) SeedSetupAndBike(double headAngle = 65.0)
    {
        var bikeId = Guid.NewGuid();
        var setupId = Guid.NewGuid();
        var bike = new Bike(bikeId, "test bike") { HeadAngle = headAngle, ForkStroke = 160 };
        var setup = new Setup(setupId, "test setup") { BikeId = bikeId };
        SetupRepository.GetAsync(setupId).Returns(Task.FromResult<Setup?>(setup));
        BikeRepository.GetAsync(bikeId).Returns(Task.FromResult<Bike?>(bike));
        SetupCache.AddOrUpdate(TestSnapshots.Setup(id: setupId, bikeId: bikeId));
        return (setup, bike);
    }

    public ProgressCapture<SessionImportEvent> CaptureImportProgress(List<SessionImportEvent>? events = null) => new(events ?? []);

    public static RecordedSessionReprocessResult CreateReprocessResult(
        RecordedSessionDomainSnapshot domain,
        RecordedSessionSource source,
        TelemetryData? telemetryData = null,
        Track? generatedFullTrack = null)
    {
        var data = telemetryData ?? TestTelemetryData.CreateMinimal();
        var fingerprint = new ProcessingFingerprint(
            SchemaVersion: 2,
            ProcessingVersion: 1,
            SetupId: domain.Setup!.Id,
            BikeId: domain.Bike!.Id,
            TrackProjectionVersion: 1,
            DependencyHash: "dependency",
            SourceHash: source.SourceHash);
        return new RecordedSessionReprocessResult(
            new ProcessedTelemetryPayload(
                data,
                data.BinaryForm,
                AppJson.Serialize(fingerprint)),
            generatedFullTrack,
            fingerprint);
    }

    private void ConfigureDefaultReprocessor()
    {
        Reprocessor
            .ProcessImportedSstAsync(
                Arg.Any<RecordedSessionDomainSnapshot>(),
                Arg.Any<RecordedSessionSource>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var domain = callInfo.ArgAt<RecordedSessionDomainSnapshot>(0);
                var source = callInfo.ArgAt<RecordedSessionSource>(1);
                return Task.FromResult(CreateReprocessResult(domain, source));
            });
    }

    private void ConfigureDefaultWriter()
    {
        SessionTelemetryWriter
            .PutProcessedSessionAsync(
                Arg.Any<Session>(),
                Arg.Any<ProcessedTelemetryPayload>(),
                Arg.Any<Track?>(),
                Arg.Any<RecordedSessionSource?>())
            .Returns(callInfo =>
            {
                var session = callInfo.ArgAt<Session>(0);
                var payload = callInfo.ArgAt<ProcessedTelemetryPayload>(1);
                var track = callInfo.ArgAt<Track?>(2);
                session.ProcessedData = payload.Data;
                session.ProcessingFingerprintJson = payload.FingerprintJson;
                session.FullTrack = track?.Id;
                session.HasProcessedData = session.ProcessedData is not null;
                session.Updated = 10;
                return Task.FromResult(session);
            });
    }
}

internal sealed class ProgressCapture<T>(List<T> events) : IProgress<T>
{
    public List<T> Events { get; } = events;

    public void Report(T value) => Events.Add(value);
}
