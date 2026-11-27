using System.Reactive;
using System.Reactive.Subjects;
using NSubstitute;
using Sufni.App.Infrastructure;
using Sufni.App.Shared.Models;
using Sufni.App.Terrain.Coordinators;
using Sufni.App.Terrain.Formatting;
using Sufni.App.Terrain.Models;
using Sufni.App.Terrain.Queries;
using Sufni.App.Terrain.ViewModels;
using Sufni.App.Tests.TestSupport.Extensions;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Terrain.ViewModels;

public sealed class SetupTerrainLabelsViewModelTests : IDisposable
{
    private static readonly SensorScale NoData = new SensorScale.NotEnoughData(0, 0);

    private readonly Guid setupId = Guid.NewGuid();
    private readonly ITerrainLabelQuery query = Substitute.For<ITerrainLabelQuery>();
    private readonly ITerrainLabelCoordinator coordinator = Substitute.For<ITerrainLabelCoordinator>();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();
    private readonly Subject<Unit> changes = new();
    private readonly List<Guid> openedSessions = [];
    private readonly List<FailurePresentation> errors = [];

    public SetupTerrainLabelsViewModelTests()
    {
        query.Changes.Returns(changes);
        query.ForSetup(setupId).Returns(new SetupTerrainLabels(null, []));
    }

    public void Dispose() => changes.Dispose();

    [Fact]
    public void RowsFollowTheQueryAndUpdateOnAChange()
    {
        var first = Row(RoughnessClass.Rough);
        var second = Row(RoughnessClass.Calm);
        query.ForSetup(setupId).Returns(new SetupTerrainLabels(null, [first]));
        using var viewModel = Create();
        Assert.Equal([first.LabelId], viewModel.Labels.Select(item => item.LabelId));

        query.ForSetup(setupId).Returns(new SetupTerrainLabels(null, [second, first]));
        changes.OnNext(Unit.Default);

        Assert.Equal([second.LabelId, first.LabelId], viewModel.Labels.Select(item => item.LabelId));
    }

    [Fact]
    public void SteepnessRowsAreListedApart()
    {
        var roughness = Row(RoughnessClass.Rough);
        var steepness = SteepnessRow(GradeBand.SteepDescent);
        query.ForSetup(setupId).Returns(new SetupTerrainLabels(null, [roughness]) { SteepnessLabels = [steepness] });

        using var viewModel = Create();

        Assert.Equal(roughness.LabelId, Assert.Single(viewModel.Labels).LabelId);
        Assert.Equal(steepness.LabelId, Assert.Single(viewModel.SteepnessLabels).LabelId);
    }

    [Fact]
    public void ScaleLinesCoverTheSensorsWithRidesOrLabelledMetres()
    {
        var scale = new SetupTerrainScale(new SensorScale.Ready(5), NoData, NoData, NoData, new TerrainLabelCoverage(0, 0, 40, 0, 0));
        query.ForSetup(setupId).Returns(new SetupTerrainLabels(scale, []));

        using var viewModel = Create();

        Assert.Equal([.. TerrainSourceText.ScaleLines(scale, sensor => sensor == TerrainSensor.FrameImu),
            .. TerrainSourceText.ScaleLines(scale, sensor => sensor == TerrainSensor.RearAxleImu)], viewModel.ScaleLines);
    }

    [Fact]
    public void OpenRideOpensTheSessionOfTheRow()
    {
        var row = Row(RoughnessClass.Rough);
        query.ForSetup(setupId).Returns(new SetupTerrainLabels(null, [row]));
        using var viewModel = Create();

        viewModel.OpenRideCommand.Execute(viewModel.Labels[0]);

        Assert.Equal([row.SessionId], openedSessions);
    }

    [Fact]
    public async Task ChangeClassCallsTheCoordinatorWithTheChosenClass()
    {
        var row = Row(RoughnessClass.Rough);
        query.ForSetup(setupId).Returns(new SetupTerrainLabels(null, [row]));
        coordinator.SetClassAsync(row.LabelId, new TerrainLabelClass.Roughness(RoughnessClass.Smooth)).Returns(new TerrainLabelChangeResult.Applied());
        dialogService.AnswerClassPrompt(prompt =>
        {
            prompt.SelectedClass = new TerrainLabelClass.Roughness(RoughnessClass.Smooth);
            prompt.ConfirmCommand.Execute(null);
        });
        using var viewModel = Create();

        await viewModel.ChangeClassCommand.ExecuteAsync(viewModel.Labels[0]);

        await coordinator.Received(1).SetClassAsync(row.LabelId, new TerrainLabelClass.Roughness(RoughnessClass.Smooth));
    }

    [Fact]
    public async Task ChangeClassOfASteepnessLabelOffersTheGradeBands()
    {
        var row = SteepnessRow(GradeBand.Descent);
        query.ForSetup(setupId).Returns(new SetupTerrainLabels(null, []) { SteepnessLabels = [row] });
        IEnumerable<TerrainLabelClass>? offered = null;
        dialogService.AnswerClassPrompt(prompt =>
        {
            offered = prompt.Classes.Select(option => option.Class);
            prompt.SelectedClass = new TerrainLabelClass.Steepness(GradeBand.SteepDescent);
            prompt.ConfirmCommand.Execute(null);
        });
        using var viewModel = Create();

        await viewModel.ChangeClassCommand.ExecuteAsync(viewModel.SteepnessLabels[0]);

        Assert.All(offered!, option => Assert.IsType<TerrainLabelClass.Steepness>(option));
        await coordinator.Received(1).SetClassAsync(row.LabelId, new TerrainLabelClass.Steepness(GradeBand.SteepDescent));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteDeletesOnlyAfterConfirmation(bool confirmed)
    {
        var row = Row(RoughnessClass.Rough);
        query.ForSetup(setupId).Returns(new SetupTerrainLabels(null, [row]));
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(confirmed);
        coordinator.DeleteAsync(row.LabelId).Returns(new TerrainLabelChangeResult.Applied());
        using var viewModel = Create();

        await viewModel.DeleteCommand.ExecuteAsync(viewModel.Labels[0]);

        await coordinator.Received(confirmed ? 1 : 0).DeleteAsync(row.LabelId);
    }

    [Fact]
    public async Task FailedWriteIsReported()
    {
        var row = Row(RoughnessClass.Rough);
        query.ForSetup(setupId).Returns(new SetupTerrainLabels(null, [row]));
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        coordinator.DeleteAsync(row.LabelId)
            .Returns(new TerrainLabelChangeResult.Failed(TerrainLabelWriteFailureReason.Delete, "storage unavailable"));
        using var viewModel = Create();

        await viewModel.DeleteCommand.ExecuteAsync(viewModel.Labels[0]);

        Assert.Equal(FailurePresentation.From(TerrainLabelWriteFailureReason.Delete, "storage unavailable"), Assert.Single(errors));
    }

    private SetupTerrainLabelsViewModel Create() =>
        new(setupId, query, coordinator, dialogService, openedSessions.Add, errors.Add);

    private static SetupTerrainLabelRow Row(RoughnessClass roughness) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "ride", 1_767_261_600, roughness, new TelemetryTimeRange(2, 4));

    private static SetupSteepnessLabelRow SteepnessRow(GradeBand band) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "ride", 1_767_261_600, band, new TelemetryTimeRange(2, 4));
}
