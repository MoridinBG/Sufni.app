using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Services;
using Sufni.App.Bikes.Stores;
using Sufni.App.Bikes.ViewModels.Editors;

namespace Sufni.App.Tests.Bikes.ViewModels.Editors;

public class BikeEditorStateControllerTests
{
    private static BikeDraft Bike() => BikeDraft.From(TestSnapshots.Bike());

    private static BikeEditorStateController ShownPhotoAnalysis()
    {
        var linkage = TestSnapshots.FullSuspensionLinkageSpec(true);
        var draft = Bike() with { RearSuspension = new RearSuspensionSpec.Linkage(linkage),
            FrontWheel = new(702, null, null), RearWheel = new(700, null, null), ShockStroke = linkage.ShockStroke,
            PixelsToMillimeters = 2, HeadAngle = 65, ImageBytes = [1] };
        var editor = new BikeEditorStateController(draft, true);
        editor.Actions.Prepared(draft, new BikeEditorImage(draft.ImageBytesMemory, new Avalonia.Size(500, 50)), BikeDraftLoadKind.Open);
        var data = new Sufni.Kinematics.CoordinateList([2], [4]);
        editor.Actions.AnalysisCompleted(editor.Current.Request.Generation,
            new BikeEditorAnalysisResult.Computed(new BikeAnalysisPresentationData(data, data)));
        return editor;
    }

    [Fact]
    public void TheShownAnalysisDrawsTheAxlePathOnThePhoto()
    {
        using var editor = ShownPhotoAnalysis();

        Assert.Equal(new Avalonia.Point(1, 48), Assert.Single(editor.Current.Geometry.RearAxlePath));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnIncompleteLinkageHidesTheAxlePathButKeepsTheShownAnalysis(bool unfinishedLink)
    {
        using var editor = ShownPhotoAnalysis();

        if (unfinishedLink) editor.Actions.AddLink();
        else editor.Actions.ShockStroke(null);

        Assert.IsType<RearSuspensionSpec.LinkageDraft>(editor.Current.Geometry.Spec);
        Assert.Empty(editor.Current.Geometry.RearAxlePath);
        Assert.NotNull(editor.Current.Presentation);
    }

    [Fact]
    public void AnIncompleteLinkageKeepsThePhotoHeadAngle()
    {
        using var editor = ShownPhotoAnalysis();

        editor.Actions.AddLink();

        Assert.True(editor.Current.Geometry.IsHeadAngleDerived);
        Assert.Equal(71.6, editor.Current.Geometry.EffectiveHeadAngle);
    }

    [Fact]
    public void LeavingLinkageModeKeepsTheLastPhotoHeadAngleForManualEditing()
    {
        using var editor = ShownPhotoAnalysis();

        editor.Actions.Mode(RearSuspensionKind.None);

        Assert.False(editor.Current.Geometry.IsHeadAngleDerived);
        Assert.Equal(71.6, editor.Current.Geometry.EffectiveHeadAngle);
    }

    [Fact]
    public void ChangingModeClearsTheShownAnalysis()
    {
        using var editor = ShownPhotoAnalysis();

        editor.Actions.Mode(RearSuspensionKind.None);

        Assert.Null(editor.Current.Presentation);
    }

    [Fact]
    public void DraftAcceptancePreservesEditsMadeDuringSave()
    {
        var baseline = Bike();
        using var editor = new BikeEditorStateController(baseline, true);
        editor.Actions.Prepared(baseline, null, BikeDraftLoadKind.Open);
        editor.Actions.ForkStroke(125);
        var saved = editor.Current.Capture(baseline.Name);
        editor.Actions.ForkStroke(140);

        editor.Actions.BaselineAccepted(saved);

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
        editor.Actions.Prepared(baseline, null, BikeDraftLoadKind.Open);
        editor.Actions.ForkStroke(125);
        editor.Actions.LoadStarted();
        editor.Actions.ForkStroke(140);
        editor.Actions.LoadFailed(BikeDraftLoadKind.Reset);

        Assert.Equal(BikeEditorPhase.Ready, editor.Current.Phase);
        Assert.Equal(baseline.RearSuspension, editor.Current.Request.Spec);
        Assert.Equal(125, editor.Current.Inputs.ForkStroke);
        Assert.True(editor.Current.Geometry.InputsDirty);
    }

    [Fact]
    public void ReentrantFieldEditsReachSubscribersAsSeparateCoherentStates()
    {
        var baseline = Bike();
        using var editor = new BikeEditorStateController(baseline, true);
        editor.Actions.Prepared(baseline, null, BikeDraftLoadKind.Open);
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
    public async Task ASupersededCompletionIsRejectedWhileTheCurrentAnalysisStaysBusy()
    {
        var baseline = Bike();
        using var editor = new BikeEditorStateController(baseline, true);
        editor.Actions.Prepared(baseline, null, BikeDraftLoadKind.Open);
        var old = editor.Current.Request.Generation;
        editor.Actions.LoadStarted();
        editor.Actions.Prepared(baseline, null, BikeDraftLoadKind.Reset);

        Assert.False(await editor.Actions.AnalysisCompleted(old, new BikeEditorAnalysisResult.NotApplicable()));
        Assert.True(editor.Current.Request.Busy);
    }

    [Fact]
    public void UnloadingTheViewCancelsThePendingAnalysis()
    {
        var baseline = Bike();
        using var editor = new BikeEditorStateController(baseline, true);
        editor.Actions.Prepared(baseline, null, BikeDraftLoadKind.Open);
        editor.Actions.LoadStarted();
        editor.Actions.Prepared(baseline, null, BikeDraftLoadKind.Reset);

        editor.Actions.Unloaded();

        Assert.False(editor.Current.Request.Busy);
        Assert.Null(editor.Current.Request.Spec);
    }

    [Fact]
    public void LoadingTheViewRerunsTheAnalysisForTheCurrentSpec()
    {
        var baseline = Bike();
        using var editor = new BikeEditorStateController(baseline, true);
        editor.Actions.Prepared(baseline, null, BikeDraftLoadKind.Open);
        editor.Actions.Unloaded();

        editor.Actions.Loaded();

        Assert.Equal(baseline.RearSuspension, editor.Current.Request.Spec);
    }

    [Fact]
    public void RimAndTireWidthCalculateTheWheelDiameter()
    {
        var baseline = Bike();
        using var editor = new BikeEditorStateController(baseline, false);
        editor.Actions.Prepared(baseline, null, BikeDraftLoadKind.Open);
        editor.Actions.Mode(RearSuspensionKind.Linkage);

        editor.Actions.WheelRim(true, Sufni.Kinematics.EtrtoRimSize.Inch29);
        editor.Actions.WheelWidth(true, 2.4);

        Assert.Equal(743.9, editor.Current.Inputs.FrontWheel!.DiameterMm);
    }

    [Fact]
    public void ADirectDiameterReplacesTheRimAndTireWidth()
    {
        var baseline = Bike();
        using var editor = new BikeEditorStateController(baseline, false);
        editor.Actions.Prepared(baseline, null, BikeDraftLoadKind.Open);
        editor.Actions.Mode(RearSuspensionKind.Linkage);
        editor.Actions.WheelRim(true, Sufni.Kinematics.EtrtoRimSize.Inch29);
        editor.Actions.WheelWidth(true, 2.4);

        editor.Actions.WheelDiameter(true, 740);

        Assert.Equal(new WheelSpec(740, null, null), editor.Current.Inputs.FrontWheel);
    }
}
