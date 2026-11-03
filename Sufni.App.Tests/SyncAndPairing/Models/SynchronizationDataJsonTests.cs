using System.Text.Json;
using Sufni.App.Infrastructure;
using Sufni.App.Bikes.Models;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.Tests.TestSupport.Sync;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Telemetry;

namespace Sufni.App.Tests.SyncAndPairing.Models;

public class SynchronizationDataJsonTests
{
    [Fact]
    public void RunNegotiation_PreservesPhaseOrderAcrossClientAndHardenedServerProfiles()
    {
        var request = new SynchronizationRunStart(Guid.NewGuid(), "database", SyncTestServerHarness.TransferPhases.Reverse().ToArray());
        var received = JsonSerializer.Deserialize<SynchronizationRunStart>(AppJson.Serialize(request), SyncTestServerHarness.CreateServerJsonOptions())!;
        Assert.Equal(request.RunId, received.RunId);
        Assert.Equal(request.DatabaseIncarnation, received.DatabaseIncarnation);
        Assert.Equal(request.Phases, received.Phases);
        var reply = new SynchronizationRunStarted("replacement-database");
        Assert.Equal(reply, AppJson.Deserialize<SynchronizationRunStarted>(
            JsonSerializer.Serialize(reply, SyncTestServerHarness.CreateServerJsonOptions())));
    }

    [Fact]
    public void WriteMetadata_TransmitsAuthorshipAndHubSequence_WithoutLocalSequence()
    {
        var data = new SynchronizationData { UpperBound = 42, Boards = [new Board(Guid.NewGuid(), null)
        {
            Version = 4, Updated = 123, UpdatedBy = "writer-a", LocalSequence = 55, SyncSequence = 80
        }] };
        foreach (var json in new[] { AppJson.Serialize(data), JsonSerializer.Serialize(data, SyncTestServerHarness.CreateServerJsonOptions()) })
        {
            using var document = JsonDocument.Parse(json);
            Assert.Equal(42, document.RootElement.GetProperty("upper_bound").GetInt64());
            var board = document.RootElement.GetProperty("board")[0];
            Assert.False(board.TryGetProperty("local_seq", out _));
            Assert.False(board.TryGetProperty("client_updated", out _));
            var roundTrip = AppJson.Deserialize<SynchronizationData>(json)!.Boards.Single();
            Assert.Equal((4L, 123L, "writer-a", 80L),
                (roundTrip.Version, roundTrip.Updated, roundTrip.UpdatedBy, roundTrip.SyncSequence));
        }
    }

    [Fact]
    public void SynchronizationData_RequiresUpperBound()
    {
        Assert.Throws<JsonException>(() => AppJson.Deserialize<SynchronizationData>("{}"));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<SynchronizationData>("{}", SyncTestServerHarness.CreateServerJsonOptions()));
    }

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
    public void SynchronizationData_OmitsLocalContentRevisions_FromBothJsonProfiles()
    {
        var data = new SynchronizationData
        {
            Sessions =
            [
                new Session(Guid.NewGuid(), "session", string.Empty, null)
                {
                    ProcessedTelemetryRevision = 12,

                },
            ],
            Tracks =
            [
                new Track
                {
                    Id = Guid.NewGuid(),
                    PointsRevision = 56,
                },
            ],
        };

        var lenientJson = AppJson.Serialize(data);
        var hardenedJson = JsonSerializer.Serialize(data, SyncTestServerHarness.CreateServerJsonOptions());

        foreach (var json in new[] { lenientJson, hardenedJson })
        {
            Assert.DoesNotContain("processed_telemetry_revision", json, StringComparison.Ordinal);
            Assert.DoesNotContain("track_projection_revision", json, StringComparison.Ordinal);
            Assert.DoesNotContain("points_revision", json, StringComparison.Ordinal);
        }
    }



    [Fact]
    public void ServerJson_AcceptsCompleteBikeContract()
    {
        var json = SyncDataJson(""" "rear_suspension": { "kind": "hardtail" } """);

        var data = JsonSerializer.Deserialize<SynchronizationData>(json, SyncTestServerHarness.CreateServerJsonOptions());

        Assert.IsType<RearSuspensionSpec.Hardtail>(Assert.Single(data!.Bikes).RearSuspension);
    }

    [Fact]
    public void ServerJson_RejectsSynchronizationDataBikeWithMalformedRearSuspension()
    {
        var json = SyncDataJson(""" "rear_suspension": { "kind": "linkage" } """);

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<SynchronizationData>(json, SyncTestServerHarness.CreateServerJsonOptions()));
    }

    [Fact]
    public void ServerJson_RejectsSynchronizationDataBikeWithoutRearSuspension()
    {
        var json = SyncDataJson("");

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<SynchronizationData>(json, SyncTestServerHarness.CreateServerJsonOptions()));
    }

    [Fact]
    public void LenientContext_RejectsSynchronizationDataBikeWithoutRearSuspension()
    {
        var json = SyncDataJson("");

        Assert.Throws<JsonException>(() => AppJson.Deserialize<SynchronizationData>(json));
    }

    private static Bike CreateBike(string name, RearSuspensionSpec rearSuspension) =>
        new(Guid.NewGuid(), name)
        {
            HeadAngle = 65,
            ForkStroke = 160,
            RearSuspension = rearSuspension,
            Updated = 1,

        };

    private static string SyncDataJson(string rearSuspensionMembers)
    {
        var rearSuspensionMemberText = string.IsNullOrWhiteSpace(rearSuspensionMembers)
            ? string.Empty
            : $"""
                                      {rearSuspensionMembers},
            """;

        return $$"""
                 {
                   "upper_bound": 0,
                   "board": [],
                   "bike": [
                     {
                       "id": "11111111-1111-1111-1111-111111111111",
                       "updated": 1,

                       "deleted": null,
                       "name": "bad bike",
                       "head_angle": 65,
                       "fork_stroke": 160,
                       "shock_stroke": null,
                 {{rearSuspensionMemberText}}      "front_compression_damping_cutoff_mm_per_second": 200,
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
                   "track": []
                 }
                 """;
    }
}
