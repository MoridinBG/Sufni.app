using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.Services;
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
    private readonly ITelemetryDataStore dataStore = Substitute.For<ITelemetryDataStore>();

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
        database, sessionStore, shell, backgroundTaskRunner, importSessionsResolver);

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
            coordinator.ImportAsync(dataStore, Array.Empty<ITelemetryFile>(), setupId));

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
            coordinator.ImportAsync(dataStore, Array.Empty<ITelemetryFile>(), setupId));
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
        await coordinator.ImportAsync(dataStore, new[] { file }, setup.Id);

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
        var psst = new byte[] { 9, 8, 7 };
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
        var result = await coordinator.ImportAsync(dataStore, new[] { file }, setup.Id, progress);

        // Session persisted with the expected metadata.
        var expectedTimestamp = new DateTimeOffset(startTime).ToUnixTimeSeconds();
        await database.Received(1).PutSessionAsync(Arg.Is<Session>(s =>
            s.Name == "ride-01" &&
            s.Description == "morning lap" &&
            s.Setup == setup.Id &&
            s.Timestamp == expectedTimestamp &&
            s.ProcessedData == psst));
        await file.Received(1).OnImported();
        await file.DidNotReceive().OnTrashed();

        // Store upsert and result list.
        sessionStore.Received(1).Upsert(Arg.Is<SessionSnapshot>(s =>
            s.Name == "ride-01" && s.SetupId == setup.Id && s.HasProcessedData));
        Assert.Single(result.Imported);
        Assert.Empty(result.Failures);

        // Progress reported.
        var imported = Assert.Single(progressEvents);
        Assert.IsType<SessionImportEvent.Imported>(imported);
    }

    [Fact]
    public async Task ImportAsync_ShouldBeImportedNull_CallsOnTrashed_AndDoesNotCreateSession()
    {
        var (setup, _) = SeedSetupAndBike();
        var file = CreateTelemetryFile(shouldBeImported: null);

        var coordinator = CreateCoordinator();
        var result = await coordinator.ImportAsync(dataStore, new[] { file }, setup.Id);

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
        var result = await coordinator.ImportAsync(dataStore, new[] { file }, setup.Id);

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
        var result = await coordinator.ImportAsync(dataStore, new[] { file }, setup.Id, progress);

        Assert.Empty(result.Imported);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("broken", failure.FileName);
        await database.DidNotReceive().PutSessionAsync(Arg.Any<Session>());
        sessionStore.DidNotReceiveWithAnyArgs().Upsert(default!);

        var reported = Assert.Single(progressEvents);
        var failed = Assert.IsType<SessionImportEvent.Failed>(reported);
        Assert.Equal("broken", failed.FileName);
    }

    [Fact]
    public async Task ImportAsync_PutSessionThrows_CapturedInFailures_AndReportedAsFailed()
    {
        var (setup, _) = SeedSetupAndBike();
        var file = CreateTelemetryFile(name: "persist-fail", shouldBeImported: true);
        file.GeneratePsstAsync(Arg.Any<BikeData>()).Returns(Task.FromResult(new byte[] { 1 }));
        database.PutSessionAsync(Arg.Any<Session>())
            .ThrowsAsync(new InvalidOperationException("db"));

        var coordinator = CreateCoordinator();
        var result = await coordinator.ImportAsync(dataStore, new[] { file }, setup.Id);

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
        file.GeneratePsstAsync(Arg.Any<BikeData>()).Returns(Task.FromResult(new byte[] { 1 }));
        file.OnImported().ThrowsAsync(new InvalidOperationException("post"));

        var coordinator = CreateCoordinator();
        var result = await coordinator.ImportAsync(dataStore, new[] { file }, setup.Id);

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
        var result = await coordinator.ImportAsync(dataStore, new[] { file }, setup.Id, progress);

        var failure = Assert.Single(result.Failures);
        Assert.Equal("trash-fail", failure.FileName);
        var reported = Assert.Single(progressEvents);
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
        goodFile.GeneratePsstAsync(Arg.Any<BikeData>()).Returns(Task.FromResult(new byte[] { 2 }));

        var coordinator = CreateCoordinator();
        var result = await coordinator.ImportAsync(dataStore, new[] { brokenFile, goodFile }, setup.Id);

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
            malformed: true,
            malformedMessage: "invalid telemetry payload");

        var progressEvents = new List<SessionImportEvent>();
        var progress = new ProgressCapture(progressEvents);

        var coordinator = CreateCoordinator();
        var result = await coordinator.ImportAsync(dataStore, new[] { file }, setup.Id, progress);

        Assert.Empty(result.Imported);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("bad", failure.FileName);
        await file.DidNotReceive().GeneratePsstAsync(Arg.Any<BikeData>());
        await file.DidNotReceive().OnImported();

        var reported = Assert.Single(progressEvents);
        var failed = Assert.IsType<SessionImportEvent.Failed>(reported);
        Assert.Equal("bad", failed.FileName);
    }

    [Fact]
    public async Task ImportAsync_RoutesWorkflowThroughBackgroundTaskRunner()
    {
        var (setup, _) = SeedSetupAndBike();

        var coordinator = CreateCoordinator();
        await coordinator.ImportAsync(dataStore, Array.Empty<ITelemetryFile>(), setup.Id);

        Assert.Equal(1, backgroundTaskRunner.InvocationCount);
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
