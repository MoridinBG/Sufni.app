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

    /// <summary>True when the frame IMU, which sits on the sprung part of the bike, sets most of the classes.</summary>
    public static bool IsSprung(TerrainSourceCoverage? coverage) => coverage?.Source is GroundSensors.Frame;

    public static string? Description(TerrainPresentation? presentation, SetupTerrainScale? scale)
    {
        if (presentation is null) return null;
        var coverage = presentation.Coverage;
        var lines = new List<string>();
        var shares = Enum.GetValues<GroundSensors>().Where(source => coverage.SourceShares[(int)source] > 0)
            .Select(source => $"{SourceName(source)} {coverage.SourceShares[(int)source]:P0}").ToArray();
        if (shares.Length > 0) lines.Add(string.Join(" · ", shares));
        if (IsSprung(coverage))
            lines.Add("The frame is sprung. It reads what the suspension passes on, not the ground itself.");
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
        if (coverage.LabelledShare > 0) lines.Add($"Your labels set {coverage.LabelledShare:P0} of the classes of this ride.");
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
                lines.Add($"Climbs: compared with the climbs you labelled ({Distance(labels.SmoothClimb)} smooth, " +
                          $"{Distance(labels.TechnicalClimb)} technical).");
                return lines;
            }
            lines.Add(Line("Climbs", scale.FrameClimb, "climb"));
            if (labels.SmoothClimb + labels.TechnicalClimb > 0)
                lines.Add($"Your climb labels change this comparison when each class reaches " +
                          $"{Distance(SetupTerrainScaleQuery.MinimumLabelledMetres)}. You labelled {Distance(labels.SmoothClimb)} smooth and " +
                          $"{Distance(labels.TechnicalClimb)} technical.");
        }
        return lines;

        void AddSensor(string name, SensorScale sensor, double labelled)
        {
            if (sensor is SensorScale.Anchored)
            {
                lines.Add($"{name}: compared with the terrain you labelled ({Distance(labelled)}).");
                return;
            }
            lines.Add(Line(name, sensor, "descent"));
            if (labelled > 0)
                lines.Add($"Your labels change this comparison when they reach {Distance(SetupTerrainScaleQuery.MinimumLabelledMetres)}. " +
                          $"You labelled {Distance(labelled)}.");
        }
    }

    private static string Line(string name, SensorScale sensor, string ground) => sensor switch
    {
        SensorScale.NotEnoughData data =>
            $"{name}: no classes yet. The setup has {data.Rides} of {SetupTerrainScaleQuery.MinimumRides} rides and " +
            $"{Distance(data.Metres)} of {Distance(SetupTerrainScaleQuery.MinimumMetres)}.",
        _ => $"{name}: compared with your typical {ground} on this setup."
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
