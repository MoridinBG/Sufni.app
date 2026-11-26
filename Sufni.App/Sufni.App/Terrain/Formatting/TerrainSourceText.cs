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
            lines.AddRange(ScaleLines(scale, sensor => sensor switch
            {
                TerrainSensor.FrameImu => !presentation.Frame.IsEmpty,
                TerrainSensor.ForkImu => !presentation.Fork.IsEmpty,
                TerrainSensor.RearAxleImu => !presentation.RearAxle.IsEmpty,
                null => coverage.ClimbReading,
                _ => false
            }));
        foreach (var sensor in coverage.Silent) lines.Add($"{SensorName(sensor)} sensor ignored: no movement recorded.");
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    /// <summary>
    /// What sets the scale of each IMU that <paramref name="include"/> admits, and of the climb limit for a null sensor,
    /// with the labelled distance that a sensor without an anchor has.
    /// </summary>
    public static IReadOnlyList<string> ScaleLines(SetupTerrainScale scale, Func<TerrainSensor?, bool> include)
    {
        var lines = new List<string>();
        var labels = scale.Labels;
        if (include(TerrainSensor.FrameImu)) AddSensor("Frame", scale.FrameImu, labels.FrameImu);
        if (include(TerrainSensor.ForkImu)) AddSensor("Fork", scale.ForkImu, labels.ForkImu);
        if (include(TerrainSensor.RearAxleImu)) AddSensor("Rear axle", scale.RearAxleImu, labels.RearAxleImu);
        if (include(null))
        {
            if (scale.FrameClimb is SensorScale.Anchored)
            {
                lines.Add($"Climbs: set by your labels ({Distance(labels.SmoothClimb)} smooth, {Distance(labels.TechnicalClimb)} technical).");
                return lines;
            }
            lines.Add(Line("Climbs", scale.FrameClimb));
            if (labels.SmoothClimb + labels.TechnicalClimb > 0)
                lines.Add($"Labels: {Distance(labels.SmoothClimb)} of {Distance(SetupTerrainScaleQuery.MinimumLabelledMetres)} smooth, " +
                          $"{Distance(labels.TechnicalClimb)} of {Distance(SetupTerrainScaleQuery.MinimumLabelledMetres)} technical.");
        }
        return lines;

        void AddSensor(string name, SensorScale sensor, double labelled)
        {
            if (sensor is SensorScale.Anchored)
            {
                lines.Add($"{name}: set by your labels ({Distance(labelled)}).");
                return;
            }
            lines.Add(Line(name, sensor));
            if (labelled > 0) lines.Add($"Labels: {Distance(labelled)} of {Distance(SetupTerrainScaleQuery.MinimumLabelledMetres)}.");
        }
    }

    private static string Line(string name, SensorScale sensor) => sensor switch
    {
        SensorScale.NotEnoughData data =>
            $"{name}: no classes yet. The setup has {data.Rides} of {SetupTerrainScaleQuery.MinimumRides} rides and " +
            $"{Distance(data.Metres)} of {Distance(SetupTerrainScaleQuery.MinimumMetres)}.",
        _ => $"{name}: set by the median of the rides."
    };

    private static string Distance(double metres) => UnitsFormatter.FormatDistance(metres);

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
