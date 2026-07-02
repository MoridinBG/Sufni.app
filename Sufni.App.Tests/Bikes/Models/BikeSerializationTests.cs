using System.Text.Json.Nodes;

using Sufni.App.Bikes.Models;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Bikes.Models;

public class BikeSerializationTests
{
    [Fact]
    public void BikeToJson_RoundTripsLinkageBike()
    {
        var linkage = TestSnapshots.FullSuspensionLinkageSpec();
        var bike = new Bike(Guid.NewGuid(), "legacy linkage bike")
        {
            HeadAngle = 64,
            ForkStroke = 150,
            RearSuspension = new RearSuspensionSpec.Linkage(linkage),
            ShockStroke = linkage.ShockStroke,
        };

        var json = bike.ToJson();
        var root = JsonNode.Parse(json)!.AsObject();

        Assert.Equal(2, root["schema_version"]!.GetValue<int>());

        var imported = Bike.FromJson(json);

        Assert.NotNull(imported);
        var importedLinkage = Assert.IsType<RearSuspensionSpec.Linkage>(imported!.RearSuspension);
        Assert.Equal(linkage, importedLinkage.Spec);
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
    public void BikeFromJson_RejectsPreRefactorUnversionedExport()
    {
        const string json = """
                            {
                              "name": "legacy cutoff bike",
                              "head_angle": 64,
                              "fork_stroke": 150
                            }
                            """;

        var imported = Bike.FromJson(json);

        Assert.Null(imported);
    }

}
