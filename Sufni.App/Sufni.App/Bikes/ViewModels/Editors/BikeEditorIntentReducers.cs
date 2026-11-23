using System;
using System.Linq;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Bikes.ViewModels.LinkageEditing;
using Sufni.Kinematics;

namespace Sufni.App.Bikes.ViewModels.Editors;

internal static class BikeEditorIntentReducers
{
    public static BikeEditorInputs Prepare(BikeDraft draft, BikeEditorImage? image, bool persisted)
    {
        var linkage = (draft.RearSuspension as RearSuspensionSpec.Linkage)?.Spec;
        var joints = LinkageSpecMapping.FromSpec(linkage, image?.Size.Height, draft.PixelsToMillimeters);
        var mode = draft.RearSuspension.Kind;
        return new BikeEditorInputs
        {
            ForkStroke = draft.ForkStroke, ShockStroke = draft.ShockStroke, ManualHeadAngle = draft.HeadAngle,
            Mode = mode, FrontWheel = draft.FrontWheel, RearWheel = draft.RearWheel, Chainstay = draft.Chainstay,
            Image = mode == RearSuspensionKind.Linkage ? image : null,
            Rotation = mode == RearSuspensionKind.Linkage ? draft.ImageRotationDegrees : 0,
            Linkage = !persisted && mode == RearSuspensionKind.Linkage && joints.Joints.IsEmpty ? LinkageIntentReducers.Seed() : joints,
            Curve = (draft.RearSuspension as RearSuspensionSpec.LeverageRatio)?.Spec
        };
    }

    public static BikeEditorInputs Mode(BikeEditorInputs inputs, RearSuspensionKind mode)
    {
        if (inputs.Mode == mode) return inputs;
        inputs = inputs.Mode switch
        {
            RearSuspensionKind.Linkage => inputs with { Chainstay = null, FrontWheel = null, RearWheel = null,
                Image = null, Rotation = 0, Linkage = LinkageDraft.Empty },
            RearSuspensionKind.LeverageRatio => inputs with { Curve = null, ImportErrors = [] },
            _ => inputs
        };
        return inputs with { Mode = mode, ShockStroke = mode == RearSuspensionKind.None ? null : inputs.ShockStroke,
            Linkage = mode == RearSuspensionKind.Linkage ? LinkageIntentReducers.Seed() : inputs.Linkage };
    }

    public static WheelSpec? WheelParts(EtrtoRimSize? rim, double? width, double? diameter) =>
        WheelSpec.FromValues(rim.HasValue && width.HasValue ? Math.Round(rim.Value.CalculateTotalDiameterMm(width.Value), 1) : diameter, rim, width);

    public static BikeEditorInputs Wheel(BikeEditorInputs inputs, bool front, WheelSpec? next)
    {
        var previous = front ? inputs.FrontWheel : inputs.RearWheel;
        var updated = front ? inputs with { FrontWheel = next } : inputs with { RearWheel = next };
        return previous?.DiameterMm != next?.DiameterMm ? Align(updated) : updated;
    }

    public static BikeEditorInputs Align(BikeEditorInputs inputs)
    {
        if (inputs.Image is null || BikeEditorGeometryDeriver.Scale(inputs) is not { } scale ||
            inputs.FrontWheel?.DiameterMm is not { } frontDiameter || inputs.RearWheel?.DiameterMm is not { } rearDiameter ||
            inputs.Linkage.Joints.FirstOrDefault(j => j.Type == JointType.FrontWheel) is not { } front ||
            inputs.Linkage.Joints.FirstOrDefault(j => j.Type == JointType.RearWheel) is not { } rear) return inputs;
        var (delta, _) = GroundCalculator.CalculateGroundRotation(front.X, front.Y, frontDiameter / (2 * scale),
            rear.X, rear.Y, rearDiameter / (2 * scale));
        return Math.Abs(delta) > 0.01
            ? inputs with { Rotation = inputs.Rotation + delta, Linkage = LinkageIntentReducers.Rotate(inputs.Linkage, delta) }
            : inputs;
    }
}
