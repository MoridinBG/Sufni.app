using System.Text.Json;
using Sufni.App.Infrastructure;
using Sufni.App.Bikes.Models;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.Tests.TestSupport.Fixtures;

namespace Sufni.App.Tests.SyncAndPairing.Models;

public class SynchronizationDataJsonTests
{
    [Fact]
    public void SynchronizationData_RoundTripsBikeRearSuspensionUnionCases()
    {
        var linkage = TestSnapshots.FullSuspensionLinkageSpec();
        var leverageRatio = TestSnapshots.LeverageRatioCurve((0, 0), (10, 25));
        var data = new SynchronizationData
        {
            Bikes =
            [
                CreateBike("hardtail", new RearSuspensionSpec.Hardtail()),
                CreateBike("linkage draft", new RearSuspensionSpec.LinkageDraft()),
                CreateBike("leverage draft", new RearSuspensionSpec.LeverageRatioDraft()),
                CreateBike("linkage", new RearSuspensionSpec.Linkage(linkage)),
                CreateBike("leverage ratio", new RearSuspensionSpec.LeverageRatio(leverageRatio)),
            ]
        };

        var json = AppJson.Serialize(data);

        Assert.Contains("rear_suspension", json);
        Assert.DoesNotContain("rear_suspension_kind", json);

        var roundTrip = AppJson.Deserialize<SynchronizationData>(json);

        Assert.NotNull(roundTrip);
        Assert.Collection(
            roundTrip!.Bikes.Select(bike => bike.RearSuspension),
            suspension => Assert.IsType<RearSuspensionSpec.Hardtail>(suspension),
            suspension => Assert.IsType<RearSuspensionSpec.LinkageDraft>(suspension),
            suspension => Assert.IsType<RearSuspensionSpec.LeverageRatioDraft>(suspension),
            suspension => Assert.Equal(linkage, Assert.IsType<RearSuspensionSpec.Linkage>(suspension).Spec),
            suspension => Assert.Equal(leverageRatio, Assert.IsType<RearSuspensionSpec.LeverageRatio>(suspension).Spec));
    }

    [Fact]
    public void InboundContext_RejectsSynchronizationDataBikeWithMalformedRearSuspension()
    {
        const string json = """
                            {
                              "board": [],
                              "bike": [
                                {
                                  "id": "11111111-1111-1111-1111-111111111111",
                                  "updated": 1,
                                  "client_updated": 1,
                                  "deleted": null,
                                  "name": "bad bike",
                                  "head_angle": 65,
                                  "fork_stroke": 160,
                                  "shock_stroke": null,
                                  "rear_suspension": { "kind": "linkage" },
                                  "front_compression_damping_cutoff_mm_per_second": 200,
                                  "front_rebound_damping_cutoff_mm_per_second": 200,
                                  "rear_compression_damping_cutoff_mm_per_second": 200,
                                  "rear_rebound_damping_cutoff_mm_per_second": 200,
                                  "pixels_to_millimeters": 0,
                                  "front_wheel_diameter": null,
                                  "rear_wheel_diameter": null,
                                  "front_wheel_rim_size": null,
                                  "front_wheel_tire_width": null,
                                  "rear_wheel_rim_size": null,
                                  "rear_wheel_tire_width": null,
                                  "image_rotation_degrees": 0,
                                  "image": ""
                                }
                              ],
                              "setup": [],
                              "session": [],
                              "track": [],
                              "app_preferences": null,
                              "extension": []
                            }
                            """;

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize(json, AppJson.InboundContext.SynchronizationData));
    }

    private static Bike CreateBike(string name, RearSuspensionSpec rearSuspension) =>
        new(Guid.NewGuid(), name)
        {
            HeadAngle = 65,
            ForkStroke = 160,
            RearSuspension = rearSuspension,
            Updated = 1,
            ClientUpdated = 1,
        };
}
