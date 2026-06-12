using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.SessionDetails;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.Editors;
using Sufni.Telemetry;

namespace Sufni.App.Tests.ViewModels.Editors;

public class DamperCutoffWorkflowTests
{
    private readonly RecordedSessionContext context = new();
    private readonly IBikeCoordinator bikeCoordinator = TestCoordinatorSubstitutes.Bike();
    private readonly List<string> errors = [];

    private DamperCutoffWorkflow CreateWorkflow() => new(context, bikeCoordinator, errors.Add);

    private static DampingSpeedCutoffs Cutoffs() => DampingSpeedCutoffs.FromValues(100, 200, 300, 400);

    [Fact]
    public void ApplyContext_EnablesEditingAndStagesPersistedCutoffs()
    {
        var workflow = CreateWorkflow();
        var owner = new DampingSpeedCutoffOwner(Guid.NewGuid(), 7);

        workflow.ApplyContext(Cutoffs(), owner);

        Assert.True(workflow.CanEdit);
        Assert.True(context.CanEditDampingSpeedCutoffs);
        Assert.Equal(Cutoffs(), context.DampingSpeedCutoffs);
        Assert.Equal(Cutoffs(), context.PlotDampingSpeedCutoffs);
    }

    [Fact]
    public void Preview_WithoutOwner_DoesNothing()
    {
        var workflow = CreateWorkflow();
        workflow.ApplyContext(Cutoffs(), null);

        workflow.Preview(SuspensionType.Front, DampingSpeedCircuit.Compression, 257);

        Assert.False(workflow.CanEdit);
        Assert.Equal(Cutoffs(), context.DampingSpeedCutoffs);
    }

    [Fact]
    public void Preview_RoundsToDragStep_AndLeavesPlotCutoffs()
    {
        var workflow = CreateWorkflow();
        workflow.ApplyContext(Cutoffs(), new DampingSpeedCutoffOwner(Guid.NewGuid(), 7));

        workflow.Preview(SuspensionType.Front, DampingSpeedCircuit.Compression, 257);

        Assert.Equal(260, context.DampingSpeedCutoffs.Front.CompressionMmPerSecond);
        Assert.Equal(Cutoffs(), context.PlotDampingSpeedCutoffs);
    }

    [Fact]
    public void CancelPreview_RestoresThePreviewOrigin()
    {
        var workflow = CreateWorkflow();
        workflow.ApplyContext(Cutoffs(), new DampingSpeedCutoffOwner(Guid.NewGuid(), 7));
        workflow.Preview(SuspensionType.Front, DampingSpeedCircuit.Compression, 257);
        workflow.Preview(SuspensionType.Front, DampingSpeedCircuit.Compression, 312);

        workflow.CancelPreview();

        Assert.Equal(Cutoffs(), context.DampingSpeedCutoffs);
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

        Assert.Equal(saved.DampingSpeedCutoffs, context.DampingSpeedCutoffs);
        Assert.Equal(saved.DampingSpeedCutoffs, context.PlotDampingSpeedCutoffs);
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

        Assert.Equal(current.DampingSpeedCutoffs, context.DampingSpeedCutoffs);
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

        Assert.Equal(Cutoffs(), context.DampingSpeedCutoffs);
        Assert.Equal(Cutoffs(), context.PlotDampingSpeedCutoffs);
        Assert.Single(errors);
    }
}
