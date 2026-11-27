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

        var changed = await repository.SetClassAsync(label.Id, new TerrainLabelClass.Roughness(RoughnessClass.VeryRough));

        Assert.Equal(new TerrainLabelClass.Roughness(RoughnessClass.VeryRough), changed!.LabelClass);
        var stored = (await database.GetAsync<TerrainLabel>(label.Id))!;
        Assert.Equal(new TerrainLabelClass.Roughness(RoughnessClass.VeryRough), stored.LabelClass);
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

        Assert.Null(await repository.SetClassAsync(label.Id, new TerrainLabelClass.Roughness(RoughnessClass.Calm)));
    }

    // Stored and synced rows hold the integer values, so these values never change.
    [Theory]
    [InlineData(RoughnessClass.Smooth, 0)]
    [InlineData(RoughnessClass.Calm, 1)]
    [InlineData(RoughnessClass.Medium, 2)]
    [InlineData(RoughnessClass.Rough, 3)]
    [InlineData(RoughnessClass.VeryRough, 4)]
    [InlineData(RoughnessClass.SmoothClimb, 5)]
    [InlineData(RoughnessClass.TechnicalClimb, 6)]
    public async Task InsertAsync_StoresTheFixedIntegersOfARoughnessLabel(RoughnessClass roughnessClass, int stored) =>
        Assert.Equal((0, stored), await StoredAsync(new TerrainLabelClass.Roughness(roughnessClass)));

    [Theory]
    [InlineData(GradeBand.SteepClimb, 0)]
    [InlineData(GradeBand.Climb, 1)]
    [InlineData(GradeBand.Flat, 2)]
    [InlineData(GradeBand.GentleDescent, 3)]
    [InlineData(GradeBand.Descent, 4)]
    [InlineData(GradeBand.SteepDescent, 5)]
    [InlineData(GradeBand.VerySteepDescent, 6)]
    public async Task InsertAsync_StoresTheFixedIntegersOfASteepnessLabel(GradeBand band, int stored) =>
        Assert.Equal((1, stored), await StoredAsync(new TerrainLabelClass.Steepness(band)));

    [Fact]
    public async Task GetLiveAsync_ReadsTheClassOfEachKind()
    {
        using var temp = new TempDatabase();
        await using var database = new TestPersistenceHarness(temp.DatabasePath);
        var steepness = Label();
        steepness.LabelClass = new TerrainLabelClass.Steepness(GradeBand.SteepDescent);
        await database.TerrainLabelRepository.InsertAsync(Label());
        await database.TerrainLabelRepository.InsertAsync(steepness);

        Assert.Equal([new TerrainLabelClass.Roughness(RoughnessClass.Rough), new TerrainLabelClass.Steepness(GradeBand.SteepDescent)],
            (await database.TerrainLabelRepository.GetLiveAsync()).Select(label => label.LabelClass).OrderBy(c => c.Kind));
    }

    private static async Task<(int Kind, int Class)> StoredAsync(TerrainLabelClass labelClass)
    {
        using var temp = new TempDatabase();
        await using var database = new TestPersistenceHarness(temp.DatabasePath);
        var label = Label();
        label.LabelClass = labelClass;
        await database.TerrainLabelRepository.InsertAsync(label);
        var connection = await database.GetInitializedConnectionAsync();
        return (await connection.ExecuteScalarAsync<int>("SELECT kind FROM terrain_label WHERE id=?", label.Id),
            await connection.ExecuteScalarAsync<int>("SELECT class FROM terrain_label WHERE id=?", label.Id));
    }

    private static TerrainLabel Label() =>
        new() { OwnerKey = "source-hash", StartSeconds = 2, EndSeconds = 5, LabelClass = new TerrainLabelClass.Roughness(RoughnessClass.Rough) };
}
