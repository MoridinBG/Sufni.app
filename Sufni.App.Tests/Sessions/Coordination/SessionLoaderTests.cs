using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sufni.App.Bikes.Models;
using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Store;
using Sufni.App.Tests.TestSupport.Async;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Sessions.Coordination;

public class SessionLoaderTests
{
    private readonly ISessionStore sessionStore = Substitute.For<ISessionStore>();
    private readonly TestSessionProcessedTelemetryReader processedTelemetryReader = new();
    private readonly ITrackCoordinator trackCoordinator = TestCoordinatorSubstitutes.Track();
    private readonly IRecordedSessionDomainQuery domainQuery = Substitute.For<IRecordedSessionDomainQuery>();

    [Fact]
    public async Task LoadDetailAsync_ReturnsTelemetryBeforeTrackLoad()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();

        sessionStore.Get(snapshot.Id).Returns(snapshot);
        processedTelemetryReader.Set(snapshot.Id, telemetry);
        var progress = new RecordingProgress<SessionDetailLoadProgress>();

        var result = await CreateLoader().LoadDetailAsync(snapshot.Id, progress, cancellationToken: TestContext.Current.CancellationToken);

        var loaded = Assert.IsType<SessionDetailLoadResult.Loaded>(result);
        Assert.Same(telemetry, loaded.Data.TelemetryPresentation.TelemetryData);
        Assert.Equal(snapshot.FullTrackId, loaded.Data.TelemetryPresentation.FullTrackId);
        Assert.Null(loaded.Data.TelemetryPresentation.FullTrackPoints);
        Assert.Null(loaded.Data.TelemetryPresentation.TrackPoints);
        Assert.Null(loaded.Data.TelemetryPresentation.MediaColumnWidth);
        Assert.Equal(DampingSpeedCutoffs.Default, loaded.Data.TelemetryPresentation.DampingSpeedCutoffs);
        Assert.Null(loaded.Data.TelemetryPresentation.DampingSpeedCutoffOwner);
        await trackCoordinator.DidNotReceive().LoadSessionTrackAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid?>(),
            Arg.Any<TelemetryData>(),
            Arg.Any<CancellationToken>());
        Assert.Equal(
            [
                SessionDetailLoadStage.LoadingTelemetryData,
                SessionDetailLoadStage.CheckingLocalData,
            ],
            progress.Reports.Select(report => report.Stage));
    }

    [Fact]
    public async Task LoadTrackAsync_ReturnsTrackData()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        var trackData = new SessionTrackPresentationData(
            Guid.NewGuid(),
            [new TrackPoint(1, 1, 1, 0)],
            [new TrackPoint(2, 2, 2, 0)],
            400.0);

        sessionStore.Get(snapshot.Id).Returns(snapshot);
        trackCoordinator.LoadSessionTrackAsync(snapshot.Id, snapshot.FullTrackId, telemetry, Arg.Any<CancellationToken>())
            .Returns(trackData);
        var progress = new RecordingProgress<SessionDetailLoadProgress>();

        var result = await CreateLoader().LoadTrackAsync(snapshot.Id, telemetry, progress, cancellationToken: TestContext.Current.CancellationToken);

        var loaded = Assert.IsType<SessionDetailTrackLoadResult.Loaded>(result);
        Assert.Same(trackData, loaded.Data);
        Assert.Equal(
            [
                SessionDetailLoadStage.LoadingMapData,
                SessionDetailLoadStage.FinalizingSessionData,
            ],
            progress.Reports.Select(report => report.Stage));
    }

    [Fact]
    public async Task LoadTrackAsync_ReturnsFailed_WhenTrackLoadThrows()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        sessionStore.Get(snapshot.Id).Returns(snapshot);
        trackCoordinator.LoadSessionTrackAsync(snapshot.Id, snapshot.FullTrackId, telemetry, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("track failed"));
        var progress = new RecordingProgress<SessionDetailLoadProgress>();

        var result = await CreateLoader().LoadTrackAsync(snapshot.Id, telemetry, progress, cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<SessionDetailTrackLoadResult.Failed>(result);
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
        trackCoordinator.LoadSessionTrackAsync(snapshot.Id, snapshot.FullTrackId, telemetry, cancellation.Token)
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
        Assert.False(incomplete.Missing.RecordedSourceMissingOrHashMismatch);
        Assert.Equal(
            [
                SessionDetailLoadStage.LoadingTelemetryData,
                SessionDetailLoadStage.CheckingLocalData,
            ],
            progress.Reports.Select(report => report.Stage));
        await trackCoordinator.DidNotReceive().LoadSessionTrackAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid?>(),
            Arg.Any<TelemetryData>(),
            Arg.Any<CancellationToken>());
    }

    private ISessionLoader CreateLoader() =>
        new SessionLoader(sessionStore, processedTelemetryReader, trackCoordinator, domainQuery);
}
