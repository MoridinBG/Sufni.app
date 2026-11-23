using System;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Services;
using Sufni.App.Bikes.Stores;
using Sufni.App.Bikes.ViewModels.LinkageEditing;
using Sufni.Kinematics;

namespace Sufni.App.Bikes.ViewModels.Editors;

public sealed class BikeEditorActions
{
    private readonly BikeEditorStateController controller;
    internal BikeEditorActions(BikeEditorStateController controller) => this.controller = controller;

    private void Edit(Func<BikeEditorInputs, BikeEditorInputs> edit, bool busy = true) =>
        controller.Edit(state => state with { Inputs = edit(state.Inputs) }, busy);

    public void ForkStroke(double? value) => Edit(inputs => inputs with { ForkStroke = value });
    public void ShockStroke(double? value) => Edit(inputs => inputs with { ShockStroke = value });
    public void Chainstay(double? value) => Edit(inputs => inputs with { Chainstay = value });
    public void HeadAngle(double? value) => controller.Edit(state => state.Geometry.IsHeadAngleDerived ? state :
        state with { Inputs = state.Inputs with { ManualHeadAngle = value } }, false);
    public void Mode(RearSuspensionKind mode) => controller.Edit(state => state.Inputs.Mode == mode ? state :
        state with { Inputs = BikeEditorIntentReducers.Mode(state.Inputs, mode), Selection = null, Presentation = null }, false);
    public void Overlay(bool value) => controller.Edit(state => state with { OverlayVisible = value }, false);
    public void Select(int? id) => controller.Edit(state => state with { Selection = id }, false);

    private void Wheel(bool front, Func<WheelSpec?, WheelSpec?> change) => Edit(inputs =>
        BikeEditorIntentReducers.Wheel(inputs, front, change(front ? inputs.FrontWheel : inputs.RearWheel)));
    public void WheelRim(bool front, EtrtoRimSize? rim) => Wheel(front, w => BikeEditorIntentReducers.WheelParts(rim, w?.TireWidth, w?.DiameterMm));
    public void WheelWidth(bool front, double? width) => Wheel(front, w => BikeEditorIntentReducers.WheelParts(w?.RimSize, width, w?.DiameterMm));
    public void WheelDiameter(bool front, double? value) => Wheel(front, _ => WheelSpec.FromValues(value, null, null));
    public void RemoveWheel(bool front) => Wheel(front, _ => null);
    public void Align() => Edit(BikeEditorIntentReducers.Align);

    private void Linkage(Func<LinkageDraft, LinkageDraft> edit) =>
        Edit(inputs => inputs.Mode == RearSuspensionKind.Linkage ? inputs with { Linkage = edit(inputs.Linkage) } : inputs);
    public void AddJoint(double x = 100, double y = 100) => Linkage(draft => LinkageIntentReducers.AddJoint(draft, x, y));
    public void AddLink() => Linkage(LinkageIntentReducers.AddLink);
    public void Move(int id, double x, double y) => Linkage(draft => LinkageIntentReducers.Move(draft, id, x, y));
    public void Rename(int id, string name) => Linkage(draft => LinkageIntentReducers.Rename(draft, id, name));
    public void JointType(int id, JointType? type) => Linkage(draft => LinkageIntentReducers.SetType(draft, id, type));
    public void LinkEnd(int id, bool first, int? joint) => Linkage(draft => LinkageIntentReducers.SetLinkEnd(draft, id, first, joint));
    public void DeleteSelected() => controller.Edit(state =>
        LinkageIntentReducers.CanDelete(state.Inputs.Linkage, state.Selection)
            ? state with { Selection = null, Inputs = state.Inputs with { Linkage = LinkageIntentReducers.Delete(state.Inputs.Linkage, state.Selection) } }
            : state, true);

    public void ImportCurve(LeverageRatioImportResult result) => Edit(inputs =>
        inputs.Mode == RearSuspensionKind.LeverageRatio ? result switch
        {
            LeverageRatioImportResult.Imported loaded => inputs with { Curve = loaded.Value, ImportErrors = [] },
            LeverageRatioImportResult.Invalid invalid => inputs with { ImportErrors = [.. invalid.ErrorMessages] },
            _ => inputs
        } : inputs, false);
    public void ClearCurve() => Edit(inputs => inputs with { Curve = null, ImportErrors = [] }, false);

    public bool LoadStarted() => controller.Deliver(state => Cancel(state) with { Phase = BikeEditorPhase.Loading });
    public bool Prepared(BikeDraft draft, BikeEditorImage? image, bool acceptBaseline, bool persisted, bool busy, bool recovery = false) =>
        controller.Deliver(state => state.Phase != (recovery ? BikeEditorPhase.Unavailable : BikeEditorPhase.Loading) ? null :
            BikeEditorStateController.Refresh(state, state with
            {
                Baseline = acceptBaseline ? draft : state.Baseline, IsPersisted = persisted, Phase = BikeEditorPhase.Ready,
                Inputs = BikeEditorIntentReducers.Prepare(draft, image, persisted), Selection = null, Presentation = null
            }, true, recovery, busy));
    public bool LoadFailed(bool previouslyReady, BikeDraft? recoveryBase = null) => controller.Deliver(state =>
        state.Phase != BikeEditorPhase.Loading ? null : state with {
            Phase = previouslyReady ? BikeEditorPhase.Ready : BikeEditorPhase.Unavailable, Baseline = recoveryBase ?? state.Baseline });
    public bool Saved(BikeDraft baseline) => controller.Deliver(state => BikeEditorStateController.Refresh(state,
        state with { Baseline = baseline, IsPersisted = true }, false, false, false));
    public bool ApplyImage(BikeEditorImage image) => controller.Deliver(state =>
        state.Phase == BikeEditorPhase.Ready
            ? BikeEditorStateController.Refresh(state, state with { Inputs = state.Inputs with { Image = image } }, false, true, true) : null);

    public bool AnalysisCompleted(long generation, BikeEditorAnalysisResult result) => controller.Deliver(state =>
    {
        if (state.Phase != BikeEditorPhase.Ready || state.Request.Generation != generation || state.Request.Spec is not { } spec)
            return null;
        var presentation = result is BikeEditorAnalysisResult.Computed computed ? new BikeAnalysisPresentation(spec, computed.Data) : null;
        return state with { Presentation = presentation, Request = state.Request with { Spec = null, Busy = false },
            Geometry = BikeEditorGeometryDeriver.Derive(state.Inputs, state.Baseline, presentation) };
    });
    public bool Loaded() => controller.Deliver(state => state.Phase == BikeEditorPhase.Ready ?
        BikeEditorStateController.Refresh(state, state with { AnalysisSuspended = false }, true, false, false) :
        state with { AnalysisSuspended = false });
    private static BikeEditorState Cancel(BikeEditorState state) => state with { Request = new(state.Request.Generation + 1, null, false, false) };
    public bool Unloaded() => controller.Deliver(state => Cancel(state) with { AnalysisSuspended = true });
    public bool Closed() => controller.Deliver(state => Cancel(state) with { Phase = BikeEditorPhase.Closed });
}
