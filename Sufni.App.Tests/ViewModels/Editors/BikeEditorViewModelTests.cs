using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Headless.XUnit;
using Sufni.App.BikeEditing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.Editors;
using Sufni.App.ViewModels.LinkageParts;
using Sufni.Kinematics;

namespace Sufni.App.Tests.ViewModels.Editors;

public class BikeEditorViewModelTests
{
    private readonly IBikeCoordinator bikeCoordinator = Substitute.For<IBikeCoordinator>();
    private readonly IBikeDependencyQuery dependencyQuery = Substitute.For<IBikeDependencyQuery>();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();

    public BikeEditorViewModelTests()
    {
        bikeCoordinator.LoadAnalysisAsync(Arg.Any<Linkage?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeEditorAnalysisResult>(new BikeEditorAnalysisResult.Unavailable()));
        bikeCoordinator.LoadImageAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeImageLoadResult>(new BikeImageLoadResult.Canceled()));
        bikeCoordinator.ImportBikeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeImportResult>(new BikeImportResult.Canceled()));
        bikeCoordinator.ExportBikeAsync(Arg.Any<Bike>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeExportResult>(new BikeExportResult.Canceled()));
        dependencyQuery.Changes.Returns(Observable.Empty<Unit>());
    }

    private BikeEditorViewModel CreateEditor(BikeSnapshot snapshot, bool isNew = false) =>
        new(snapshot, isNew, bikeCoordinator, dependencyQuery, shell, dialogService);

    private static BikeAnalysisPresentationData PresentationData(CoordinateList leverageRatioData) =>
        new(leverageRatioData, new CoordinateList([], []));

    // ----- Construction -----

    [AvaloniaFact]
    public void Construction_FromExistingForkOnlySnapshot_CopiesFields_AndIsNotDirty()
    {
        var snapshot = TestSnapshots.Bike(name: "trail bike", updated: 7);
        var editor = CreateEditor(snapshot);

        Assert.Equal(snapshot.Id, editor.Id);
        Assert.Equal("trail bike", editor.Name);
        Assert.Equal(snapshot.HeadAngle, editor.HeadAngle);
        Assert.Equal(snapshot.ForkStroke, editor.ForksStroke);
        Assert.Null(editor.ShockStroke);
        Assert.Null(editor.Image);
        Assert.True(editor.IsInDatabase);
        Assert.Equal(7, editor.BaselineUpdated);
        Assert.False(editor.IsDirty);
        Assert.False(editor.SaveCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void Construction_NewBike_AddsInitialJoints()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot, isNew: true);

        // AddInitialJoints contributes 7 joints (FrontWheel, BottomBracket,
        // RearWheel, two HeadTube points, two ShockEye floating points)
        // and the shock LinkViewModel.
        Assert.Equal(7, editor.JointViewModels.Count);
        Assert.Single(editor.LinkViewModels);
    }

    // ----- Dirtiness -----

    [AvaloniaFact]
    public void EditingName_FlipsIsDirtyTrue()
    {
        var snapshot = TestSnapshots.Bike(name: "before");
        var editor = CreateEditor(snapshot);
        Assert.False(editor.IsDirty);

        editor.Name = "after";

        Assert.True(editor.SaveCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void EditingHeadAngle_FlipsIsDirtyTrue()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot);
        Assert.False(editor.IsDirty);

        editor.HeadAngle = snapshot.HeadAngle + 1;

        Assert.True(editor.SaveCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void RemovingJoint_DetachesPropertyHandler_FromRemovedInstance()
    {
        var editor = CreateEditor(TestSnapshots.Bike(), isNew: true);
        var removedJoint = new JointViewModel("Detached point", JointType.Floating, 10, 10);
        editor.JointViewModels.Add(removedJoint);
        editor.JointViewModels.Remove(removedJoint);
        var isDirtyBeforeMutation = editor.IsDirty;
        var canSaveBeforeMutation = editor.SaveCommand.CanExecute(null);

        removedJoint.Name = "Detached point renamed";

        Assert.Equal(isDirtyBeforeMutation, editor.IsDirty);
        Assert.Equal(canSaveBeforeMutation, editor.SaveCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void RemovingLink_DetachesPropertyHandler_FromRemovedInstance()
    {
        var editor = CreateEditor(TestSnapshots.Bike(), isNew: true);
        var removedLink = new LinkViewModel(editor.JointViewModels[0], editor.JointViewModels[1]);
        editor.LinkViewModels.Add(removedLink);
        editor.LinkViewModels.Remove(removedLink);
        var isDirtyBeforeMutation = editor.IsDirty;
        var canSaveBeforeMutation = editor.SaveCommand.CanExecute(null);

        removedLink.A = editor.JointViewModels[2];

        Assert.Equal(isDirtyBeforeMutation, editor.IsDirty);
        Assert.Equal(canSaveBeforeMutation, editor.SaveCommand.CanExecute(null));
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

    [AvaloniaFact]
    public async Task Save_OnForkOnlyBike_HappyPath_RoutesThroughCoordinator()
    {
        var snapshot = TestSnapshots.Bike(updated: 5);
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";

        bikeCoordinator.SaveAsync(Arg.Any<Bike>(), 5)
            .Returns(new BikeSaveResult.Saved(11, new BikeEditorAnalysisResult.Unavailable()));
        TestApp.SetIsDesktop(true);

        await editor.SaveCommand.ExecuteAsync(null);

        await bikeCoordinator.Received(1).SaveAsync(
            Arg.Is<Bike>(b => b.Id == snapshot.Id && b.Name == "renamed" && b.Linkage == null),
            5);
        Assert.Equal(11, editor.BaselineUpdated);
        Assert.Empty(editor.ErrorMessages);
        shell.DidNotReceive().GoBack();
    }

    [AvaloniaFact]
    public async Task Save_OnForkOnlyBike_OnMobile_NavigatesBack()
    {
        var snapshot = TestSnapshots.Bike(updated: 5);
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";

        bikeCoordinator.SaveAsync(Arg.Any<Bike>(), 5)
            .Returns(new BikeSaveResult.Saved(11, new BikeEditorAnalysisResult.Unavailable()));
        TestApp.SetIsDesktop(false);

        await editor.SaveCommand.ExecuteAsync(null);

        shell.Received(1).GoBack();
    }

    [AvaloniaFact]
    public async Task Save_OnForkOnlyBike_OnConflict_PromptsUser_AndReloadsWhenAccepted()
    {
        var snapshot = TestSnapshots.Bike(name: "old", updated: 5);
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";

        var fresh = TestSnapshots.Bike(id: snapshot.Id, name: "remote-updated", updated: 12);
        bikeCoordinator.SaveAsync(Arg.Any<Bike>(), 5)
            .Returns(new BikeSaveResult.Conflict(fresh));
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        TestApp.SetIsDesktop(true);

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal("remote-updated", editor.Name);
        Assert.Equal(12, editor.BaselineUpdated);
    }

    [AvaloniaFact]
    public async Task Save_OnForkOnlyBike_OnConflict_Accepted_QueuesFreshAnalysisRefresh()
    {
        var snapshot = TestSnapshots.Bike(name: "old", updated: 5);
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";

        var fresh = TestSnapshots.Bike(id: snapshot.Id, name: "remote-updated", updated: 12);
        var pendingAnalysis = new TaskCompletionSource<BikeEditorAnalysisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        bikeCoordinator.SaveAsync(Arg.Any<Bike>(), 5)
            .Returns(new BikeSaveResult.Conflict(fresh));
        bikeCoordinator.LoadAnalysisAsync(Arg.Any<Linkage?>(), Arg.Any<CancellationToken>())
            .Returns(pendingAnalysis.Task);
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        TestApp.SetIsDesktop(true);

        await editor.SaveCommand.ExecuteAsync(null);

        await bikeCoordinator.Received(1).LoadAnalysisAsync(Arg.Any<Linkage?>(), Arg.Any<CancellationToken>());
        Assert.True(editor.IsPlotBusy);

        pendingAnalysis.SetResult(new BikeEditorAnalysisResult.Unavailable());
        await Task.Yield();

        Assert.False(editor.IsPlotBusy);
    }

    [AvaloniaFact]
    public async Task Save_OnForkOnlyBike_OnConflict_DoesNothing_WhenUserDeclinesReload()
    {
        var snapshot = TestSnapshots.Bike(name: "old", updated: 5);
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";

        var fresh = TestSnapshots.Bike(id: snapshot.Id, name: "remote-updated", updated: 12);
        bikeCoordinator.SaveAsync(Arg.Any<Bike>(), 5)
            .Returns(new BikeSaveResult.Conflict(fresh));
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        TestApp.SetIsDesktop(true);

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal("renamed", editor.Name);
        Assert.Equal(5, editor.BaselineUpdated);
    }

    [AvaloniaFact]
    public async Task Save_OnForkOnlyBike_OnFailed_AppendsErrorMessage()
    {
        var snapshot = TestSnapshots.Bike(updated: 5);
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";

        bikeCoordinator.SaveAsync(Arg.Any<Bike>(), 5)
            .Returns(new BikeSaveResult.Failed("disk full"));
        TestApp.SetIsDesktop(true);

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Single(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task Save_OnInvalidLinkage_AppendsErrorMessage()
    {
        var snapshot = TestSnapshots.Bike(updated: 5);
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";

        bikeCoordinator.SaveAsync(Arg.Any<Bike>(), 5)
            .Returns(new BikeSaveResult.InvalidLinkage());
        TestApp.SetIsDesktop(true);

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Single(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task Save_OnDesktop_AppliesAnalysisFromSavedResult_WithoutReloadingAnalysis()
    {
        var snapshot = TestSnapshots.Bike(updated: 5);
        var editor = CreateEditor(snapshot);
        var data = new CoordinateList([1, 2], [3, 4]);
        editor.Name = "renamed";

        bikeCoordinator.SaveAsync(Arg.Any<Bike>(), 5)
            .Returns(new BikeSaveResult.Saved(
                11,
                new BikeEditorAnalysisResult.Computed(PresentationData(data))));
        TestApp.SetIsDesktop(true);

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal(data, editor.LeverageRatioData);
        await bikeCoordinator.DidNotReceive().LoadAnalysisAsync(Arg.Any<Linkage?>(), Arg.Any<CancellationToken>());
    }

    // ----- CanDelete -----

    [AvaloniaFact]
    public void DeleteCommand_Enabled_WhenNotInDatabase()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot, isNew: true);

        Assert.True(editor.DeleteCommand.CanExecute(true));
    }

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
    public async Task Delete_OnNewUnsavedBike_NavigatesBack_WithoutCallingCoordinator()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot, isNew: true);

        await editor.DeleteCommand.ExecuteAsync(true);

        await bikeCoordinator.DidNotReceive().DeleteAsync(Arg.Any<Guid>());
        shell.Received(1).GoBack();
    }

    [AvaloniaFact]
    public async Task Delete_HappyPath_RoutesThroughCoordinator_AndNavigatesBack()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot);
        bikeCoordinator.DeleteAsync(snapshot.Id)
            .Returns(new BikeDeleteResult(BikeDeleteOutcome.Deleted));

        await editor.DeleteCommand.ExecuteAsync(true);

        await bikeCoordinator.Received(1).DeleteAsync(snapshot.Id);
        shell.Received(1).GoBack();
        Assert.Empty(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task Delete_InUse_AppendsErrorMessage_AndDoesNotNavigateBack()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot);
        bikeCoordinator.DeleteAsync(snapshot.Id)
            .Returns(new BikeDeleteResult(BikeDeleteOutcome.InUse));

        await editor.DeleteCommand.ExecuteAsync(true);

        Assert.Single(editor.ErrorMessages);
        shell.DidNotReceive().GoBack();
    }

    [AvaloniaFact]
    public async Task Delete_Failed_AppendsErrorMessage_AndDoesNotNavigateBack()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot);
        bikeCoordinator.DeleteAsync(snapshot.Id)
            .Returns(new BikeDeleteResult(BikeDeleteOutcome.Failed, "locked"));

        await editor.DeleteCommand.ExecuteAsync(true);

        Assert.Single(editor.ErrorMessages);
        shell.DidNotReceive().GoBack();
    }

    [AvaloniaFact]
    public async Task Loaded_AppliesComputedAnalysisResult()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot);
        var data = new CoordinateList([1, 2], [3, 4]);
        bikeCoordinator.LoadAnalysisAsync(Arg.Any<Linkage?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeEditorAnalysisResult>(
                new BikeEditorAnalysisResult.Computed(PresentationData(data))));

        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.Equal(data, editor.LeverageRatioData);
    }

    [AvaloniaFact]
    public async Task Loaded_ClearsLeverageRatioData_WhenAnalysisUnavailable()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot);
        editor.LeverageRatioData = new CoordinateList([1, 2], [3, 4]);
        bikeCoordinator.LoadAnalysisAsync(Arg.Any<Linkage?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeEditorAnalysisResult>(new BikeEditorAnalysisResult.Unavailable()));

        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.Null(editor.LeverageRatioData);
        Assert.Empty(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task Loaded_ClearsLeverageRatioData_AndAppendsError_WhenAnalysisFails()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot);
        editor.LeverageRatioData = new CoordinateList([1, 2], [3, 4]);
        bikeCoordinator.LoadAnalysisAsync(Arg.Any<Linkage?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeEditorAnalysisResult>(new BikeEditorAnalysisResult.Failed("boom")));

        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.Null(editor.LeverageRatioData);
        Assert.Single(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task Import_AppliesImportedBike_AndResetsBaselineToDraft()
    {
        var snapshot = TestSnapshots.Bike(updated: 5, name: "old bike");
        var editor = CreateEditor(snapshot);
        var importedBike = new Bike(Guid.NewGuid(), "imported bike")
        {
            HeadAngle = 63,
            ForkStroke = 170,
        };
        bikeCoordinator.ImportBikeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeImportResult>(
                new BikeImportResult.Imported(new ImportedBikeEditorData(
                    importedBike,
                    new BikeEditorAnalysisResult.Unavailable()))));

        await editor.ImportCommand.ExecuteAsync(null);

        Assert.Equal("imported bike", editor.Name);
        Assert.Equal(63, editor.HeadAngle);
        Assert.Equal(170, editor.ForksStroke);
        Assert.Equal(0, editor.BaselineUpdated);
        Assert.False(editor.IsInDatabase);
    }

    [AvaloniaFact]
    public async Task Reset_ShowsPlotBusyWhileAnalysisRefreshRuns()
    {
        var snapshot = TestSnapshots.Bike(name: "old bike");
        var editor = CreateEditor(snapshot);
        var pendingAnalysis = new TaskCompletionSource<BikeEditorAnalysisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        editor.Name = "renamed";
        bikeCoordinator.LoadAnalysisAsync(Arg.Any<Linkage?>(), Arg.Any<CancellationToken>())
            .Returns(pendingAnalysis.Task);

        var resetTask = editor.ResetCommand.ExecuteAsync(null);

        Assert.True(editor.IsPlotBusy);

        pendingAnalysis.SetResult(new BikeEditorAnalysisResult.Unavailable());
        await resetTask;

        Assert.False(editor.IsPlotBusy);
    }

    [AvaloniaFact]
    public async Task Unloaded_DropsPendingAnalysisResult()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot);
        var pendingAnalysis = new TaskCompletionSource<BikeEditorAnalysisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        bikeCoordinator.LoadAnalysisAsync(Arg.Any<Linkage?>(), Arg.Any<CancellationToken>())
            .Returns(pendingAnalysis.Task);

        var loadedTask = editor.LoadedCommand.ExecuteAsync(null);
        editor.UnloadedCommand.Execute(null);

        pendingAnalysis.SetResult(new BikeEditorAnalysisResult.Computed(
            PresentationData(new CoordinateList([1, 2], [3, 4]))));
        await loadedTask;

        Assert.Null(editor.LeverageRatioData);
    }

    [AvaloniaFact]
    public async Task Import_SupersedesEarlierPendingAnalysisResult()
    {
        var snapshot = TestSnapshots.Bike(updated: 5, name: "old bike");
        var editor = CreateEditor(snapshot);
        var pendingAnalysis = new TaskCompletionSource<BikeEditorAnalysisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingImport = new TaskCompletionSource<BikeImportResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var importedData = new CoordinateList([5, 6], [7, 8]);

        bikeCoordinator.LoadAnalysisAsync(Arg.Any<Linkage?>(), Arg.Any<CancellationToken>())
            .Returns(pendingAnalysis.Task);
        bikeCoordinator.ImportBikeAsync(Arg.Any<CancellationToken>())
            .Returns(pendingImport.Task);

        var loadedTask = editor.LoadedCommand.ExecuteAsync(null);
        var importTask = editor.ImportCommand.ExecuteAsync(null);

        pendingAnalysis.SetResult(new BikeEditorAnalysisResult.Computed(
            PresentationData(new CoordinateList([1, 2], [3, 4]))));
        pendingImport.SetResult(new BikeImportResult.Imported(
            new ImportedBikeEditorData(
                new Bike(Guid.NewGuid(), "imported bike")
                {
                    HeadAngle = 63,
                    ForkStroke = 170,
                },
                new BikeEditorAnalysisResult.Computed(PresentationData(importedData)))));

        await loadedTask;
        await importTask;

        Assert.Equal("imported bike", editor.Name);
        Assert.Equal(importedData, editor.LeverageRatioData);
    }
}
