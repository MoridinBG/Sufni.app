using System.Text.Json;
using Sufni.App.Infrastructure;
using Sufni.App.Bikes.Models;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Telemetry;

namespace Sufni.App.Tests.SyncAndPairing.Models;

public class SynchronizationDataJsonTests
{
    [Fact]
    public void SynchronizationData_RequiresUpperBound()
    {
        Assert.Throws<JsonException>(() => AppJson.Deserialize<SynchronizationData>("{}"));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize("{}", AppJson.InboundContext.SynchronizationData));
    }

    [Fact]
    public void SynchronizationData_RoundTripsUpperBound()
    {
        var roundTrip = AppJson.Deserialize<SynchronizationData>(AppJson.Serialize(
            new SynchronizationData { UpperBound = 42 }));

        Assert.Equal(42, roundTrip!.UpperBound);
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
                    TrackProjectionRevision = 34,
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
        var hardenedJson = JsonSerializer.Serialize(data, AppJson.InboundContext.SynchronizationData);

        foreach (var json in new[] { lenientJson, hardenedJson })
        {
            Assert.DoesNotContain("processed_telemetry_revision", json, StringComparison.Ordinal);
            Assert.DoesNotContain("track_projection_revision", json, StringComparison.Ordinal);
            Assert.DoesNotContain("points_revision", json, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SynchronizationData_RoundTripsAppPreferencesSnapshot()
    {
        var selectedLayerId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var data = new SynchronizationData
        {
            AppPreferences = new AppPreferencesSyncData
            {
                Updated = 42,
                Maps = new MapPreferencesSyncData
                {
                    SelectedLayerId = selectedLayerId,
                },
                Session = new SessionPreferencesSyncData
                {
                    Sessions =
                    {
                        [sessionId] = SessionPreferences.Default with
                        {
                            SignalDisplay = SessionPreferences.Default.SignalDisplay with
                            {
                                TravelSmoothing = PlotSmoothingLevel.Strong,
                            },
                            SignalLayout = new SignalLayoutPreferences(
                            [
                                new SignalLayoutRowPreferences(SignalRowIds.Imu, isExpanded: false),
                            ]),
                        },
                    },
                },
            },
        };

        var roundTripped = AppJson.Deserialize<SynchronizationData>(AppJson.Serialize(data));

        Assert.NotNull(roundTripped?.AppPreferences);
        Assert.Equal(42, roundTripped!.AppPreferences!.Updated);
        Assert.Equal(selectedLayerId, roundTripped.AppPreferences.Maps.SelectedLayerId);
        Assert.Equal(PlotSmoothingLevel.Strong, roundTripped.AppPreferences.Session.Sessions[sessionId].SignalDisplay.TravelSmoothing);
        Assert.False(roundTripped.AppPreferences.Session.Sessions[sessionId].SignalLayout.Rows[0].IsExpanded);
    }

    [Fact]
    public void InboundContext_RejectsSynchronizationDataBikeWithMalformedRearSuspension()
    {
        var json = SyncDataJson(""" "rear_suspension": { "kind": "linkage" } """);

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize(json, AppJson.InboundContext.SynchronizationData));
    }

    [Fact]
    public void InboundContext_RejectsSynchronizationDataBikeWithoutRearSuspension()
    {
        var json = SyncDataJson("");

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize(json, AppJson.InboundContext.SynchronizationData));
    }

    [Fact]
    public void InboundContext_RejectsSynchronizationDataBikeWithLegacyRearSuspensionTriple()
    {
        var json = SyncDataJson(
            """
            "rear_suspension_kind": "none",
            "linkage": null,
            "leverage_ratio": null
            """);

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize(json, AppJson.InboundContext.SynchronizationData));
    }

    [Fact]
    public void LenientContext_RejectsSynchronizationDataBikeWithoutRearSuspension()
    {
        var json = SyncDataJson("");

        Assert.Throws<JsonException>(() => AppJson.Deserialize<SynchronizationData>(json));
    }

    [Fact]
    public void LenientContext_RejectsSynchronizationDataBikeWithLegacyRearSuspensionTriple()
    {
        var json = SyncDataJson(
            """
            "rear_suspension_kind": "none",
            "linkage": null,
            "leverage_ratio": null
            """);

        Assert.Throws<JsonException>(() => AppJson.Deserialize<SynchronizationData>(json));
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

    private static string SyncDataJson(string rearSuspensionMembers)
    {
        var rearSuspensionMemberText = string.IsNullOrWhiteSpace(rearSuspensionMembers)
            ? string.Empty
            : $"""
                                      {rearSuspensionMembers},
            """;

        return $$"""
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
                   "track": [],
                   "app_preferences": null,
                   "extension": []
                 }
                 """;
    }
}
