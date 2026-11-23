using System;
using System.Linq;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Services;
using Sufni.App.Bikes.Stores;
using Sufni.App.Bikes.ViewModels.LinkageEditing;
using Sufni.Kinematics;

namespace Sufni.App.Bikes.ViewModels.Editors;

internal static class BikeEditorIntentReducers
{
    public static BikeEditorState HeadAngle(BikeEditorState state, double? value) =>
        state.Geometry.IsHeadAngleDerived ? state : state with { Inputs = state.Inputs with { ManualHeadAngle = value } };

    public static BikeEditorState Mode(BikeEditorState state, RearSuspensionKind mode) =>
        state.Inputs.Mode == mode ? state : state with { Inputs = Mode(state.Inputs, mode), Selection = null, Presentation = null };

    public static BikeEditorState Linkage(BikeEditorState state, Func<LinkageDraft, LinkageDraft> edit) =>
        state.Inputs.Mode == RearSuspensionKind.Linkage
            ? state with { Inputs = state.Inputs with { Linkage = edit(state.Inputs.Linkage) } }
            : state;

    public static BikeEditorState DeleteSelected(BikeEditorState state) =>
        LinkageIntentReducers.CanDelete(state.Inputs.Linkage, state.Selection)
            ? state with { Selection = null, Inputs = state.Inputs with { Linkage = LinkageIntentReducers.Delete(state.Inputs.Linkage, state.Selection) } }
            : state;

    public static BikeEditorState ImportCurve(BikeEditorState state, LeverageRatioImportResult result) =>
        state.Inputs.Mode != RearSuspensionKind.LeverageRatio ? state : result switch
        {
            LeverageRatioImportResult.Imported loaded => state with { Inputs = state.Inputs with { Curve = loaded.Value, ImportErrors = [] } },
            LeverageRatioImportResult.Invalid invalid => state with { Inputs = state.Inputs with { ImportErrors = [.. invalid.ErrorMessages] } },
            _ => state
        };

    public static BikeEditorState ClearCurve(BikeEditorState state) =>
        state with { Inputs = state.Inputs with { Curve = null, ImportErrors = [] } };

    public static BikeEditorState LoadStarted(BikeEditorState state) => CancelAnalysis(state) with { Phase = BikeEditorPhase.Loading };

    public static BikeEditorState? Prepared(BikeEditorState state, BikeDraft draft, BikeEditorImage? image, BikeDraftLoadKind kind)
    {
        var expected = kind == BikeDraftLoadKind.ImageRecovery ? BikeEditorPhase.Unavailable : BikeEditorPhase.Loading;
        if (state.Phase != expected) return null;
        var persisted = kind == BikeDraftLoadKind.ConflictReload || state.IsPersisted;
        return state with
        {
            Baseline = kind is BikeDraftLoadKind.Open or BikeDraftLoadKind.ConflictReload ? draft : state.Baseline,
            IsPersisted = persisted,
            Phase = BikeEditorPhase.Ready,
            Inputs = Prepare(draft, image, persisted),
            Selection = null,
            Presentation = null
        };
    }

    // An initial load has no previous inputs to return to; a reload keeps the inputs it would have replaced.
    public static BikeEditorState? LoadFailed(BikeEditorState state, BikeDraftLoadKind kind, BikeDraft? recoveryBase) =>
        state.Phase != BikeEditorPhase.Loading ? null
        : kind == BikeDraftLoadKind.Open ? state with { Phase = BikeEditorPhase.Unavailable, Baseline = recoveryBase ?? state.Baseline }
        : state with { Phase = BikeEditorPhase.Ready };

    public static BikeEditorState BaselineAccepted(BikeEditorState state, BikeDraft baseline) =>
        state with { Baseline = baseline, IsPersisted = true };

    public static BikeEditorState? ApplyImage(BikeEditorState state, BikeEditorImage image) =>
        state.Phase == BikeEditorPhase.Ready && state.Inputs.Mode == RearSuspensionKind.Linkage
            ? state with { Inputs = state.Inputs with { Image = image } }
            : null;

    public static BikeEditorState? AnalysisCompleted(BikeEditorState state, long generation, BikeEditorAnalysisResult result)
    {
        if (state.Phase != BikeEditorPhase.Ready || state.Request.Generation != generation || state.Request.Spec is not { } spec)
            return null;
        var presentation = result is BikeEditorAnalysisResult.Computed computed ? new BikeAnalysisPresentation(spec, computed.Data) : null;
        return state with { Presentation = presentation, Request = state.Request with { Spec = null, Busy = false } };
    }

    public static BikeEditorState Loaded(BikeEditorState state) => state with { AnalysisSuspended = false };

    public static BikeEditorState Unloaded(BikeEditorState state) => CancelAnalysis(state) with { AnalysisSuspended = true };

    public static BikeEditorState Closed(BikeEditorState state) => CancelAnalysis(state) with { Phase = BikeEditorPhase.Closed };

    private static BikeEditorState CancelAnalysis(BikeEditorState state) =>
        state with { Request = new(state.Request.Generation + 1, null, false, false) };

    // While the photo defines the head angle, the manual angle follows it, so a lost calibration keeps the last calculated value.
    public static BikeEditorState FollowPhotoHeadAngle(BikeEditorState state) =>
        state.Geometry.IsHeadAngleDerived && state.Inputs.ManualHeadAngle != state.Geometry.EffectiveHeadAngle
            ? state with { Inputs = state.Inputs with { ManualHeadAngle = state.Geometry.EffectiveHeadAngle } }
            : state;

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
