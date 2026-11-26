using NSubstitute;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Store;
using Sufni.App.Shared.Models;
using Sufni.App.Shared.Stores;
using Sufni.App.Terrain.Coordinators;
using Sufni.App.Terrain.Models;
using Sufni.App.Terrain.Services;
using Sufni.App.Terrain.Stores;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Terrain.Coordinators;

public sealed class TerrainLabelCoordinatorTests : IDisposable
{
    private readonly ITerrainLabelRepository repository = Substitute.For<ITerrainLabelRepository>();
    private readonly ISessionStore sessionStore = Substitute.For<ISessionStore>();
    private readonly IRecordedSessionSourceStore sourceStore = Substitute.For<IRecordedSessionSourceStore>();
    private readonly TerrainLabelStore labels = new(new StorePublication(new InlineUiThreadDispatcher()));
    private readonly SessionSnapshot session = TestSnapshots.Session(id: Guid.NewGuid()) with { WindowStartSeconds = 10 };
    private readonly TerrainLabelCoordinator coordinator;

    public TerrainLabelCoordinatorTests()
    {
        sessionStore.Get(session.Id).Returns(session);
        sourceStore.Get(session.RecordingSourceSessionId).Returns(new RecordedSessionSourceSnapshot(
            session.RecordingSourceSessionId, RecordedSessionSourceKind.ImportedSst, "ride.SST", 1, "source-hash"));
        coordinator = new TerrainLabelCoordinator(repository, labels, sessionStore, sourceStore, new InlineBackgroundTaskRunner());
    }

    public void Dispose() => labels.Dispose();

    [Fact]
    public async Task CreateAsync_StoresSourceSeconds_AndPublishesTheLabel()
    {
        TerrainLabel? saved = null;
        repository.InsertAsync(Arg.Do<TerrainLabel>(label => saved = label)).Returns(Task.CompletedTask);

        var result = await coordinator.CreateAsync(session.Id, RoughnessClass.Rough, 2, 5);

        Assert.IsType<TerrainLabelChangeResult.Applied>(result);
        Assert.Equal((12d, 15d), (saved!.StartSeconds, saved.EndSeconds));
        Assert.Equal(new TerrainLabelSnapshot(saved.Id, "source-hash", 12, 15, RoughnessClass.Rough),
            Assert.Single(labels.GetForOwner("source-hash")));
    }

    [Fact]
    public async Task CreateAsync_ReturnsInvalidWithoutWriting_WhenTheRangeOverlapsALabelOfTheSource()
    {
        await labels.PublishLabelsAsync([new(Guid.NewGuid(), "source-hash", 14, 20, RoughnessClass.Calm)], []);

        var result = await coordinator.CreateAsync(session.Id, RoughnessClass.Rough, 2, 5);

        Assert.IsType<TerrainLabelChangeResult.Invalid>(result);
        await repository.DidNotReceive().InsertAsync(Arg.Any<TerrainLabel>());
    }

    [Theory]
    [InlineData(2, 5, true)]
    [InlineData(10, 12, false)]
    public async Task ValidateRange_ReportsOnlyARangeThatOverlapsALabel(double start, double end, bool overlaps)
    {
        await labels.PublishLabelsAsync([new(Guid.NewGuid(), "source-hash", 14, 20, RoughnessClass.Calm)], []);

        Assert.Equal(overlaps, coordinator.ValidateRange(session.Id, start, end) is not null);
    }

    [Fact]
    public async Task CreateAsync_ReturnsSourceUnavailable_WithoutASource()
    {
        sourceStore.Get(session.RecordingSourceSessionId).Returns((RecordedSessionSourceSnapshot?)null);

        var result = await coordinator.CreateAsync(session.Id, RoughnessClass.Rough, 2, 5);

        Assert.IsType<TerrainLabelChangeResult.SourceUnavailable>(result);
        await repository.DidNotReceive().InsertAsync(Arg.Any<TerrainLabel>());
    }

    [Fact]
    public async Task SetClassAsync_PublishesTheChangedLabel()
    {
        var label = new TerrainLabel { OwnerKey = "source-hash", StartSeconds = 12, EndSeconds = 15, Class = RoughnessClass.Rough };
        await labels.PublishLabelsAsync([TerrainLabelSnapshot.From(label)], []);
        repository.SetClassAsync(label.Id, RoughnessClass.Smooth).Returns(new TerrainLabel
        {
            Id = label.Id, OwnerKey = label.OwnerKey, StartSeconds = 12, EndSeconds = 15, Class = RoughnessClass.Smooth
        });

        Assert.IsType<TerrainLabelChangeResult.Applied>(await coordinator.SetClassAsync(label.Id, RoughnessClass.Smooth));

        Assert.Equal(RoughnessClass.Smooth, labels.Get(label.Id)!.Class);
    }

    [Fact]
    public async Task DeleteAsync_PublishesTheRemoval()
    {
        var label = new TerrainLabel { OwnerKey = "source-hash", StartSeconds = 12, EndSeconds = 15, Class = RoughnessClass.Rough };
        await labels.PublishLabelsAsync([TerrainLabelSnapshot.From(label)], []);
        repository.DeleteAsync(label.Id).Returns(new TerrainLabel { Id = label.Id, OwnerKey = label.OwnerKey, Deleted = 100 });

        Assert.IsType<TerrainLabelChangeResult.Applied>(await coordinator.DeleteAsync(label.Id));

        Assert.Null(labels.Get(label.Id));
    }

    [Fact]
    public async Task SetClassAsync_ReturnsLabelMissing_WhenTheLabelIsGone()
    {
        repository.SetClassAsync(Arg.Any<Guid>(), Arg.Any<RoughnessClass>()).Returns((TerrainLabel?)null);

        Assert.IsType<TerrainLabelChangeResult.LabelMissing>(await coordinator.SetClassAsync(Guid.NewGuid(), RoughnessClass.Calm));
    }

    [Fact]
    public async Task DeleteAsync_ReturnsTheReason_WhenPersistenceFails()
    {
        repository.DeleteAsync(Arg.Any<Guid>()).Returns(Task.FromException<TerrainLabel?>(new IOException("storage unavailable")));

        var failure = Assert.IsType<TerrainLabelChangeResult.Failed>(await coordinator.DeleteAsync(Guid.NewGuid()));

        Assert.Equal(TerrainLabelWriteFailureReason.Delete, failure.Reason);
    }
}
