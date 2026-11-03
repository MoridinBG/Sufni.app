using Sufni.App.Bikes.Services;
using System.Text.Json.Nodes;
using Sufni.Kinematics;

using Sufni.App.Bikes.Models;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Bikes.Services;

public class BikeFileFormatTests
{
    [Theory]
    [InlineData("{\"kind\":\"hardtail\"}", null)]
    [InlineData("{\"kind\":\"leverage_ratio\",\"leverage_ratio\":{\"points\":[{\"shock_travel_mm\":0,\"wheel_travel_mm\":0},{\"shock_travel_mm\":20,\"wheel_travel_mm\":50}]}}", 20.0)]
    public void Parse_NormalizesShockStrokeFromRearSuspension(string rearJson, double? expected)
    {
        var json = """
            {"schema_version":2,"name":"import","shock_stroke":999,"image":"",
             "damping_speed_cutoffs":{"front":{"compression_mm_per_second":200,"rebound_mm_per_second":200},
                                     "rear":{"compression_mm_per_second":200,"rebound_mm_per_second":200}},
             "rear_suspension":
            """ + rearJson + "}";

        var imported = BikeFileFormat.Parse(json);

        Assert.NotNull(imported);
        Assert.Equal(expected, imported.ShockStroke);
    }

    [Fact]
    public void BikeToJson_RoundTripsHardtailBike()
    {
        var bike = new Bike(Guid.NewGuid(), "hardtail bike")
        {
            HeadAngle = 65,
            ForkStroke = 160,
            RearSuspension = new RearSuspensionSpec.Hardtail(),
            FrontWheelRimSize = EtrtoRimSize.Inch29,
            FrontWheelTireWidth = 2.4,
            FrontWheelDiameterMm = TestSnapshots.WheelDiameter(EtrtoRimSize.Inch29, 2.4),
            ImageRotationDegrees = 4,
            Updated = 42, Deleted = 1, Version = 3, LocalSequence = 8, SyncSequence = 9,
        };

        var json = BikeFileFormat.Serialize(bike);
        var root = JsonNode.Parse(json)!.AsObject();

        Assert.Equal(2, root["schema_version"]!.GetValue<int>());
        foreach (var key in new[] { "id", "updated", "updated_by", "deleted", "version", "local_seq", "sync_seq" })
            Assert.False(root.ContainsKey(key));
        Assert.False(root.ContainsKey("front_wheel_diameter"));
        Assert.Equal(bike.FrontWheelDiameterMm, root["front_wheel"]!["diameter_mm"]!.GetValue<double>());

        var imported = BikeFileFormat.Parse(json);

        Assert.NotNull(imported);
        Assert.IsType<RearSuspensionSpec.Hardtail>(imported!.RearSuspension);
        Assert.Equal(bike.FrontWheelDiameterMm, imported.FrontWheelDiameterMm);
        Assert.Equal(EtrtoRimSize.Inch29, imported.FrontWheelRimSize);
        Assert.Equal(2.4, imported.FrontWheelTireWidth);
        Assert.Equal(4, imported.ImageRotationDegrees);
    }

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

        var json = BikeFileFormat.Serialize(bike);
        var root = JsonNode.Parse(json)!.AsObject();

        Assert.Equal(2, root["schema_version"]!.GetValue<int>());
        Assert.False(root.ContainsKey("rear_suspension_kind"));
        Assert.False(root.ContainsKey("linkage"));
        Assert.False(root.ContainsKey("leverage_ratio"));
        Assert.Equal("linkage", root["rear_suspension"]!["kind"]!.GetValue<string>());

        root["shock_stroke"] = 999;
        var imported = BikeFileFormat.Parse(root.ToJsonString());

        Assert.NotNull(imported);
        Assert.Equal(0.5, imported.ShockStroke);
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

        var json = BikeFileFormat.Serialize(bike);
        var root = JsonNode.Parse(json)!.AsObject();

        Assert.Equal(2, root["schema_version"]!.GetValue<int>());

        var imported = BikeFileFormat.Parse(json);

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

        Assert.Throws<System.Text.Json.JsonException>(() => BikeFileFormat.Parse(json));
    }

    [Fact]
    public void BikeFromJson_RejectsUnsupportedSchemaVersion()
    {
        var bike = new Bike(Guid.NewGuid(), "schema bike")
        {
            HeadAngle = 64,
            ForkStroke = 150,
            RearSuspension = new RearSuspensionSpec.Hardtail(),
        };
        var root = JsonNode.Parse(BikeFileFormat.Serialize(bike))!.AsObject();
        root["schema_version"] = 1;

        var imported = BikeFileFormat.Parse(root.ToJsonString());

        Assert.Null(imported);
    }

    [Fact]
    public void BikeFromJson_RejectsVersionedExportWithoutRearSuspension()
    {
        var bike = new Bike(Guid.NewGuid(), "missing union bike")
        {
            HeadAngle = 64,
            ForkStroke = 150,
            RearSuspension = new RearSuspensionSpec.Hardtail(),
        };
        var root = JsonNode.Parse(BikeFileFormat.Serialize(bike))!.AsObject();
        root.Remove("rear_suspension");

        Assert.Throws<System.Text.Json.JsonException>(() => BikeFileFormat.Parse(root.ToJsonString()));
    }
}
