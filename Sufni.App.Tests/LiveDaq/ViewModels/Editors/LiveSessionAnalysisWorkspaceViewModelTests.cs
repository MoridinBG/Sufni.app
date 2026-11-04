using NSubstitute;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.LiveDaq.Coordinators.Capture;
using Sufni.App.LiveDaq.Queries;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.LiveDaq.ViewModels.Editors;
using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Shared.Common;
using Sufni.App.Shared.Models;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.LiveDaq;
using Sufni.Telemetry;

namespace Sufni.App.Tests.LiveDaq.ViewModels.Editors;

public class LiveSessionAnalysisWorkspaceViewModelTests : IDisposable
{
    private readonly BikeStoreStub bikes = new();
    private readonly ILiveCaptureCoordinator capture = Substitute.For<ILiveCaptureCoordinator>();
    private readonly List<FailurePresentation> errors = [];

    public void Dispose() => bikes.Dispose();

    private LiveDaqSessionContext CreateSessionContext(Guid? bikeId = null, DampingSpeedCutoffs? dampingSpeedCutoffs = null)
    {
        var context = LiveCaptureTestData.Context(bikeId: bikeId);
        bikes.Add(TestSnapshots.Bike(context.BikeId) with { DampingSpeedCutoffs = dampingSpeedCutoffs ?? DampingSpeedCutoffs.Default });
        return context;
    }

    private LiveSessionAnalysisWorkspaceViewModel CreateWorkspace(LiveDaqSessionContext context, IBikeCoordinator? bikeCoordinator = null) =>
        new(context, capture, bikes, bikeCoordinator ?? TestCoordinatorSubstitutes.Bike(), errors.Add);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CutoffCommit_UsesTheBikePublishedBeforeTheNextEdit(bool conflict)
    {
        var bikeCoordinator = TestCoordinatorSubstitutes.Bike();
        var bike = TestSnapshots.Bike() with { FrontCompressionDampingCutoffMmPerSecond = 350 };
        var initial = DampingSpeedCutoffs.FromValues(100, 200, 300, 400);
        bikeCoordinator.UpdateDampingSpeedCutoffAsync(bike.Id, 100, SuspensionType.Front, DampingSpeedCircuit.Compression, 350)
            .Returns(_ =>
            {
                bikes.Add(bike);
                return conflict ? new BikeDampingSpeedCutoffUpdateResult.Conflict(bike)
                    : new BikeDampingSpeedCutoffUpdateResult.Saved(bike);
            });
        bikeCoordinator.UpdateDampingSpeedCutoffAsync(bike.Id, 350, SuspensionType.Front, DampingSpeedCircuit.Compression, 400)
            .Returns(_ =>
            {
                var saved = bike with { FrontCompressionDampingCutoffMmPerSecond = 400 };
                bikes.Add(saved);
                return new BikeDampingSpeedCutoffUpdateResult.Saved(saved);
            });
        using var workspace = CreateWorkspace(CreateSessionContext(bikeId: bike.Id, dampingSpeedCutoffs: initial), bikeCoordinator);

        await workspace.CommitDampingSpeedCutoffAsync(SuspensionType.Front, DampingSpeedCircuit.Compression, 350);
        await workspace.CommitDampingSpeedCutoffAsync(SuspensionType.Front, DampingSpeedCircuit.Compression, 400);

        await bikeCoordinator.Received(1).UpdateDampingSpeedCutoffAsync(bike.Id, 350, SuspensionType.Front, DampingSpeedCircuit.Compression, 400);
        Assert.Equal(400, workspace.DampingSpeedCutoffs.Front.CompressionMmPerSecond);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DampingSpeedCutoffCommitUnsuccessful_RestoresPersistedCutoffs(bool missingBike)
    {
        var bikeCoordinator = TestCoordinatorSubstitutes.Bike();
        var bikeId = Guid.NewGuid();
        var initialCutoffs = DampingSpeedCutoffs.FromValues(100, 200, 300, 400);
        bikeCoordinator.UpdateDampingSpeedCutoffAsync(
                bikeId,
                initialCutoffs.Rear.ReboundMmPerSecond,
                SuspensionType.Rear,
                DampingSpeedCircuit.Rebound,
                600)
            .Returns(_ =>
            {
                if (missingBike) bikes.Remove(bikeId);
                return missingBike ? new BikeDampingSpeedCutoffUpdateResult.Missing()
                    : new BikeDampingSpeedCutoffUpdateResult.Failed(BikeDampingCutoffFailureReason.Save, "disk full");
            });
        var context = CreateSessionContext(
            bikeId: bikeId,
            dampingSpeedCutoffs: initialCutoffs);
        using var workspace = CreateWorkspace(context, bikeCoordinator);
        workspace.PreviewDampingSpeedCutoff(SuspensionType.Rear, DampingSpeedCircuit.Rebound, 600);

        await workspace.CommitDampingSpeedCutoffAsync(SuspensionType.Rear, DampingSpeedCircuit.Rebound, 600);

        Assert.Equal(missingBike ? DampingSpeedCutoffs.Default : initialCutoffs, workspace.DampingSpeedCutoffs);
        Assert.Equal(missingBike ? DampingSpeedCutoffs.Default : initialCutoffs, workspace.PlotDampingSpeedCutoffs);
        Assert.Equal(!missingBike, workspace.CanEditDampingSpeedCutoffs);
        Assert.Equal(missingBike ? 0 : 1, errors.Count);
    }


    [Fact]
    public void ReturningToRetainedOptions_RestoresResultsWithoutAnotherState()
    {
        using var workspace = CreateWorkspace(CreateSessionContext());
        var state = LiveCaptureTestData.State(true, TestTelemetryData.CreateProcessed());
        workspace.Apply(state);
        var key = workspace.AnalysisResultState.CurrentInputs!.CreateKey(
            RecordedSessionAnalysisFamily.VelocityDistribution, SuspensionType.Front);
        var result = workspace.AnalysisResultState.Get(key);
        Assert.NotNull(result);

        workspace.SelectedVelocityAverageMode = VelocityAverageMode.StrokePeakAveraged;
        Assert.Null(workspace.AnalysisResultState.Get(key));
        Assert.Equal(SessionDampingPercentages.Empty, workspace.DampingPercentages);
        Assert.Same(state.Analysis!.Telemetry, workspace.TelemetryData);
        Assert.Equal(SurfaceStateKind.Ready, workspace.FrontAnalysisState.Kind);
        workspace.SelectedVelocityAverageMode = VelocityAverageMode.SampleAveraged;

        Assert.Same(result, workspace.AnalysisResultState.Get(key));
        Assert.Same(state.Analysis!.Telemetry, workspace.TelemetryData);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void SurfaceReadiness_RequiresAcceptedTravelAndCalibration(bool calibrated, bool travelAccepted)
    {
        var context = LiveCaptureTestData.Context(hasFrontTravelCalibration: calibrated);
        using var workspace = CreateWorkspace(context);
        var waiting = LiveCaptureTestData.State(false);
        if (!travelAccepted)
            waiting = waiting with { Controls = waiting.Controls with
            {
                SessionHeader = waiting.Controls.SessionHeader! with { AcceptedTravelRateMhz = 0 },
            }};
        workspace.Apply(waiting);
        Assert.Equal(calibrated && travelAccepted ? SurfaceStateKind.WaitingForData : SurfaceStateKind.Hidden,
            workspace.FrontAnalysisState.Kind);

        workspace.Apply(LiveCaptureTestData.State(true, TestTelemetryData.CreateProcessed(), header: waiting.Controls.SessionHeader));

        Assert.Equal(calibrated && travelAccepted ? SurfaceStateKind.Ready : SurfaceStateKind.Hidden,
            workspace.FrontAnalysisState.Kind);
    }

    [Fact]
    public void BikePublication_UpdatesCutoffsAndInvalidatesAnalysisForPreviousCutoffs()
    {
        var context = CreateSessionContext();
        using var workspace = CreateWorkspace(context);
        var telemetry = TestTelemetryData.CreateProcessed();
        workspace.Apply(LiveCaptureTestData.State(true, telemetry));
        var key = workspace.AnalysisResultState.CurrentInputs!.CreateKey(
            RecordedSessionAnalysisFamily.VelocityDistribution, SuspensionType.Front);
        var updated = bikes.Get(context.BikeId)! with { FrontCompressionDampingCutoffMmPerSecond = 700 };

        bikes.Add(updated);

        Assert.Equal(700, workspace.DampingSpeedCutoffs.Front.CompressionMmPerSecond);
        Assert.Equal(workspace.DampingSpeedCutoffs, workspace.PlotDampingSpeedCutoffs);
        Assert.Same(telemetry, workspace.TelemetryData);
        Assert.Null(workspace.AnalysisResultState.Get(key));
        Assert.Equal(SessionDampingPercentages.Empty, workspace.DampingPercentages);
        capture.Received().UpdateAnalysisOptions(Arg.Is<LiveAnalysisOptions>(options =>
            options.DampingCutoffs.Front.CompressionMmPerSecond == 700 &&
            options.PlotDampingCutoffs.Front.CompressionMmPerSecond == 700));
    }

    [Fact]
    public void CaptureReset_ClearsTelemetryWhileAnOptionRefreshIsPending()
    {
        using var workspace = CreateWorkspace(CreateSessionContext());
        var state = LiveCaptureTestData.State(true, TestTelemetryData.CreateProcessed());
        workspace.Apply(state);
        workspace.PreviewDampingSpeedCutoff(SuspensionType.Front, DampingSpeedCircuit.Compression, 700);

        workspace.Apply(state with { Analysis = null });

        Assert.Null(workspace.TelemetryData);
        Assert.Null(workspace.AnalysisResultState.CurrentInputs);
        Assert.Equal(SurfaceStateKind.WaitingForData, workspace.FrontAnalysisState.Kind);
    }

    [Fact]
    public void PreviewCancellation_RestoresTheCurrentBikeCutoffs()
    {
        var context = CreateSessionContext();
        using var workspace = CreateWorkspace(context);
        bikes.Add(bikes.Get(context.BikeId)! with { RearReboundDampingCutoffMmPerSecond = 500 });
        workspace.PreviewDampingSpeedCutoff(SuspensionType.Rear, DampingSpeedCircuit.Rebound, 900);
        Assert.Equal(900, workspace.DampingSpeedCutoffs.Rear.ReboundMmPerSecond);

        workspace.CancelDampingSpeedCutoffPreview();

        Assert.Equal(500, workspace.DampingSpeedCutoffs.Rear.ReboundMmPerSecond);
    }

    [Fact]
    public async Task CutoffCompletion_AfterDisposal_DoesNotChangePresentationOrReportFailure()
    {
        var context = CreateSessionContext();
        var bikeCoordinator = TestCoordinatorSubstitutes.Bike();
        var pending = new TaskCompletionSource<BikeDampingSpeedCutoffUpdateResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        bikeCoordinator.UpdateDampingSpeedCutoffAsync(context.BikeId, Arg.Any<double>(),
            SuspensionType.Front, DampingSpeedCircuit.Compression, 800).Returns(pending.Task);
        using var workspace = CreateWorkspace(context, bikeCoordinator);
        var commit = workspace.CommitDampingSpeedCutoffAsync(SuspensionType.Front, DampingSpeedCircuit.Compression, 800);
        workspace.Dispose();
        bikes.Add(bikes.Get(context.BikeId)! with { FrontCompressionDampingCutoffMmPerSecond = 900 });
        pending.SetResult(new BikeDampingSpeedCutoffUpdateResult.Conflict(bikes.Get(context.BikeId)!));

        await commit.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(800, workspace.DampingSpeedCutoffs.Front.CompressionMmPerSecond);
        Assert.Empty(errors);
    }
}
