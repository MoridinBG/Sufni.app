using NSubstitute;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Services;

namespace Sufni.App.Tests.MapsAndTracks.Services;

public class FullTrackPointReaderTests
{
    [Fact]
    public async Task GetTrackPointsAsync_LoadsCurrentPointsAfterRevisionChanges()
    {
        var trackId = Guid.NewGuid();
        var first = Points(1);
        var second = Points(2);
        var trackRepository = Substitute.For<ITrackRepository>();
        trackRepository.GetTrackPayloadMetadataAsync(trackId)
            .Returns(
                new TrackPayloadMetadata(trackId, PointsRevision: 5),
                new TrackPayloadMetadata(trackId, PointsRevision: 6));
        trackRepository.GetTrackPayloadAsync(trackId, pointsRevision: 5)
            .Returns(new TrackPayload(trackId, PointsRevision: 5, first));
        trackRepository.GetTrackPayloadAsync(trackId, pointsRevision: 6)
            .Returns(new TrackPayload(trackId, PointsRevision: 6, second));
        var reader = new FullTrackPointReader(trackRepository);

        var firstRead = await reader.GetTrackPointsAsync(trackId, cancellationToken: TestContext.Current.CancellationToken);
        var updatedRead = await reader.GetTrackPointsAsync(trackId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(new[] { 1.0, 2.0 }, firstRead!.Select(point => point.Time));
        Assert.Equal(new[] { 2.0, 3.0 }, updatedRead!.Select(point => point.Time));
    }

    [Fact]
    public async Task GetTrackPointsAsync_RetriesChangedPayloadAndReturnsCurrentPoints()
    {
        var trackId = Guid.NewGuid();
        var points = Points(1);
        var trackRepository = Substitute.For<ITrackRepository>();
        trackRepository.GetTrackPayloadMetadataAsync(trackId)
            .Returns(
                new TrackPayloadMetadata(trackId, PointsRevision: 5),
                new TrackPayloadMetadata(trackId, PointsRevision: 6));
        trackRepository.GetTrackPayloadAsync(trackId, pointsRevision: 5)
            .Returns((TrackPayload?)null);
        trackRepository.GetTrackPayloadAsync(trackId, pointsRevision: 6)
            .Returns(new TrackPayload(trackId, PointsRevision: 6, points));
        var reader = new FullTrackPointReader(trackRepository);

        var result = await reader.GetTrackPointsAsync(trackId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(new[] { 1.0, 2.0 }, result!.Select(point => point.Time));
    }

    [Fact]
    public async Task GetTrackPointsAsync_CallerCancellationDoesNotCancelSharedPayloadLoad()
    {
        var trackId = Guid.NewGuid();
        var points = Points(1);
        var payloadRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var payloadCompletion = new TaskCompletionSource<TrackPayload?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var trackRepository = Substitute.For<ITrackRepository>();
        trackRepository.GetTrackPayloadMetadataAsync(trackId)
            .Returns(new TrackPayloadMetadata(trackId, PointsRevision: 5));
        trackRepository.GetTrackPayloadAsync(trackId, pointsRevision: 5)
            .Returns(_ =>
            {
                payloadRequested.TrySetResult();
                return payloadCompletion.Task;
            });
        var reader = new FullTrackPointReader(trackRepository);
        using var firstCancellation = new CancellationTokenSource();

        var firstRead = reader.GetTrackPointsAsync(trackId, firstCancellation.Token);
        await payloadRequested.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        var secondRead = reader.GetTrackPointsAsync(trackId, cancellationToken: TestContext.Current.CancellationToken);
        await firstCancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstRead);
        payloadCompletion.SetResult(new TrackPayload(trackId, PointsRevision: 5, points));
        var secondResult = await secondRead.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Same(points, secondResult);
        await trackRepository.Received(1).GetTrackPayloadAsync(trackId, pointsRevision: 5);
    }

    private static List<TrackPoint> Points(double startTime) =>
    [
        new TrackPoint(startTime, 1, 1, 10),
        new TrackPoint(startTime + 1, 2, 2, 11),
    ];
}
