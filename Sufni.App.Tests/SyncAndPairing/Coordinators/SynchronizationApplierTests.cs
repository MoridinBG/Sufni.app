using System.Reactive.Linq;
using NSubstitute;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Services;
using Sufni.App.LiveDaq.Queries;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Segments.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Models;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Tests.TestSupport.Sync;

namespace Sufni.App.Tests.SyncAndPairing.Coordinators;

public class SynchronizationApplierTests
{
    [Theory]
    [InlineData(SynchronizationDirection.Push)]
    [InlineData(SynchronizationDirection.Pull)]
    public async Task ApplyAsync_PublishesAllRelatedFamiliesTogether_AndWaitsForDispatcher(SynchronizationDirection direction)
    {
        var dispatcher = new ManualUiThreadDispatcher();
        await using var fixture = new InboundSyncFixture(dispatcher);
        var applier = fixture.CreateRowApplier();
        var bike = new Bike(Guid.NewGuid(), "bike");
        var setup = new Setup(Guid.NewGuid(), "setup") { BikeId = bike.Id };
        var board = new Board(Guid.NewGuid(), setup.Id);
        var session = new Session(Guid.NewGuid(), "ride", "", setup.Id, 100);
        var track = new Track { Points = [new TrackPoint(100, 1, 2, 3), new TrackPoint(101, 2, 3, 4)] };
        var layer = new CustomTileLayer { Name = "Trail", UrlTemplate = "https://tiles/{z}/{x}/{y}" };
        var segment = new SessionSegment { OwnerKey = "source-hash", Name = "climb", StartSeconds = 1, EndSeconds = 2 };
        var observed = new List<(string?, Guid?, Guid?, string?, bool, string?, string?)>();
        using var subscription = fixture.Publication.Committed.Subscribe(_ => observed.Add((
            fixture.Bikes.Get(bike.Id)?.Name, fixture.Setups.Get(setup.Id)?.BikeId,
            fixture.Boards.Get(board.Id)?.SetupId, fixture.Sessions.Get(session.Id)?.Name,
            fixture.Tracks.Get(track.Id) is not null, fixture.Layers.Get(layer.Id)?.Name,
            fixture.Segments.Get(segment.Id)?.Name)));

        var apply = applier.ApplyAsync(new SynchronizationData
        {
            Bikes = [bike], Setups = [setup], Boards = [board], Sessions = [session],
            Tracks = [track], CustomTileLayers = [layer], Segments = [segment]
        }, direction);
        await dispatcher.WaitForWorkAsync(TestContext.Current.CancellationToken).AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        Assert.False(apply.IsCompleted);
        Assert.Empty(observed);
        Assert.NotNull(await fixture.Database.GetAsync<Board>(board.Id));
        await dispatcher.DrainAsync();
        Assert.Contains(session.Id, (await apply.AwaitBoundedAsync(TimeSpan.FromSeconds(5))).SessionIds);
        Assert.Equal(("bike", bike.Id, setup.Id, "ride", true, "Trail", "climb"), Assert.Single(observed));

        var deletion = applier.ApplyAsync(new SynchronizationData
        {
            Deletions = [new SynchronizationDeletion { EntityTable = "session", Id = session.Id, Version = 2, Updated = 2, Deleted = 2 },
                         new SynchronizationDeletion { EntityTable = "track", Id = track.Id, Version = 2, Updated = 2, Deleted = 2 }]
        }, direction);
        await dispatcher.WaitForWorkAsync(TestContext.Current.CancellationToken).AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        await dispatcher.DrainAsync();
        await deletion.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(("bike", bike.Id, setup.Id, null, false, "Trail", "climb"), observed[1]);
    }

}
