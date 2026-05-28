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
            FrontCompressionDampingCutoffMmPerSecond = 110,
            FrontReboundDampingCutoffMmPerSecond = 120,
            RearCompressionDampingCutoffMmPerSecond = 230,
            RearReboundDampingCutoffMmPerSecond = 240,
        };

        var imported = Bike.FromJson(bike.ToJson());

        Assert.NotNull(imported);
        Assert.Equal(RearSuspensionKind.LeverageRatio, imported!.RearSuspensionKind);
        Assert.NotNull(imported.LeverageRatio);
        Assert.Equal(leverageRatio.Points, imported.LeverageRatio!.Points);
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
