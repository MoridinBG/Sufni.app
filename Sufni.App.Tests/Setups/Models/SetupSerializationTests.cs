using System.Text.Json.Nodes;

using Sufni.App.Bikes.Models;
using Sufni.App.Setups.Models;
namespace Sufni.App.Tests.Setups.Models;

public class SetupSerializationTests
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

        var json = setup.ToJson(bike, boardId: null);
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
        var root = JsonNode.Parse(setup.ToJson(bike, boardId: null))!.AsObject();
        root["bike"]!.AsObject()["schema_version"] = 1;

        var payload = Setup.FromJson(root.ToJsonString());

        Assert.Null(payload);
    }
}
