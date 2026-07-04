using System;
using System.Collections.Generic;
using System.Linq;
using Sufni.Kinematics;

using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Setups.Models.SensorConfigurations;
using Sufni.App.Setups.Stores;

namespace Sufni.App.Sessions.Processing.RecordedSessionProjection;

internal sealed record ProcessingDependencyInputs(
    ProcessingDependencyInputs.SetupPayload Setup,
    ProcessingDependencyInputs.BikePayload Bike)
{
    public static ProcessingDependencyInputs Create(SetupSnapshot setup, BikeSnapshot bike)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(bike);

        return new ProcessingDependencyInputs(
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
                bike.Kind,
                LinkagePayload.FromRearSuspension(bike.RearSuspension),
                LeverageRatioPayload.FromRearSuspension(bike.RearSuspension)));
    }

    public static ProcessingDependencyInputs Create(SetupProcessingInput setup, BikeProcessingInput bike)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(bike);

        return new ProcessingDependencyInputs(
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
                bike.Kind,
                LinkagePayload.FromRearSuspension(bike.RearSuspension),
                LeverageRatioPayload.FromRearSuspension(bike.RearSuspension)));
    }

    internal sealed record SetupPayload(
        Guid Id,
        Guid BikeId,
        SensorPayload? FrontSensorConfiguration,
        SensorPayload? RearSensorConfiguration);

    internal sealed record BikePayload(
        Guid Id,
        double HeadAngle,
        double? ForkStroke,
        double? ShockStroke,
        RearSuspensionKind RearSuspensionKind,
        LinkagePayload? Linkage,
        LeverageRatioPayload? LeverageRatio);

    internal sealed record SensorPayload(
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

    internal sealed record LinkagePayload : IEquatable<LinkagePayload>
    {
        public LinkagePayload(
            double shockStroke,
            string? shockAName,
            string? shockBName,
            IReadOnlyList<JointPayload> joints,
            IReadOnlyList<LinkPayload> links)
        {
            ArgumentNullException.ThrowIfNull(joints);
            ArgumentNullException.ThrowIfNull(links);

            ShockStroke = shockStroke;
            ShockAName = shockAName;
            ShockBName = shockBName;
            Joints = [.. joints];
            Links = [.. links];
        }

        public double ShockStroke { get; }

        public string? ShockAName { get; }

        public string? ShockBName { get; }

        public IReadOnlyList<JointPayload> Joints { get; }

        public IReadOnlyList<LinkPayload> Links { get; }

        public static LinkagePayload? FromRearSuspension(RearSuspensionSpec rearSuspension)
        {
            if (rearSuspension is not RearSuspensionSpec.Linkage linkage)
            {
                return null;
            }

            return new LinkagePayload(
                linkage.Spec.ShockStroke,
                linkage.Spec.Shock.A,
                linkage.Spec.Shock.B,
                [.. linkage.Spec.Joints
                    .OrderBy(joint => joint.Name, StringComparer.Ordinal)
                    .Select(joint => new JointPayload(joint.Name, joint.Type, joint.X, joint.Y))],
                [.. linkage.Spec.Links
                    .OrderBy(link => link.A, StringComparer.Ordinal)
                    .ThenBy(link => link.B, StringComparer.Ordinal)
                    .Select(link => new LinkPayload(link.A, link.B))]);
        }

        public bool Equals(LinkagePayload? other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return other is not null &&
                   ShockStroke.Equals(other.ShockStroke) &&
                   ShockAName == other.ShockAName &&
                   ShockBName == other.ShockBName &&
                   SequenceEqual(Joints, other.Joints) &&
                   SequenceEqual(Links, other.Links);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(ShockStroke);
            hash.Add(ShockAName);
            hash.Add(ShockBName);
            AddSequence(ref hash, Joints);
            AddSequence(ref hash, Links);
            return hash.ToHashCode();
        }
    }

    internal sealed record JointPayload(string? Name, JointType? Type, double X, double Y);

    internal sealed record LinkPayload(string? AName, string? BName);

    internal sealed record LeverageRatioPayload : IEquatable<LeverageRatioPayload>
    {
        public LeverageRatioPayload(IReadOnlyList<LeverageRatioPointPayload> points)
        {
            ArgumentNullException.ThrowIfNull(points);

            Points = [.. points];
        }

        public IReadOnlyList<LeverageRatioPointPayload> Points { get; }

        public static LeverageRatioPayload? FromRearSuspension(RearSuspensionSpec rearSuspension) =>
            rearSuspension is RearSuspensionSpec.LeverageRatio leverageRatio
                ? new LeverageRatioPayload(
                    [.. leverageRatio.Spec.Points.Select(point => new LeverageRatioPointPayload(
                        point.ShockTravelMm,
                        point.WheelTravelMm))])
                : null;

        public bool Equals(LeverageRatioPayload? other) =>
            ReferenceEquals(this, other) ||
            other is not null && SequenceEqual(Points, other.Points);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            AddSequence(ref hash, Points);
            return hash.ToHashCode();
        }
    }

    internal sealed record LeverageRatioPointPayload(double ShockTravelMm, double WheelTravelMm);

    private static bool SequenceEqual<T>(IReadOnlyList<T> left, IReadOnlyList<T> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        var comparer = EqualityComparer<T>.Default;
        for (var index = 0; index < left.Count; index++)
        {
            if (!comparer.Equals(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static void AddSequence<T>(ref HashCode hash, IReadOnlyList<T> values)
    {
        hash.Add(values.Count);
        foreach (var value in values)
        {
            hash.Add(value);
        }
    }
}
