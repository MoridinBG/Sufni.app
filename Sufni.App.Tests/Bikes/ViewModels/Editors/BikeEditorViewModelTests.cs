using Sufni.App.Shared.Models;
using Microsoft.Extensions.Time.Testing;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Headless.XUnit;
using Sufni.App.Tests.TestSupport.Async;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sufni.Kinematics;

using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Queries;
using Sufni.App.Bikes.Services;
using Sufni.App.Bikes.Stores;
using Sufni.App.Bikes.ViewModels.Editors;
using Sufni.App.Infrastructure;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.ViewModels.LinkageEditing;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Bikes.ViewModels.Editors;

[Collection("Ui")]
public class BikeEditorViewModelTests : BikeEditorFixture
{
    [AvaloniaFact]
    public void Construction_UsesPhotoAngle_WhenItDiffersFromManualInput()
    {
        var snapshot = FullSuspensionSnapshot(
            includeHeadTubeJoints: true,
            frontWheelRimSize: EtrtoRimSize.Inch29,
            frontWheelTireWidth: 2.4,
            frontWheelDiameter: TestSnapshots.WheelDiameter(EtrtoRimSize.Inch29, 2.4),
            rearWheelRimSize: EtrtoRimSize.Inch275,
            rearWheelTireWidth: 2.5,
            rearWheelDiameter: TestSnapshots.WheelDiameter(EtrtoRimSize.Inch275, 2.5),
            imageRotationDegrees: 12.5,
            updated: 7) with
        {
            HeadAngle = 79.9,
        };

        var editor = CreateEditor(snapshot);

        Assert.True(editor.Frame.IsHeadAngleDerived);
        Assert.NotEqual(79.9, editor.Frame.HeadAngle);
    }

    [AvaloniaFact]
    public void AdditionalHeadTubeTypedPoint_DoesNotChangeWhichJointsDriveHeadAngle()
    {
        var snapshot = FullSuspensionSnapshot(
            includeHeadTubeJoints: true,
            frontWheelRimSize: EtrtoRimSize.Inch29,
            frontWheelTireWidth: 2.4,
            frontWheelDiameter: TestSnapshots.WheelDiameter(EtrtoRimSize.Inch29, 2.4),
            rearWheelRimSize: EtrtoRimSize.Inch275,
            rearWheelTireWidth: 2.5,
            rearWheelDiameter: TestSnapshots.WheelDiameter(EtrtoRimSize.Inch275, 2.5),
            imageRotationDegrees: 12.5,
            updated: 7);
        var baselineEditor = CreateEditor(snapshot);
        var affectedEditor = CreateEditor(snapshot);

        var baselineHeadTube1 = Assert.Single(baselineEditor.Linkage.JointViewModels, joint => joint.Name == JointNames.HeadTube1);
        var affectedHeadTube1 = Assert.Single(affectedEditor.Linkage.JointViewModels, joint => joint.Name == JointNames.HeadTube1);
        var extraHeadTube = Assert.Single(affectedEditor.Linkage.JointViewModels, joint => joint.Name == JointNames.ShockEye1);

        var baselineHeadAngle = baselineEditor.Frame.HeadAngle;
        baselineHeadTube1.Y += 10;

        Assert.NotEqual(baselineHeadAngle, baselineEditor.Frame.HeadAngle);

        extraHeadTube.Type = JointType.HeadTube;
        affectedHeadTube1.Y += 10;

        Assert.Equal(baselineEditor.Frame.HeadAngle, affectedEditor.Frame.HeadAngle);
    }

    [AvaloniaTheory]
    [InlineData(0.0)]
    [InlineData(-10.0)]
    [InlineData(double.NaN)]
    public void InvalidChainstay_KeepsLastPhotoAngleForManualEditing(double invalidChainstay)
    {
        var snapshot = FullSuspensionSnapshot(
            includeHeadTubeJoints: true,
            frontWheelDiameter: TestSnapshots.WheelDiameter(EtrtoRimSize.Inch29, 2.4),
            rearWheelDiameter: TestSnapshots.WheelDiameter(EtrtoRimSize.Inch275, 2.5));
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
        editor.Name = "renamed";   // make dirty
        editor.Frame.ForksStroke = null; // invalidate

        Assert.False(editor.SaveCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void SaveCommand_Enabled_WhenForkOnlyAndChanged()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";

        // CanSave allows fork-only bikes when ShockStroke is null.
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
        var snapshot = FullSuspensionSnapshot(
            includeHeadTubeJoints: true,
            frontWheelRimSize: EtrtoRimSize.Inch29,
            frontWheelTireWidth: 2.4,
            frontWheelDiameter: TestSnapshots.WheelDiameter(EtrtoRimSize.Inch29, 2.4),
            rearWheelRimSize: EtrtoRimSize.Inch275,
            rearWheelTireWidth: 2.5,
            rearWheelDiameter: TestSnapshots.WheelDiameter(EtrtoRimSize.Inch275, 2.5),
            imageRotationDegrees: 12.5,
            updated: 7);
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
