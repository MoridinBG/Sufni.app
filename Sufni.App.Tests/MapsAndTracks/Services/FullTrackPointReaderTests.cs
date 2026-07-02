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
            .Returns(new TrackPayloadMetadata(trackId, Updated: 5));
        trackRepository.GetTrackPayloadAsync(trackId, updated: 5)
            .Returns(new TrackPayload(trackId, Updated: 5, points));
        var reader = new FullTrackPointReader(trackRepository, capacity: 8);

        var firstRead = await reader.GetTrackPointsAsync(trackId);
        var cachedRead = await reader.GetTrackPointsAsync(trackId);

        Assert.Same(points, firstRead);
        Assert.Same(points, cachedRead);
        await trackRepository.Received(2).GetTrackPayloadMetadataAsync(trackId);
        await trackRepository.Received(1).GetTrackPayloadAsync(trackId, updated: 5);
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
                new TrackPayloadMetadata(trackId, Updated: 5),
                new TrackPayloadMetadata(trackId, Updated: 6));
        trackRepository.GetTrackPayloadAsync(trackId, updated: 5)
            .Returns(new TrackPayload(trackId, Updated: 5, first));
        trackRepository.GetTrackPayloadAsync(trackId, updated: 6)
            .Returns(new TrackPayload(trackId, Updated: 6, second));
        var reader = new FullTrackPointReader(trackRepository, capacity: 8);

        var firstRead = await reader.GetTrackPointsAsync(trackId);
        var updatedRead = await reader.GetTrackPointsAsync(trackId);

        Assert.Same(first, firstRead);
        Assert.Same(second, updatedRead);
        await trackRepository.Received(1).GetTrackPayloadAsync(trackId, updated: 5);
        await trackRepository.Received(1).GetTrackPayloadAsync(trackId, updated: 6);
    }

    [Fact]
    public async Task GetTrackPointsAsync_RetriesWhenPayloadChangesAfterMetadataRead()
    {
        var trackId = Guid.NewGuid();
        var points = Points(1);
        var trackRepository = Substitute.For<ITrackRepository>();
        trackRepository.GetTrackPayloadMetadataAsync(trackId)
            .Returns(
                new TrackPayloadMetadata(trackId, Updated: 5),
                new TrackPayloadMetadata(trackId, Updated: 6));
        trackRepository.GetTrackPayloadAsync(trackId, updated: 5)
            .Returns((TrackPayload?)null);
        trackRepository.GetTrackPayloadAsync(trackId, updated: 6)
            .Returns(new TrackPayload(trackId, Updated: 6, points));
        var reader = new FullTrackPointReader(trackRepository, capacity: 8);

        var result = await reader.GetTrackPointsAsync(trackId);

        Assert.Same(points, result);
        await trackRepository.Received(1).GetTrackPayloadAsync(trackId, updated: 5);
        await trackRepository.Received(1).GetTrackPayloadAsync(trackId, updated: 6);
    }

    private static List<TrackPoint> Points(double startTime) =>
    [
        new TrackPoint(startTime, 1, 1, 10),
        new TrackPoint(startTime + 1, 2, 2, 11),
    ];
}
