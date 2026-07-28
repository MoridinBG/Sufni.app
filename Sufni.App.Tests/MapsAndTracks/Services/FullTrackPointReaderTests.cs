using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.MapsAndTracks.Services;

namespace Sufni.App.Tests.MapsAndTracks.Services;

public class FullTrackPointReaderTests
{
    [Fact]
    public async Task GetTrackPointsAsync_CachesPayloadByTrackUpdatedTimestamp()
    {
        var trackId = Guid.NewGuid();
        var points = Points(1);
        var trackRepository = Substitute.For<ITrackRepository>();
        trackRepository.GetTrackPayloadMetadataAsync(trackId)
            .Returns(new TrackPayloadMetadata(trackId, PointsRevision: 5));
        trackRepository.GetTrackPayloadAsync(trackId, pointsRevision: 5)
            .Returns(new TrackPayload(trackId, PointsRevision: 5, points));
        var reader = new FullTrackPointReader(trackRepository, capacity: 8);

        var firstRead = await reader.GetTrackPointsAsync(trackId, cancellationToken: TestContext.Current.CancellationToken);
        var cachedRead = await reader.GetTrackPointsAsync(trackId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Same(points, firstRead);
        Assert.Same(points, cachedRead);
        await trackRepository.Received(2).GetTrackPayloadMetadataAsync(trackId);
        await trackRepository.Received(1).GetTrackPayloadAsync(trackId, pointsRevision: 5);
    }

    [Fact]
    public async Task GetTrackPointsAsync_DoesNotRetainPayloadOverPointBudget()
    {
        var trackId = Guid.NewGuid();
        var first = Points(1);
        var second = Points(2);
        var trackRepository = Substitute.For<ITrackRepository>();
        trackRepository.GetTrackPayloadMetadataAsync(trackId)
            .Returns(new TrackPayloadMetadata(trackId, PointsRevision: 5));
        trackRepository.GetTrackPayloadAsync(trackId, pointsRevision: 5)
            .Returns(
                new TrackPayload(trackId, PointsRevision: 5, first),
                new TrackPayload(trackId, PointsRevision: 5, second));
        var reader = new FullTrackPointReader(trackRepository, capacity: 8, pointBudget: 1);

        var firstRead = await reader.GetTrackPointsAsync(trackId, cancellationToken: TestContext.Current.CancellationToken);
        var secondRead = await reader.GetTrackPointsAsync(trackId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Same(first, firstRead);
        Assert.Same(second, secondRead);
        await trackRepository.Received(2).GetTrackPayloadAsync(trackId, pointsRevision: 5);
    }

    [Fact]
    public async Task GetTrackPointsAsync_LoadsNewPayloadWhenUpdatedTimestampChanges()
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
        var reader = new FullTrackPointReader(trackRepository, capacity: 8);

        var firstRead = await reader.GetTrackPointsAsync(trackId, cancellationToken: TestContext.Current.CancellationToken);
        var updatedRead = await reader.GetTrackPointsAsync(trackId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Same(first, firstRead);
        Assert.Same(second, updatedRead);
        await trackRepository.Received(1).GetTrackPayloadAsync(trackId, pointsRevision: 5);
        await trackRepository.Received(1).GetTrackPayloadAsync(trackId, pointsRevision: 6);
    }

    [Fact]
    public async Task GetTrackPointsAsync_RetriesWhenPayloadChangesAfterMetadataRead()
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
        var reader = new FullTrackPointReader(trackRepository, capacity: 8);

        var result = await reader.GetTrackPointsAsync(trackId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Same(points, result);
        await trackRepository.Received(1).GetTrackPayloadAsync(trackId, pointsRevision: 5);
        await trackRepository.Received(1).GetTrackPayloadAsync(trackId, pointsRevision: 6);
    }

    [Fact]
    public async Task GetTrackPointsExactAsync_UsesExpectedRevisionWithoutMetadataRetry_AndReusesIt()
    {
        var trackId = Guid.NewGuid();
        var points = Points(1);
        var trackRepository = Substitute.For<ITrackRepository>();
        trackRepository.GetTrackPayloadAsync(trackId, pointsRevision: 5)
            .Returns(new TrackPayload(trackId, PointsRevision: 5, points));
        var reader = new FullTrackPointReader(trackRepository, capacity: 8);

        var first = await reader.GetTrackPointsExactAsync(trackId, pointsRevision: 5, cancellationToken: TestContext.Current.CancellationToken);
        var second = await reader.GetTrackPointsExactAsync(trackId, pointsRevision: 5, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Same(points, first);
        Assert.Same(points, second);
        await trackRepository.Received(1).GetTrackPayloadAsync(trackId, pointsRevision: 5);
        await trackRepository.DidNotReceive().GetTrackPayloadMetadataAsync(trackId);
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
        var reader = new FullTrackPointReader(trackRepository, capacity: 8);
        using var firstCancellation = new CancellationTokenSource();

        var firstRead = reader.GetTrackPointsAsync(trackId, firstCancellation.Token);
        await payloadRequested.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        var secondRead = reader.GetTrackPointsAsync(trackId, cancellationToken: TestContext.Current.CancellationToken);
        await firstCancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstRead);
        payloadCompletion.SetResult(new TrackPayload(trackId, PointsRevision: 5, points));
        var secondResult = await secondRead;

        Assert.Same(points, secondResult);
        await trackRepository.Received(1).GetTrackPayloadAsync(trackId, pointsRevision: 5);
    }

    private static List<TrackPoint> Points(double startTime) =>
    [
        new TrackPoint(startTime, 1, 1, 10),
        new TrackPoint(startTime + 1, 2, 2, 11),
    ];
}
