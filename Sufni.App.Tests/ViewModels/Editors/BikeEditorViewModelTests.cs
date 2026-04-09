using Avalonia.Headless.XUnit;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.Tests.ViewModels.Editors;

public class BikeEditorViewModelTests
{
    private readonly IBikeCoordinator bikeCoordinator = Substitute.For<IBikeCoordinator>();
    private readonly IFilesService filesService = Substitute.For<IFilesService>();
    private readonly IBikeDependencyQuery dependencyQuery = Substitute.For<IBikeDependencyQuery>();
    private readonly ISetupStore setupStore = Substitute.For<ISetupStore>();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();

    private BikeEditorViewModel CreateEditor(BikeSnapshot snapshot, bool isNew = false) =>
        new(snapshot, isNew, bikeCoordinator, filesService, dependencyQuery, setupStore, shell, dialogService);

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

        // AddInitialJoints contributes 5 joints (FrontWheel, BottomBracket,
        // RearWheel, two ShockEye floating joints) and the shock LinkViewModel.
        Assert.Equal(5, editor.JointViewModels.Count);
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
            .Returns(new BikeSaveResult.Saved(11));
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
            .Returns(new BikeSaveResult.Saved(11));
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
}
