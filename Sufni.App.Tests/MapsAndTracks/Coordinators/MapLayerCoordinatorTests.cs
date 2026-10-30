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
        using var map = new InMemoryMapPreferences();
        var existing = new TileLayerConfig { Name = "Existing" };
        map.ApplySynced(new(existing.Id, [existing]));
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        map.BeforeWrite = () => completion.Task;
        await using var coordinator = CreateCoordinator(map);
        var added = new TileLayerConfig { Name = "Added" };

        var adding = coordinator.AddAndSelectAsync(added);
        Assert.False(adding.IsCompleted);
        Assert.Equal(existing.Id, map.Value.SelectedLayerId);
        Assert.Single(map.Value.CustomLayers);
        completion.SetResult();

        Assert.IsType<MapLayerWriteResult.Saved>(await adding.AwaitBoundedAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal([existing.Id, added.Id], map.Value.CustomLayers.Select(layer => layer.Id));
        Assert.True(map.Value.CustomLayers[1].IsCustom);
        Assert.Equal(added.Id, map.Value.SelectedLayerId);
    }

    [Fact]
    public async Task ConcurrentAdds_PreserveBothLayers()
    {
        using var map = new InMemoryMapPreferences();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        map.BeforeWrite = () => completion.Task;
        await using var coordinator = CreateCoordinator(map);
        var first = new TileLayerConfig { Name = "First" };
        var second = new TileLayerConfig { Name = "Second" };

        var firstAdd = coordinator.AddAndSelectAsync(first);
        var secondAdd = coordinator.AddAndSelectAsync(second);
        completion.SetResult();
        var results = await Task.WhenAll(firstAdd, secondAdd).AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        Assert.All(results, result => Assert.IsType<MapLayerWriteResult.Saved>(result));
        Assert.Equal([first.Id, second.Id], map.Value.CustomLayers.Select(layer => layer.Id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedWrite_LeavesSavedPreferencesUnchanged(bool adding)
    {
        using var map = new InMemoryMapPreferences();
        var original = new TileLayerConfig { Name = "Original" };
        map.ApplySynced(new(original.Id, [original]));
        map.WriteFailure = new IOException("disk full");
        await using var coordinator = CreateCoordinator(map);
        var other = new TileLayerConfig { Name = "Other" };

        var result = adding
            ? await coordinator.AddAndSelectAsync(other)
            : await coordinator.SelectAsync(other.Id);

        Assert.Equal(adding ? MapLayerFailureReason.Add : MapLayerFailureReason.Select,
            Assert.IsType<MapLayerWriteResult.Failed>(result).Reason);
        Assert.Equal(original.Id, map.Value.SelectedLayerId);
        Assert.Equal(original.Id, Assert.Single(map.Value.CustomLayers).Id);
    }

    [Fact]
    public async Task AddAndSelectAsync_ReportsSelectionFailure_WithoutLosingSavedLayer()
    {
        using var map = new InMemoryMapPreferences();
        var originalId = Guid.NewGuid();
        map.ApplySynced(new(originalId, []));
        map.BeforeWrite = () =>
        {
            if (map.Value.CustomLayers.Count > 0) throw new IOException("selection write failed");
            return Task.CompletedTask;
        };
        await using var coordinator = CreateCoordinator(map);
        var added = new TileLayerConfig { Name = "Added" };

        var result = await coordinator.AddAndSelectAsync(added);

        Assert.Equal(MapLayerFailureReason.Select, Assert.IsType<MapLayerWriteResult.Failed>(result).Reason);
        Assert.Equal(added.Id, Assert.Single(map.Value.CustomLayers).Id);
        Assert.Equal(originalId, map.Value.SelectedLayerId);
    }

    [Fact]
    public async Task DisposeAsync_WaitsForAcceptedWriteToFinish()
    {
        using var map = new InMemoryMapPreferences();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        map.BeforeWrite = () => completion.Task;
        var coordinator = CreateCoordinator(map);
        var added = new TileLayerConfig { Name = "Added" };
        var adding = coordinator.AddAndSelectAsync(added);

        var disposing = coordinator.DisposeAsync().AsTask();
        Assert.False(disposing.IsCompleted);
        completion.SetResult();

        Assert.IsType<MapLayerWriteResult.Saved>(await adding.AwaitBoundedAsync(TimeSpan.FromSeconds(2)));
        await disposing.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(added.Id, map.Value.SelectedLayerId);
    }

    private static MapLayerCoordinator CreateCoordinator(InMemoryMapPreferences map)
    {
        var preferences = Substitute.For<IAppPreferences>();
        preferences.Map.Returns(map);
        return new MapLayerCoordinator(preferences, new InlineBackgroundTaskRunner());
    }
}
