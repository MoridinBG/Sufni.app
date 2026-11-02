using System;
using Sufni.App.Setups.Models.SensorConfigurations;
using Sufni.App.Setups.Stores;

namespace Sufni.App.Setups.Coordinators;

public sealed record SetupDraft(Guid Id, string? Name, Guid BikeId, Guid? BoardId, SensorDraft? FrontSensor, SensorDraft? RearSensor)
{
    public static SetupDraft From(SetupSnapshot snapshot, Guid? boardId) => new(
        snapshot.Id, snapshot.Name, snapshot.BikeId, boardId,
        SensorDraft.FromJson(snapshot.FrontSensorConfigurationJson), SensorDraft.FromJson(snapshot.RearSensorConfigurationJson));
}

/// <summary>Uncommitted calibration inputs, including incomplete fields while editing.</summary>
public abstract record SensorDraft
{
    public abstract SensorType Type { get; }
    public sealed record LinearFork(double? Length, int? Resolution) : SensorDraft
    {
        public override SensorType Type => SensorType.LinearFork;
    }
    public sealed record LinearShock(double? Length, int? Resolution, bool UsesShockStroke = false) : SensorDraft
    {
        public override SensorType Type => UsesShockStroke ? SensorType.LinearShockStroke : SensorType.LinearShock;
    }
    public sealed record RotationalFork(double? MaxLength, double? ArmLength) : SensorDraft
    {
        public override SensorType Type => SensorType.RotationalFork;
    }
    public sealed record RotationalShock(string? CentralJoint, string? AdjacentJoint1, string? AdjacentJoint2) : SensorDraft
    {
        public override SensorType Type => SensorType.RotationalShock;
    }

    public static SensorDraft? FromJson(string? json) => json is null ? null : SensorConfiguration.FromJson(json) switch
    {
        LinearForkSensorConfiguration value => new LinearFork(value.Length, value.Resolution),
        LinearShockSensorConfiguration value => new LinearShock(value.Length, value.Resolution, value.Type == SensorType.LinearShockStroke),
        RotationalForkSensorConfiguration value => new RotationalFork(value.MaxLength, value.ArmLength),
        RotationalShockSensorConfiguration value => new RotationalShock(value.CentralJoint, value.AdjacentJoint1, value.AdjacentJoint2),
        _ => null,
    };
}
