using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.Telemetry;
using static Sufni.App.Tests.TestSupport.Fixtures.TestTelemetryData;

using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Plots.Views.Plots;
using Sufni.App.Sessions.Plots;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Sessions.Plots.Views.Plots;

[Collection("Ui")]
public class AnalysisPlotViewTests
{
    [AvaloniaTheory]
    [InlineData(AnalysisPlotKind.TravelDistribution, SuspensionType.Front, null, typeof(TravelDistributionPlot))]
    [InlineData(AnalysisPlotKind.TravelFrequencyDistribution, SuspensionType.Rear, null, typeof(TravelFrequencyDistributionPlot))]
    [InlineData(AnalysisPlotKind.VelocityDistribution, SuspensionType.Front, null, typeof(VelocityDistributionPlot))]
    [InlineData(AnalysisPlotKind.Balance, null, BalanceType.Compression, typeof(BalancePlot))]
    public async Task AnalysisPlotView_UsesExpectedPlotModel_ForPlotKind(
        AnalysisPlotKind plotKind,
        SuspensionType? suspensionType,
        BalanceType? balanceType,
        Type expectedPlotModelType)
    {
        var view = new TestableAnalysisPlotView
        {
            AnalysisPlotKind = plotKind,
        };
        if (suspensionType.HasValue)
        {
            view.SuspensionType = suspensionType.Value;
        }
        if (balanceType.HasValue)
        {
            view.BalanceType = balanceType.Value;
        }

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        Assert.Equal(expectedPlotModelType, mounted.View.PlotModelType);
    }

    [AvaloniaFact]
    public async Task AnalysisPlotView_AnalysisRangeChangeReloadsAnalysisPlot()
    {
        var view = new TestableAnalysisPlotView
        {
            AnalysisPlotKind = AnalysisPlotKind.TravelDistribution,
            SuspensionType = SuspensionType.Front,
        };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        view.Telemetry = CreateMinimal();
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Null(mounted.View.PlotAnalysisRange);

        var range = new TelemetryTimeRange(0.25, 0.75);
        view.AnalysisRange = range;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(range, mounted.View.PlotAnalysisRange);
    }

    [AvaloniaFact]
    public async Task AnalysisPlotView_RendersPublishedStateAnalysisResult()
    {
        var telemetry = CreateProcessed();
        var inputs = CreateAnalysisInputs();
        var key = inputs.CreateKey(RecordedSessionAnalysisFamily.TravelDistribution, SuspensionType.Front);
        using var state = new TestAnalysisResultState(inputs);
        var view = new TestableAnalysisPlotView
        {
            AnalysisPlotKind = AnalysisPlotKind.TravelDistribution,
            AnalysisResultState = state,
            SuspensionType = SuspensionType.Front,
            Telemetry = telemetry,
        };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(key, Assert.Single(state.Requests));
        Assert.Empty(GetBars(PlotViewTestSupport.GetRenderedPlot(mounted.View).Plot));

        state.Publish(key, CreateTravelDistributionResult(telemetry, range: null));
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.NotEmpty(GetBars(PlotViewTestSupport.GetRenderedPlot(mounted.View).Plot));
        Assert.Single(state.Requests);
    }

    [AvaloniaFact]
    public async Task AnalysisPlotView_ClearsPlot_WhenStateResultDoesNotMatchPlotKind()
    {
        var telemetry = CreateProcessed();
        var inputs = CreateAnalysisInputs();
        var key = inputs.CreateKey(RecordedSessionAnalysisFamily.TravelDistribution, SuspensionType.Front);
        using var state = new TestAnalysisResultState(inputs);
        var view = new TestableAnalysisPlotView
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
    public async Task AnalysisPlotView_RequestsNewResult_WhenInputsInvalidateAfterRangeBindingChanged()
    {
        var telemetry = CreateProcessed();
        var initialInputs = CreateAnalysisInputs();
        var range = new TelemetryTimeRange(0, telemetry.Metadata.Duration);
        var nextInputs = initialInputs with { AnalysisRange = range };
        var initialKey = initialInputs.CreateKey(RecordedSessionAnalysisFamily.TravelDistribution, SuspensionType.Front);
        var nextKey = nextInputs.CreateKey(RecordedSessionAnalysisFamily.TravelDistribution, SuspensionType.Front);
        using var state = new TestAnalysisResultState(initialInputs);
        var view = new TestableAnalysisPlotView
        {
            AnalysisPlotKind = AnalysisPlotKind.TravelDistribution,
            AnalysisResultState = state,
            SuspensionType = SuspensionType.Front,
            Telemetry = telemetry,
        };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);
        await ViewTestHelpers.FlushDispatcherAsync();
        state.Publish(initialKey, CreateTravelDistributionResult(telemetry, range: null));
        await ViewTestHelpers.FlushDispatcherAsync();

        view.AnalysisRange = range;
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.DoesNotContain(nextKey, state.Requests);

        state.Invalidate(nextInputs);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Contains(nextKey, state.Requests);
        state.Publish(nextKey, CreateTravelDistributionResult(telemetry, range));
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.NotEmpty(GetBars(PlotViewTestSupport.GetRenderedPlot(mounted.View).Plot));
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
        var view = new TestableAnalysisPlotView
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
    public async Task AnalysisPlotView_DefersReloadToDispatcher_InsteadOfRunningSynchronously()
    {
        var telemetry = CreateProcessed();
        var inputs = CreateAnalysisInputs();
        using var state = new TestAnalysisResultState(inputs);
        var view = new TestableAnalysisPlotView
        {
            AnalysisPlotKind = AnalysisPlotKind.TravelDistribution,
            AnalysisResultState = state,
            SuspensionType = SuspensionType.Front,
            Telemetry = telemetry,
        };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);
        await ViewTestHelpers.FlushDispatcherAsync();
        state.Requests.Clear();

        // A reload triggered by a bound-property change must be applied on a
        // later dispatcher turn, not synchronously on the property-change
        // callback, so teardown-time property reverts cannot recompute inline.
        view.AnalysisRange = new TelemetryTimeRange(0.25, 0.75);
        Assert.Empty(state.Requests);

        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.NotEmpty(state.Requests);
    }

    [AvaloniaFact]
    public async Task AnalysisPlotView_DoesNotRecomputeSynchronously_WhenStateInputsUnavailable()
    {
        var telemetry = CreateProcessed();
        var inputs = CreateAnalysisInputs();
        var key = inputs.CreateKey(RecordedSessionAnalysisFamily.TravelDistribution, SuspensionType.Front);
        using var state = new TestAnalysisResultState(inputs);
        var view = new TestableAnalysisPlotView
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

    [AvaloniaFact]
    public async Task AnalysisPlotView_DoesNotReload_WhenAnalysisResultStateCleared()
    {
        var telemetry = CreateProcessed();
        var inputs = CreateAnalysisInputs();
        var key = inputs.CreateKey(RecordedSessionAnalysisFamily.TravelDistribution, SuspensionType.Front);
        using var state = new TestAnalysisResultState(inputs);
        var view = new TestableAnalysisPlotView
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

        // Clearing the state binding (as the DataContext reverts during tab
        // teardown) must not trigger a reload that clears or recomputes the plot.
        view.AnalysisResultState = null;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.NotEmpty(GetBars(PlotViewTestSupport.GetRenderedPlot(mounted.View).Plot));
    }

    [AvaloniaFact]
    public async Task AnalysisPlotView_UsesAvaloniaTitleAndSuppressesScottPlotTitle()
    {
        var view = new TestableAnalysisPlotView
        {
            AnalysisPlotKind = AnalysisPlotKind.TravelDistribution,
            SuspensionType = SuspensionType.Rear,
            TravelDistributionMode = TravelDistributionMode.DynamicSag,
        };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        Assert.Equal("Rear travel distribution", mounted.View.AnalysisTitle);
        Assert.False(mounted.View.PlotShowsScottPlotTitle);

        view.Telemetry = CreateProcessed();
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Empty(mounted.View.ScottPlotTitle);
    }

    [AvaloniaFact]
    public async Task AnalysisPlotView_AppliesExtensionOverlayDescriptors_WhenContributionsChangeAndTelemetryReloads()
    {
        var slots = new RecordedSessionExtensionSlots();
        var view = new TestableAnalysisPlotView
        {
            AnalysisPlotKind = AnalysisPlotKind.TravelDistribution,
            SuspensionType = SuspensionType.Front,
            ExtensionSlots = slots,
            Telemetry = CreateProcessed(),
        };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);
        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        Assert.Empty(plot.Plot.PlottableList.OfType<HorizontalSpan>());
        Assert.Empty(plot.Plot.PlottableList.OfType<Scatter>());

        slots.AnalysisOverlays.Add(new RecordedSessionAnalysisOverlayContribution(
            "extension",
            "rear-overlay",
            Order: 0,
            RecordedSessionAnalysisPlotTarget.TravelDistribution(SuspensionType.Rear),
            ViewModel: null,
            new RecordedSessionAnalysisPlotOverlayDescriptor(
                [new RecordedSessionPlotLineOverlay(1, 2, 3, 4, CreateOverlayStyle())],
                [new RecordedSessionPlotBandOverlay(5, 6, CreateOverlayStyle())],
                Labels: [])));
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Empty(plot.Plot.PlottableList.OfType<HorizontalSpan>());
        Assert.Empty(plot.Plot.PlottableList.OfType<Scatter>());

        slots.AnalysisOverlays.Add(new RecordedSessionAnalysisOverlayContribution(
            "extension",
            "front-overlay",
            Order: 1,
            RecordedSessionAnalysisPlotTarget.TravelDistribution(SuspensionType.Front),
            ViewModel: null,
            new RecordedSessionAnalysisPlotOverlayDescriptor(
                [new RecordedSessionPlotLineOverlay(2, 12, 8, 18, CreateOverlayStyle(width: 3))],
                [new RecordedSessionPlotBandOverlay(20, 30, CreateOverlayStyle(opacity: 0.35))],
                Labels:
                [
                    new RecordedSessionPlotLabelOverlay(
                        X: 0,
                        Y: 18,
                        "match avg: 18.0 mm",
                        new RecordedSessionPlotLabelStyle(
                            new RecordedSessionMapColor(255, 17, 34, 51),
                            BackgroundColor: null,
                            FontSize: 11,
                            RecordedSessionPlotLabelAnchor.Top),
                        RecordedSessionPlotLabelPlacement.PlotRightEdge)
                ])));
        await ViewTestHelpers.FlushDispatcherAsync();

        AssertAnalysisOverlay(plot);

        view.Telemetry = CreateProcessed();
        await ViewTestHelpers.FlushDispatcherAsync();

        AssertAnalysisOverlay(plot);
    }

    [AvaloniaFact]
    public async Task AnalysisPlotView_ForwardsActiveAnalysisSelectionToSelectablePlot()
    {
        var telemetry = CreateProcessed();
        var selection = CreateFrontDampingSelection(telemetry);
        var view = new TestableAnalysisPlotView
        {
            AnalysisPlotKind = AnalysisPlotKind.VelocityDistribution,
            SuspensionType = SuspensionType.Front,
            Telemetry = telemetry,
        };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        view.ActiveAnalysisSelection = selection;
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        Assert.Contains(GetBars(plot.Plot), bar => bar.LineWidth == 3.0f);
    }

    private static RecordedSessionPlotOverlayStyle CreateOverlayStyle(double width = 2, double opacity = 0.75)
    {
        return new RecordedSessionPlotOverlayStyle(
            new RecordedSessionMapColor(255, 17, 34, 51),
            width,
            opacity);
    }

    private static void AssertAnalysisOverlay(ScottPlot.Avalonia.AvaPlot plot)
    {
        var span = Assert.Single(plot.Plot.PlottableList.OfType<HorizontalSpan>());
        Assert.Equal(20, span.X1, 3);
        Assert.Equal(30, span.X2, 3);
        Assert.True(span.IsVisible);
        Assert.False(span.EnableAutoscale);

        var line = Assert.Single(plot.Plot.PlottableList.OfType<Scatter>());
        Assert.False(line.MarkerStyle.IsVisible);
        Assert.Equal(3, line.LineStyle.Width, 3);
        Assert.Contains(
            "match avg: 18.0 mm",
            plot.Plot.PlottableList.OfType<Text>().SelectMany(PlotTestHelpers.ReadTextLabels));
    }

    private static DampingRangeSelection CreateFrontDampingSelection(TelemetryData telemetry)
    {
        var histogram = TelemetryStatistics.CalculateVelocityHistogram(telemetry, SuspensionType.Front);
        for (var velocityBinIndex = 0; velocityBinIndex < histogram.Values.Count; velocityBinIndex++)
        {
            var travelValues = histogram.Values[velocityBinIndex];
            for (var travelBinIndex = 0; travelBinIndex < travelValues.Length; travelBinIndex++)
            {
                if (travelValues[travelBinIndex] > 0)
                {
                    return new DampingRangeSelection(
                        SuspensionType.Front,
                        VelocityAverageMode.SampleAveraged,
                        velocityBinIndex,
                        travelBinIndex,
                        travelBinIndex);
                }
            }
        }

        Assert.Fail("Expected a non-empty front damping histogram bin.");
        return default!;
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
            DampingPercentages: SessionDampingPercentages.Empty,
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
    }

    private sealed class TestableAnalysisPlotView : AnalysisPlotView
    {
        public Type PlotModelType => PlotModel.GetType();
        public TelemetryTimeRange? PlotAnalysisRange => PlotModel.AnalysisRange;
        public bool PlotShowsScottPlotTitle => PlotModel.ShowTitle;
        public string ScottPlotTitle => PlotControl.Plot.Axes.Title.Label.Text;
    }
}
