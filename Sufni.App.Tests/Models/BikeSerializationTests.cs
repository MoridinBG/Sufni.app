using System.Text.Json.Nodes;
using Sufni.App.Models;
using Sufni.App.Tests.Infrastructure;

namespace Sufni.App.Tests.Models;

public class BikeSerializationTests
{
    [Fact]
    public void BikeFromJson_LegacyLinkageExportWithoutRearSuspensionKind_ResolvesAsLinkageBike()
    {
        var bike = new Bike(Guid.NewGuid(), "legacy linkage bike")
        {
            HeadAngle = 64,
            ForkStroke = 150,
            Linkage = TestSnapshots.FullSuspensionLinkage(),
            ShockStroke = 0.5,
        };

        var json = JsonNode.Parse(bike.ToJson())!.AsObject();
        json.Remove("rear_suspension_kind");
        var imported = Bike.FromJson(json.ToJsonString());

        Assert.NotNull(imported);
        Assert.Equal(RearSuspensionKind.Linkage, imported!.RearSuspensionKind);
        var resolution = RearSuspensionResolver.Resolve(imported.RearSuspensionKind, imported.Linkage, imported.LeverageRatio);
        Assert.IsType<RearSuspensionResolution.Linkage>(resolution);
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
            RearSuspensionKind = RearSuspensionKind.LeverageRatio,
            LeverageRatio = leverageRatio,
        };

        var imported = Bike.FromJson(bike.ToJson());

        Assert.NotNull(imported);
        Assert.Equal(RearSuspensionKind.LeverageRatio, imported!.RearSuspensionKind);
        Assert.NotNull(imported.LeverageRatio);
        Assert.Equal(leverageRatio.Points, imported.LeverageRatio!.Points);
    }

}