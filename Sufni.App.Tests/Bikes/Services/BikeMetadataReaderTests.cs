using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Services;
using Sufni.App.Bikes.Stores;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Persistence;

namespace Sufni.App.Tests.Bikes.Services;

public class BikeMetadataReaderTests
{
    [Fact]
    public async Task GetAllAsync_ReadsActiveMetadataWithImagePresence()
    {
        await using var context = PersistenceTestData.CreateConnectionContext($"bike-metadata-{Guid.NewGuid():N}", inMemory: true);
        var connection = await context.GetInitializedConnectionAsync(TestContext.Current.CancellationToken);
        var bike = new Bike(Guid.NewGuid(), "pictured")
        {
            HeadAngle = 65, ForkStroke = 170, PixelsToMillimeters = 2, ImageRotationDegrees = 13,
            RearSuspension = new RearSuspensionSpec.Linkage(TestSnapshots.FullSuspensionLinkageSpec()),
            FrontWheelDiameterMm = 760, RearWheelDiameterMm = 750,
            DampingSpeedCutoffs = DampingSpeedCutoffs.FromValues(100, 200, 300, 400),
            ImageBytes = TestImages.SmallPngBytes(), Updated = 12,
        };
        var empty = new Bike(Guid.NewGuid(), "no image");
        var deleted = new Bike(Guid.NewGuid(), "deleted") { ImageBytes = [1], Deleted = 1 };
        await connection.InsertAllAsync(new[] { bike, empty, deleted });

        var snapshots = await new BikeMetadataReader(context).GetAllAsync();

        Assert.Equal(2, snapshots.Count);
        var pictured = Assert.Single(snapshots, item => item.Id == bike.Id);
        Assert.True(pictured.HasImage);
        Assert.Equal("pictured", pictured.Name);
        Assert.DoesNotContain(snapshots, item => item.Id == deleted.Id);
        Assert.False(Assert.Single(snapshots, item => item.Id == empty.Id).HasImage);
    }
}
