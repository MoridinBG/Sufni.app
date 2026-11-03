using Sufni.Kinematics;
using Sufni.App.Shared.Models;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.Time.Testing;
using System.Reactive.Linq;
using DynamicData;
using NSubstitute;

using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Stores;
using Sufni.App.Infrastructure;
using Sufni.App.Setups.Coordinators;
using Sufni.App.Setups.Stores;
using Sufni.App.Setups.ViewModels.Editors;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.Models.SensorConfigurations;
using Sufni.App.Setups.ViewModels.SensorConfigurations;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Doubles;
namespace Sufni.App.Tests.Setups.ViewModels.Editors;

public class SetupEditorViewModelTests : IDisposable
{
    private readonly ISetupCoordinator setupCoordinator = TestCoordinatorSubstitutes.Setup();
    private readonly BikeStoreStub bikeStore = new();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();
    private readonly IUiThreadDispatcher uiThreadDispatcher = new InlineUiThreadDispatcher();

    public void Dispose() => bikeStore.Dispose();

    private SetupEditorViewModel CreateEditor(SetupSnapshot snapshot, bool isNew = false, Guid? boardId = null) =>
        new(snapshot, isNew, bikeStore, setupCoordinator, shell, dialogService, uiThreadDispatcher, timeProvider: new FakeTimeProvider(), boardId: boardId);

    private const string LinearForkConfigurationJson =
        "{\"type\":\"linear_fork\",\"length\":100,\"resolution\":12}";

    // ----- Construction -----

    [AvaloniaFact]
    public void Construction_PopulatesNameBoardSensorConfigs_FromSnapshot()
    {
        var bike = TestSnapshots.Bike();
        var boardId = Guid.NewGuid();
        var snapshot = TestSnapshots.Setup(name: "race day", bikeId: bike.Id, updated: 9)
            with
        { FrontSensorConfigurationJson = LinearForkConfigurationJson };

        var editor = CreateEditor(snapshot, boardId: boardId);

        Assert.Equal("race day", editor.Name);
        Assert.Equal(boardId, editor.BoardId);
        Assert.Equal(SensorType.LinearFork, editor.ForkSensorConfiguration?.Type);
        Assert.Null(editor.ShockSensorConfiguration);
        Assert.True(editor.IsInDatabase);
    }

    [AvaloniaFact]
    public void Loaded_PopulatesSelectedBike_FromBikeStore()
    {
        var bike = TestSnapshots.Bike();
        bikeStore.Add(bike);
        var snapshot = TestSnapshots.Setup(bikeId: bike.Id);

        var editor = CreateEditor(snapshot);
        editor.LoadedCommand.Execute(null);

        Assert.Single(editor.Bikes);
        Assert.Equal(bike.Id, editor.SelectedBike?.Id);
    }

    // ----- Sensor type switching -----

    [AvaloniaFact]
    public void SettingForkSensorType_RecreatesForkSensorConfigurationViewModel()
    {
        var snapshot = TestSnapshots.Setup();
        var editor = CreateEditor(snapshot);

        editor.ForkSensorType = SensorType.LinearFork;

        Assert.NotNull(editor.ForkSensorConfiguration);
        Assert.Equal(SensorType.LinearFork, editor.ForkSensorConfiguration!.Type);
    }

    [AvaloniaFact]
    public void ReplacingForkSensorConfiguration_DetachesOldPropertyChangedHandler()
    {
        var snapshot = TestSnapshots.Setup() with
        {
            FrontSensorConfigurationJson = LinearForkConfigurationJson
        };
        var editor = CreateEditor(snapshot);
        var staleConfiguration = Assert.IsType<LinearForkSensorConfigurationViewModel>(editor.ForkSensorConfiguration);
        var activeConfiguration = Assert.IsType<LinearForkSensorConfigurationViewModel>(
            SensorConfigurationViewModel.FromDraft(new SensorDraft.LinearFork(100, 12)));

        editor.ForkSensorConfiguration = activeConfiguration;

        var saveCommandStateChanges = 0;
        editor.SaveCommand.CanExecuteChanged += (_, _) => saveCommandStateChanges++;

        Assert.True(staleConfiguration.Length.HasValue);
        staleConfiguration.Length = staleConfiguration.Length.Value + 5;

        Assert.Equal(0, saveCommandStateChanges);
    }

    [AvaloniaFact]
    public void Loaded_PreservesCompatibleShockSensorType_WhenRearBikeOptionsPopulate()
    {
        var bike = TestSnapshots.Bike() with
        {
            ShockStroke = 0.5,
            RearSuspension = new RearSuspensionSpec.Linkage(
                TestSnapshots.FullSuspensionLinkageSpec(includeHeadTubeJoints: true)),
        };
        bikeStore.Add(bike);
        var snapshot = TestSnapshots.Setup(bikeId: bike.Id) with
        {
            RearSensorConfigurationJson = SensorConfiguration.ToJson(new LinearShockSensorConfiguration
            {
                Length = 75,
                Resolution = 14
            })
        };

        var editor = CreateEditor(snapshot);
        editor.LoadedCommand.Execute(null);

        Assert.Equal(SensorType.LinearShock, editor.ShockSensorType);
    }

    [AvaloniaFact]
    public void SelectedBike_UpdatesAllowedShockSensorTypes_FromRearSuspensionKind()
    {
        var linkageBike = TestSnapshots.Bike() with
        {
            ShockStroke = 0.5,
            RearSuspension = new RearSuspensionSpec.Linkage(
                TestSnapshots.FullSuspensionLinkageSpec(includeHeadTubeJoints: true)),
        };
        var leverageRatioBike = TestSnapshots.LeverageRatioBike(
            TestSnapshots.LeverageRatioCurve((0, 0), (10, 25), (20, 50)));
        bikeStore.Add(linkageBike);
        bikeStore.Add(leverageRatioBike);

        var editor = CreateEditor(TestSnapshots.Setup(bikeId: linkageBike.Id));
        editor.LoadedCommand.Execute(null);

        Assert.Equal(new SensorType?[] { null, SensorType.LinearShock, SensorType.RotationalShock }, editor.ShockSensorTypes);

        editor.SelectedBike = leverageRatioBike;

        Assert.Equal(new SensorType?[] { null, SensorType.LinearShockStroke }, editor.ShockSensorTypes);
    }

    [AvaloniaFact]
    public void SelectedBike_DraftRearSuspension_RestrictsSensorTypes()
    {
        var draftBike = TestSnapshots.Bike() with
        {
            RearSuspension = new RearSuspensionSpec.LinkageDraft(),
        };
        bikeStore.Add(draftBike);

        var editor = CreateEditor(TestSnapshots.Setup(bikeId: draftBike.Id));
        editor.LoadedCommand.Execute(null);

        Assert.Equal(new SensorType?[] { null }, editor.ShockSensorTypes);
    }

    [AvaloniaFact]
    public void SelectedBike_HardtailResolution_ReportsHardtailDescription()
    {
        var hardtailBike = TestSnapshots.Bike();
        bikeStore.Add(hardtailBike);

        var editor = CreateEditor(TestSnapshots.Setup(bikeId: hardtailBike.Id));
        editor.LoadedCommand.Execute(null);

        Assert.Equal("Hardtail", editor.RearSuspensionDescription);
    }

    [AvaloniaFact]
    public void ChangingBike_ClearsIncompatibleRearSensorConfiguration()
    {
        var linkageBike = TestSnapshots.Bike() with
        {
            ShockStroke = 0.5,
            RearSuspension = new RearSuspensionSpec.Linkage(
                TestSnapshots.FullSuspensionLinkageSpec(includeHeadTubeJoints: true)),
        };
        var leverageRatioBike = TestSnapshots.LeverageRatioBike(
            TestSnapshots.LeverageRatioCurve((0, 0), (10, 25), (20, 50)));
        bikeStore.Add(linkageBike);
        bikeStore.Add(leverageRatioBike);
        var snapshot = TestSnapshots.Setup(bikeId: linkageBike.Id) with
        {
            RearSensorConfigurationJson = SensorConfiguration.ToJson(new LinearShockSensorConfiguration
            {
                Length = 75,
                Resolution = 14
            })
        };

        var editor = CreateEditor(snapshot);
        editor.LoadedCommand.Execute(null);

        editor.SelectedBike = leverageRatioBike;

        Assert.Null(editor.ShockSensorType);
        Assert.Null(editor.ShockSensorConfiguration);
        Assert.NotNull(editor.RearSensorCompatibilityMessage);
    }

    [AvaloniaFact]
    public void SettingShockSensorType_ToLinearShockStroke_OnCurveBike_CreatesMatchingViewModel()
    {
        var leverageRatioBike = TestSnapshots.LeverageRatioBike(
            TestSnapshots.LeverageRatioCurve((0, 0), (10, 25), (20, 50)));
        bikeStore.Add(leverageRatioBike);

        var editor = CreateEditor(TestSnapshots.Setup(bikeId: leverageRatioBike.Id));
        editor.LoadedCommand.Execute(null);

        editor.ShockSensorType = SensorType.LinearShockStroke;

        var configuration = Assert.IsType<LinearShockSensorConfigurationViewModel>(editor.ShockSensorConfiguration);
        Assert.Equal(SensorType.LinearShockStroke, configuration.Type);
    }

    [AvaloniaFact]
    public void SettingShockSensorType_ToRotationalShock_UsesLinkageSpecJointsWithoutBikeImage()
    {
        var linkage = TestSnapshots.FullSuspensionLinkageSpec(includeHeadTubeJoints: true);
        var linkageBike = TestSnapshots.Bike() with
        {
            ShockStroke = linkage.ShockStroke,
            RearSuspension = new RearSuspensionSpec.Linkage(linkage),
            HasImage = false,
        };
        bikeStore.Add(linkageBike);

        var editor = CreateEditor(TestSnapshots.Setup(bikeId: linkageBike.Id));
        editor.LoadedCommand.Execute(null);

        editor.ShockSensorType = SensorType.RotationalShock;

        var configuration = Assert.IsType<RotationalShockSensorConfigurationViewModel>(editor.ShockSensorConfiguration);
        Assert.Equal(
            linkage.Joints.Select(joint => joint.Name),
            configuration.Joints.Select(joint => joint.Name));
    }

    // ----- CanSave -----

    [AvaloniaFact]
    public void SaveCommand_Disabled_WhenNoSensorConfiguration()
    {
        var bike = TestSnapshots.Bike();
        bikeStore.Add(bike);
        var boardId = Guid.NewGuid();
        var snapshot = TestSnapshots.Setup(bikeId: bike.Id);

        var editor = CreateEditor(snapshot, boardId: boardId);
        editor.LoadedCommand.Execute(null);
        editor.Name = "renamed";

        Assert.False(editor.SaveCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void SaveCommand_Enabled_WhenForkConfigurationValid_AndDirty()
    {
        var bike = TestSnapshots.Bike();
        bikeStore.Add(bike);
        var boardId = Guid.NewGuid();
        var snapshot = TestSnapshots.Setup(bikeId: bike.Id)
            with
        { FrontSensorConfigurationJson = LinearForkConfigurationJson };

        var editor = CreateEditor(snapshot, boardId: boardId);
        editor.LoadedCommand.Execute(null);
        editor.Name = "renamed";

        Assert.True(editor.SaveCommand.CanExecute(null));
    }

    // ----- Save -----

    [AvaloniaFact]
    public async Task Save_HappyPath_RoutesThroughCoordinator_AndUpdatesBaseline()
    {
        var bike = TestSnapshots.Bike();
        bikeStore.Add(bike);
        var boardId = Guid.NewGuid();
        var snapshot = TestSnapshots.Setup(bikeId: bike.Id, updated: 5)
            with
        { FrontSensorConfigurationJson = LinearForkConfigurationJson };
        var editor = CreateEditor(snapshot, boardId: boardId);
        editor.LoadedCommand.Execute(null);
        editor.Name = "renamed";
        var sensor = Assert.IsType<LinearForkSensorConfigurationViewModel>(editor.ForkSensorConfiguration);
        sensor.Length = 150;
        Assert.True(editor.IsDirty);

        setupCoordinator.SaveAsync(Arg.Any<SetupDraft>(), SetupDraft.From(snapshot, boardId))
            .Returns(new SetupSaveResult.Saved(snapshot with { Name = "renamed", FrontSensorConfigurationJson = "{\"type\":\"linear_fork\",\"length\":150,\"resolution\":12}", Updated = 11 }, boardId));

        await editor.SaveCommand.ExecuteAsync(null);

        await setupCoordinator.Received(1).SaveAsync(
            Arg.Is<SetupDraft>(s => s.Id == snapshot.Id && s.Name == "renamed" && s.BikeId == bike.Id),
            SetupDraft.From(snapshot, boardId));
        shell.Received(1).CompleteEdit(new EditorKey.Setup(snapshot.Id));
        Assert.False(editor.IsDirty);
        shell.DidNotReceive().GoBack();
    }

    [AvaloniaFact]
    public async Task Save_PreservesSensorEditsMadeWhilePersistenceIsPending()
    {
        var bike = TestSnapshots.Bike();
        bikeStore.Add(bike);
        var board = Guid.NewGuid();
        var snapshot = TestSnapshots.Setup(bikeId: bike.Id) with { FrontSensorConfigurationJson = LinearForkConfigurationJson };
        var editor = CreateEditor(snapshot, boardId: board);
        var sensor = Assert.IsType<LinearForkSensorConfigurationViewModel>(editor.ForkSensorConfiguration);
        sensor.Length = 150;
        var pending = new TaskCompletionSource<SetupSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        setupCoordinator.SaveAsync(Arg.Any<SetupDraft>(), SetupDraft.From(snapshot, board)).Returns(pending.Task);
        var save = editor.SaveCommand.ExecuteAsync(null);
        sensor.Length = 175;
        pending.SetResult(new SetupSaveResult.Saved(snapshot with
        {
            FrontSensorConfigurationJson = "{\"type\":\"linear_fork\",\"length\":150,\"resolution\":12}", Updated = 2,
        }, board));

        await save.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(175, sensor.Length);
        Assert.True(editor.IsDirty);
        shell.DidNotReceive().CompleteEdit(Arg.Any<EditorKey>());
        await editor.ResetCommand.ExecuteAsync(null);
        Assert.Equal(150, Assert.IsType<LinearForkSensorConfigurationViewModel>(editor.ForkSensorConfiguration).Length);
        Assert.False(editor.IsDirty);
    }

    [AvaloniaFact]
    public async Task Save_OnConflict_PromptsUser_AndReloadsWhenAccepted()
    {
        var bike = TestSnapshots.Bike();
        bikeStore.Add(bike);
        var boardId = Guid.NewGuid();
        var snapshot = TestSnapshots.Setup(name: "old", bikeId: bike.Id, updated: 5)
            with
        { FrontSensorConfigurationJson = LinearForkConfigurationJson };
        var editor = CreateEditor(snapshot, boardId: boardId);
        editor.LoadedCommand.Execute(null);
        editor.Name = "renamed";

        var fresh = TestSnapshots.Setup(id: snapshot.Id, name: "remote-updated", bikeId: bike.Id, updated: 12)
            with
        { FrontSensorConfigurationJson = LinearForkConfigurationJson };
        setupCoordinator.SaveAsync(Arg.Any<SetupDraft>(), SetupDraft.From(snapshot, boardId))
            .Returns(new SetupSaveResult.Conflict(fresh, boardId));
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal("remote-updated", editor.Name);
    }

    [AvaloniaFact]
    public async Task Save_OnConflict_DoesNothing_WhenUserDeclinesReload()
    {
        var bike = TestSnapshots.Bike();
        bikeStore.Add(bike);
        var boardId = Guid.NewGuid();
        var snapshot = TestSnapshots.Setup(name: "old", bikeId: bike.Id, updated: 5)
            with
        { FrontSensorConfigurationJson = LinearForkConfigurationJson };
        var editor = CreateEditor(snapshot, boardId: boardId);
        editor.LoadedCommand.Execute(null);
        editor.Name = "renamed";

        var fresh = TestSnapshots.Setup(id: snapshot.Id, name: "remote-updated", bikeId: bike.Id, updated: 12)
            with
        { FrontSensorConfigurationJson = LinearForkConfigurationJson };
        setupCoordinator.SaveAsync(Arg.Any<SetupDraft>(), SetupDraft.From(snapshot, boardId))
            .Returns(new SetupSaveResult.Conflict(fresh, boardId));
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal("renamed", editor.Name);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_RejectedOrFailed_AppendsErrorAndKeepsDraft(bool boardInUse)
    {
        var bike = TestSnapshots.Bike();
        bikeStore.Add(bike);
        var boardId = Guid.NewGuid();
        var snapshot = TestSnapshots.Setup(bikeId: bike.Id, updated: 5)
            with
        { FrontSensorConfigurationJson = LinearForkConfigurationJson };
        var editor = CreateEditor(snapshot, boardId: boardId);
        editor.LoadedCommand.Execute(null);
        editor.Name = "renamed";

        setupCoordinator.SaveAsync(Arg.Any<SetupDraft>(), SetupDraft.From(snapshot, boardId))
            .Returns(boardInUse
                ? new SetupSaveResult.BoardInUse(boardId)
                : new SetupSaveResult.Failed(SetupSaveFailureReason.Save, "disk full"));

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Single(editor.ErrorMessages);
        Assert.True(editor.IsDirty);
        Assert.Equal(boardId, editor.BoardId);
        shell.DidNotReceive().CompleteEdit(Arg.Any<EditorKey>());
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TryPrepareClose_KeepsChanges_WhenSaveFailsOrConflictReloadIsDeclined(
        bool sensorOnly,
        bool conflict)
    {
        var bike = TestSnapshots.Bike();
        bikeStore.Add(bike);
        var boardId = Guid.NewGuid();
        var snapshot = TestSnapshots.Setup(bikeId: bike.Id, updated: 5) with
        {
            FrontSensorConfigurationJson = LinearForkConfigurationJson
        };
        var editor = CreateEditor(snapshot, boardId: boardId);
        editor.LoadedCommand.Execute(null);
        var sensor = Assert.IsType<LinearForkSensorConfigurationViewModel>(editor.ForkSensorConfiguration);
        if (sensorOnly) sensor.Length = 150;
        else editor.Name = "Changed setup";
        Assert.True(editor.SaveCommand.CanExecute(null));
        setupCoordinator.SaveAsync(Arg.Any<SetupDraft>(), SetupDraft.From(snapshot, boardId)).Returns(
            conflict
                ? new SetupSaveResult.Conflict(snapshot with { Updated = 6 })
                : new SetupSaveResult.Failed(SetupSaveFailureReason.Save, "disk full"));
        dialogService.ShowCloseConfirmationAsync(true).Returns(PromptResult.Yes);
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        Assert.False(await editor.TryPrepareCloseAsync());

        Assert.True(editor.IsDirty);
        Assert.True(editor.SaveCommand.CanExecute(null));
    }

    // ----- Delete -----

    [AvaloniaFact]
    public async Task Delete_OnNewUnsavedSetup_NavigatesBack_WithoutCallingCoordinator()
    {
        var snapshot = TestSnapshots.Setup();
        var editor = CreateEditor(snapshot, isNew: true);

        await editor.DeleteCommand.ExecuteAsync(true);

        await setupCoordinator.DidNotReceive().DeleteAsync(Arg.Any<Guid>());
        await shell.Received(1).CloseEditorAsync(new EditorKey.Setup(editor.Id));
    }

    [AvaloniaFact]
    public async Task Delete_HappyPath_NavigatesBack()
    {
        var snapshot = TestSnapshots.Setup();
        var editor = CreateEditor(snapshot);
        setupCoordinator.DeleteAsync(snapshot.Id)
            .Returns(new SetupDeleteResult.Deleted());

        await editor.DeleteCommand.ExecuteAsync(true);

        await shell.Received(1).CloseEditorAsync(new EditorKey.Setup(editor.Id));
        Assert.Empty(editor.ErrorMessages);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delete_InUseOrFailed_KeepsEditorOpen(bool inUse)
    {
        var snapshot = TestSnapshots.Setup();
        var editor = CreateEditor(snapshot);
        setupCoordinator.DeleteAsync(snapshot.Id)
            .Returns(inUse ? new SetupDeleteResult.InUse() : new SetupDeleteResult.Failed(SetupDeleteFailureReason.Delete, "locked"));

        await editor.DeleteCommand.ExecuteAsync(true);

        Assert.Single(editor.ErrorMessages);
        await shell.DidNotReceive().CloseEditorAsync(Arg.Any<EditorKey>());
    }

    // ----- EditBike -----

    [AvaloniaFact]
    public async Task EditBike_RoutesToBikeCoordinator()
    {
        var bike = TestSnapshots.Bike();
        var snapshot = TestSnapshots.Setup();
        var editor = CreateEditor(snapshot);

        await editor.EditBikeCommand.ExecuteAsync(bike);

        shell.Received(1).OpenEditor(new EditorKey.Bike(bike.Id));
    }
}
