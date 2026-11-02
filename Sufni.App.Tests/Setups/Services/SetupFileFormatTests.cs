using Sufni.App.Bikes.Services;
using Sufni.App.Setups.Services;
using System.Text.Json.Nodes;

using Sufni.App.Bikes.Models;
using Sufni.App.Setups.Models;
namespace Sufni.App.Tests.Setups.Services;

public class SetupFileFormatTests
{
    [Fact]
    public void SetupToJson_EmbedsBikeExportDocumentV2()
    {
        var bike = new Bike(Guid.NewGuid(), "setup bike")
        {
            HeadAngle = 65,
            ForkStroke = 160,
            RearSuspension = new RearSuspensionSpec.Hardtail(),
        };
        var setup = new Setup(Guid.NewGuid(), "race setup")
        {
            BikeId = bike.Id,
        };

        var json = SetupFileFormat.Serialize(setup, bike, boardId: null);
        var root = JsonNode.Parse(json)!.AsObject();
        var bikeNode = root["bike"]!.AsObject();

        Assert.Equal(2, bikeNode["schema_version"]!.GetValue<int>());
        Assert.Equal("hardtail", bikeNode["rear_suspension"]!["kind"]!.GetValue<string>());
        Assert.False(bikeNode.ContainsKey("rear_suspension_kind"));
    }

    [Fact]
    public void SetupFromJson_RejectsUnsupportedNestedBikeSchemaVersion()
    {
        var bike = new Bike(Guid.NewGuid(), "setup bike")
        {
            HeadAngle = 65,
            ForkStroke = 160,
            RearSuspension = new RearSuspensionSpec.Hardtail(),
        };
        var setup = new Setup(Guid.NewGuid(), "race setup")
        {
            BikeId = bike.Id,
        };
        var root = JsonNode.Parse(SetupFileFormat.Serialize(setup, bike, boardId: null))!.AsObject();
        root["bike"]!.AsObject()["schema_version"] = 1;

        Assert.Throws<System.Text.Json.JsonException>(() => SetupFileFormat.Parse(root.ToJsonString()));
    }
}
