using Sufni.App.Terrain.Models;
using Sufni.App.Tests.TestSupport.Persistence;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Terrain.Services;

public class TerrainLabelRepositoryTests
{
    [Fact]
    public async Task SetClassAsync_ChangesTheClass_AndAdvancesTheAuthoredVersionAndSequences()
    {
        using var temp = new TempDatabase();
        await using var database = new TestPersistenceHarness(temp.DatabasePath, writerId: "writer-a");
        var repository = database.TerrainLabelRepository;
        var label = Label();
        await repository.InsertAsync(label);
        var inserted = (await database.GetAsync<TerrainLabel>(label.Id))!;

        var changed = await repository.SetClassAsync(label.Id, RoughnessClass.VeryRough);

        Assert.Equal(RoughnessClass.VeryRough, changed!.Class);
        var stored = (await database.GetAsync<TerrainLabel>(label.Id))!;
        Assert.Equal(RoughnessClass.VeryRough, stored.Class);
        Assert.Equal(inserted.Version + 1, stored.Version);
        Assert.Equal("writer-a", stored.UpdatedBy);
        Assert.True(stored.LocalSequence > inserted.LocalSequence);
        Assert.True(stored.SyncSequence > inserted.SyncSequence);
    }

    [Fact]
    public async Task DeleteAsync_HidesTheLabel_AndRecordsItsDeletion()
    {
        using var temp = new TempDatabase();
        await using var database = new TestPersistenceHarness(temp.DatabasePath);
        var repository = database.TerrainLabelRepository;
        var kept = Label();
        var deleted = Label();
        await repository.InsertAsync(kept);
        await repository.InsertAsync(deleted);

        Assert.NotNull(await repository.DeleteAsync(deleted.Id));

        Assert.Equal(kept.Id, Assert.Single(await repository.GetLiveAsync()).Id);
        var connection = await database.GetInitializedConnectionAsync();
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sync_deletion WHERE entity_table='terrain_label' AND id=?", deleted.Id));
    }

    [Fact]
    public async Task SetClassAsync_ReturnsNull_WhenTheLabelIsDeleted()
    {
        using var temp = new TempDatabase();
        await using var database = new TestPersistenceHarness(temp.DatabasePath);
        var repository = database.TerrainLabelRepository;
        var label = Label();
        await repository.InsertAsync(label);
        await repository.DeleteAsync(label.Id);

        Assert.Null(await repository.SetClassAsync(label.Id, RoughnessClass.Calm));
    }

    // Stored and synced rows hold the integer value, so these values never change.
    [Theory]
    [InlineData(RoughnessClass.Smooth, 0)]
    [InlineData(RoughnessClass.Calm, 1)]
    [InlineData(RoughnessClass.Medium, 2)]
    [InlineData(RoughnessClass.Rough, 3)]
    [InlineData(RoughnessClass.VeryRough, 4)]
    [InlineData(RoughnessClass.SmoothClimb, 5)]
    [InlineData(RoughnessClass.TechnicalClimb, 6)]
    public async Task InsertAsync_StoresTheFixedIntegerOfTheClass(RoughnessClass roughnessClass, int stored)
    {
        using var temp = new TempDatabase();
        await using var database = new TestPersistenceHarness(temp.DatabasePath);
        var label = Label();
        label.Class = roughnessClass;

        await database.TerrainLabelRepository.InsertAsync(label);

        var connection = await database.GetInitializedConnectionAsync();
        Assert.Equal(stored, await connection.ExecuteScalarAsync<int>("SELECT roughness_class FROM terrain_label WHERE id=?", label.Id));
    }

    private static TerrainLabel Label() =>
        new() { OwnerKey = "source-hash", StartSeconds = 2, EndSeconds = 5, Class = RoughnessClass.Rough };
}
