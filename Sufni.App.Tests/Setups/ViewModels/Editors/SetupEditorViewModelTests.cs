using Sufni.Kinematics;
using Sufni.Telemetry;
using Sufni.App.Shared.Models;
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
using Sufni.App.Terrain.Coordinators;
using Sufni.App.Terrain.Queries;
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
    private readonly ITerrainLabelQuery labelQuery = TestTerrainLabelQueries.Empty();
    private readonly ITerrainLabelCoordinator labelCoordinator = Substitute.For<ITerrainLabelCoordinator>();

    public void Dispose() => bikeStore.Dispose();

    private SetupEditorViewModel CreateEditor(SetupSnapshot snapshot, bool isNew = false, Guid? boardId = null) =>
        new(snapshot, isNew, bikeStore, setupCoordinator, labelQuery, labelCoordinator, shell, dialogService, uiThreadDispatcher, timeProvider: new FakeTimeProvider(), boardId: boardId);

    private const string LinearForkConfigurationJson =
        "{\"type\":\"linear_fork\",\"length\":100,\"resolution\":12}";

    // ----- Construction -----

    // ----- Sensor type switching -----

    [Fact]
    public void Construction_PreservesCompatibleShockSensorType_WhenRearBikeOptionsPopulate()
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

        Assert.Equal(SensorType.LinearShock, editor.ShockSensorType);
    }

    [Fact]
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

        Assert.Equal(new SensorType?[] { null, SensorType.LinearShock, SensorType.RotationalShock }, editor.ShockSensorTypes);

        editor.SelectedBikeId = leverageRatioBike.Id;

        Assert.Equal(new SensorType?[] { null, SensorType.LinearShockStroke }, editor.ShockSensorTypes);
    }

    [Fact]
    public void SelectedBike_DraftRearSuspension_RestrictsSensorTypes()
    {
        var draftBike = TestSnapshots.Bike() with
        {
            RearSuspension = new RearSuspensionSpec.LinkageDraft(),
        };
        bikeStore.Add(draftBike);

        var editor = CreateEditor(TestSnapshots.Setup(bikeId: draftBike.Id));

        Assert.Equal(new SensorType?[] { null }, editor.ShockSensorTypes);
    }

    [Fact]
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

        editor.SelectedBikeId = leverageRatioBike.Id;

        Assert.Null(editor.ShockSensorType);
        Assert.Null(editor.ShockSensorConfiguration);
        Assert.NotNull(editor.RearSensorCompatibilityMessage);
    }

    [Fact]
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

        editor.ShockSensorType = SensorType.RotationalShock;

        var configuration = Assert.IsType<RotationalShockSensorConfigurationViewModel>(editor.ShockSensorConfiguration);
        Assert.Equal(
            linkage.Joints.Select(joint => joint.Name),
            configuration.Joints.Select(joint => joint.Name));
    }

    // ----- CanSave -----

    [Fact]
    public void SaveCommand_Disabled_WhenNoSensorConfiguration()
    {
        var bike = TestSnapshots.Bike();
        bikeStore.Add(bike);
        var boardId = Guid.NewGuid();
        var snapshot = TestSnapshots.Setup(bikeId: bike.Id);

        var editor = CreateEditor(snapshot, boardId: boardId);
        editor.Name = "renamed";

        Assert.False(editor.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void SaveCommand_Enabled_WhenForkConfigurationValid_AndDirty()
    {
        var bike = TestSnapshots.Bike();
        bikeStore.Add(bike);
        var boardId = Guid.NewGuid();
        var snapshot = TestSnapshots.Setup(bikeId: bike.Id)
            with
        { FrontSensorConfigurationJson = LinearForkConfigurationJson };

        var editor = CreateEditor(snapshot, boardId: boardId);
        editor.Name = "renamed";

        Assert.True(editor.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void SaveCommand_Disabled_WhenBoardIdTextIsNotAGuid()
    {
        var bike = TestSnapshots.Bike();
        bikeStore.Add(bike);
        var snapshot = TestSnapshots.Setup(bikeId: bike.Id)
            with
        { FrontSensorConfigurationJson = LinearForkConfigurationJson };
        var editor = CreateEditor(snapshot, boardId: Guid.NewGuid());
        editor.Name = "renamed";
        Assert.True(editor.SaveCommand.CanExecute(null));

        editor.BoardIdText = "not a board id";

        Assert.False(editor.SaveCommand.CanExecute(null));
    }

    // ----- Board ID text -----

    [Fact]
    public async Task TryPrepareClose_PromptsImmediatelyAfterInvalidBoardIdEdit()
    {
        var editor = CreateEditor(TestSnapshots.Setup());
        dialogService.ShowCloseConfirmationAsync(false).Returns(PromptResult.Cancel);

        editor.BoardIdText = "not a board id";

        Assert.False(await editor.TryPrepareCloseAsync());
        Assert.True(editor.IsDirty);
        await dialogService.Received(1).ShowCloseConfirmationAsync(false);
    }

    [Fact]
    public void BoardIdText_ReportsError_WhenTextIsNotAGuid()
    {
        var editor = CreateEditor(TestSnapshots.Setup(), boardId: Guid.NewGuid());

        editor.BoardIdText = "not a board id";

        Assert.Null(editor.BoardId);
        Assert.True(editor.HasErrors);
        Assert.NotEmpty(editor.GetErrors(nameof(SetupEditorViewModel.BoardIdText)));
    }

    [Fact]
    public void BoardIdText_ClearsBoardIdWithoutError_WhenTextIsEmptied()
    {
        var editor = CreateEditor(TestSnapshots.Setup(), boardId: Guid.NewGuid());

        editor.BoardIdText = "";

        Assert.Null(editor.BoardId);
        Assert.False(editor.HasErrors);
        Assert.Empty(editor.GetErrors(nameof(SetupEditorViewModel.BoardIdText)));
    }

    [Fact]
    public async Task ResetCommand_DiscardsInvalidBoardIdText_WhenSetupHasNoBoard()
    {
        var editor = CreateEditor(TestSnapshots.Setup());
        Assert.False(editor.IsDirty);

        editor.BoardIdText = "not a board id";

        Assert.True(editor.ResetCommand.CanExecute(null));
        await editor.ResetCommand.ExecuteAsync(null);
        Assert.False(editor.HasErrors);
        Assert.False(editor.IsDirty);
    }

    // ----- Save -----

    [Theory]
    [InlineData("D")]
    [InlineData("N")]
    [InlineData("B")]
    public async Task Save_SubmitsBoardIdParsedFromText(string format)
    {
        var bike = TestSnapshots.Bike();
        bikeStore.Add(bike);
        var originalBoardId = Guid.NewGuid();
        var newBoardId = Guid.NewGuid();
        var snapshot = TestSnapshots.Setup(bikeId: bike.Id)
            with
        { FrontSensorConfigurationJson = LinearForkConfigurationJson };
        var editor = CreateEditor(snapshot, boardId: originalBoardId);

        editor.BoardIdText = newBoardId.ToString(format);
        await editor.SaveCommand.ExecuteAsync(null);

        await setupCoordinator.Received(1).SaveAsync(
            Arg.Is<SetupDraft>(draft => draft.BoardId == newBoardId),
            SetupDraft.From(snapshot, originalBoardId));
    }

    [Fact]
    public async Task Save_HappyPath_RoutesThroughCoordinator_AndUpdatesBaseline()
    {
        var bike = TestSnapshots.Bike();
        bikeStore.Add(bike);
        var boardId = Guid.NewGuid();
        var snapshot = TestSnapshots.Setup(bikeId: bike.Id, updated: 5)
            with
        { FrontSensorConfigurationJson = LinearForkConfigurationJson };
        var editor = CreateEditor(snapshot, boardId: boardId);
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

    [Fact]
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

    [Fact]
    public async Task Save_OnConflict_PromptsUser_AndReloadsWhenAccepted()
    {
        var bike = TestSnapshots.Bike();
        bikeStore.Add(bike);
        var boardId = Guid.NewGuid();
        var snapshot = TestSnapshots.Setup(name: "old", bikeId: bike.Id, updated: 5)
            with
        { FrontSensorConfigurationJson = LinearForkConfigurationJson };
        var editor = CreateEditor(snapshot, boardId: boardId);
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

    [Fact]
    public async Task Save_OnConflict_DoesNothing_WhenUserDeclinesReload()
    {
        var bike = TestSnapshots.Bike();
        bikeStore.Add(bike);
        var boardId = Guid.NewGuid();
        var snapshot = TestSnapshots.Setup(name: "old", bikeId: bike.Id, updated: 5)
            with
        { FrontSensorConfigurationJson = LinearForkConfigurationJson };
        var editor = CreateEditor(snapshot, boardId: boardId);
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

    [Theory]
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

    [Theory]
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

    [Theory]
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

    // ----- Terrain labels -----

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void TerrainLabels_ExistOnlyForASavedSetup(bool isNew, bool present)
    {
        var editor = CreateEditor(TestSnapshots.Setup(), isNew: isNew);

        Assert.Equal(present, editor.TerrainLabels is not null);
    }

    [Fact]
    public void TerrainLabels_OpenRideOpensTheSessionAndLeavesTheDraftClean()
    {
        var snapshot = TestSnapshots.Setup();
        var row = LabelRow();
        labelQuery.ForSetup(snapshot.Id).Returns(new SetupTerrainLabels(null, [row]));
        var editor = CreateEditor(snapshot);

        editor.TerrainLabels!.OpenRideCommand.Execute(editor.TerrainLabels.Labels[0]);

        shell.Received(1).OpenEditor(new EditorKey.Session(row.SessionId));
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public async Task TerrainLabels_FailedWriteShowsInTheEditorErrors()
    {
        var snapshot = TestSnapshots.Setup();
        var row = LabelRow();
        labelQuery.ForSetup(snapshot.Id).Returns(new SetupTerrainLabels(null, [row]));
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        labelCoordinator.DeleteAsync(row.LabelId)
            .Returns(new TerrainLabelChangeResult.Failed(TerrainLabelWriteFailureReason.Delete, "locked"));
        var editor = CreateEditor(snapshot);

        await editor.TerrainLabels!.DeleteCommand.ExecuteAsync(editor.TerrainLabels.Labels[0]);

        Assert.Single(editor.ErrorMessages);
        Assert.False(editor.IsDirty);
    }

    private static SetupTerrainLabelRow LabelRow() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "ride", 1_767_261_600, RoughnessClass.Rough, new TelemetryTimeRange(2, 4));
}
