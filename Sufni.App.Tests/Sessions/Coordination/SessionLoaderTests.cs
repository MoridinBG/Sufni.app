using Sufni.App.MapsAndTracks.Stores;
using Sufni.App.Shared.Models;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sufni.App.Bikes.Models;
using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Store;
using Sufni.App.Tests.TestSupport.Async;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Telemetry;
using Sufni.Telemetry.Tests;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Processing.Services;

namespace Sufni.App.Tests.Sessions.Coordination;

public class SessionLoaderTests
{
    private readonly ITrackStore trackStore = Substitute.For<ITrackStore>();
    private readonly ISessionStore sessionStore = Substitute.For<ISessionStore>();
    private readonly TestSessionProcessedTelemetryReader processedTelemetryReader = new();
    private readonly ITrackCoordinator trackCoordinator = TestCoordinatorSubstitutes.Track();
    private readonly IRecordedSessionDomainQuery domainQuery = Substitute.For<IRecordedSessionDomainQuery>();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadDetailAsync_OpensProcessedData_WhenSourceIsAbsentOrFingerprintDiffers(bool sourcePresent)
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        var source = sourcePresent
            ? new RecordedSessionSourceSnapshot(snapshot.Id, RecordedSessionSourceKind.ImportedSst, "source.SST", 1, "captured")
            : null;
        var persisted = new ProcessingFingerprint(4, 6, Guid.NewGuid(), Guid.NewGuid(), 1, "dependency", "older-source");
        domainQuery.Get(snapshot.Id).Returns(new RecordedSessionDomainSnapshot(
            snapshot, null, null, null, persisted, source,
            sourcePresent ? new SessionStaleness.Stale() : new SessionStaleness.MissingRawSource()));
        sessionStore.Get(snapshot.Id).Returns(snapshot);
        processedTelemetryReader.Set(snapshot.Id, telemetry);

        var result = await CreateLoader().LoadDetailAsync(snapshot.Id, new RecordingProgress<SessionDetailLoadProgress>(),
            TestContext.Current.CancellationToken);

        Assert.Same(telemetry, Assert.IsType<SessionDetailLoadResult.Loaded>(result).Data.TelemetryPresentation.TelemetryData);
    }

    [Fact]
    public async Task LoadTrackAsync_ReturnsFailed_WhenTrackLoadThrows()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        sessionStore.Get(snapshot.Id).Returns(snapshot);
        trackCoordinator.LoadSessionTrackAsync(snapshot.Id, telemetry, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("track failed"));
        var progress = new RecordingProgress<SessionDetailLoadProgress>();

        var result = await CreateLoader().LoadTrackAsync(snapshot.Id, telemetry, progress, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(SessionTrackFailureReason.Load, Assert.IsType<SessionDetailTrackLoadResult.Failed>(result).Reason);
        Assert.Equal(
            [SessionDetailLoadStage.LoadingMapData],
            progress.Reports.Select(report => report.Stage));
    }

    [Fact]
    public async Task LoadTrackAsync_PropagatesCancellation()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        sessionStore.Get(snapshot.Id).Returns(snapshot);
        trackCoordinator.LoadSessionTrackAsync(snapshot.Id, telemetry, cancellation.Token)
            .Returns(Task.FromCanceled<SessionTrackPresentationData>(cancellation.Token));
        var progress = new RecordingProgress<SessionDetailLoadProgress>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateLoader().LoadTrackAsync(snapshot.Id, telemetry, progress, cancellation.Token));

        Assert.Equal(
            [SessionDetailLoadStage.LoadingMapData],
            progress.Reports.Select(report => report.Stage));
    }

    [Fact]
    public async Task LoadDetailAsync_ReturnsIncompleteLocalData_WhenTelemetryMissing()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        sessionStore.Get(snapshot.Id).Returns(snapshot);
        processedTelemetryReader.Set(snapshot.Id, null);
        var progress = new RecordingProgress<SessionDetailLoadProgress>();

        var result = await CreateLoader().LoadDetailAsync(
            snapshot.Id,
            progress, cancellationToken: TestContext.Current.CancellationToken);

        var incomplete = Assert.IsType<SessionDetailLoadResult.IncompleteLocalData>(result);
        Assert.Equal(snapshot.Id, incomplete.SessionId);
        Assert.True(incomplete.Missing.ProcessedTelemetryBlob);
        Assert.False(incomplete.Missing.RecordedSourceMissing);
        Assert.Equal(
            [
                SessionDetailLoadStage.LoadingTelemetryData,
                SessionDetailLoadStage.CheckingLocalData,
            ],
            progress.Reports.Select(report => report.Stage));
        await trackCoordinator.DidNotReceive().LoadSessionTrackAsync(
            Arg.Any<Guid>(),
            Arg.Any<TelemetryData>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadDetailAsync_SeparatesReadFailureReasonFromDiagnostics()
    {
        processedTelemetryReader.ReadFailure = new IOException("corrupt payload");

        var result = await CreateLoader().LoadDetailAsync(Guid.NewGuid(),
            new RecordingProgress<SessionDetailLoadProgress>(), TestContext.Current.CancellationToken);

        var failure = Assert.IsType<SessionDetailLoadResult.Failed>(result);
        Assert.Equal(SessionLoadFailureReason.Read, failure.Reason);
        Assert.NotEmpty(failure.Details!);
    }

    [Fact]
    public async Task LoadDetailAsync_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateLoader().LoadDetailAsync(
            Guid.NewGuid(), new RecordingProgress<SessionDetailLoadProgress>(), cancellation.Token));
    }

    [Fact]
    public async Task LoadDetailAsync_DecodesSourceLessLegacyFixture()
    {
        var id = Guid.NewGuid();
        var repository = Substitute.For<ISessionRepository>();
        repository.GetSessionPsstPayloadMetadataAsync(id).Returns(new SessionPsstPayloadMetadata(id, true, 1, null));
        repository.GetSessionRawPsstAsync(id, 1).Returns(GoldenTelemetryCompatibilityFixtures.ProcessedSourceLess.GetBytes());
        ISessionLoader loader = CreateStoredLoader(repository);

        var loaded = Assert.IsType<SessionDetailLoadResult.Loaded>(await loader.LoadDetailAsync(id,
            new RecordingProgress<SessionDetailLoadProgress>(), TestContext.Current.CancellationToken));

        var telemetry = loaded.Data.TelemetryPresentation.TelemetryData;
        Assert.Equal("legacy-source-less.sst", telemetry.Metadata.SourceName);
        Assert.Equal(0.02, telemetry.Metadata.Duration);
        Assert.Equal([10, 20], telemetry.Front.Travel);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("current")]
    public async Task LoadDetailAsync_UsesCurrentGenerationAfterPayloadRevisionAdvances(string? fingerprint)
    {
        var id = Guid.NewGuid();
        var repository = Substitute.For<ISessionRepository>();
        repository.GetSessionPsstPayloadMetadataAsync(id).Returns(
            new SessionPsstPayloadMetadata(id, true, 1, fingerprint),
            new SessionPsstPayloadMetadata(id, true, 2, fingerprint));
        repository.GetSessionRawPsstAsync(id, 1).Returns((byte[]?)null);
        repository.GetSessionRawPsstAsync(id, 2).Returns(TestTelemetryData.CreateMinimal(duration: 66).BinaryForm);
        ISessionLoader loader = CreateStoredLoader(repository);

        var loaded = Assert.IsType<SessionDetailLoadResult.Loaded>(await loader.LoadDetailAsync(id,
            new RecordingProgress<SessionDetailLoadProgress>(), TestContext.Current.CancellationToken));

        Assert.Equal(66, loaded.Data.TelemetryPresentation.TelemetryData.Metadata.Duration);
    }

    [Fact]
    public async Task LoadDetailAsync_CanRetryAfterCorruptPayloadIsReplaced()
    {
        var id = Guid.NewGuid();
        var repository = Substitute.For<ISessionRepository>();
        repository.GetSessionPsstPayloadMetadataAsync(id).Returns(new SessionPsstPayloadMetadata(id, true, 1, null));
        repository.GetSessionRawPsstAsync(id, 1).Returns(new byte[] { 1, 2, 3 }, TestTelemetryData.CreateMinimal(duration: 66).BinaryForm);
        ISessionLoader loader = CreateStoredLoader(repository);

        Assert.IsType<SessionDetailLoadResult.Failed>(await loader.LoadDetailAsync(id,
            new RecordingProgress<SessionDetailLoadProgress>(), TestContext.Current.CancellationToken));
        var loaded = Assert.IsType<SessionDetailLoadResult.Loaded>(await loader.LoadDetailAsync(id,
            new RecordingProgress<SessionDetailLoadProgress>(), TestContext.Current.CancellationToken));

        Assert.Equal(66, loaded.Data.TelemetryPresentation.TelemetryData.Metadata.Duration);
    }

    private ISessionLoader CreateStoredLoader(ISessionRepository repository) =>
        new SessionLoader(sessionStore,
            new SessionProcessedTelemetryReader(repository, new SessionTelemetryProcessor(), new InlineBackgroundTaskRunner()),
            trackCoordinator, domainQuery, trackStore);

    private ISessionLoader CreateLoader() =>
        new SessionLoader(sessionStore, processedTelemetryReader, trackCoordinator, domainQuery, trackStore);
}
