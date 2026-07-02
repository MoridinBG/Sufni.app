using System.Text.Json.Nodes;

using Sufni.App.Bikes.Models;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Bikes.Models;

public class BikeSerializationTests
{
    [Fact]
    public void BikeToJson_RoundTripsLinkageBike()
    {
        var linkage = TestSnapshots.FullSuspensionLinkage();
        var bike = new Bike(Guid.NewGuid(), "legacy linkage bike")
        {
            HeadAngle = 64,
            ForkStroke = 150,
            RearSuspension = new RearSuspensionSpec.Linkage(linkage.ToSpec()),
            ShockStroke = 0.5,
        };

        var imported = Bike.FromJson(bike.ToJson());

        Assert.NotNull(imported);
        var importedLinkage = Assert.IsType<RearSuspensionSpec.Linkage>(imported!.RearSuspension);
        Assert.Equal(linkage.ToSpec(), importedLinkage.Spec);
    }

    [Fact]
    public void BikeToJson_RoundTripsLeverageRatioBike()
    {
        var leverageRatio = TestSnapshots.LeverageRatioCurve((0, 0), (10, 25), (20, 50));
        var bike = new Bike(Guid.NewGuid(), "curve bike")
        {
            HeadAngle = 64,
            ForkStroke = 150,
            ShockStroke = 20,
            RearSuspension = new RearSuspensionSpec.LeverageRatio(leverageRatio),
            FrontCompressionDampingCutoffMmPerSecond = 110,
            FrontReboundDampingCutoffMmPerSecond = 120,
            RearCompressionDampingCutoffMmPerSecond = 230,
            RearReboundDampingCutoffMmPerSecond = 240,
        };

        var imported = Bike.FromJson(bike.ToJson());

        Assert.NotNull(imported);
        var importedLeverageRatio = Assert.IsType<RearSuspensionSpec.LeverageRatio>(imported!.RearSuspension);
        Assert.Equal(leverageRatio.Points, importedLeverageRatio.Spec.Points);
        Assert.Equal(110, imported.FrontCompressionDampingCutoffMmPerSecond);
        Assert.Equal(120, imported.FrontReboundDampingCutoffMmPerSecond);
        Assert.Equal(230, imported.RearCompressionDampingCutoffMmPerSecond);
        Assert.Equal(240, imported.RearReboundDampingCutoffMmPerSecond);
    }

    [Fact]
    public void BikeFromJson_LegacyExportWithoutDampingCutoffs_UsesDefaults()
    {
        var bike = new Bike(Guid.NewGuid(), "legacy cutoff bike")
        {
            HeadAngle = 64,
            ForkStroke = 150,
        };

        var json = JsonNode.Parse(bike.ToJson())!.AsObject();
        json.Remove("front_compression_damping_cutoff_mm_per_second");
        json.Remove("front_rebound_damping_cutoff_mm_per_second");
        json.Remove("rear_compression_damping_cutoff_mm_per_second");
        json.Remove("rear_rebound_damping_cutoff_mm_per_second");

        var imported = Bike.FromJson(json.ToJsonString());

        Assert.NotNull(imported);
        Assert.Equal(200, imported!.FrontCompressionDampingCutoffMmPerSecond);
        Assert.Equal(200, imported.FrontReboundDampingCutoffMmPerSecond);
        Assert.Equal(200, imported.RearCompressionDampingCutoffMmPerSecond);
        Assert.Equal(200, imported.RearReboundDampingCutoffMmPerSecond);
    }

}
