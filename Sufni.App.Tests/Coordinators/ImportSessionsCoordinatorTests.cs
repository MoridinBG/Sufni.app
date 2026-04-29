using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Services.Management;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels;
using Sufni.Telemetry;
using static Sufni.App.Tests.Infrastructure.TestTelemetryFactories;

namespace Sufni.App.Tests.Coordinators;

public class ImportSessionsCoordinatorTests
{
    private readonly IDatabaseService database = Substitute.For<IDatabaseService>();
    private readonly ISessionStoreWriter sessionStore = Substitute.For<ISessionStoreWriter>();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private readonly RecordingBackgroundTaskRunner backgroundTaskRunner = new();
    private readonly IDaqManagementService daqManagementService = Substitute.For<IDaqManagementService>();

    /// <summary>
    /// Resolver for `ImportSessionsViewModel` is a `Func<T>` — the tests
    /// never actually want a real view model, so the default throws if
    /// invoked. Individual tests assert on the substituted shell's
    /// recorded forwarding of this resolver.
    /// </summary>
    private Func<ImportSessionsViewModel> importSessionsResolver =
        () => throw new InvalidOperationException(
            "The resolver should not be invoked directly from tests.");

    private ImportSessionsCoordinator CreateCoordinator() => new(
        database, sessionStore, shell, backgroundTaskRunner, daqManagementService, importSessionsResolver);

    // ----- OpenAsync -----

    [Fact]
    public async Task OpenAsync_ForwardsResolverToShellOpenOrFocus()
    {
        var coordinator = CreateCoordinator();

        await coordinator.OpenAsync();

        shell.Received(1).OpenOrFocus(
            Arg.Any<Func<ImportSessionsViewModel, bool>>(),
            importSessionsResolver);
    }

    // ----- ImportAsync argument-loading failures -----

    [Fact]
    public async Task ImportAsync_Throws_WhenSetupCannotBeLoaded()
    {
        var setupId = Guid.NewGuid();
        database.GetAsync<Setup>(setupId).Returns(Task.FromResult<Setup>(null!));

        var coordinator = CreateCoordinator();

        await Assert.ThrowsAsync<Exception>(() =>
            coordinator.ImportAsync(Array.Empty<ITelemetryFile>(), setupId));

        await database.DidNotReceive().GetAsync<Bike>(Arg.Any<Guid>());
    }

    [Fact]
    public async Task ImportAsync_Throws_WhenBikeCannotBeLoaded()
    {
        var setupId = Guid.NewGuid();
        var bikeId = Guid.NewGuid();
        var setup = new Setup(setupId, "setup") { BikeId = bikeId };
        database.GetAsync<Setup>(setupId).Returns(Task.FromResult(setup));
        database.GetAsync<Bike>(bikeId).Returns(Task.FromResult<Bike>(null!));

        var coordinator = CreateCoordinator();

        await Assert.ThrowsAsync<Exception>(() =>
            coordinator.ImportAsync(Array.Empty<ITelemetryFile>(), setupId));
    }

    // ----- ImportAsync BikeData construction -----

    [Fact]
    public async Task ImportAsync_ConstructsBikeDataFromHeadAngleAndSensorConfigs_AndForwardsIntoGeneratePsst()
    {
        var (setup, bike) = SeedSetupAndBike(headAngle: 64.5);
        var file = CreateTelemetryFile(shouldBeImported: true);

        BikeData? captured = null;
        file.GeneratePsstAsync(Arg.Any<BikeData>())
            .Returns(info =>
            {
                captured = info.Arg<BikeData>();
                return Task.FromResult(new byte[] { 1, 2, 3 });
            });

        var coordinator = CreateCoordinator();
        await coordinator.ImportAsync(new[] { file }, setup.Id);

        Assert.NotNull(captured);
        Assert.Equal(64.5, captured!.HeadAngle);
        // Front/rear sensor configs are null when the setup has no
        // sensor configuration JSON, so max-travel and measurement funcs
        // are null too.
        Assert.Null(captured.FrontMaxTravel);
        Assert.Null(captured.RearMaxTravel);
        Assert.Null(captured.FrontMeasurementToTravel);
        Assert.Null(captured.RearMeasurementToTravel);
    }

    // ----- ImportAsync per-file branches -----

    [Fact]
    public async Task ImportAsync_ShouldBeImportedTrue_GeneratesPsstWritesSessionUpsertsAndReports()
    {
        var (setup, bike) = SeedSetupAndBike();
        var psst = CreatePsst();
        var startTime = new DateTime(2025, 6, 1, 12, 34, 56, DateTimeKind.Utc);
        var file = CreateTelemetryFile(
            name: "ride-01",
            description: "morning lap",
            startTime: startTime,
            shouldBeImported: true);
        file.GeneratePsstAsync(Arg.Any<BikeData>()).Returns(Task.FromResult(psst));

        var progressEvents = new List<SessionImportEvent>();
        var progress = new ProgressCapture(progressEvents);

        var coordinator = CreateCoordinator();
        var result = await coordinator.ImportAsync(new[] { file }, setup.Id, progress);

        // Session persisted with the expected metadata.
        var expectedTimestamp = new DateTimeOffset(startTime).ToUnixTimeSeconds();
        await database.Received(1).PutSessionAsync(Arg.Is<Session>(s =>
            s.Name == "ride-01" &&
            s.Description == "morning lap" &&
            s.Setup == setup.Id &&
            s.Timestamp == expectedTimestamp &&
            s.ProcessedData == psst &&
            s.FullTrack == null));
        await database.DidNotReceive().PutAsync(Arg.Any<Track>());
        await file.Received(1).OnImported();
        await file.DidNotReceive().OnTrashed();

        // Store upsert and result list.
        sessionStore.Received(1).Upsert(Arg.Is<SessionSnapshot>(s =>
            s.Name == "ride-01" && s.SetupId == setup.Id && s.HasProcessedData));
        Assert.Single(result.Imported);
        Assert.Empty(result.Failures);

        // Progress reported.
        var nonProgressEvents = progressEvents.Where(e => e is not SessionImportEvent.Progress).ToList();
        var imported = Assert.Single(nonProgressEvents);
        Assert.IsType<SessionImportEvent.Imported>(imported);
        Assert.Contains(progressEvents, e => e is SessionImportEvent.Progress { Current: 1, Total: 1 });
    }

    [Fact]
    public async Task ImportAsync_WithGpsData_PersistsTrackAndAssociatesSession()
    {
        var (setup, _) = SeedSetupAndBike();
        var gpsRecords = new[]
        {
            new GpsRecord(new DateTime(2025, 6, 1, 12, 34, 56, DateTimeKind.Utc), 42.6977, 23.3219, 590f, 5f, 180f, 3, 10, 1f, 2f),
            new GpsRecord(new DateTime(2025, 6, 1, 12, 34, 57, DateTimeKind.Utc), 42.6978, 23.3220, 591f, 5.5f, 182f, 3, 10, 1f, 2f)
        };

        var psst = CreatePsst(gpsRecords);
        var file = CreateTelemetryFile(name: "ride-gps", shouldBeImported: true);
        file.GeneratePsstAsync(Arg.Any<BikeData>()).Returns(Task.FromResult(psst));

        var coordinator = CreateCoordinator();
        var result = await coordinator.ImportAsync(new[] { file }, setup.Id);

        await database.Received(1).PutAsync(Arg.Is<Track>(track => track.Points.Count == 2));
        await database.Received(1).PutSessionAsync(Arg.Is<Session>(s =>
            s.Name == "ride-gps" &&
            s.ProcessedData == psst &&
            s.FullTrack != null));
        sessionStore.Received(1).Upsert(Arg.Is<SessionSnapshot>(s =>
            s.Name == "ride-gps" &&
            s.FullTrackId != null));
        Assert.Single(result.Imported);
        Assert.NotNull(result.Imported[0].FullTrackId);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public async Task ImportAsync_ShouldBeImportedNull_CallsOnTrashed_AndDoesNotCreateSession()
    {
        var (setup, _) = SeedSetupAndBike();
        var file = CreateTelemetryFile(shouldBeImported: null);

        var coordinator = CreateCoordinator();
        var result = await coordinator.ImportAsync(new[] { file }, setup.Id);

        await file.Received(1).OnTrashed();
        await file.DidNotReceive().GeneratePsstAsync(Arg.Any<BikeData>());
        await database.DidNotReceive().PutSessionAsync(Arg.Any<Session>());
        sessionStore.DidNotReceiveWithAnyArgs().Upsert(default!);
        Assert.Empty(result.Imported);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public async Task ImportAsync_ShouldBeImportedFalse_IsLeftAlone()
    {
        var (setup, _) = SeedSetupAndBike();
        var file = CreateTelemetryFile(shouldBeImported: false);

        var coordinator = CreateCoordinator();
        var result = await coordinator.ImportAsync(new[] { file }, setup.Id);

        await file.DidNotReceive().OnTrashed();
        await file.DidNotReceive().OnImported();
        await file.DidNotReceive().GeneratePsstAsync(Arg.Any<BikeData>());
        await database.DidNotReceive().PutSessionAsync(Arg.Any<Session>());
        Assert.Empty(result.Imported);
        Assert.Empty(result.Failures);
    }

    // ----- ImportAsync per-file failure handling -----

    [Fact]
    public async Task ImportAsync_GeneratePsstThrows_CapturedInFailures_AndReportedAsFailed()
    {
        var (setup, _) = SeedSetupAndBike();
        var file = CreateTelemetryFile(name: "broken", shouldBeImported: true);
        file.GeneratePsstAsync(Arg.Any<BikeData>())
            .ThrowsAsync(new InvalidOperationException("boom"));

        var progressEvents = new List<SessionImportEvent>();
        var progress = new ProgressCapture(progressEvents);

        var coordinator = CreateCoordinator();
        var result = await coordinator.ImportAsync(new[] { file }, setup.Id, progress);

        Assert.Empty(result.Imported);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("broken", failure.FileName);
        await database.DidNotReceive().PutSessionAsync(Arg.Any<Session>());
        sessionStore.DidNotReceiveWithAnyArgs().Upsert(default!);

        var nonProgressEvents = progressEvents.Where(e => e is not SessionImportEvent.Progress).ToList();
        var reported = Assert.Single(nonProgressEvents);
        var failed = Assert.IsType<SessionImportEvent.Failed>(reported);
        Assert.Equal("broken", failed.FileName);
    }

    [Fact]
    public async Task ImportAsync_PutSessionThrows_CapturedInFailures_AndReportedAsFailed()
    {
        var (setup, _) = SeedSetupAndBike();
        var file = CreateTelemetryFile(name: "persist-fail", shouldBeImported: true);
        file.GeneratePsstAsync(Arg.Any<BikeData>()).Returns(Task.FromResult(CreatePsst()));
        database.PutSessionAsync(Arg.Any<Session>())
            .ThrowsAsync(new InvalidOperationException("db"));

        var coordinator = CreateCoordinator();
        var result = await coordinator.ImportAsync(new[] { file }, setup.Id);

        Assert.Empty(result.Imported);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("persist-fail", failure.FileName);
        await file.DidNotReceive().OnImported();
        sessionStore.DidNotReceiveWithAnyArgs().Upsert(default!);
    }

    [Fact]
    public async Task ImportAsync_OnImportedThrows_CapturedInFailures()
    {
        var (setup, _) = SeedSetupAndBike();
        var file = CreateTelemetryFile(name: "post-import-fail", shouldBeImported: true);
        file.GeneratePsstAsync(Arg.Any<BikeData>()).Returns(Task.FromResult(CreatePsst()));
        file.OnImported().ThrowsAsync(new InvalidOperationException("post"));

        var coordinator = CreateCoordinator();
        var result = await coordinator.ImportAsync(new[] { file }, setup.Id);

        Assert.Empty(result.Imported);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("post-import-fail", failure.FileName);
        sessionStore.DidNotReceiveWithAnyArgs().Upsert(default!);
    }

    [Fact]
    public async Task ImportAsync_OnTrashedThrows_CapturedInFailures()
    {
        var (setup, _) = SeedSetupAndBike();
        var file = CreateTelemetryFile(name: "trash-fail", shouldBeImported: null);
        file.OnTrashed().ThrowsAsync(new InvalidOperationException("trash"));

        var progressEvents = new List<SessionImportEvent>();
        var progress = new ProgressCapture(progressEvents);

        var coordinator = CreateCoordinator();
        var result = await coordinator.ImportAsync(new[] { file }, setup.Id, progress);

        var failure = Assert.Single(result.Failures);
        Assert.Equal("trash-fail", failure.FileName);
        var nonProgressEvents = progressEvents.Where(e => e is not SessionImportEvent.Progress).ToList();
        var reported = Assert.Single(nonProgressEvents);
        var failed = Assert.IsType<SessionImportEvent.Failed>(reported);
        Assert.Equal("trash-fail", failed.FileName);
    }

    [Fact]
    public async Task ImportAsync_ContinuesAfterPerFileFailure()
    {
        var (setup, _) = SeedSetupAndBike();

        var brokenFile = CreateTelemetryFile(name: "broken", shouldBeImported: true);
        brokenFile.GeneratePsstAsync(Arg.Any<BikeData>())
            .ThrowsAsync(new InvalidOperationException("boom"));

        var goodFile = CreateTelemetryFile(name: "ok", shouldBeImported: true);
        goodFile.GeneratePsstAsync(Arg.Any<BikeData>()).Returns(Task.FromResult(CreatePsst()));

        var coordinator = CreateCoordinator();
        var result = await coordinator.ImportAsync(new[] { brokenFile, goodFile }, setup.Id);

        Assert.Single(result.Failures);
        Assert.Single(result.Imported);
        Assert.Equal("ok", result.Imported[0].Name);
        await database.Received(1).PutSessionAsync(Arg.Is<Session>(s => s.Name == "ok"));
        sessionStore.Received(1).Upsert(Arg.Is<SessionSnapshot>(s => s.Name == "ok"));
    }

    [Fact]
    public async Task ImportAsync_MalformedFile_IsRejectedWithoutParsing()
    {
        var (setup, _) = SeedSetupAndBike();
        var file = CreateTelemetryFile(
            name: "bad",
            shouldBeImported: true,
            malformedMessage: "invalid telemetry payload");

        var progressEvents = new List<SessionImportEvent>();
        var progress = new ProgressCapture(progressEvents);

        var coordinator = CreateCoordinator();
        var result = await coordinator.ImportAsync(new[] { file }, setup.Id, progress);

        Assert.Empty(result.Imported);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("bad", failure.FileName);
        await file.DidNotReceive().GeneratePsstAsync(Arg.Any<BikeData>());
        await file.DidNotReceive().OnImported();

        var nonProgressEvents = progressEvents.Where(e => e is not SessionImportEvent.Progress).ToList();
        var reported = Assert.Single(nonProgressEvents);
        var failed = Assert.IsType<SessionImportEvent.Failed>(reported);
        Assert.Equal("bad", failed.FileName);
    }

    [Fact]
    public async Task ImportAsync_ImportableMalformedFile_IsImported()
    {
        var (setup, _) = SeedSetupAndBike();
        var file = CreateTelemetryFile(
            name: "trimmed",
            shouldBeImported: true,
            malformedMessage: "trailing chunk was trimmed",
            canImport: true);
        file.GeneratePsstAsync(Arg.Any<BikeData>()).Returns(Task.FromResult(CreatePsst()));

        var coordinator = CreateCoordinator();
        var result = await coordinator.ImportAsync(new[] { file }, setup.Id);

        Assert.Single(result.Imported);
        Assert.Empty(result.Failures);
        await file.Received(1).GeneratePsstAsync(Arg.Any<BikeData>());
        await file.Received(1).OnImported();
    }

    [Fact]
    public async Task ImportAsync_ReportsProgressForEachProcessedFile_IgnoringSkipped()
    {
        var (setup, _) = SeedSetupAndBike();
        var psst = CreatePsst();

        var importedFile = CreateTelemetryFile(name: "import-me", shouldBeImported: true);
        importedFile.GeneratePsstAsync(Arg.Any<BikeData>()).Returns(Task.FromResult(psst));

        var trashedFile = CreateTelemetryFile(name: "trash-me", shouldBeImported: null);
        var ignoredFile = CreateTelemetryFile(name: "leave-me", shouldBeImported: false);

        var progressEvents = new List<SessionImportEvent>();
        var progress = new ProgressCapture(progressEvents);

        var coordinator = CreateCoordinator();
        await coordinator.ImportAsync(
            new[] { importedFile, ignoredFile, trashedFile },
            setup.Id,
            progress);

        var progressUpdates = progressEvents.OfType<SessionImportEvent.Progress>().ToList();
        Assert.Equal(2, progressUpdates.Count);
        Assert.All(progressUpdates, p => Assert.Equal(2, p.Total));
        Assert.Equal(1, progressUpdates[0].Current);
        Assert.Equal(2, progressUpdates[1].Current);
    }

    [Fact]
    public async Task ImportAsync_RoutesWorkflowThroughBackgroundTaskRunner()
    {
        var (setup, _) = SeedSetupAndBike();

        var coordinator = CreateCoordinator();
        await coordinator.ImportAsync(Array.Empty<ITelemetryFile>(), setup.Id);

        Assert.Equal(1, backgroundTaskRunner.InvocationCount);
    }

    // ----- ImportAsync NetworkTelemetryFile session lifecycle -----

    [Fact]
    public async Task ImportAsync_OpensOneSessionPerNetworkEndpoint_RoutesTrashThroughSession_AndDisposes()
    {
        var (setup, _) = SeedSetupAndBike();

        var endpointA = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 1557);
        var endpointB = new IPEndPoint(IPAddress.Parse("10.0.0.2"), 1557);

        var sessionA = Substitute.For<IDaqManagementSession>();
        var sessionB = Substitute.For<IDaqManagementSession>();

        daqManagementService.OpenSessionAsync("10.0.0.1", 1557, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(sessionA));
        daqManagementService.OpenSessionAsync("10.0.0.2", 1557, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(sessionB));

        sessionA.TrashFileAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DaqManagementResult>(new DaqManagementResult.Ok()));
        sessionB.TrashFileAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DaqManagementResult>(new DaqManagementResult.Ok()));

        var fileA1 = new NetworkTelemetryFile(endpointA, daqManagementService, 1, "00001.SST", 3,
            DateTimeOffset.FromUnixTimeSeconds(111), TimeSpan.FromSeconds(6))
        { ShouldBeImported = null };
        var fileA2 = new NetworkTelemetryFile(endpointA, daqManagementService, 2, "00002.SST", 3,
            DateTimeOffset.FromUnixTimeSeconds(222), TimeSpan.FromSeconds(6))
        { ShouldBeImported = null };
        var fileB = new NetworkTelemetryFile(endpointB, daqManagementService, 3, "00003.SST", 3,
            DateTimeOffset.FromUnixTimeSeconds(333), TimeSpan.FromSeconds(6))
        { ShouldBeImported = null };

        var coordinator = CreateCoordinator();
        await coordinator.ImportAsync(new ITelemetryFile[] { fileA1, fileA2, fileB }, setup.Id);

        await daqManagementService.Received(1)
            .OpenSessionAsync("10.0.0.1", 1557, Arg.Any<CancellationToken>());
        await daqManagementService.Received(1)
            .OpenSessionAsync("10.0.0.2", 1557, Arg.Any<CancellationToken>());

        await sessionA.Received(1).TrashFileAsync(1, Arg.Any<CancellationToken>());
        await sessionA.Received(1).TrashFileAsync(2, Arg.Any<CancellationToken>());
        await sessionB.Received(1).TrashFileAsync(3, Arg.Any<CancellationToken>());

        await daqManagementService.DidNotReceiveWithAnyArgs()
            .TrashFileAsync(default!, default, default, default);

        await sessionA.Received(1).DisposeAsync();
        await sessionB.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task ImportAsync_RoutesGetFileThroughSession_AndDisposes_WhenGetFileReturnsError()
    {
        var (setup, _) = SeedSetupAndBike();

        var endpoint = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 1557);
        var session = Substitute.For<IDaqManagementSession>();

        daqManagementService.OpenSessionAsync("10.0.0.1", 1557, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(session));
        session.GetFileAsync(Arg.Any<DaqFileClass>(), Arg.Any<int>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DaqGetFileResult>(
                new DaqGetFileResult.Error(DaqManagementErrorCode.Busy, "Device busy")));

        var file = new NetworkTelemetryFile(endpoint, daqManagementService, 1, "00001.SST", 3,
            DateTimeOffset.FromUnixTimeSeconds(111), TimeSpan.FromSeconds(6))
        { ShouldBeImported = true };

        var coordinator = CreateCoordinator();
        var result = await coordinator.ImportAsync(new ITelemetryFile[] { file }, setup.Id);

        await session.Received(1)
            .GetFileAsync(DaqFileClass.RootSst, 1, Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        await daqManagementService.DidNotReceiveWithAnyArgs()
            .GetFileAsync(default!, default, default!, default, default!, default);
        await session.Received(1).DisposeAsync();

        Assert.Single(result.Failures);
    }

    [Fact]
    public async Task ImportAsync_DisposesSessions_EvenWhenSessionThrows()
    {
        var (setup, _) = SeedSetupAndBike();

        var endpoint = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 1557);
        var session = Substitute.For<IDaqManagementSession>();

        daqManagementService.OpenSessionAsync("10.0.0.1", 1557, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(session));
        session.GetFileAsync(Arg.Any<DaqFileClass>(), Arg.Any<int>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("boom"));

        var file = new NetworkTelemetryFile(endpoint, daqManagementService, 1, "00001.SST", 3,
            DateTimeOffset.FromUnixTimeSeconds(111), TimeSpan.FromSeconds(6))
        { ShouldBeImported = true };

        var coordinator = CreateCoordinator();
        var result = await coordinator.ImportAsync(new ITelemetryFile[] { file }, setup.Id);

        Assert.Single(result.Failures);
        await session.Received(1).DisposeAsync();
    }

    // ----- helpers -----

    private (Setup setup, Bike bike) SeedSetupAndBike(double headAngle = 65.0)
    {
        var bikeId = Guid.NewGuid();
        var setupId = Guid.NewGuid();
        var bike = new Bike(bikeId, "test bike") { HeadAngle = headAngle, ForkStroke = 160 };
        var setup = new Setup(setupId, "test setup") { BikeId = bikeId };
        database.GetAsync<Setup>(setupId).Returns(Task.FromResult(setup));
        database.GetAsync<Bike>(bikeId).Returns(Task.FromResult(bike));
        return (setup, bike);
    }

    private static byte[] CreatePsst(params GpsRecord[] gpsRecords)
    {
        var telemetryData = CreateTelemetryData();
        telemetryData.GpsData = gpsRecords.Length > 0 ? gpsRecords : null;
        return telemetryData.BinaryForm;
    }

    /// <summary>
    /// `IProgress<T>.Report` is void — using a capture list lets tests
    /// assert on the sequence of progress events synchronously without
    /// an NSubstitute stub.
    /// </summary>
    private sealed class ProgressCapture(List<SessionImportEvent> events) : IProgress<SessionImportEvent>
    {
        public void Report(SessionImportEvent value) => events.Add(value);
    }
}
