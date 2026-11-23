using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Services;
using Sufni.App.Bikes.ViewModels.Editors;

namespace Sufni.App.Tests.Bikes.ViewModels.Editors;

public class BikeEditorStateTests
{
    private static BikeDraft Bike() => BikeDraft.From(TestSnapshots.Bike());

    [Fact]
    public void DraftAcceptancePreservesEditsMadeDuringSave()
    {
        var baseline = Bike();
        using var editor = new BikeEditorStateController(baseline, true);
        editor.Actions.Prepared(baseline, null, true, true, false);
        editor.Actions.ForkStroke(125);
        var saved = editor.Current.Capture(baseline.Name);
        editor.Actions.ForkStroke(140);

        editor.Actions.Saved(saved);

        Assert.True(editor.Current.Geometry.InputsDirty);
        Assert.Equal(140, editor.Current.Inputs.ForkStroke);
        editor.Actions.ForkStroke(125);
        Assert.False(editor.Current.Geometry.InputsDirty);
    }

    [Fact]
    public void ReloadRejectsEditsAndRestoresPreviousInputsOnFailure()
    {
        var baseline = Bike();
        using var editor = new BikeEditorStateController(baseline, true);
        editor.Actions.Prepared(baseline, null, true, true, false);
        editor.Actions.ForkStroke(125);
        editor.Actions.LoadStarted();
        editor.Actions.ForkStroke(140);
        editor.Actions.LoadFailed(true);

        Assert.Equal(BikeEditorPhase.Ready, editor.Current.Phase);
        Assert.Equal(125, editor.Current.Inputs.ForkStroke);
        Assert.True(editor.Current.Geometry.InputsDirty);
    }

    [Fact]
    public void ReentrantFieldEditsReachSubscribersAsSeparateCoherentStates()
    {
        var baseline = Bike();
        using var editor = new BikeEditorStateController(baseline, true);
        editor.Actions.Prepared(baseline, null, true, true, false);
        using var first = editor.States.Subscribe(state =>
        {
            if (state.Inputs.ForkStroke == 130) editor.Actions.ForkStroke(140);
        });
        var observed = new List<double?>();
        using var second = editor.States.Subscribe(state => observed.Add(state.Inputs.ForkStroke));
        observed.Clear();

        editor.Actions.ForkStroke(130);
        Assert.Equal(new double?[] { 130, 140 }, observed);
    }

    [Fact]
    public void SupersededCompletionCannotDismissPendingAnalysis()
    {
        var baseline = Bike();
        using var editor = new BikeEditorStateController(baseline, true);
        editor.Actions.Prepared(baseline, null, true, true, false);
        var old = editor.Current.Request.Generation;
        editor.Actions.LoadStarted();
        editor.Actions.Prepared(baseline, null, false, true, true);

        Assert.False(editor.Actions.AnalysisCompleted(old, new BikeEditorAnalysisResult.NotApplicable()));
        Assert.True(editor.Current.Request.Busy);
        editor.Actions.Unloaded();
        Assert.False(editor.Current.Request.Busy);
        editor.Actions.Loaded();
        Assert.Equal(baseline.RearSuspension, editor.Current.Request.Spec);
    }

    [Fact]
    public void DirectWheelDiameterReplacesCalculatedDimensions()
    {
        var baseline = Bike();
        using var editor = new BikeEditorStateController(baseline, false);
        editor.Actions.Prepared(baseline, null, true, false, false);
        editor.Actions.Mode(RearSuspensionKind.Linkage);
        editor.Actions.WheelRim(true, Sufni.Kinematics.EtrtoRimSize.Inch29);
        editor.Actions.WheelWidth(true, 2.4);
        Assert.Equal(743.9, editor.Current.Inputs.FrontWheel!.DiameterMm);
        editor.Actions.WheelDiameter(true, 740);
        Assert.Null(editor.Current.Inputs.FrontWheel!.TireWidth);
        Assert.Null(editor.Current.Inputs.FrontWheel.RimSize);
        editor.Actions.RemoveWheel(true);
        Assert.Null(editor.Current.Inputs.FrontWheel);
    }
}
