using System;
using System.Collections.Generic;
using System.Linq;
using Sufni.App.Shared.Formatting;
using Sufni.App.Terrain.Models;
using Sufni.App.Terrain.Queries;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Formatting;

internal static class TerrainSourceText
{
    public static string StripTitle(TerrainSourceCoverage? coverage) =>
        coverage?.Source is { } source ? $"Terrain · {SourceName(source)}" : "Terrain";

    public static string? Description(TerrainPresentation? presentation, SetupTerrainScale? scale)
    {
        if (presentation is null) return null;
        var coverage = presentation.Coverage;
        var lines = new List<string>();
        var shares = Enum.GetValues<GroundSensors>().Where(source => coverage.SourceShares[(int)source] > 0)
            .Select(source => $"{SourceName(source)} {coverage.SourceShares[(int)source]:P0}").ToArray();
        if (shares.Length > 0) lines.Add(string.Join(" · ", shares));
        if (coverage.ForkClipped) lines.Add("Fork reached full scale.");
        if (coverage.RearAxleClipped) lines.Add("Rear axle reached full scale.");
        if (coverage.FrameMissing) lines.Add("Frame data absent in part or all of the ride.");
        if (coverage.ForkMissing) lines.Add("Fork data absent in part or all of the ride.");
        if (coverage.RearAxleMissing) lines.Add("Rear axle data absent in part or all of the ride.");
        if (scale is not null)
        {
            AddNotEnoughData("Frame", !presentation.Frame.IsEmpty, scale.FrameImu);
            AddNotEnoughData("Fork", !presentation.Fork.IsEmpty, scale.ForkImu);
            AddNotEnoughData("Rear axle", !presentation.RearAxle.IsEmpty, scale.RearAxleImu);
            AddNotEnoughData("Climbs", coverage.ClimbReading, scale.FrameClimb);
        }
        foreach (var sensor in coverage.Silent) lines.Add($"{SensorName(sensor)} sensor ignored: no movement recorded.");
        return lines.Count == 0 ? null : string.Join("\n", lines);

        void AddNotEnoughData(string name, bool hasReadings, SensorScale sensor)
        {
            if (!hasReadings || sensor is not SensorScale.NotEnoughData data) return;
            lines.Add($"{name}: no classes yet. The setup has {data.Rides} of {SetupTerrainScaleQuery.MinimumRides} rides and " +
                      $"{UnitsFormatter.FormatDistance(data.Metres)} of {UnitsFormatter.FormatDistance(SetupTerrainScaleQuery.MinimumMetres)}.");
        }
    }

    private static string SourceName(GroundSensors source) => source switch
    {
        GroundSensors.ForkAndRearAxle => "Fork and rear axle",
        GroundSensors.Fork => "Fork",
        GroundSensors.RearAxle => "Rear axle",
        _ => "Frame"
    };

    private static string SensorName(TerrainSensor sensor) => sensor switch
    {
        TerrainSensor.ForkTravel => "Fork suspension",
        TerrainSensor.ShockTravel => "Rear suspension",
        TerrainSensor.FrameImu => "Frame",
        TerrainSensor.ForkImu => "Fork",
        _ => "Rear axle"
    };
}
