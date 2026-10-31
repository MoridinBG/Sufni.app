using NSubstitute;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Services;

namespace Sufni.App.Tests.MapsAndTracks.Services;

public class SessionTrackReaderTests
{
    [Fact]
    public async Task GetSessionTrackAsync_EmptyWindowIsAvailableWhenExactFullTrackExists()
    {
        var fullReader = Substitute.For<IFullTrackPointReader>();
        var key = new SessionTrackKey(Guid.NewGuid(), 7, 101, 1, 0);
        fullReader.GetTrackPointsExactAsync(key.TrackId, 7, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<TrackPoint>)[new TrackPoint(100, 0, 0, 0), new TrackPoint(110, 1, 1, 1)]);
        using var reader = new SessionTrackReader(fullReader, new InlineBackgroundTaskRunner());

        var result = await reader.GetSessionTrackAsync(key, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSessionTrackAsync_DerivesAlignedWindowAndReusesTheSameInputs()
    {
        var fullReader = Substitute.For<IFullTrackPointReader>();
        var key = new SessionTrackKey(Guid.NewGuid(), 7, 100, 2, 0.5, WindowOriginFraction: 0.25);
        fullReader.GetTrackPointsExactAsync(key.TrackId, 7, Arg.Any<CancellationToken>()).Returns(Points());
        using var reader = new SessionTrackReader(fullReader, new InlineBackgroundTaskRunner());

        var first = await reader.GetSessionTrackAsync(key, TestContext.Current.CancellationToken);
        var repeated = await reader.GetSessionTrackAsync(key, TestContext.Current.CancellationToken);

        Assert.NotNull(first);
        Assert.Equal(100.75, first[0].Time);
        Assert.Equal(102, first[^1].Time);
        Assert.Same(first, repeated);
        await fullReader.Received(1).GetTrackPointsExactAsync(key.TrackId, 7, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("track")]
    [InlineData("revision")]
    [InlineData("timestamp")]
    [InlineData("duration")]
    [InlineData("offset")]
    [InlineData("projection")]
    [InlineData("origin")]
    public async Task GetSessionTrackAsync_ChangingInputsCannotReuseThePreviousProjection(string changedInput)
    {
        var fullReader = Substitute.For<IFullTrackPointReader>();
        fullReader.GetTrackPointsExactAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Points(), Points(10));
        using var reader = new SessionTrackReader(fullReader, new InlineBackgroundTaskRunner());
        var key = new SessionTrackKey(Guid.NewGuid(), 7, 100, 2, 0);
        var changed = changedInput switch
        {
            "track" => key with { TrackId = Guid.NewGuid() },
            "revision" => key with { PointsRevision = 8 },
            "timestamp" => key with { Timestamp = 101 },
            "duration" => key with { DurationSeconds = 3 },
            "offset" => key with { GpsOffsetSeconds = 1 },
            "origin" => key with { WindowOriginFraction = 0.25 },
            _ => key with { ProjectionVersion = 2 }
        };

        var first = await reader.GetSessionTrackAsync(key, TestContext.Current.CancellationToken);
        var second = await reader.GetSessionTrackAsync(changed, TestContext.Current.CancellationToken);

        Assert.Equal(0, first![0].X);
        Assert.True(second![0].X >= 10);
    }

    [Fact]
    public async Task GetSessionTrackAsync_MissingExactRevisionDoesNotAdvanceToLatest()
    {
        var fullReader = Substitute.For<IFullTrackPointReader>();
        var key = new SessionTrackKey(Guid.NewGuid(), 7, 100, 2, 0);
        fullReader.GetTrackPointsExactAsync(key.TrackId, 7, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<TrackPoint>?)null);
        fullReader.GetTrackPointsAsync(key.TrackId, Arg.Any<CancellationToken>()).Returns(Points());
        using var reader = new SessionTrackReader(fullReader, new InlineBackgroundTaskRunner());

        Assert.Null(await reader.GetSessionTrackAsync(key, TestContext.Current.CancellationToken));
        await fullReader.DidNotReceive().GetTrackPointsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSessionTrackAsync_EvictsByProjectedPointWeight()
    {
        var fullReader = Substitute.For<IFullTrackPointReader>();
        fullReader.GetTrackPointsExactAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(Points());
        using var reader = new SessionTrackReader(fullReader, new InlineBackgroundTaskRunner(), capacity: 8, pointBudget: 25);
        var key = new SessionTrackKey(Guid.NewGuid(), 7, 100, 2, 0);
        var first = await reader.GetSessionTrackAsync(key, TestContext.Current.CancellationToken);
        await reader.GetSessionTrackAsync(key with { GpsOffsetSeconds = 1 }, TestContext.Current.CancellationToken);
        var repeated = await reader.GetSessionTrackAsync(key, TestContext.Current.CancellationToken);

        Assert.NotSame(first, repeated);
        Assert.Equal(first!.Select(point => point.X), repeated!.Select(point => point.X));
        await fullReader.Received(3).GetTrackPointsExactAsync(key.TrackId, 7, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSessionTrackAsync_CancellationPreventsPointReads()
    {
        var fullReader = Substitute.For<IFullTrackPointReader>();
        using var reader = new SessionTrackReader(fullReader, new InlineBackgroundTaskRunner());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.GetSessionTrackAsync(
            new SessionTrackKey(Guid.NewGuid(), 7, 100, 2, 0), cancellation.Token));
        await fullReader.DidNotReceive().GetTrackPointsExactAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    private static IReadOnlyList<TrackPoint> Points(double shift = 0) => Enumerable.Range(0, 6)
        .Select(index => new TrackPoint(100 + index, shift + index, index, index)).ToArray();
}
