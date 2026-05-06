using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sufni.App.Models;
using Sufni.App.Models.SensorConfigurations;
using Sufni.App.Stores;
using Sufni.Kinematics;

namespace Sufni.App.SessionGraph;

/// <summary>
/// Builds the canonical hash for setup and bike inputs that affect telemetry
/// processing. The payload is normalized to include calibration and suspension
/// geometry while ignoring metadata that does not change computed telemetry.
/// </summary>
public static class ProcessingDependencyHash
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    static ProcessingDependencyHash()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
    }

    public static string Compute(SetupSnapshot setup, BikeSnapshot bike)
    {
        var payload = new DependencyPayload(
            Setup: new SetupPayload(
                setup.Id,
                setup.BikeId,
                SensorPayload.FromJson(setup.FrontSensorConfigurationJson),
                SensorPayload.FromJson(setup.RearSensorConfigurationJson)),
            Bike: new BikePayload(
                bike.Id,
                bike.HeadAngle,
                bike.ForkStroke,
                bike.ShockStroke,
                bike.RearSuspensionKind,
                LinkagePayload.FromLinkage(bike.Linkage),
                LeverageRatioPayload.FromLeverageRatio(bike.LeverageRatio)));

        using var stream = new MemoryStream();
        JsonSerializer.Serialize(stream, payload, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    private sealed record DependencyPayload(SetupPayload Setup, BikePayload Bike);

    private sealed record SetupPayload(
        Guid Id,
        Guid BikeId,
        SensorPayload? FrontSensorConfiguration,
        SensorPayload? RearSensorConfiguration);

    private sealed record BikePayload(
        Guid Id,
        double HeadAngle,
        double? ForkStroke,
        double? ShockStroke,
        RearSuspensionKind RearSuspensionKind,
        LinkagePayload? Linkage,
        LeverageRatioPayload? LeverageRatio);

    private sealed record SensorPayload(
        SensorType Type,
        double? Length,
        int? Resolution,
        double? MaxLength,
        double? ArmLength,
        string? CentralJoint,
        string? AdjacentJoint1,
        string? AdjacentJoint2)
    {
        public static SensorPayload? FromJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return SensorConfiguration.FromJson(json) switch
            {
                LinearForkSensorConfiguration linearFork => new SensorPayload(
                    linearFork.Type,
                    Length: linearFork.Length,
                    Resolution: linearFork.Resolution,
                    MaxLength: null,
                    ArmLength: null,
                    CentralJoint: null,
                    AdjacentJoint1: null,
                    AdjacentJoint2: null),
                RotationalForkSensorConfiguration rotationalFork => new SensorPayload(
                    rotationalFork.Type,
                    Length: null,
                    Resolution: null,
                    MaxLength: rotationalFork.MaxLength,
                    ArmLength: rotationalFork.ArmLength,
                    CentralJoint: null,
                    AdjacentJoint1: null,
                    AdjacentJoint2: null),
                LinearShockSensorConfiguration linearShock => new SensorPayload(
                    linearShock.Type,
                    Length: linearShock.Length,
                    Resolution: linearShock.Resolution,
                    MaxLength: null,
                    ArmLength: null,
                    CentralJoint: null,
                    AdjacentJoint1: null,
                    AdjacentJoint2: null),
                RotationalShockSensorConfiguration rotationalShock => new SensorPayload(
                    rotationalShock.Type,
                    Length: null,
                    Resolution: null,
                    MaxLength: null,
                    ArmLength: null,
                    CentralJoint: rotationalShock.CentralJoint,
                    AdjacentJoint1: rotationalShock.AdjacentJoint1,
                    AdjacentJoint2: rotationalShock.AdjacentJoint2),
                _ => null
            };
        }
    }

    private sealed record LinkagePayload(
        double ShockStroke,
        string? ShockAName,
        string? ShockBName,
        IReadOnlyList<JointPayload> Joints,
        IReadOnlyList<LinkPayload> Links)
    {
        public static LinkagePayload? FromLinkage(Linkage? linkage)
        {
            if (linkage is null)
            {
                return null;
            }

            return new LinkagePayload(
                linkage.ShockStroke,
                linkage.Shock.A_Name,
                linkage.Shock.B_Name,
                [.. linkage.Joints
                    .OrderBy(joint => joint.Name, StringComparer.Ordinal)
                    .Select(joint => new JointPayload(joint.Name, joint.Type, joint.X, joint.Y))],
                [.. linkage.Links
                    .OrderBy(link => link.A_Name, StringComparer.Ordinal)
                    .ThenBy(link => link.B_Name, StringComparer.Ordinal)
                    .Select(link => new LinkPayload(link.A_Name, link.B_Name))]);
        }
    }

    private sealed record JointPayload(string? Name, JointType? Type, double X, double Y);

    private sealed record LinkPayload(string? AName, string? BName);

    private sealed record LeverageRatioPayload(IReadOnlyList<LeverageRatioPointPayload> Points)
    {
        public static LeverageRatioPayload? FromLeverageRatio(LeverageRatio? leverageRatio) => leverageRatio is null
            ? null
            : new LeverageRatioPayload(
                [.. leverageRatio.Points.Select(point => new LeverageRatioPointPayload(
                    point.ShockTravelMm,
                    point.WheelTravelMm))]);
    }

    private sealed record LeverageRatioPointPayload(double ShockTravelMm, double WheelTravelMm);
}
