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

// Constructs linkage parts with Avalonia brushes and image resources.
[Collection("Ui")]
public class BikeEditorViewModelTests
{
    private readonly IBikeCoordinator bikeCoordinator = TestCoordinatorSubstitutes.Bike();
    private readonly IBikeDependencyQuery dependencyQuery = Substitute.For<IBikeDependencyQuery>();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();
    private readonly IUiThreadDispatcher uiThreadDispatcher = new InlineUiThreadDispatcher();
    private readonly FakeTimeProvider timeProvider = new();

    public BikeEditorViewModelTests()
    {
        dependencyQuery.Changes.Returns(Observable.Empty<Unit>());
    }

    private BikeEditorViewModel CreateEditor(BikeSnapshot snapshot, bool isNew = false, bool hasPointer = true, IBackgroundTaskRunner? background = null, byte[]? storedImage = null)
    {
        bikeCoordinator.LoadDraftAsync(snapshot.Id, Arg.Any<CancellationToken>()).Returns(new BikeDraftLoadResult.Loaded(storedImage is null ? DraftFrom(snapshot) : BikeDraft.From(snapshot, storedImage)));
        var editor = new BikeEditorViewModel(snapshot, isNew, bikeCoordinator, dependencyQuery, shell, dialogService, uiThreadDispatcher, new InputCapabilities(hasPointer, !hasPointer, hasPointer, !hasPointer, CommandModifier.Control), background ?? new InlineBackgroundTaskRunner(), timeProvider: timeProvider);
        return editor;
    }

    private static BikeDraft DraftFrom(BikeSnapshot snapshot) =>
        BikeDraft.From(snapshot, snapshot.HasImage ? TestImages.SmallPngBytes() : []);

    private static BikeAnalysisPresentationData PresentationData(CoordinateList leverageRatioData) =>
        new(leverageRatioData, new CoordinateList([], []));


    private static BikeSnapshot FullSuspensionSnapshot(
        bool includeHeadTubeJoints = false,
        EtrtoRimSize? frontWheelRimSize = null,
        double? frontWheelTireWidth = null,
        double? frontWheelDiameter = null,
        EtrtoRimSize? rearWheelRimSize = null,
        double? rearWheelTireWidth = null,
        double? rearWheelDiameter = null,
        double imageRotationDegrees = 0,
        long updated = 1)
    {
        var bike = new Bike(Guid.NewGuid(), "full sus")
        {
            HeadAngle = 64,
            ForkStroke = 170,
            ImageBytes = TestImages.SmallPngBytes(),
            PixelsToMillimeters = 1,
            RearSuspension = new RearSuspensionSpec.Linkage(
                TestSnapshots.FullSuspensionLinkageSpec(includeHeadTubeJoints)),
            FrontWheelRimSize = frontWheelRimSize,
            FrontWheelTireWidth = frontWheelTireWidth,
            FrontWheelDiameterMm = frontWheelDiameter,
            RearWheelRimSize = rearWheelRimSize,
            RearWheelTireWidth = rearWheelTireWidth,
            RearWheelDiameterMm = rearWheelDiameter,
            ImageRotationDegrees = imageRotationDegrees,
            Updated = updated,
        };
        bike.ShockStroke = 0.5;
        return BikeSnapshot.From(bike);
    }

    // ----- Construction -----

    [AvaloniaFact]
    public async Task ImageDraft_WaitsForBackgroundDecodeBeforeEnablingSave()
    {
        var background = new DeferredBackgroundTaskRunner();
        var snapshot = FullSuspensionSnapshot();
        var editor = CreateEditor(snapshot, background: background);
        Assert.True(editor.IsLoadingDraft);
        Assert.Null(editor.ImageCanvas.Image);
        Assert.False(editor.SaveCommand.CanExecute(null));
        Assert.False(editor.ExportCommand.CanExecute(null));

        await background.RunNextAsync();
        await editor.LoadedCommand.ExecuteAsync(null).AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(editor.ImageCanvas.Image);
        Assert.False(editor.IsLoadingDraft);
        Assert.False(editor.IsDirty);
        editor.Name = "edited";
        Assert.True(editor.SaveCommand.CanExecute(null));
        await bikeCoordinator.Received(1).LoadDraftAsync(snapshot.Id, Arg.Any<CancellationToken>());
        await editor.PrepareCloseAsync();
        Assert.Null(editor.ImageCanvas.Image);
    }

    [AvaloniaFact]
    public async Task ImageDraft_LoadBelongsToEditorAcrossViewDetach()
    {
        var background = new DeferredBackgroundTaskRunner();
        var editor = CreateEditor(FullSuspensionSnapshot(), background: background);
        editor.UnloadedCommand.Execute(null);

        await background.RunNextAsync();
        await editor.LoadedCommand.ExecuteAsync(null).AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(editor.ImageCanvas.Image);
        Assert.False(editor.IsDirty);
        await editor.PrepareCloseAsync();
    }

    [AvaloniaFact]
    public async Task Close_CancelsPendingImageDecodeAndDoesNotPublishIt()
    {
        var background = new DeferredBackgroundTaskRunner();
        var editor = CreateEditor(FullSuspensionSnapshot(), background: background);

        var close = editor.PrepareCloseAsync();
        await background.RunNextAsync();
        await close.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Null(editor.ImageCanvas.Image);
        Assert.Empty(editor.ImageCanvas.ImageBytes);
    }

    [AvaloniaFact]
    public void Construction_PreservesPersistedHeadAngle_WhenItDiffersFromDerivedGeometry()
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

        Assert.Equal(79.9, editor.HeadAngle);
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

        var baselineHeadTube1 = Assert.Single(baselineEditor.LinkageEditor.JointViewModels, joint => joint.Name == JointNames.HeadTube1);
        var affectedHeadTube1 = Assert.Single(affectedEditor.LinkageEditor.JointViewModels, joint => joint.Name == JointNames.HeadTube1);
        var extraHeadTube = Assert.Single(affectedEditor.LinkageEditor.JointViewModels, joint => joint.Name == JointNames.ShockEye1);

        var baselineHeadAngle = baselineEditor.HeadAngle;
        baselineHeadTube1.Y += 10;

        Assert.NotEqual(baselineHeadAngle, baselineEditor.HeadAngle);

        extraHeadTube.Type = JointType.HeadTube;
        affectedHeadTube1.Y += 10;

        Assert.Equal(baselineEditor.HeadAngle, affectedEditor.HeadAngle);
    }

    [AvaloniaTheory]
    [InlineData(0.0)]
    [InlineData(-10.0)]
    [InlineData(double.NaN)]
    public void InvalidChainstay_ClearsDerivedScale_AndDerivedHeadAngle(double invalidChainstay)
    {
        var snapshot = FullSuspensionSnapshot(
            includeHeadTubeJoints: true,
            frontWheelDiameter: TestSnapshots.WheelDiameter(EtrtoRimSize.Inch29, 2.4),
            rearWheelDiameter: TestSnapshots.WheelDiameter(EtrtoRimSize.Inch275, 2.5));
        var editor = CreateEditor(snapshot);
        Assert.True(editor.PixelsToMillimeters.HasValue);
        Assert.True(editor.HeadAngle.HasValue);

        editor.Chainstay = invalidChainstay;

        Assert.Null(editor.PixelsToMillimeters);
        Assert.Null(editor.HeadAngle);
    }

    [AvaloniaFact]
    public void InvalidChainstay_DoesNotTouchManualHeadAngle_WithoutPhotoJoints()
    {
        var snapshot = TestSnapshots.Bike(name: "manual head angle");
        var editor = CreateEditor(snapshot);
        var manualHeadAngle = editor.HeadAngle;
        Assert.NotNull(manualHeadAngle);

        editor.Chainstay = 0;

        Assert.Equal(manualHeadAngle, editor.HeadAngle);
    }

    // ----- Dirtiness -----

    [AvaloniaFact]
    public void SaveCommand_Disabled_WhenOnlyOneWheelIsConfigured()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";
        editor.WheelGeometry.FrontWheelDiameter = 760;

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

    // ----- CanSave -----

    [AvaloniaFact]
    public void SaveCommand_Disabled_WhenForkStrokeMissing()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";   // make dirty
        editor.ForksStroke = null; // invalidate

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

    // ----- Save (fork-only path) -----

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

        Assert.Equal("remote-updated", editor.Name);
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

        Assert.Equal("renamed", editor.Name);
    }

    [AvaloniaFact]
    public async Task SetRearSuspensionModeCommand_RejectsChangesWithoutPointerCapability()
    {
        var editor = CreateEditor(TestSnapshots.Bike(), hasPointer: false);

        Assert.False(editor.SetRearSuspensionModeCommand.CanExecute(RearSuspensionKind.Linkage));
        await editor.SetRearSuspensionModeCommand.ExecuteAsync(RearSuspensionKind.Linkage);

        Assert.Equal(RearSuspensionKind.None, editor.RearSuspensionMode);
        Assert.False(editor.LeverageRatioEditor.CanEdit);
        await dialogService.DidNotReceiveWithAnyArgs().ShowConfirmationAsync(default!, default!);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SetRearSuspensionModeCommand_KeepsDraftUntilConfirmation(bool accept)
    {
        var editor = CreateEditor(FullSuspensionSnapshot());
        var originalJoint = editor.LinkageEditor.JointViewModels[0];
        var confirmation = new TaskCompletionSource<bool>();
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(confirmation.Task);

        var task = editor.SetRearSuspensionModeCommand.ExecuteAsync(RearSuspensionKind.None);

        Assert.True(editor.SetRearSuspensionModeCommand.IsRunning);
        Assert.False(editor.SetRearSuspensionModeCommand.CanExecute(RearSuspensionKind.LeverageRatio));
        Assert.Equal(RearSuspensionKind.Linkage, editor.RearSuspensionMode);
        Assert.Contains(originalJoint, editor.LinkageEditor.JointViewModels);
        confirmation.SetResult(accept);
        await task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(accept ? RearSuspensionKind.None : RearSuspensionKind.Linkage, editor.RearSuspensionMode);
        Assert.Equal(accept, editor.IsDirty);
        if (!accept) Assert.Contains(originalJoint, editor.LinkageEditor.JointViewModels);
    }

    [AvaloniaFact]
    public async Task SetRearSuspensionModeCommand_ClearsWheelState_WhenSwitchingFromLinkageToLeverageRatio()
    {
        var snapshot = FullSuspensionSnapshot(
            includeHeadTubeJoints: true,
            frontWheelRimSize: EtrtoRimSize.Inch29,
            frontWheelTireWidth: 2.4,
            frontWheelDiameter: TestSnapshots.WheelDiameter(EtrtoRimSize.Inch29, 2.4),
            rearWheelRimSize: EtrtoRimSize.Inch275,
            rearWheelTireWidth: 2.5,
            rearWheelDiameter: TestSnapshots.WheelDiameter(EtrtoRimSize.Inch275, 2.5));
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        var editor = CreateEditor(snapshot);

        await editor.SetRearSuspensionModeCommand.ExecuteAsync(RearSuspensionKind.LeverageRatio);

        Assert.Equal(RearSuspensionKind.LeverageRatio, editor.RearSuspensionMode);
        Assert.Null(editor.WheelGeometry.FrontWheelRimSize);
        Assert.Null(editor.WheelGeometry.FrontWheelTireWidth);
        Assert.Null(editor.WheelGeometry.FrontWheelDiameter);
        Assert.Null(editor.WheelGeometry.RearWheelRimSize);
        Assert.Null(editor.WheelGeometry.RearWheelTireWidth);
        Assert.Null(editor.WheelGeometry.RearWheelDiameter);
    }

    [AvaloniaFact]
    public async Task SetRearSuspensionModeCommand_ClearsShockStroke_WhenSwitchingFromLinkageToHardtail()
    {
        var snapshot = FullSuspensionSnapshot();
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        var editor = CreateEditor(snapshot);
        Assert.NotNull(editor.ShockStroke);

        await editor.SetRearSuspensionModeCommand.ExecuteAsync(RearSuspensionKind.None);

        Assert.Equal(RearSuspensionKind.None, editor.RearSuspensionMode);
        Assert.Null(editor.ShockStroke);
    }

    [AvaloniaFact]
    public async Task SetRearSuspensionModeCommand_PreservesShockStroke_WhenSwitchingBetweenLinkageAndLeverageRatio()
    {
        var snapshot = FullSuspensionSnapshot();
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        var editor = CreateEditor(snapshot);
        var originalShockStroke = editor.ShockStroke;
        Assert.NotNull(originalShockStroke);

        await editor.SetRearSuspensionModeCommand.ExecuteAsync(RearSuspensionKind.LeverageRatio);

        Assert.Equal(originalShockStroke, editor.ShockStroke);
    }

    [AvaloniaFact]
    public async Task SetRearSuspensionModeCommand_DoesNotReprompt_AfterOutgoingPayloadAlreadyDiscarded()
    {
        var snapshot = FullSuspensionSnapshot();
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        var editor = CreateEditor(snapshot);

        await editor.SetRearSuspensionModeCommand.ExecuteAsync(RearSuspensionKind.None);
        await editor.SetRearSuspensionModeCommand.ExecuteAsync(RearSuspensionKind.Linkage);
        dialogService.ClearReceivedCalls();

        await editor.SetRearSuspensionModeCommand.ExecuteAsync(RearSuspensionKind.LeverageRatio);

        Assert.Equal(RearSuspensionKind.LeverageRatio, editor.RearSuspensionMode);
        await dialogService.DidNotReceiveWithAnyArgs()
            .ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>());
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

        Assert.Equal("later edit", editor.Name);
        Assert.True(editor.IsDirty);
        shell.DidNotReceive().CompleteEdit(Arg.Any<EditorKey>());
        await editor.ResetCommand.ExecuteAsync(null);
        Assert.Equal("submitted", editor.Name);
        Assert.False(editor.IsDirty);
    }

    // ----- CanDelete -----

    [AvaloniaFact]
    public void DeleteCommand_Disabled_WhenDependencyQuerySaysInUse()
    {
        var snapshot = TestSnapshots.Bike();
        dependencyQuery.IsBikeInUse(snapshot.Id).Returns(true);
        var editor = CreateEditor(snapshot);

        Assert.False(editor.DeleteCommand.CanExecute(true));
    }

    // ----- Delete -----

    [AvaloniaFact]
    public async Task Analysis_DiscardsCompletionForSupersededSpec()
    {
        var editor = CreateEditor(FullSuspensionSnapshot(), isNew: true);
        var pending = new TaskCompletionSource<BikeEditorAnalysisResult>();
        CancellationToken previousToken = default;
        bikeCoordinator.LoadAnalysisAsync(Arg.Any<RearSuspensionSpec>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            previousToken = call.Arg<CancellationToken>();
            return pending.Task;
        });
        var initialLoad = editor.LoadedCommand.ExecuteAsync(null);
        var current = new CoordinateList([3, 4], [5, 6]);
        var computed = new TaskCompletionSource();
        bikeCoordinator.LoadAnalysisAsync(Arg.Any<RearSuspensionSpec>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            computed.SetResult();
            return new BikeEditorAnalysisResult.Computed(PresentationData(current));
        });

        editor.ShockStroke = 25;
        Assert.True(previousToken.IsCancellationRequested);
        timeProvider.Advance(TimeSpan.FromMilliseconds(200));
        await computed.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        pending.SetResult(new BikeEditorAnalysisResult.Computed(PresentationData(new CoordinateList([0, 1], [1, 2]))));
        await initialLoad.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(current, editor.LeverageRatioData);
        Assert.False(editor.IsPlotBusy);
    }

    [AvaloniaFact]
    public async Task Loaded_AppliesComputedAnalysisResult()
    {
        var snapshot = FullSuspensionSnapshot(
            frontWheelDiameter: 760,
            rearWheelDiameter: 750);
        var editor = CreateEditor(snapshot, isNew: true);
        var data = new CoordinateList([1, 2], [3, 4]);
        var rearAxlePathData = new CoordinateList([2, 4], [0.25, 0.75]);
        bikeCoordinator.LoadAnalysisAsync(Arg.Any<RearSuspensionSpec>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeEditorAnalysisResult>(
                new BikeEditorAnalysisResult.Computed(new BikeAnalysisPresentationData(data, rearAxlePathData))));

        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.Equal(data, editor.LeverageRatioData);
        Assert.Equal(2, editor.ImageCanvas.RearAxlePath.Count);
        Assert.Equal(2, editor.ImageCanvas.RearAxlePath[0].X, 3);
        Assert.Equal(0.75, editor.ImageCanvas.RearAxlePath[0].Y, 3);
        Assert.Equal(4, editor.ImageCanvas.RearAxlePath[1].X, 3);
        Assert.Equal(0.25, editor.ImageCanvas.RearAxlePath[1].Y, 3);
    }

    [AvaloniaFact]
    public async Task Loaded_ClearsLeverageRatioData_WhenAnalysisUnavailable()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot, isNew: true);
        editor.LeverageRatioData = new CoordinateList([1, 2], [3, 4]);
        bikeCoordinator.LoadAnalysisAsync(Arg.Any<RearSuspensionSpec>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeEditorAnalysisResult>(new BikeEditorAnalysisResult.NotApplicable()));

        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.Null(editor.LeverageRatioData);
        Assert.Empty(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task Loaded_ClearsLeverageRatioData_AndAppendsError_WhenAnalysisFails()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot, isNew: true);
        editor.LeverageRatioData = new CoordinateList([1, 2], [3, 4]);
        bikeCoordinator.LoadAnalysisAsync(Arg.Any<RearSuspensionSpec>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeEditorAnalysisResult>(new BikeEditorAnalysisResult.Failed(BikeAnalysisFailureReason.Analyze, "boom")));

        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.Null(editor.LeverageRatioData);
        Assert.Single(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task OpenImage_DecodesSelectedBytes_AndPreservesRotation()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot);
        var imageBytes = TestImages.SmallPngBytes();
        editor.ImageCanvas.ImageRotationDegrees = 12.5;
        bikeCoordinator.LoadImageAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeImageLoadResult>(
                new BikeImageLoadResult.Loaded(imageBytes, "bike.png")));

        await editor.OpenImageCommand.ExecuteAsync(null);

        Assert.Equal(imageBytes, editor.ImageCanvas.ImageBytes);
        Assert.NotNull(editor.ImageCanvas.Image);
        Assert.Equal(1, editor.ImageCanvas.Image.Size.Width);
        Assert.Equal(1, editor.ImageCanvas.Image.Size.Height);
        Assert.Equal(12.5, editor.ImageCanvas.ImageRotationDegrees);
        Assert.Empty(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task OpenImage_DropsPendingResult_WhenEditorIsUnloaded()
    {
        var editor = CreateEditor(TestSnapshots.Bike());
        var pendingImage = new TaskCompletionSource<BikeImageLoadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        bikeCoordinator.LoadImageAsync(Arg.Any<CancellationToken>())
            .Returns(pendingImage.Task);

        var openImageTask = editor.OpenImageCommand.ExecuteAsync(null);
        editor.UnloadedCommand.Execute(null);
        pendingImage.SetResult(new BikeImageLoadResult.Loaded(TestImages.SmallPngBytes(), "bike.png"));
        await openImageTask;

        Assert.Null(editor.ImageCanvas.Image);
        Assert.Empty(editor.ImageCanvas.ImageBytes);
        Assert.Empty(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task OpenImage_PreservesCurrentImage_AndSurfacesFailure_WhenBytesAreInvalid()
    {
        var snapshot = FullSuspensionSnapshot(imageRotationDegrees: 12.5);
        var editor = CreateEditor(snapshot);
        var currentImage = editor.ImageCanvas.Image;
        var currentImageBytes = editor.ImageCanvas.ImageBytes.ToArray();
        bikeCoordinator.LoadImageAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeImageLoadResult>(
                new BikeImageLoadResult.Loaded([], "invalid.png")));

        await editor.OpenImageCommand.ExecuteAsync(null);

        Assert.Same(currentImage, editor.ImageCanvas.Image);
        Assert.Equal(currentImageBytes, editor.ImageCanvas.ImageBytes);
        Assert.Equal(12.5, editor.ImageCanvas.ImageRotationDegrees);
        Assert.NotEmpty(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task Reset_ShowsPlotBusyWhileAnalysisRefreshRuns()
    {
        var snapshot = FullSuspensionSnapshot();
        var editor = CreateEditor(snapshot);
        var pendingAnalysis = new TaskCompletionSource<BikeEditorAnalysisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        editor.ShockStroke = snapshot.ShockStroke + 1;
        bikeCoordinator.LoadAnalysisAsync(Arg.Any<RearSuspensionSpec>(), Arg.Any<CancellationToken>())
            .Returns(pendingAnalysis.Task);

        var resetTask = editor.ResetCommand.ExecuteAsync(null);

        Assert.True(editor.IsPlotBusy);

        pendingAnalysis.SetResult(new BikeEditorAnalysisResult.NotApplicable());
        await resetTask;

        Assert.False(editor.IsPlotBusy);
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
        editor.ImageCanvas.ImageRotationDegrees = 22.5;

        await editor.ResetCommand.ExecuteAsync(null);

        Assert.Equal(snapshot.Name, editor.Name);
        Assert.Equal(snapshot.ImageRotationDegrees, editor.ImageCanvas.ImageRotationDegrees);
        Assert.False(editor.IsDirty);
        Assert.False(editor.SaveCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Unloaded_DropsPendingAnalysisResult()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot, isNew: true);
        var pendingAnalysis = new TaskCompletionSource<BikeEditorAnalysisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        bikeCoordinator.LoadAnalysisAsync(Arg.Any<RearSuspensionSpec>(), Arg.Any<CancellationToken>())
            .Returns(pendingAnalysis.Task);

        var loadedTask = editor.LoadedCommand.ExecuteAsync(null);
        editor.UnloadedCommand.Execute(null);

        pendingAnalysis.SetResult(new BikeEditorAnalysisResult.Computed(
            PresentationData(new CoordinateList([1, 2], [3, 4]))));
        await loadedTask;

        Assert.Null(editor.LeverageRatioData);
    }

    [AvaloniaFact]
    public async Task ImportLeverageRatio_FailurePreservesCurveAndSeparatesDiagnostics()
    {
        var curve = TestSnapshots.LeverageRatioCurve((0, 0), (10, 25));
        var editor = CreateEditor(TestSnapshots.LeverageRatioBike(curve));
        bikeCoordinator.ImportLeverageRatioAsync(Arg.Any<CancellationToken>())
            .Returns(new LeverageRatioImportResult.Failed(LeverageRatioImportFailureReason.Import, "disk full"));

        await editor.ImportLeverageRatioCommand.ExecuteAsync(null);

        Assert.Equal(curve, editor.LeverageRatioEditor.BuildCurrent());
        var failure = Assert.Single(editor.ErrorMessages);
        Assert.Equal("disk full", failure.Details);
        Assert.DoesNotContain("disk full", failure.Summary);
    }

}
