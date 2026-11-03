using Sufni.App.Bikes.Services;
using System;
using System.Text.Json;
using Sufni.Kinematics;

using Sufni.App.Bikes.Models;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Setups.Models.SensorConfigurations;
namespace Sufni.App.Tests.Shared.Models;

public class ModelJsonTests
{

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
    public void TrackJson_RoundTripsPointValuesAndQuality()
    {
        const string json = """
            [{"time":10,"x":1.25,"y":2.5,"ele":3.75,"spd":4.5,"fix":3,"sats":12,"epe2d":0.5,"epe3d":0.8},
             {"time":11,"x":4.5,"y":5.5,"ele":null}]
            """;
        var rehydratedTrack = new Track { PointsJson = json };

        Assert.Equal(2, rehydratedTrack.Points.Count);
        Assert.Equal(1.25, rehydratedTrack.Points[0].X);
        Assert.Equal(3.75, rehydratedTrack.Points[0].Elevation);
        Assert.Equal((byte)3, rehydratedTrack.Points[0].FixMode);
        Assert.Equal((byte)12, rehydratedTrack.Points[0].Satellites);
        Assert.Equal(0.5f, rehydratedTrack.Points[0].Epe2d);
        Assert.Equal(0.8f, rehydratedTrack.Points[0].Epe3d);
        Assert.Null(rehydratedTrack.Points[1].FixMode);
        using var serialized = JsonDocument.Parse(rehydratedTrack.PointsJson);
        Assert.Equal(3, serialized.RootElement[0].GetProperty("fix").GetInt32());
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

}
