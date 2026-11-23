using System;
using System.Threading.Tasks;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Services;
using Sufni.App.Bikes.Stores;
using Sufni.App.Bikes.ViewModels.LinkageEditing;
using Sufni.Kinematics;
using Reduce = Sufni.App.Bikes.ViewModels.Editors.BikeEditorIntentReducers;

namespace Sufni.App.Bikes.ViewModels.Editors;

/// <summary>
/// The intents the bike editor accepts. Edits apply only in <see cref="BikeEditorPhase.Ready"/>.
/// Controller intents complete with whether the reducer accepted them.
/// </summary>
public sealed class BikeEditorActions
{
    private readonly BikeEditorStateController controller;
    internal BikeEditorActions(BikeEditorStateController controller) => this.controller = controller;

    private void Edit(Func<BikeEditorState, BikeEditorState> reduce, BikeAnalysisTrigger analysis = BikeAnalysisTrigger.Edit) =>
        controller.Edit(reduce, analysis);
    private void EditInputs(Func<BikeEditorInputs, BikeEditorInputs> edit) =>
        Edit(state => state with { Inputs = edit(state.Inputs) });
    private Task<bool> Deliver(Func<BikeEditorState, BikeEditorState?> reduce, BikeAnalysisTrigger? analysis = null) =>
        controller.Deliver(state => reduce(state) is { } next ? new BikeEditorTransition(next, analysis) : null);

    public void ForkStroke(double? value) => EditInputs(inputs => inputs with { ForkStroke = value });
    public void ShockStroke(double? value) => EditInputs(inputs => inputs with { ShockStroke = value });
    public void Chainstay(double? value) => EditInputs(inputs => inputs with { Chainstay = value });
    public void HeadAngle(double? value) => Edit(state => Reduce.HeadAngle(state, value));
    public void Mode(RearSuspensionKind mode) => Edit(state => Reduce.Mode(state, mode), BikeAnalysisTrigger.QuietEdit);
    public void Overlay(bool value) => Edit(state => state with { OverlayVisible = value });
    public void Select(int? id) => Edit(state => state with { Selection = id });

    private void Wheel(bool front, Func<WheelSpec?, WheelSpec?> change) => EditInputs(inputs =>
        Reduce.Wheel(inputs, front, change(front ? inputs.FrontWheel : inputs.RearWheel)));
    public void WheelRim(bool front, EtrtoRimSize? rim) => Wheel(front, w => Reduce.WheelParts(rim, w?.TireWidth, w?.DiameterMm));
    public void WheelWidth(bool front, double? width) => Wheel(front, w => Reduce.WheelParts(w?.RimSize, width, w?.DiameterMm));
    public void WheelDiameter(bool front, double? value) => Wheel(front, _ => WheelSpec.FromValues(value, null, null));
    public void RemoveWheel(bool front) => Wheel(front, _ => null);
    public void Align() => EditInputs(Reduce.Align);

    private void Linkage(Func<LinkageDraft, LinkageDraft> edit) => Edit(state => Reduce.Linkage(state, edit));
    public void AddJoint() => Linkage(draft => LinkageIntentReducers.AddJoint(draft));
    public void AddJoint(double x, double y) => Linkage(draft => LinkageIntentReducers.AddJoint(draft, x, y));
    public void AddLink() => Linkage(LinkageIntentReducers.AddLink);
    public void Move(int id, double x, double y) => Linkage(draft => LinkageIntentReducers.Move(draft, id, x, y));
    public void Rename(int id, string name) => Linkage(draft => LinkageIntentReducers.Rename(draft, id, name));
    public void JointType(int id, JointType? type) => Linkage(draft => LinkageIntentReducers.SetType(draft, id, type));
    public void LinkEnd(int id, bool first, int? joint) => Linkage(draft => LinkageIntentReducers.SetLinkEnd(draft, id, first, joint));
    public void DeleteSelected() => Edit(Reduce.DeleteSelected);

    public void ImportCurve(LeverageRatioImportResult result) =>
        Edit(state => Reduce.ImportCurve(state, result), BikeAnalysisTrigger.QuietEdit);
    public void ClearCurve() => Edit(Reduce.ClearCurve, BikeAnalysisTrigger.QuietEdit);

    public Task<bool> LoadStarted() => Deliver(Reduce.LoadStarted);
    public Task<bool> Prepared(BikeDraft draft, BikeEditorImage? image, BikeDraftLoadKind kind) =>
        Deliver(state => Reduce.Prepared(state, draft, image, kind), kind switch
        {
            BikeDraftLoadKind.Open => BikeAnalysisTrigger.Load,
            BikeDraftLoadKind.ImageRecovery => BikeAnalysisTrigger.Recovery,
            _ => BikeAnalysisTrigger.Reload
        });
    public Task<bool> LoadFailed(BikeDraftLoadKind kind, BikeDraft? recoveryBase = null) => controller.Deliver(state =>
        Reduce.LoadFailed(state, kind, recoveryBase) is { } next
            ? new BikeEditorTransition(next, ResumesAnalysis(next) ? BikeAnalysisTrigger.Load : null)
            : null);
    public Task<bool> BaselineAccepted(BikeDraft baseline) => Deliver(state => Reduce.BaselineAccepted(state, baseline));
    public Task<bool> ApplyImage(BikeEditorImage image) =>
        Deliver(state => Reduce.ApplyImage(state, image), BikeAnalysisTrigger.Edit);
    public Task<bool> AnalysisCompleted(long generation, BikeEditorAnalysisResult result) =>
        Deliver(state => Reduce.AnalysisCompleted(state, generation, result));
    public void Loaded() => _ = controller.Deliver(state =>
        new BikeEditorTransition(Reduce.Loaded(state), state.Phase == BikeEditorPhase.Ready ? BikeAnalysisTrigger.Load : null));
    public void Unloaded() => _ = Deliver(Reduce.Unloaded);
    public void Closed() => _ = Deliver(Reduce.Closed);

    // A reload cancels the analysis; returning to the previous inputs reruns it unless its result is still shown.
    private static bool ResumesAnalysis(BikeEditorState state) =>
        state.Phase == BikeEditorPhase.Ready && state.Presentation?.Spec != state.Geometry.Spec;
}
