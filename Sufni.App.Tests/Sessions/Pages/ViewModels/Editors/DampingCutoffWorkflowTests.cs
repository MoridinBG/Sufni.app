using System.Reactive.Linq;
using NSubstitute;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Pages.ViewModels.Editors;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Shared.Models;
using Sufni.App.Tests.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Sessions.Pages.ViewModels.Editors;

public class DampingCutoffWorkflowTests : IDisposable
{
    private readonly IBikeCoordinator bikeCoordinator = TestCoordinatorSubstitutes.Bike();
    private readonly List<FailurePresentation> errors = [];
    private readonly RecordedSessionEditorStateControllerTestDriver driver = new();
    private readonly IDisposable subscription;
    private RecordedSessionEditorState state = RecordedSessionEditorState.CreateInitial();
    private readonly DampingCutoffWorkflow workflow;
    private readonly BikeSnapshot bike = TestSnapshots.Bike(updated: 7) with
    {
        FrontCompressionDampingCutoffMmPerSecond = 100, FrontReboundDampingCutoffMmPerSecond = 200,
        RearCompressionDampingCutoffMmPerSecond = 300, RearReboundDampingCutoffMmPerSecond = 400,
    };

    public DampingCutoffWorkflowTests()
    {
        subscription = driver.Controller.State.Subscribe(value => state = value);
        workflow = new DampingCutoffWorkflow(() => state.Domain, () => state.Intent.DampingSpeedCutoffs,
            driver.Actions.SetDampingSpeedCutoffs, bikeCoordinator, errors.Add);
        PublishBike(bike);
    }

    [Fact]
    public void Preview_WithoutBike_DoesNothing()
    {
        PublishBike(null);
        workflow.Preview(SuspensionType.Front, DampingSpeedCircuit.Compression, 257);
        Assert.Equal(DampingSpeedCutoffs.Default, state.Intent.DampingSpeedCutoffs);
        Assert.False(state.Presentation.CanEditDampingSpeedCutoffs);
    }

    [Fact]
    public void Preview_RoundsToDragStep_AndLeavesCommittedPlotCutoffs()
    {
        workflow.Preview(SuspensionType.Front, DampingSpeedCircuit.Compression, 257);
        Assert.Equal(260, state.Intent.DampingSpeedCutoffs.Front.CompressionMmPerSecond);
        Assert.Equal(bike.DampingSpeedCutoffs, state.Presentation.PlotDampingSpeedCutoffs);
    }

    [Fact]
    public void CancelPreview_RestoresTheCurrentProjectedBike()
    {
        workflow.Preview(SuspensionType.Front, DampingSpeedCircuit.Compression, 257);
        var changed = bike with { FrontCompressionDampingCutoffMmPerSecond = 330, Updated = 8 };
        PublishBike(changed);
        workflow.Preview(SuspensionType.Front, DampingSpeedCircuit.Compression, 450);

        workflow.CancelPreview();

        Assert.Equal(changed.DampingSpeedCutoffs, state.Intent.DampingSpeedCutoffs);
    }

    [Fact]
    public async Task CommitAsync_OlderSavedOutcomeDoesNotOverwriteNewerProjectedCutoffs()
    {
        var pending = new TaskCompletionSource<BikeDampingSpeedCutoffUpdateResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        bikeCoordinator.UpdateDampingSpeedCutoffAsync(bike.Id, bike.Updated, SuspensionType.Front, DampingSpeedCircuit.Rebound, 270)
            .Returns(pending.Task);
        var commit = workflow.CommitAsync(SuspensionType.Front, DampingSpeedCircuit.Rebound, 266);
        var latest = bike with { FrontReboundDampingCutoffMmPerSecond = 500, Updated = 9 };
        PublishBike(latest);
        pending.SetResult(new BikeDampingSpeedCutoffUpdateResult.Saved(bike with { FrontReboundDampingCutoffMmPerSecond = 270, Updated = 8 }));

        await commit.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(latest.DampingSpeedCutoffs, state.Intent.DampingSpeedCutoffs);
        Assert.Equal(latest.DampingSpeedCutoffs, state.Presentation.PlotDampingSpeedCutoffs);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task CommitAsync_ConflictRestoresProjectedCutoffsAndReportsConflict()
    {
        var latest = bike with { RearCompressionDampingCutoffMmPerSecond = 310, Updated = 9 };
        bikeCoordinator.UpdateDampingSpeedCutoffAsync(bike.Id, bike.Updated, SuspensionType.Rear, DampingSpeedCircuit.Compression, 500)
            .Returns(_ =>
            {
                PublishBike(latest);
                return new BikeDampingSpeedCutoffUpdateResult.Conflict(latest);
            });

        await workflow.CommitAsync(SuspensionType.Rear, DampingSpeedCircuit.Compression, 500);

        Assert.Equal(latest.DampingSpeedCutoffs, state.Intent.DampingSpeedCutoffs);
        Assert.Single(errors);
    }

    [Fact]
    public async Task CommitAsync_FailureRestoresCommittedValuesAndReportsDiagnostics()
    {
        bikeCoordinator.UpdateDampingSpeedCutoffAsync(bike.Id, bike.Updated, SuspensionType.Rear, DampingSpeedCircuit.Compression, 500)
            .Returns(new BikeDampingSpeedCutoffUpdateResult.Failed(BikeDampingCutoffFailureReason.Save, "disk full"));
        workflow.Preview(SuspensionType.Rear, DampingSpeedCircuit.Compression, 500);

        await workflow.CommitAsync(SuspensionType.Rear, DampingSpeedCircuit.Compression, 500);

        Assert.Equal(bike.DampingSpeedCutoffs, state.Intent.DampingSpeedCutoffs);
        Assert.Equal(bike.DampingSpeedCutoffs, state.Presentation.PlotDampingSpeedCutoffs);
        Assert.Equal("disk full", Assert.Single(errors).Details);
    }

    private void PublishBike(BikeSnapshot? value) => driver.DomainStates.OnNext(new RecordedSessionDomainSnapshot(
        state.Domain?.Session ?? TestSnapshots.Session(), null, value, null, null, null, new SessionStaleness.Current()));

    public void Dispose()
    {
        subscription.Dispose();
        driver.Dispose();
    }
}
