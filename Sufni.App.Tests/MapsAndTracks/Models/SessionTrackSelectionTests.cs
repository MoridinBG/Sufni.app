using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Stores;

namespace Sufni.App.Tests.MapsAndTracks.Models;

public class SessionTrackSelectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Select_PrefersLargestOverlapThenLowestIdRegardlessOfInputOrder(bool reversed)
    {
        var lowest = new TrackSnapshot(new Guid("00000000-0000-0000-0000-000000000001"), 99, 111, 1);
        var tie = lowest with { Id = new Guid("00000000-0000-0000-0000-000000000002"), StartTime = 98 };
        var partial = new TrackSnapshot(Guid.NewGuid(), 99, 105, 1);
        var outside = new TrackSnapshot(Guid.NewGuid(), 110, 120, 1);
        TrackSnapshot[] tracks = [tie, partial, outside, lowest];

        Assert.Equal(lowest, SessionTrackSelection.Select(reversed ? tracks.Reverse() : tracks, 100, 10, 0));
    }

    [Fact]
    public void Select_UsesAlignedFractionalWindowAndRejectsNonOverlaps()
    {
        var first = new TrackSnapshot(Guid.NewGuid(), 100, 101, 1);
        var shifted = new TrackSnapshot(Guid.NewGuid(), 101.25, 102.25, 1);
        TrackSnapshot[] tracks = [first, shifted];

        Assert.Equal(first, SessionTrackSelection.Select(tracks, 100, 1, 0));
        Assert.Equal(shifted, SessionTrackSelection.Select(tracks, 100, 1, 1.25));
        Assert.Null(SessionTrackSelection.Select(tracks, 103, 1, 0));
        Assert.Null(SessionTrackSelection.Select(tracks, 100, null, 0));
    }
}
