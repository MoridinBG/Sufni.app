using System.Reactive.Linq;
using Avalonia.Headless.XUnit;
using DynamicData;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.Models.SensorConfigurations;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.Editors;
using Sufni.App.ViewModels.SensorConfigurations;

namespace Sufni.App.Tests.ViewModels.Editors;

public class SetupEditorViewModelTests
{
    private readonly SetupCoordinator setupCoordinator = TestCoordinatorSubstitutes.Setup();
    private readonly BikeCoordinator bikeCoordinator = TestCoordinatorSubstitutes.Bike();
    private readonly IBikeStore bikeStore = Substitute.For<IBikeStore>();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();

    private readonly SourceCache<BikeSnapshot, Guid> bikesCache = new(b => b.Id);

    public SetupEditorViewModelTests()
    {
        // The SetupEditorViewModel binds to bikeStore.Connect() in
        // Loaded; back the substitute with a real DynamicData source
        // cache so the binding actually publishes snapshots.
        bikeStore.Connect().Returns(bikesCache.Connect());
    }

    private SetupEditorViewModel CreateEditor(SetupSnapshot snapshot, bool isNew = false) =>
        new(snapshot, isNew, bikeStore, bikeCoordinator, setupCoordinator, shell, dialogService);

    private const string LinearForkConfigurationJson =
        "{\"type\":\"linear_fork\",\"length\":100,\"resolution\":12}";

    // ----- Construction -----

    [AvaloniaFact]
    public void Construction_PopulatesNameBoardSensorConfigs_FromSnapshot()
    {
        var bike = TestSnapshots.Bike();
        var boardId = Guid.NewGuid();
        var snapshot = TestSnapshots.Setup(name: "race day", bikeId: bike.Id, boardId: boardId, updated: 9)
            with
        { FrontSensorConfigurationJson = LinearForkConfigurationJson };

        var editor = CreateEditor(snapshot);

        Assert.Equal("race day", editor.Name);
        Assert.Equal(boardId, editor.BoardId);
        Assert.Equal(SensorType.LinearFork, editor.ForkSensorConfiguration?.Type);
        Assert.Null(editor.ShockSensorConfiguration);
        Assert.True(editor.IsInDatabase);
        Assert.Equal(9, editor.BaselineUpdated);
    }

    [AvaloniaFact]
    public void Loaded_PopulatesSelectedBike_FromBikeStore()
    {
        var bike = TestSnapshots.Bike();
        bikesCache.AddOrUpdate(bike);
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
            SensorConfigurationViewModel.FromJson(LinearForkConfigurationJson));

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
            RearSuspensionKind = RearSuspensionKind.Linkage,
            ShockStroke = 0.5,
            Linkage = TestSnapshots.FullSuspensionLinkage(includeHeadTubeJoints: true),
        };
        bikesCache.AddOrUpdate(bike);
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
            RearSuspensionKind = RearSuspensionKind.Linkage,
            ShockStroke = 0.5,
            Linkage = TestSnapshots.FullSuspensionLinkage(includeHeadTubeJoints: true),
        };
        var leverageRatioBike = TestSnapshots.LeverageRatioBike(
            TestSnapshots.LeverageRatioCurve((0, 0), (10, 25), (20, 50)));
        bikesCache.AddOrUpdate(linkageBike);
        bikesCache.AddOrUpdate(leverageRatioBike);

        var editor = CreateEditor(TestSnapshots.Setup(bikeId: linkageBike.Id));
        editor.LoadedCommand.Execute(null);

        Assert.Equal(new SensorType?[] { null, SensorType.LinearShock, SensorType.RotationalShock }, editor.ShockSensorTypes);

        editor.SelectedBike = leverageRatioBike;

        Assert.Equal(new SensorType?[] { null, SensorType.LinearShockStroke }, editor.ShockSensorTypes);
    }

    [AvaloniaFact]
    public void SelectedBike_InvalidResolution_RestrictsSensorTypes()
    {
        var invalidBike = TestSnapshots.Bike() with
        {
            RearSuspensionKind = RearSuspensionKind.Linkage,
            LeverageRatio = TestSnapshots.LeverageRatioCurve((0, 0), (10, 25), (20, 50)),
        };
        bikesCache.AddOrUpdate(invalidBike);

        var editor = CreateEditor(TestSnapshots.Setup(bikeId: invalidBike.Id));
        editor.LoadedCommand.Execute(null);

        Assert.Equal(new SensorType?[] { null }, editor.ShockSensorTypes);
    }

    [AvaloniaFact]
    public void SelectedBike_HardtailResolution_ReportsHardtailDescription()
    {
        var hardtailBike = TestSnapshots.Bike();
        bikesCache.AddOrUpdate(hardtailBike);

        var editor = CreateEditor(TestSnapshots.Setup(bikeId: hardtailBike.Id));
        editor.LoadedCommand.Execute(null);

        Assert.Equal("Hardtail", editor.RearSuspensionDescription);
    }

    [AvaloniaFact]
    public void ChangingBike_ClearsIncompatibleRearSensorConfiguration()
    {
        var linkageBike = TestSnapshots.Bike() with
        {
            RearSuspensionKind = RearSuspensionKind.Linkage,
            ShockStroke = 0.5,
            Linkage = TestSnapshots.FullSuspensionLinkage(includeHeadTubeJoints: true),
        };
        var leverageRatioBike = TestSnapshots.LeverageRatioBike(
            TestSnapshots.LeverageRatioCurve((0, 0), (10, 25), (20, 50)));
        bikesCache.AddOrUpdate(linkageBike);
        bikesCache.AddOrUpdate(leverageRatioBike);
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
        bikesCache.AddOrUpdate(leverageRatioBike);

        var editor = CreateEditor(TestSnapshots.Setup(bikeId: leverageRatioBike.Id));
        editor.LoadedCommand.Execute(null);

        editor.ShockSensorType = SensorType.LinearShockStroke;

        var configuration = Assert.IsType<LinearShockSensorConfigurationViewModel>(editor.ShockSensorConfiguration);
        Assert.Equal(SensorType.LinearShockStroke, configuration.Type);
    }

    // ----- CanSave -----

    [AvaloniaFact]
    public void SaveCommand_Disabled_WhenNoSensorConfiguration()
    {
        var bike = TestSnapshots.Bike();
        bikesCache.AddOrUpdate(bike);
        var snapshot = TestSnapshots.Setup(bikeId: bike.Id, boardId: Guid.NewGuid());

        var editor = CreateEditor(snapshot);
        editor.LoadedCommand.Execute(null);
        editor.Name = "renamed";

        Assert.False(editor.SaveCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void SaveCommand_Enabled_WhenForkConfigurationValid_AndDirty()
    {
        var bike = TestSnapshots.Bike();
        bikesCache.AddOrUpdate(bike);
        var snapshot = TestSnapshots.Setup(bikeId: bike.Id, boardId: Guid.NewGuid())
            with
        { FrontSensorConfigurationJson = LinearForkConfigurationJson };

        var editor = CreateEditor(snapshot);
        editor.LoadedCommand.Execute(null);
        editor.Name = "renamed";

        Assert.True(editor.SaveCommand.CanExecute(null));
    }

    // ----- Save -----

    [AvaloniaFact]
    public async Task Save_HappyPath_RoutesThroughCoordinator_AndUpdatesBaseline()
    {
        var bike = TestSnapshots.Bike();
        bikesCache.AddOrUpdate(bike);
        var boardId = Guid.NewGuid();
        var snapshot = TestSnapshots.Setup(bikeId: bike.Id, boardId: boardId, updated: 5)
            with
        { FrontSensorConfigurationJson = LinearForkConfigurationJson };
        var editor = CreateEditor(snapshot);
        editor.LoadedCommand.Execute(null);
        editor.Name = "renamed";

        setupCoordinator.SaveAsync(Arg.Any<Setup>(), boardId, 5)
            .Returns(new SetupSaveResult.Saved(11));

        await editor.SaveCommand.ExecuteAsync(null);

        await setupCoordinator.Received(1).SaveAsync(
            Arg.Is<Setup>(s => s.Id == snapshot.Id && s.Name == "renamed" && s.BikeId == bike.Id),
            boardId,
            5);
        Assert.Equal(11, editor.BaselineUpdated);
        shell.DidNotReceive().GoBack();
    }

    [AvaloniaFact]
    public async Task Save_OnMobile_DoesNotNavigateDirectly()
    {
        var bike = TestSnapshots.Bike();
        bikesCache.AddOrUpdate(bike);
        var boardId = Guid.NewGuid();
        var snapshot = TestSnapshots.Setup(bikeId: bike.Id, boardId: boardId, updated: 5)
            with
        { FrontSensorConfigurationJson = LinearForkConfigurationJson };
        var editor = CreateEditor(snapshot);
        editor.LoadedCommand.Execute(null);
        editor.Name = "renamed";

        setupCoordinator.SaveAsync(Arg.Any<Setup>(), boardId, 5)
            .Returns(new SetupSaveResult.Saved(11));

        await editor.SaveCommand.ExecuteAsync(null);

        shell.DidNotReceive().GoBack();
    }

    [AvaloniaFact]
    public async Task Save_OnConflict_PromptsUser_AndReloadsWhenAccepted()
    {
        var bike = TestSnapshots.Bike();
        bikesCache.AddOrUpdate(bike);
        var boardId = Guid.NewGuid();
        var snapshot = TestSnapshots.Setup(name: "old", bikeId: bike.Id, boardId: boardId, updated: 5)
            with
        { FrontSensorConfigurationJson = LinearForkConfigurationJson };
        var editor = CreateEditor(snapshot);
        editor.LoadedCommand.Execute(null);
        editor.Name = "renamed";

        var fresh = TestSnapshots.Setup(id: snapshot.Id, name: "remote-updated", bikeId: bike.Id, boardId: boardId, updated: 12)
            with
        { FrontSensorConfigurationJson = LinearForkConfigurationJson };
        setupCoordinator.SaveAsync(Arg.Any<Setup>(), boardId, 5)
            .Returns(new SetupSaveResult.Conflict(fresh));
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal("remote-updated", editor.Name);
        Assert.Equal(12, editor.BaselineUpdated);
    }

    [AvaloniaFact]
    public async Task Save_OnConflict_DoesNothing_WhenUserDeclinesReload()
    {
        var bike = TestSnapshots.Bike();
        bikesCache.AddOrUpdate(bike);
        var boardId = Guid.NewGuid();
        var snapshot = TestSnapshots.Setup(name: "old", bikeId: bike.Id, boardId: boardId, updated: 5)
            with
        { FrontSensorConfigurationJson = LinearForkConfigurationJson };
        var editor = CreateEditor(snapshot);
        editor.LoadedCommand.Execute(null);
        editor.Name = "renamed";

        var fresh = TestSnapshots.Setup(id: snapshot.Id, name: "remote-updated", bikeId: bike.Id, boardId: boardId, updated: 12)
            with
        { FrontSensorConfigurationJson = LinearForkConfigurationJson };
        setupCoordinator.SaveAsync(Arg.Any<Setup>(), boardId, 5)
            .Returns(new SetupSaveResult.Conflict(fresh));
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal("renamed", editor.Name);
        Assert.Equal(5, editor.BaselineUpdated);
    }

    [AvaloniaFact]
    public async Task Save_OnFailed_AppendsErrorMessage()
    {
        var bike = TestSnapshots.Bike();
        bikesCache.AddOrUpdate(bike);
        var boardId = Guid.NewGuid();
        var snapshot = TestSnapshots.Setup(bikeId: bike.Id, boardId: boardId, updated: 5)
            with
        { FrontSensorConfigurationJson = LinearForkConfigurationJson };
        var editor = CreateEditor(snapshot);
        editor.LoadedCommand.Execute(null);
        editor.Name = "renamed";

        setupCoordinator.SaveAsync(Arg.Any<Setup>(), boardId, 5)
            .Returns(new SetupSaveResult.Failed("disk full"));

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Single(editor.ErrorMessages);
    }

    // ----- Delete -----

    [AvaloniaFact]
    public async Task Delete_OnNewUnsavedSetup_NavigatesBack_WithoutCallingCoordinator()
    {
        var snapshot = TestSnapshots.Setup();
        var editor = CreateEditor(snapshot, isNew: true);

        await editor.DeleteCommand.ExecuteAsync(true);

        await setupCoordinator.DidNotReceive().DeleteAsync(Arg.Any<Guid>());
        shell.Received(1).GoBack();
    }

    [AvaloniaFact]
    public async Task Delete_HappyPath_NavigatesBack()
    {
        var snapshot = TestSnapshots.Setup();
        var editor = CreateEditor(snapshot);
        setupCoordinator.DeleteAsync(snapshot.Id)
            .Returns(new SetupDeleteResult(SetupDeleteOutcome.Deleted));

        await editor.DeleteCommand.ExecuteAsync(true);

        shell.Received(1).GoBack();
        Assert.Empty(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task Delete_Failed_AppendsErrorMessage_AndDoesNotNavigateBack()
    {
        var snapshot = TestSnapshots.Setup();
        var editor = CreateEditor(snapshot);
        setupCoordinator.DeleteAsync(snapshot.Id)
            .Returns(new SetupDeleteResult(SetupDeleteOutcome.Failed, "locked"));

        await editor.DeleteCommand.ExecuteAsync(true);

        Assert.Single(editor.ErrorMessages);
        shell.DidNotReceive().GoBack();
    }

    // ----- EditBike -----

    [AvaloniaFact]
    public async Task EditBike_RoutesToBikeCoordinator()
    {
        var bike = TestSnapshots.Bike();
        var snapshot = TestSnapshots.Setup();
        var editor = CreateEditor(snapshot);

        await editor.EditBikeCommand.ExecuteAsync(bike);

        await bikeCoordinator.Received(1).OpenEditAsync(bike.Id);
    }
}
