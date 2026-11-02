using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Tests.TestSupport.Async;

namespace Sufni.App.Tests.MapsAndTracks.Services;

public class MapGeometryFactoryTests
{
    [Fact]
    public async Task BuildAsync_DefersGeometryAndIndexConstructionToBackgroundWork()
    {
        var background = new DeferredBackgroundTaskRunner();
        var factory = new MapGeometryFactory(background);
        TrackPoint[] points = [new(0, 0, 0, null), new(1, 100, 100, null)];
        var building = factory.BuildAsync(new(points, points, null), null, TestContext.Current.CancellationToken);
        Assert.False(building.IsCompleted);

        await background.RunNextAsync();
        var geometry = await building.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Single(geometry.SessionTrack.LineFeatures);
        Assert.Equal(2, geometry.SessionTrack.MarkerFeatures.Count);
        Assert.Equal(points[1], geometry.SessionTrack.TimeIndex!.FindClosest(1));
    }

    [Fact]
    public async Task BuildAsync_ReusesUnchangedTrackShapesWhenOnlyComparisonChanges()
    {
        var factory = new MapGeometryFactory(new InlineBackgroundTaskRunner());
        TrackPoint[] points = [new(0, 0, 0, null), new(1, 100, 100, null)];
        var original = await factory.BuildAsync(new(points, points, null), null, TestContext.Current.CancellationToken);

        var changed = await factory.BuildAsync(new(points, points, new(points, points)), original, TestContext.Current.CancellationToken);

        Assert.Same(original.FullTrack, changed.FullTrack);
        Assert.Same(original.SessionTrack, changed.SessionTrack);
        Assert.Equal(2, changed.MatchedFeatures.Count);
    }
}
