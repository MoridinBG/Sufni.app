using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Bikes.Models;
using Sufni.Telemetry;
using static Sufni.App.Tests.TestSupport.Fixtures.TestTelemetryData;

using Sufni.App.Segments.Models;
using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Plots.Views.Plots;
using Sufni.App.Sessions.Plots;
using Sufni.App.Shared.Plots;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Sessions.Plots.Views.Plots;

[Collection("Ui")]
public class AnalysisPlotViewTests
{
    [AvaloniaFact]
    public async Task AnalysisPlotView_ClearsPlot_WhenStateResultDoesNotMatchPlotKind()
    {
        var telemetry = CreateProcessed();
        var inputs = CreateAnalysisInputs();
        var key = inputs.CreateKey(RecordedSessionAnalysisFamily.TravelDistribution, SuspensionType.Front);
        using var state = new TestAnalysisResultState(inputs);
        var view = new AnalysisPlotView
        {
            AnalysisPlotKind = AnalysisPlotKind.TravelDistribution,
            AnalysisResultState = state,
            SuspensionType = SuspensionType.Front,
            Telemetry = telemetry,
        };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        state.Publish(key, CreateTravelDistributionResult(telemetry, range: null));
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.NotEmpty(GetBars(PlotViewTestSupport.GetRenderedPlot(mounted.View).Plot));

        state.Publish(
            key,
            new TravelFrequencyDistributionAnalysisResult(
                TelemetryStatistics.CalculateTravelFrequencyHistogram(telemetry, SuspensionType.Front),
                HasStrokeData: true));
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Empty(GetBars(PlotViewTestSupport.GetRenderedPlot(mounted.View).Plot));
        Assert.Single(state.Requests);
    }

    [AvaloniaFact]
    public async Task AnalysisPlotView_DefersStateBackedRequestsUntilDemandActive()
    {
        var telemetry = CreateProcessed();
        var initialInputs = CreateAnalysisInputs();
        var nextRange = new TelemetryTimeRange(0, telemetry.Metadata.Duration);
        var nextInputs = initialInputs with { AnalysisRange = nextRange };
        var initialKey = initialInputs.CreateKey(RecordedSessionAnalysisFamily.TravelDistribution, SuspensionType.Front);
        var nextKey = nextInputs.CreateKey(RecordedSessionAnalysisFamily.TravelDistribution, SuspensionType.Front);
        using var state = new TestAnalysisResultState(initialInputs);
        var view = new AnalysisPlotView
        {
            IsAnalysisDemandActive = false,
            AnalysisPlotKind = AnalysisPlotKind.TravelDistribution,
            AnalysisResultState = state,
            SuspensionType = SuspensionType.Front,
            Telemetry = telemetry,
        };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Empty(state.Requests);

        view.IsAnalysisDemandActive = true;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(initialKey, Assert.Single(state.Requests));

        state.Requests.Clear();
        view.IsAnalysisDemandActive = false;
        state.Invalidate(nextInputs);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Empty(state.Requests);

        view.IsAnalysisDemandActive = true;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(nextKey, Assert.Single(state.Requests));
    }

    [AvaloniaFact]
    public async Task AnalysisPlotView_DoesNotRecomputeSynchronously_WhenStateInputsUnavailable()
    {
        var telemetry = CreateProcessed();
        var inputs = CreateAnalysisInputs();
        var key = inputs.CreateKey(RecordedSessionAnalysisFamily.TravelDistribution, SuspensionType.Front);
        using var state = new TestAnalysisResultState(inputs);
        var view = new AnalysisPlotView
        {
            AnalysisPlotKind = AnalysisPlotKind.TravelDistribution,
            AnalysisResultState = state,
            SuspensionType = SuspensionType.Front,
            Telemetry = telemetry,
        };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);
        await ViewTestHelpers.FlushDispatcherAsync();
        state.Publish(key, CreateTravelDistributionResult(telemetry, range: null));
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.NotEmpty(GetBars(PlotViewTestSupport.GetRenderedPlot(mounted.View).Plot));

        // With the result state's inputs unavailable (as during disposal on tab
        // close), a state-backed plot must clear rather than recompute the
        // histogram synchronously from telemetry.
        state.SimulateUnavailable();
        view.AnalysisRange = new TelemetryTimeRange(0.1, 0.5);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Empty(GetBars(PlotViewTestSupport.GetRenderedPlot(mounted.View).Plot));
    }

    private static Bar[] GetBars(Plot plot)
    {
        var bars = new List<Bar>();
        foreach (var plottable in plot.PlottableList)
        {
            if (plottable is Bar bar)
            {
                bars.Add(bar);
                continue;
            }

            if (plottable.GetType().GetProperty("Bars")?.GetValue(plottable) is IEnumerable<Bar> nestedBars)
            {
                bars.AddRange(nestedBars);
            }
        }

        return [.. bars];
    }

    private static TravelDistributionAnalysisResult CreateTravelDistributionResult(
        TelemetryData telemetry,
        TelemetryTimeRange? range)
    {
        var options = new TravelStatisticsOptions(range, TravelDistributionMode.ActiveSuspension);
        return new TravelDistributionAnalysisResult(
            TelemetryStatistics.CalculateTravelHistogram(telemetry, SuspensionType.Front, options),
            TelemetryStatistics.CalculateTravelStatistics(telemetry, SuspensionType.Front, options),
            telemetry.Front.MaxTravel,
            HasStrokeData: true);
    }

    private static RecordedSessionAnalysisInputs CreateAnalysisInputs() =>
        new(
            TelemetryGeneration: 1,
            AnalysisRange: null,
            TravelDistributionMode: TravelDistributionMode.ActiveSuspension,
            VelocityAverageMode: VelocityAverageMode.SampleAveraged,
            BalanceDisplacementMode: BalanceDisplacementMode.Zenith,
            BalanceSpeedMode: BalanceSpeedMode.Both,
            DampingSpeedCutoffs: DampingSpeedCutoffs.Default,
            PlotDampingSpeedCutoffs: DampingSpeedCutoffs.Default,
            SessionInsightsTargetProfile: SessionInsightsTargetProfile.Trail);

    private sealed class TestAnalysisResultState(RecordedSessionAnalysisInputs inputs) : IRecordedSessionAnalysisResultState
    {
        private readonly Subject<RecordedSessionAnalysisInputs> inputChanges = new();
        private readonly Subject<RecordedSessionAnalysisResultChanged> changes = new();
        private readonly Dictionary<RecordedSessionAnalysisKey, RecordedSessionAnalysisResult> results = [];

        public RecordedSessionAnalysisInputs? CurrentInputs { get; private set; } = inputs;
        public List<RecordedSessionAnalysisKey> Requests { get; } = [];

        public IObservable<RecordedSessionAnalysisResultChanged> Connect() => changes;

        public IObservable<RecordedSessionAnalysisInputs> ConnectInputs() => inputChanges;

        public RecordedSessionAnalysisResult? Get(RecordedSessionAnalysisKey key) =>
            results.GetValueOrDefault(key);

        public Task RequestAsync(RecordedSessionAnalysisKey key, CancellationToken cancellationToken = default)
        {
            Requests.Add(key);
            return Task.CompletedTask;
        }

        public void Invalidate(RecordedSessionAnalysisInputs nextInputs)
        {
            CurrentInputs = nextInputs;
            results.Clear();
            inputChanges.OnNext(nextInputs);
        }

        public void SimulateUnavailable() => CurrentInputs = null;

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

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

}
