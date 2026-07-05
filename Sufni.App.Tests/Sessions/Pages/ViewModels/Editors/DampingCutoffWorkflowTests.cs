using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.Telemetry;

using Sufni.App.Bikes.Coordinators;
using Sufni.App.Sessions.Pages.ViewModels.Editors;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Doubles;
namespace Sufni.App.Tests.Sessions.Pages.ViewModels.Editors;

public class DampingCutoffWorkflowTests
{
    private readonly IBikeCoordinator bikeCoordinator = TestCoordinatorSubstitutes.Bike();
    private readonly List<string> errors = [];
    private bool canEditDampingSpeedCutoffs;
    private DampingSpeedCutoffs dampingSpeedCutoffs = DampingSpeedCutoffs.Default;
    private DampingSpeedCutoffs plotDampingSpeedCutoffs = DampingSpeedCutoffs.Default;

    private DampingCutoffWorkflow CreateWorkflow()
    {
        return new DampingCutoffWorkflow(
            () => dampingSpeedCutoffs,
            value => canEditDampingSpeedCutoffs = value,
            value => dampingSpeedCutoffs = value,
            value => plotDampingSpeedCutoffs = value,
            bikeCoordinator,
            errors.Add);
    }

    private static DampingSpeedCutoffs Cutoffs() => DampingSpeedCutoffs.FromValues(100, 200, 300, 400);

    [Fact]
    public void ApplyContext_EnablesEditingAndStagesPersistedCutoffs()
    {
        var workflow = CreateWorkflow();
        var owner = new DampingSpeedCutoffOwner(Guid.NewGuid(), 7);

        workflow.ApplyContext(Cutoffs(), owner);

        Assert.True(workflow.CanEdit);
        Assert.True(canEditDampingSpeedCutoffs);
        Assert.Equal(Cutoffs(), dampingSpeedCutoffs);
        Assert.Equal(Cutoffs(), plotDampingSpeedCutoffs);
    }

    [Fact]
    public void Preview_WithoutOwner_DoesNothing()
    {
        var workflow = CreateWorkflow();
        workflow.ApplyContext(Cutoffs(), null);

        workflow.Preview(SuspensionType.Front, DampingSpeedCircuit.Compression, 257);

        Assert.False(workflow.CanEdit);
        Assert.Equal(Cutoffs(), dampingSpeedCutoffs);
    }

    [Fact]
    public void Preview_RoundsToDragStep_AndLeavesPlotCutoffs()
    {
        var workflow = CreateWorkflow();
        workflow.ApplyContext(Cutoffs(), new DampingSpeedCutoffOwner(Guid.NewGuid(), 7));

        workflow.Preview(SuspensionType.Front, DampingSpeedCircuit.Compression, 257);

        Assert.Equal(260, dampingSpeedCutoffs.Front.CompressionMmPerSecond);
        Assert.Equal(Cutoffs(), plotDampingSpeedCutoffs);
    }

    [Fact]
    public void CancelPreview_RestoresThePreviewOrigin()
    {
        var workflow = CreateWorkflow();
        workflow.ApplyContext(Cutoffs(), new DampingSpeedCutoffOwner(Guid.NewGuid(), 7));
        workflow.Preview(SuspensionType.Front, DampingSpeedCircuit.Compression, 257);
        workflow.Preview(SuspensionType.Front, DampingSpeedCircuit.Compression, 312);

        workflow.CancelPreview();

        Assert.Equal(Cutoffs(), dampingSpeedCutoffs);
    }

    [Fact]
    public async Task CommitAsync_Saved_AppliesTheSavedSnapshotCutoffs()
    {
        var workflow = CreateWorkflow();
        var bikeId = Guid.NewGuid();
        var owner = new DampingSpeedCutoffOwner(bikeId, 7);
        var saved = TestSnapshots.Bike(id: bikeId, updated: 8) with
        {
            FrontCompressionDampingCutoffMmPerSecond = 100,
            FrontReboundDampingCutoffMmPerSecond = 270,
            RearCompressionDampingCutoffMmPerSecond = 300,
            RearReboundDampingCutoffMmPerSecond = 400,
        };
        bikeCoordinator.UpdateDampingSpeedCutoffAsync(
                bikeId, owner.BaselineUpdated, SuspensionType.Front, DampingSpeedCircuit.Rebound, 270)
            .Returns(new BikeDampingSpeedCutoffUpdateResult.Saved(saved));
        workflow.ApplyContext(Cutoffs(), owner);

        await workflow.CommitAsync(SuspensionType.Front, DampingSpeedCircuit.Rebound, 266);

        Assert.Equal(saved.DampingSpeedCutoffs, dampingSpeedCutoffs);
        Assert.Equal(saved.DampingSpeedCutoffs, plotDampingSpeedCutoffs);
        Assert.True(workflow.CanEdit);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task CommitAsync_Conflict_AppliesCurrentSnapshotAndReportsError()
    {
        var workflow = CreateWorkflow();
        var bikeId = Guid.NewGuid();
        var owner = new DampingSpeedCutoffOwner(bikeId, 7);
        var current = TestSnapshots.Bike(id: bikeId, updated: 9) with
        {
            FrontCompressionDampingCutoffMmPerSecond = 110,
            FrontReboundDampingCutoffMmPerSecond = 210,
            RearCompressionDampingCutoffMmPerSecond = 310,
            RearReboundDampingCutoffMmPerSecond = 410,
        };
        bikeCoordinator.UpdateDampingSpeedCutoffAsync(
                bikeId, owner.BaselineUpdated, SuspensionType.Rear, DampingSpeedCircuit.Compression, 500)
            .Returns(new BikeDampingSpeedCutoffUpdateResult.Conflict(current));
        workflow.ApplyContext(Cutoffs(), owner);

        await workflow.CommitAsync(SuspensionType.Rear, DampingSpeedCircuit.Compression, 500);

        Assert.Equal(current.DampingSpeedCutoffs, dampingSpeedCutoffs);
        Assert.True(workflow.CanEdit);
        Assert.Single(errors);
    }

    [Fact]
    public async Task CommitAsync_Failed_RestoresPersistedCutoffsAndReportsError()
    {
        var workflow = CreateWorkflow();
        var bikeId = Guid.NewGuid();
        var owner = new DampingSpeedCutoffOwner(bikeId, 7);
        bikeCoordinator.UpdateDampingSpeedCutoffAsync(
                bikeId, owner.BaselineUpdated, SuspensionType.Rear, DampingSpeedCircuit.Compression, 500)
            .Returns(new BikeDampingSpeedCutoffUpdateResult.Failed("disk full"));
        workflow.ApplyContext(Cutoffs(), owner);
        workflow.Preview(SuspensionType.Rear, DampingSpeedCircuit.Compression, 500);

        await workflow.CommitAsync(SuspensionType.Rear, DampingSpeedCircuit.Compression, 500);

        Assert.Equal(Cutoffs(), dampingSpeedCutoffs);
        Assert.Equal(Cutoffs(), plotDampingSpeedCutoffs);
        Assert.Single(errors);
    }
}
