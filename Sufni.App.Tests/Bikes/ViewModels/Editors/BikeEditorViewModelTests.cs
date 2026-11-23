using Avalonia.Headless.XUnit;
using NSubstitute;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Infrastructure;
using Sufni.App.Shared.Models;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Kinematics;

namespace Sufni.App.Tests.Bikes.ViewModels.Editors;

[Collection("Ui")]
public class BikeEditorViewModelTests : BikeEditorFixture
{
    [AvaloniaFact]
    public async Task ResetRestoresJointOrderWhileKeepingSurvivingRows()
    {
        var snapshot = FullSuspensionSnapshot();
        var spec = snapshot.Linkage!;
        var pivot = new JointSpec("Pivot", JointType.Floating, 10, 5);
        var ordered = new LinkageSpec([pivot, .. spec.Joints], spec.Links, spec.Shock, spec.ShockStroke);
        var editor = CreateEditor(snapshot with { RearSuspension = new RearSuspensionSpec.Linkage(ordered) });
        var rows = editor.Linkage.JointViewModels;
        var surviving = rows[1];
        editor.Linkage.SelectedPoint = rows[0];
        editor.Linkage.DeleteSelectedItemCommand.Execute(null);
        Assert.Same(surviving, rows[0]);

        await editor.ResetCommand.ExecuteAsync(null);

        Assert.Equal(ordered.Joints.Select(joint => joint.Name), rows.Select(row => row.Name));
        Assert.Same(surviving, rows[1]);
        await editor.PrepareCloseAsync();
    }

    [AvaloniaFact]
    public void Construction_UsesPhotoAngle_WhenItDiffersFromManualInput()
    {
        var calibrated = CalibratedFullSuspensionSnapshot(imageRotationDegrees: 12.5);

        var editor = CreateEditor(calibrated with { HeadAngle = 79.9 });

        Assert.True(editor.Frame.IsHeadAngleDerived);
        Assert.Equal(calibrated.HeadAngle, editor.Frame.HeadAngle);
    }

    [AvaloniaFact]
    public void SavedCustomHeadTubeNamesCalculateWithoutRenamingThePoints()
    {
        var snapshot = FullSuspensionSnapshot(true, frontWheelDiameter: 702, rearWheelDiameter: 700);
        var spec = snapshot.Linkage!;
        var joints = spec.Joints.Select((joint, index) => joint.Type == JointType.HeadTube
            ? joint with { Name = $"Point{index}" } : joint).ToArray();
        var editor = CreateEditor(snapshot with { HeadAngle = 62.3,
            RearSuspension = new RearSuspensionSpec.Linkage(new LinkageSpec(joints, spec.Links, spec.Shock, spec.ShockStroke)) });

        Assert.True(editor.Frame.IsHeadAngleDerived);
        Assert.Equal(71.6, editor.Frame.HeadAngle);
        Assert.Equal(joints.Select(joint => joint.Name), editor.Linkage.JointViewModels.Select(row => row.Name));
    }

    [AvaloniaTheory]
    [InlineData(0.0)]
    [InlineData(-10.0)]
    [InlineData(double.NaN)]
    public void InvalidChainstay_KeepsLastPhotoAngleForManualEditing(double invalidChainstay)
    {
        var snapshot = CalibratedFullSuspensionSnapshot();
        var editor = CreateEditor(snapshot);
        Assert.True(editor.Linkage.PixelsToMillimeters.HasValue);
        var headTube = editor.Linkage.JointViewModels.Single(joint => joint.Name == JointNames.HeadTube1);
        headTube.X += 3;
        var calculated = editor.Frame.HeadAngle;
        Assert.NotNull(calculated);
        Assert.NotEqual(snapshot.HeadAngle, calculated);
        editor.Frame.HeadAngle = 61;
        Assert.Equal(calculated, editor.Frame.HeadAngle);

        editor.Linkage.Chainstay = invalidChainstay;

        Assert.Null(editor.Linkage.PixelsToMillimeters);
        Assert.False(editor.Frame.IsHeadAngleDerived);
        Assert.Equal(calculated, editor.Frame.HeadAngle);
        editor.Frame.HeadAngle = 63;
        Assert.Equal(63, editor.Frame.HeadAngle);
    }

    [AvaloniaFact]
    public void InvalidChainstay_DoesNotTouchManualHeadAngle_WithoutPhotoJoints()
    {
        var snapshot = TestSnapshots.Bike(name: "manual head angle");
        var editor = CreateEditor(snapshot);
        var manualHeadAngle = editor.Frame.HeadAngle;
        Assert.NotNull(manualHeadAngle);

        editor.Linkage.Chainstay = 0;

        Assert.Equal(manualHeadAngle, editor.Frame.HeadAngle);
    }

    [AvaloniaFact]
    public void SaveCommand_Disabled_WhenOnlyOneWheelIsConfigured()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";
        editor.Wheels.FrontWheelDiameter = 760;

        Assert.False(editor.SaveCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void SaveCommand_Enabled_WhenOnlyOneWheelIsConfigured_OnLeverageRatioBike()
    {
        var snapshot = TestSnapshots.LeverageRatioBike(
            TestSnapshots.LeverageRatioCurve((0, 0), (30, 75), (60, 150))) with
        {
            FrontWheelDiameterMm = 760,
        };
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";

        Assert.True(editor.SaveCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void SaveCommand_Disabled_WhenForkStrokeMissing()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";
        editor.Frame.ForksStroke = null;

        Assert.False(editor.SaveCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void SaveCommand_Enabled_WhenForkOnlyAndChanged()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";

        Assert.True(editor.SaveCommand.CanExecute(null));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CloseCommand_KeepsEditorOpen_WhenSaveFailsOrConflictReloadIsDeclined(bool conflict)
    {
        var snapshot = TestSnapshots.Bike(updated: 5);
        var editor = CreateEditor(snapshot);
        editor.Name = "Changed bike";
        Assert.True(editor.SaveCommand.CanExecute(null));
        bikeCoordinator.SaveAsync(Arg.Any<BikeDraft>(), DraftFrom(snapshot)).Returns(
            conflict
                ? new BikeSaveResult.Conflict(DraftFrom(snapshot with { Updated = 6 }))
                : new BikeSaveResult.Failed(BikeSaveFailureReason.Save, "disk full"));
        dialogService.ShowCloseConfirmationAsync(true).Returns(PromptResult.Yes);
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        await editor.CloseCommand.ExecuteAsync(null);

        shell.DidNotReceive().Close(editor);
        shell.DidNotReceive().CompleteEdit(Arg.Any<EditorKey>());
        Assert.True(editor.IsDirty);
        Assert.True(editor.SaveCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Save_OnForkOnlyBike_OnConflict_PromptsUser_AndReloadsWhenAccepted()
    {
        var snapshot = TestSnapshots.Bike(name: "old", updated: 5);
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";

        var fresh = TestSnapshots.Bike(id: snapshot.Id, name: "remote-updated", updated: 12);
        bikeCoordinator.SaveAsync(Arg.Any<BikeDraft>(), DraftFrom(snapshot))
            .Returns(new BikeSaveResult.Conflict(DraftFrom(fresh)));
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal(fresh.Name, editor.Name);
        Assert.False(editor.IsDirty);
        Assert.False(editor.SaveCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Save_OnForkOnlyBike_OnConflict_DoesNothing_WhenUserDeclinesReload()
    {
        var snapshot = TestSnapshots.Bike(name: "old", updated: 5);
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";

        var fresh = TestSnapshots.Bike(id: snapshot.Id, name: "remote-updated", updated: 12);
        bikeCoordinator.SaveAsync(Arg.Any<BikeDraft>(), DraftFrom(snapshot))
            .Returns(new BikeSaveResult.Conflict(DraftFrom(fresh)));
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.True(editor.IsDirty);
        Assert.NotEqual(fresh.Name, editor.Name);
    }

    [AvaloniaFact]
    public async Task Save_KeepsEditsMadeDuringPersistenceDirty()
    {
        var snapshot = TestSnapshots.Bike(updated: 5);
        var editor = CreateEditor(snapshot);
        editor.Name = "submitted";
        var pending = new TaskCompletionSource<BikeSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        BikeDraft? submitted = null;
        bikeCoordinator.SaveAsync(Arg.Any<BikeDraft>(), DraftFrom(snapshot)).Returns(call =>
        {
            submitted = call.ArgAt<BikeDraft>(0);
            return pending.Task;
        });
        var save = editor.SaveCommand.ExecuteAsync(null);
        editor.Name = "later edit";
        pending.SetResult(new BikeSaveResult.Saved(submitted!.ToSnapshot() with { Updated = 11 }));

        await save.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.NotEqual(submitted?.Name, editor.Name);
        Assert.True(editor.IsDirty);
        shell.DidNotReceive().CompleteEdit(Arg.Any<EditorKey>());
        await editor.ResetCommand.ExecuteAsync(null);
        Assert.False(editor.IsDirty);
    }

    [AvaloniaFact]
    public void DeleteCommand_Disabled_WhenDependencyQuerySaysInUse()
    {
        var snapshot = TestSnapshots.Bike();
        dependencyQuery.IsBikeInUse(snapshot.Id).Returns(true);
        var editor = CreateEditor(snapshot);

        Assert.False(editor.DeleteCommand.CanExecute(true));
    }

    [AvaloniaFact]
    public async Task Reset_RestoresAcceptedSnapshot_AndClearsDirtyState()
    {
        var snapshot = CalibratedFullSuspensionSnapshot(imageRotationDegrees: 12.5);
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";
        editor.Linkage.Chainstay = 150;

        await editor.ResetCommand.ExecuteAsync(null);

        Assert.Equal(snapshot.Name, editor.Name);
        Assert.Equal(snapshot.ImageRotationDegrees, editor.Linkage.ImageRotationDegrees);
        Assert.False(editor.IsDirty);
        Assert.False(editor.SaveCommand.CanExecute(null));
    }
}
