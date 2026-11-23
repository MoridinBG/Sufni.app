using System;
using System.Collections.Immutable;
using System.Linq;
using Avalonia;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Bikes.ViewModels.LinkageEditing;
using Sufni.Kinematics;

namespace Sufni.App.Bikes.ViewModels.Editors;

public static class BikeEditorGeometryDeriver
{
    public static double? PhotoHeadAngle(RearSuspensionKind mode, bool hasImage, HeadAngleGeometry geometry) =>
        mode == RearSuspensionKind.Linkage && hasImage ? BikeGeometry.HeadAngle(geometry) : null;

    public static double? Scale(BikeEditorInputs inputs)
    {
        var joints = inputs.Linkage.Joints;
        return joints.FirstOrDefault(j => j.Type == JointType.BottomBracket) is { } bracket &&
            joints.FirstOrDefault(j => j.Type == JointType.RearWheel) is { } rear
            ? GeometryUtils.CalculatePixelsToMillimetersFromChainstay(inputs.Chainstay, rear, bracket) : null;
    }

    public static BikeEditorGeometry Derive(BikeEditorInputs inputs, BikeDraft baseline, BikeAnalysisPresentation? presentation)
    {
        var scale = Scale(inputs);
        var joints = inputs.Linkage.Joints;
        var front = joints.FirstOrDefault(j => j.Type == JointType.FrontWheel);
        var rear = joints.FirstOrDefault(j => j.Type == JointType.RearWheel);
        var (first, second) = BikeGeometry.HeadTubePair(joints, joint => joint.Type);
        var photoAngle = PhotoHeadAngle(inputs.Mode, inputs.Image is not null, new(
            first, second, front, rear, inputs.FrontWheel?.DiameterMm, inputs.RearWheel?.DiameterMm, scale));
        var angle = photoAngle ?? inputs.ManualHeadAngle;
        var height = inputs.Image?.Size.Height;
        RearSuspensionSpec spec = inputs.Mode switch
        {
            RearSuspensionKind.None => new RearSuspensionSpec.Hardtail(),
            RearSuspensionKind.Linkage => LinkageSpecMapping.ToSpec(inputs.Linkage, height, scale, inputs.ShockStroke) is { } linkage
                ? new RearSuspensionSpec.Linkage(linkage) : new RearSuspensionSpec.LinkageDraft(),
            RearSuspensionKind.LeverageRatio => inputs.Curve is { } curve
                ? new RearSuspensionSpec.LeverageRatio(curve) : new RearSuspensionSpec.LeverageRatioDraft(),
            _ => throw new ArgumentOutOfRangeException(nameof(inputs))
        };
        var frontOverlay = Overlay(front, inputs.FrontWheel, scale);
        var rearOverlay = Overlay(rear, inputs.RearWheel, scale);
        var hasWheels = front is not null && rear is not null && inputs.FrontWheel?.DiameterMm is not null && inputs.RearWheel?.DiameterMm is not null;
        Rect? bounds = null;
        Rect imageBounds = default;
        if (inputs.Image is { Size: var size })
        {
            var (x, y, right, bottom) = CoordinateRotation.GetRotatedBounds(size.Width, size.Height, inputs.Rotation);
            bounds = imageBounds = new Rect(x, y, right - x, bottom - y);
        }
        void Include(Rect rect) => bounds = bounds?.Union(rect) ?? rect;
        foreach (var joint in joints) Include(new Rect(joint.X - 20, joint.Y - 20, 40, 40));
        if (hasWheels)
        {
            Include(new Rect(frontOverlay.Left, frontOverlay.Top, frontOverlay.Diameter, frontOverlay.Diameter));
            Include(new Rect(rearOverlay.Left, rearOverlay.Top, rearOverlay.Diameter, rearOverlay.Diameter));
        }

        var lengths = inputs.Linkage.Links.ToImmutableDictionary(link => link.Id, link =>
            inputs.Linkage.Joint(link.A) is { } a && inputs.Linkage.Joint(link.B) is { } b
                ? (scale ?? 0) * GeometryUtils.CalculateDistance(a, b) : 0);
        ImmutableArray<Point> path = presentation?.Spec == spec && presentation.Data.RearAxlePathData is { } data &&
            scale is { } factor && height is { } h
            ? [.. data.X.Zip(data.Y, (x, y) => new Point(x / factor, h - y / factor))] : [];
        return new(scale, angle, photoAngle.HasValue, spec,
            BikeSaveValidator.HasRequiredInputs(angle, inputs.ForkStroke, inputs.Mode, inputs.FrontWheel?.DiameterMm, inputs.RearWheel?.DiameterMm),
            BikeAuthoredDraftComparison.HasChanges(inputs, baseline, angle, spec), frontOverlay, rearOverlay, hasWheels,
            imageBounds, (bounds ?? default(Rect)).Inflate(20), path, lengths);
    }

    private static BikeWheelOverlay Overlay(LinkageJoint? joint, WheelSpec? wheel, double? scale)
    {
        var diameter = wheel?.DiameterMm / scale ?? 0;
        var rim = wheel?.DiameterMm is null ? 0 : (wheel.RimSize?.BeadDiameterMm ?? wheel.DiameterMm * 0.83) / scale ?? 0;
        return new((joint?.X - diameter / 2) ?? 0, (joint?.Y - diameter / 2) ?? 0, diameter, rim);
    }
}
