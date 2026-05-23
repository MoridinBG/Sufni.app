using System;
using System.Text.Json;
using Sufni.App.Models;
using Sufni.App.Models.SensorConfigurations;
using Sufni.Kinematics;

namespace Sufni.App.Tests.Models;

public class ModelJsonTests
{
    [Fact]
    public void BikeToJson_ExcludesSynchronizationFields()
    {
        var bike = new Bike(Guid.NewGuid(), "exported bike")
        {
            HeadAngle = 64,
            ForkStroke = 150,
            PixelsToMillimeters = 2.5,
        };

        var json = bike.ToJson();

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("exported bike", root.GetProperty("name").GetString());
        Assert.Equal(64, root.GetProperty("head_angle").GetDouble());
        Assert.False(root.TryGetProperty("id", out _));
        Assert.False(root.TryGetProperty("updated", out _));
        Assert.False(root.TryGetProperty("client_updated", out _));
        Assert.False(root.TryGetProperty("deleted", out _));
    }

    [Fact]
    public void BikeFromJson_ResolvesLinkageJointReferences()
    {
        var bike = new Bike(Guid.NewGuid(), "linkage bike")
        {
            HeadAngle = 64,
            ForkStroke = 150,
            Linkage = CreateSimpleLinkage(),
        };

        var imported = Bike.FromJson(bike.ToJson());

        Assert.NotNull(imported);
        Assert.NotNull(imported!.Linkage);
        Assert.Equal(0.5, imported.ShockStroke);
        Assert.NotNull(imported.Linkage!.Links[0].A);
        Assert.NotNull(imported.Linkage.Shock.A);
        Assert.Same(imported.Linkage.Joints[0], imported.Linkage.Links[0].A);
        Assert.Same(imported.Linkage.Joints[2], imported.Linkage.Shock.A);
        Assert.Equal(0.5, imported.Linkage.ShockStroke);
    }

    [Fact]
    public void SensorConfigurationJson_RemainsCaseInsensitiveAndUsesSnakeCaseEnum()
    {
        const string json = "{\"TYPE\":\"linear_fork\",\"Length\":100,\"Resolution\":12}";

        var configuration = Assert.IsType<LinearForkSensorConfiguration>(SensorConfiguration.FromJson(json));
        var serialized = SensorConfiguration.ToJson(configuration);

        using var document = JsonDocument.Parse(serialized);
        var root = document.RootElement;

        Assert.Equal(100, configuration.Length);
        Assert.Equal(12, configuration.Resolution);
        Assert.Equal("linear_fork", root.GetProperty("type").GetString());
        Assert.Equal(100, root.GetProperty("length").GetDouble());
        Assert.Equal(12, root.GetProperty("resolution").GetInt32());
    }

    [Fact]
    public void TrackAndSessionJson_RoundTripTrackPoints()
    {
        var points =
            new[]
            {
                new TrackPoint(10, 1.25, 2.5, 3.75, 4.5, 3, 12, 0.5f, 0.8f),
                new TrackPoint(11, 4.5, 5.5, null)
            };

        var track = new Track { Points = [.. points] };
        var rehydratedTrack = new Track { PointsJson = track.PointsJson };

        var session = new Session { Track = [.. points] };
        var rehydratedSession = new Session { TrackJson = session.TrackJson };

        Assert.Equal(2, rehydratedTrack.Points.Count);
        Assert.Equal(1.25, rehydratedTrack.Points[0].X);
        Assert.Equal(3.75, rehydratedTrack.Points[0].Elevation);
        Assert.Equal((byte)3, rehydratedTrack.Points[0].FixMode);
        Assert.Equal((byte)12, rehydratedTrack.Points[0].Satellites);
        Assert.Equal(0.5f, rehydratedTrack.Points[0].Epe2d);
        Assert.Equal(0.8f, rehydratedTrack.Points[0].Epe3d);
        Assert.Equal(2, rehydratedSession.Track!.Count);
        Assert.Equal(11, rehydratedSession.Track[1].Time);
        Assert.Null(rehydratedSession.Track[1].Elevation);
        Assert.Null(rehydratedSession.Track[1].FixMode);
        Assert.Null(rehydratedSession.Track[1].Satellites);
        Assert.Null(rehydratedSession.Track[1].Epe2d);
        Assert.Null(rehydratedSession.Track[1].Epe3d);
    }

    [Fact]
    public void TrackPointJson_DeserializesMissingGpsQualityAsNull()
    {
        const string json = """
                            [
                              {
                                "time": 10,
                                "x": 1.25,
                                "y": 2.5,
                                "ele": 3.75,
                                "spd": 4.5
                              }
                            ]
                            """;

        var points = AppJson.Deserialize<List<TrackPoint>>(json);

        var point = Assert.Single(points!);
        Assert.Equal(10, point.Time);
        Assert.Equal(4.5, point.Speed);
        Assert.Null(point.FixMode);
        Assert.Null(point.Satellites);
        Assert.Null(point.Epe2d);
        Assert.Null(point.Epe3d);
    }

    private static Linkage CreateSimpleLinkage()
    {
        var mapping = new JointNameMapping();
        var bottomBracket = new Joint(mapping.BottomBracket, JointType.BottomBracket, 0, 0);
        var rearWheel = new Joint(mapping.RearWheel, JointType.RearWheel, 4, 0);
        var shockEye1 = new Joint(mapping.ShockEye1, JointType.Floating, 4, 3);
        var shockEye2 = new Joint(mapping.ShockEye2, JointType.Fixed, 0, 3);

        var linkage = new Linkage
        {
            Joints = [bottomBracket, rearWheel, shockEye1, shockEye2],
            Links =
            [
                new Link(bottomBracket, rearWheel),
                new Link(rearWheel, shockEye1),
            ],
            Shock = new Link(shockEye1, shockEye2),
            ShockStroke = 0.5,
        };
        linkage.ResolveJoints();
        return linkage;
    }
}
