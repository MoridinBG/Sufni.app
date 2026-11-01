using Sufni.App.Tests.TestSupport.Fixtures;
using NSubstitute;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Shared.Models;
using Sufni.App.Tests.TestSupport.Doubles;

namespace Sufni.App.Tests.MapsAndTracks.Coordinators;

public class MapLayerCoordinatorTests
{
    [Fact]
    public async Task AddAndSelectAsync_AppendsToSavedLayers_AndSelectsOnlyAfterSave()
    {
        using var fixture = new MapLayerFixture();
        var map = fixture.Map;
        var existing = new TileLayerConfig { Name = "Existing" };
        await fixture.SeedAsync(existing);
        map.Seed(existing.Id);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Repository.BeforeWrite = () => completion.Task;
        await using var coordinator = fixture.CreateCoordinator();
        var added = new TileLayerConfig { Name = "Added" };

        var adding = coordinator.AddAndSelectAsync(added);
        Assert.False(adding.IsCompleted);
        Assert.Equal(existing.Id, map.SelectedLayerId);
        Assert.Single(fixture.Store.GetAll());
        completion.SetResult();

        Assert.IsType<MapLayerWriteResult.Saved>(await adding.AwaitBoundedAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal([existing.Id, added.Id], fixture.Store.GetAll().Select(layer => layer.Id));
        Assert.True(fixture.Store.GetAll()[1].ToConfig().IsCustom);
        Assert.Equal(added.Id, map.SelectedLayerId);
    }

    [Fact]
    public async Task ConcurrentAdds_PreserveBothLayers()
    {
        using var fixture = new MapLayerFixture();
        var map = fixture.Map;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Repository.BeforeWrite = () => completion.Task;
        await using var coordinator = fixture.CreateCoordinator();
        var first = new TileLayerConfig { Name = "First" };
        var second = new TileLayerConfig { Name = "Second" };

        var firstAdd = coordinator.AddAndSelectAsync(first);
        var secondAdd = coordinator.AddAndSelectAsync(second);
        completion.SetResult();
        var results = await Task.WhenAll(firstAdd, secondAdd).AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        Assert.All(results, result => Assert.IsType<MapLayerWriteResult.Saved>(result));
        Assert.Equal([first.Id, second.Id], fixture.Store.GetAll().Select(layer => layer.Id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedWrite_LeavesSavedPreferencesUnchanged(bool adding)
    {
        using var fixture = new MapLayerFixture();
        var map = fixture.Map;
        var original = new TileLayerConfig { Name = "Original" };
        await fixture.SeedAsync(original);
        map.Seed(original.Id);
        if (adding) fixture.Repository.WriteFailure = new IOException("disk full");
        else map.WriteFailure = new IOException("disk full");
        await using var coordinator = fixture.CreateCoordinator();
        var other = new TileLayerConfig { Name = "Other" };

        var result = adding
            ? await coordinator.AddAndSelectAsync(other)
            : await coordinator.SelectAsync(other.Id);

        Assert.Equal(adding ? MapLayerFailureReason.Add : MapLayerFailureReason.Select,
            Assert.IsType<MapLayerWriteResult.Failed>(result).Reason);
        Assert.Equal(original.Id, map.SelectedLayerId);
        Assert.Equal(original.Id, Assert.Single(fixture.Store.GetAll()).Id);
    }

    [Fact]
    public async Task AddAndSelectAsync_ReportsSelectionFailure_WithoutLosingSavedLayer()
    {
        using var fixture = new MapLayerFixture();
        var map = fixture.Map;
        var originalId = Guid.NewGuid();
        map.Seed(originalId);
        map.BeforeWrite = () =>
        {
            if (fixture.Store.GetAll().Count > 0) throw new IOException("selection write failed");
            return Task.CompletedTask;
        };
        await using var coordinator = fixture.CreateCoordinator();
        var added = new TileLayerConfig { Name = "Added" };

        var result = await coordinator.AddAndSelectAsync(added);

        Assert.Equal(MapLayerFailureReason.Select, Assert.IsType<MapLayerWriteResult.Failed>(result).Reason);
        Assert.Equal(added.Id, Assert.Single(fixture.Store.GetAll()).Id);
        Assert.Equal(originalId, map.SelectedLayerId);
    }

    [Fact]
    public async Task DisposeAsync_WaitsForAcceptedWriteToFinish()
    {
        using var fixture = new MapLayerFixture();
        var map = fixture.Map;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Repository.BeforeWrite = () => completion.Task;
        var coordinator = fixture.CreateCoordinator();
        var added = new TileLayerConfig { Name = "Added" };
        var adding = coordinator.AddAndSelectAsync(added);

        var disposing = coordinator.DisposeAsync().AsTask();
        Assert.False(disposing.IsCompleted);
        completion.SetResult();

        Assert.IsType<MapLayerWriteResult.Saved>(await adding.AwaitBoundedAsync(TimeSpan.FromSeconds(2)));
        await disposing.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(added.Id, map.SelectedLayerId);
    }

}
