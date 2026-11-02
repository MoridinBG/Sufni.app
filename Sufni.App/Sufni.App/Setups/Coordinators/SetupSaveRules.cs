using System;
using System.Linq;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Shared.Models;

namespace Sufni.App.Setups.Coordinators;

internal static class SetupSaveRules
{
    public static SetupValidationFailureReason? Validate(SetupDraft draft, BikeSnapshot? bike)
    {
        if (string.IsNullOrEmpty(draft.Name)) return SetupValidationFailureReason.Name;
        if (draft.BoardId is null) return SetupValidationFailureReason.Board;
        if (bike is null || bike.Id != draft.BikeId) return SetupValidationFailureReason.Bike;
        if (draft.FrontSensor is null && draft.RearSensor is null) return SetupValidationFailureReason.Sensor;
        if (draft.FrontSensor is not null && (draft.FrontSensor is not (SensorDraft.LinearFork or SensorDraft.RotationalFork) || !IsComplete(draft.FrontSensor)))
            return SetupValidationFailureReason.Calibration;
        if (draft.RearSensor is not null)
        {
            if (!IsComplete(draft.RearSensor)) return SetupValidationFailureReason.Calibration;
            var compatible = (bike.RearSuspension, draft.RearSensor) switch
            {
                (RearSuspensionSpec.Linkage, SensorDraft.LinearShock { UsesShockStroke: false }) => true,
                (RearSuspensionSpec.Linkage linkage, SensorDraft.RotationalShock rotational) =>
                    new[] { rotational.CentralJoint, rotational.AdjacentJoint1, rotational.AdjacentJoint2 }
                        .All(name => linkage.Spec.Joints.Any(joint => joint.Name == name)),
                (RearSuspensionSpec.LeverageRatio, SensorDraft.LinearShock { UsesShockStroke: true }) => true,
                _ => false,
            };
            if (!compatible) return SetupValidationFailureReason.RearCompatibility;
        }
        return null;
    }

    public static bool IsComplete(SensorDraft draft) => draft switch
    {
        SensorDraft.LinearFork value => value.Length.HasValue && value.Resolution.HasValue,
        SensorDraft.LinearShock value => value.Length.HasValue && value.Resolution.HasValue,
        SensorDraft.RotationalFork value => value.MaxLength.HasValue && value.ArmLength.HasValue,
        SensorDraft.RotationalShock value => value.CentralJoint is not null && value.AdjacentJoint1 is not null && value.AdjacentJoint2 is not null &&
            value.CentralJoint != value.AdjacentJoint1 && value.CentralJoint != value.AdjacentJoint2 && value.AdjacentJoint1 != value.AdjacentJoint2,
        _ => throw new ArgumentOutOfRangeException(nameof(draft)),
    };
}
