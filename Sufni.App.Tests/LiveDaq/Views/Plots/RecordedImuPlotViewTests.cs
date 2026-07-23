using System.Reactive.Subjects;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using ScottPlot.Avalonia;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.LiveDaq.Services.Imu;
using Sufni.App.LiveDaq.Views.Plots;
using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Shared.Plots;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.Telemetry;

namespace Sufni.App.Tests.LiveDaq.Views.Plots;

[Collection("Ui")]
public class RecordedImuPlotViewTests
{
    [AvaloniaFact]
    public async Task RecordedImuPlotViews_RequestOneSharedProjection_WhenRowsBecomeEffectivelyVisible()
    {
        ViewTestHelpers.EnsurePlotViewStyle();
        var telemetry = TestTelemetryData.CreateWithImu();
        var computer = new ProjectionAnalysisComputer();
        var backgroundTaskRunner = new ControllableBackgroundTaskRunner();
        using var state = new RecordedSessionAnalysisResultState(
            computer,
            backgroundTaskRunner,
            new InlineUiThreadDispatcher(),
            () => telemetry);
        state.Invalidate(CreateInputs());
        var imuView = new CountingImuPlotView
        {
            Telemetry = telemetry,
            AnalysisResultState = state,
        };
        var pitchRollView = new CountingFramePitchRollPlotView
        {
            Telemetry = telemetry,
            AnalysisResultState = state,
        };
        var content = new StackPanel
        {
            Children =
            {
                imuView,
                pitchRollView,
            },
        };
        var hiddenAncestor = new Border
        {
            IsVisible = false,
            Child = content,
        };
        var host = new Window
        {
            Width = 900,
            Height = 700,
            Content = hiddenAncestor,
        };

        host.Show();
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(0, backgroundTaskRunner.RunCount);
        Assert.Equal(0, imuView.ProjectionLoadCount);
        Assert.Equal(0, pitchRollView.ProjectionLoadCount);

        hiddenAncestor.IsVisible = true;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(1, backgroundTaskRunner.RunCount);
        Assert.Equal(0, imuView.ProjectionLoadCount);
        Assert.Equal(0, pitchRollView.ProjectionLoadCount);

        await backgroundTaskRunner.CompleteNextAsync();
        await ViewTestHelpers.FlushDispatcherAsync();
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(1, computer.ComputeCount);
        Assert.Equal(1, imuView.ProjectionLoadCount);
        Assert.Equal(1, pitchRollView.ProjectionLoadCount);
        Assert.NotEmpty(GetPlot(imuView).Plot.PlottableList);
        Assert.NotEmpty(GetPlot(pitchRollView).Plot.PlottableList);

        state.Invalidate(CreateInputs(telemetryGeneration: 2));
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(2, backgroundTaskRunner.RunCount);
        Assert.Equal(1, imuView.ProjectionLoadCount);
        Assert.Equal(1, pitchRollView.ProjectionLoadCount);

        await backgroundTaskRunner.CompleteNextAsync();
        await ViewTestHelpers.FlushDispatcherAsync();
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(2, computer.ComputeCount);
        Assert.Equal(2, imuView.ProjectionLoadCount);
        Assert.Equal(2, pitchRollView.ProjectionLoadCount);

        host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }

    [AvaloniaFact]
    public async Task RecordedImuPlotView_DetachesResultStateSubscriptions_WhenRemovedFromVisualTree()
    {
        ViewTestHelpers.EnsurePlotViewStyle();
        var telemetry = TestTelemetryData.CreateWithImu();
        var inputs = CreateInputs();
        using var state = new TrackingAnalysisResultState(inputs);
        state.Publish(
            inputs.ImuDisplayProjectionKey,
            new ImuDisplayProjectionAnalysisResult(ImuDisplaySignalProcessor.ProcessRecorded(telemetry)));
        var view = new ImuPlotView
        {
            Telemetry = telemetry,
            AnalysisResultState = state,
        };
        var host = new Window
        {
            Width = 900,
            Height = 700,
            Content = view,
        };

        Assert.False(state.HasInputObservers);
        Assert.False(state.HasResultObservers);

        host.Show();
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.True(state.HasInputObservers);
        Assert.True(state.HasResultObservers);

        host.Content = null;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(state.HasInputObservers);
        Assert.False(state.HasResultObservers);

        host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }

    private static RecordedSessionAnalysisInputs CreateInputs(int telemetryGeneration = 1) => new(
        TelemetryGeneration: telemetryGeneration,
        AnalysisRange: null,
        TravelDistributionMode: TravelDistributionMode.ActiveSuspension,
        VelocityAverageMode: VelocityAverageMode.SampleAveraged,
        BalanceDisplacementMode: BalanceDisplacementMode.Zenith,
        BalanceSpeedMode: BalanceSpeedMode.Both,
        DampingSpeedCutoffs: DampingSpeedCutoffs.Default,
        DampingPercentages: SessionDampingPercentages.Empty,
        SessionInsightsTargetProfile: SessionInsightsTargetProfile.Trail);

    private static AvaPlot GetPlot(Control view) => view.GetVisualDescendants().OfType<AvaPlot>().Single();

    private sealed class CountingImuPlotView : ImuPlotView
    {
        public int ProjectionLoadCount { get; private set; }

        protected override void LoadProjection(
            TelemetryPlot plotModel,
            TelemetryData telemetry,
            RecordedImuDisplaySeries projection)
        {
            ProjectionLoadCount++;
            base.LoadProjection(plotModel, telemetry, projection);
        }
    }

    private sealed class CountingFramePitchRollPlotView : FramePitchRollPlotView
    {
        public int ProjectionLoadCount { get; private set; }

        protected override void LoadProjection(
            TelemetryPlot plotModel,
            TelemetryData telemetry,
            RecordedImuDisplaySeries projection)
        {
            ProjectionLoadCount++;
            base.LoadProjection(plotModel, telemetry, projection);
        }
    }

    private sealed class ProjectionAnalysisComputer : IRecordedSessionAnalysisComputer
    {
        public int ComputeCount { get; private set; }

        public RecordedSessionAnalysisResult Compute(RecordedSessionAnalysisKey key, TelemetryData telemetry)
        {
            ComputeCount++;
            Assert.Equal(RecordedSessionAnalysisFamily.ImuDisplayProjection, key.Family);
            return new ImuDisplayProjectionAnalysisResult(ImuDisplaySignalProcessor.ProcessRecorded(telemetry));
        }
    }

    private sealed class ControllableBackgroundTaskRunner : IBackgroundTaskRunner
    {
        private readonly Queue<Func<Task>> completions = new();

        public int RunCount { get; private set; }

        public Task CompleteNextAsync() => completions.Dequeue()();

        public Task RunAsync(Func<Task> work, CancellationToken cancellationToken = default)
        {
            RunCount++;
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            completions.Enqueue(async () =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await work();
                    completion.TrySetResult();
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            });
            return completion.Task;
        }

        public Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken = default)
        {
            RunCount++;
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            completions.Enqueue(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    completion.TrySetResult(work());
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }

                return Task.CompletedTask;
            });
            return completion.Task;
        }

        public Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default)
        {
            RunCount++;
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            completions.Enqueue(async () =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    completion.TrySetResult(await work());
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            });
            return completion.Task;
        }
    }

    private sealed class TrackingAnalysisResultState(RecordedSessionAnalysisInputs inputs)
        : IRecordedSessionAnalysisResultState
    {
        private readonly Subject<RecordedSessionAnalysisInputs> inputChanges = new();
        private readonly Subject<RecordedSessionAnalysisResultChanged> changes = new();
        private readonly Dictionary<RecordedSessionAnalysisKey, RecordedSessionAnalysisResult> results = [];

        public RecordedSessionAnalysisInputs? CurrentInputs { get; private set; } = inputs;
        public bool HasInputObservers => inputChanges.HasObservers;
        public bool HasResultObservers => changes.HasObservers;

        public IObservable<RecordedSessionAnalysisInputs> ConnectInputs() => inputChanges;

        public IObservable<RecordedSessionAnalysisResultChanged> Connect() => changes;

        public RecordedSessionAnalysisResult? Get(RecordedSessionAnalysisKey key) =>
            results.GetValueOrDefault(key);

        public Task RequestAsync(RecordedSessionAnalysisKey key, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void Invalidate(RecordedSessionAnalysisInputs nextInputs)
        {
            CurrentInputs = nextInputs;
            results.Clear();
            inputChanges.OnNext(nextInputs);
        }

        public void Publish(RecordedSessionAnalysisKey key, RecordedSessionAnalysisResult result)
        {
            results[key] = result;
            changes.OnNext(new RecordedSessionAnalysisResultChanged(key, result));
        }

        public void Dispose()
        {
            inputChanges.Dispose();
            changes.Dispose();
        }
    }
}
