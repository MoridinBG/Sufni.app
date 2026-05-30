using System;
using System.Collections.Generic;
using System.Linq;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Theming;
using Sufni.Telemetry;

namespace Sufni.App.Plots;

public abstract record RecordedTimeSeriesValues;

public sealed record SampledValues(double[] Samples, int SampleRate) : RecordedTimeSeriesValues;

public sealed record ExplicitValues(double[] XValues, double[] YValues) : RecordedTimeSeriesValues;

public sealed record RecordedTimeSeries(
    string Label,
    string Unit,
    Color Color,
    RecordedTimeSeriesValues Values,
    string Format = "0.##",
    float LineWidth = 2.0f,
    string? SourceKey = null)
{
    public Func<double, string>? CursorValueFormatter { get; init; }
}

public readonly record struct RecordedTimeSeriesValueRange(double Minimum, double Maximum);

public sealed record RecordedTimeSeriesData(
    string Title,
    string EmptyMessage,
    double DurationSeconds,
    IReadOnlyList<RecordedTimeSeries> Series,
    RecordedTimeSeriesValueRange? ValueRange = null,
    TelemetryData? MarkerSource = null,
    IReadOnlyList<RecordedTimeRangeOverlaySetRegistration>? InitialRangeOverlays = null,
    bool ShowLegendWhenSingleSource = false,
    bool EnableInteractiveLegend = false,
    string? InteractiveLegendRowId = null);

public abstract class RecordedTimeSeriesPlot(Plot plot, SufniTheme? theme = null) : TelemetryPlot(plot, theme)
{
    private const double AirtimeLabelAverageCharacterWidthFactor = 0.58;
    private const double AirtimeLabelVisualInsetFraction = 0.08;

    private readonly List<PlottableCursorReadoutSeries> cursorSeries = [];
    private readonly Dictionary<string, RangeOverlayRenderState> rangeOverlayStates = [];
    private double cursorDurationSeconds;

    public VerticalLine? CursorLine { get; protected set; }

    public override void Clear()
    {
        rangeOverlayStates.Clear();
        base.Clear();
    }

    public void SetRangeOverlaySet(string id, RecordedTimeRangeOverlaySet set)
    {
        var state = GetOrCreateRangeOverlayState(id);
        state.Set = set;
        ApplyRangeOverlaySet(state);
    }

    public void ClearRangeOverlaySet(string id)
    {
        if (!rangeOverlayStates.TryGetValue(id, out var state))
        {
            return;
        }

        state.Set = null;
        state.ActiveRangeCount = 0;
        state.ActiveLabelCount = 0;
        foreach (var span in state.Spans)
        {
            span.IsVisible = false;
        }

        foreach (var label in state.Labels)
        {
            label.Text.IsVisible = false;
        }
    }

    public void SetRangeOverlayVisibility(string id, bool isVisible)
    {
        if (!rangeOverlayStates.TryGetValue(id, out var state))
        {
            return;
        }

        state.IsVisible = isVisible;
        for (var index = 0; index < state.Spans.Count; index++)
        {
            state.Spans[index].IsVisible = isVisible && index < state.ActiveRangeCount;
        }

        UpdateRangeOverlayLabelsForVisibility(state);
    }

    public void UpdateRangeOverlayLabelVisibility(
        string id,
        double visibleMinimumSeconds,
        double visibleMaximumSeconds,
        double dataAreaWidthPixels)
    {
        if (!rangeOverlayStates.TryGetValue(id, out var state))
        {
            return;
        }

        if (!state.IsVisible)
        {
            HideRangeOverlayLabels(state);
            return;
        }

        if (state.Set?.LabelOptions is not { } labelOptions)
        {
            return;
        }

        if (state.ActiveLabelCount == 0 || dataAreaWidthPixels <= 0)
        {
            return;
        }

        if (!labelOptions.CullCollisions)
        {
            UpdateRangeOverlayLabelsForVisibility(state);
            return;
        }

        var selected = AirtimeLabelLayout.SelectVisibleLabels(
            state.Labels
                .Take(state.ActiveLabelCount)
                .Select((overlay, index) => new AirtimeLabelLayoutCandidate(
                    index,
                    overlay.CenterSeconds,
                    overlay.WidthPixels,
                    overlay.DurationSeconds))
                .ToArray(),
            visibleMinimumSeconds,
            visibleMaximumSeconds,
            dataAreaWidthPixels);

        for (var i = 0; i < state.ActiveLabelCount; i++)
        {
            state.Labels[i].Text.IsVisible = selected[i];
        }
    }

    protected override void SetCursorLinePosition(double position)
    {
        if (CursorLine is not null)
        {
            CursorLine.Position = position;
        }
    }

    protected override CursorReadout? GetCursorReadout(double position)
    {
        return CreateCursorReadout(
            position,
            cursorDurationSeconds,
            cursorSeries
                .Where(series => series.Plottable.IsVisible)
                .Select(series => series.CursorSeries)
                .ToArray());
    }

    protected void LoadTimeSeries(RecordedTimeSeriesData data)
    {
        ResetCursorReadout();
        CursorLine = null;
        rangeOverlayStates.Clear();
        cursorSeries.Clear();
        cursorDurationSeconds = data.DurationSeconds;

        ConfigureTimeSeriesFrame(data.Title);

        var preparedSeries = data.Series
            .Select(PrepareSeries)
            .Where(series => series is not null)
            .Cast<PreparedTimeSeries>()
            .ToArray();

        if (preparedSeries.Length == 0)
        {
            ShowTimeSeriesEmptyState(data.EmptyMessage, data.DurationSeconds);
            return;
        }

        var interactiveLegendEnabled =
            data.EnableInteractiveLegend &&
            data.InteractiveLegendRowId is not null &&
            SourceVisibility is not null;

        foreach (var series in preparedSeries)
        {
            var plottable = series.AddToPlot(Plot);
            cursorSeries.Add(new PlottableCursorReadoutSeries(plottable, series.CursorSeries));
            if (interactiveLegendEnabled)
            {
                RegisterInteractiveLegendSource(
                    plottable,
                    data.InteractiveLegendRowId!,
                    series.Source.SourceKey ?? series.Source.Label);
            }
        }

        if (preparedSeries.Length > 1 || data.ShowLegendWhenSingleSource)
        {
            ShowSourceLegend();
        }

        if (interactiveLegendEnabled)
        {
            EnableInteractiveSourceLegend();
        }

        var valueRange = data.ValueRange ?? GetValueRange(preparedSeries);
        Plot.Axes.SetLimits(0, data.DurationSeconds, valueRange.Minimum, valueRange.Maximum);
        AddMirroredTimeSeriesAxisRules(0, data.DurationSeconds, valueRange.Minimum, valueRange.Maximum);

        foreach (var registration in data.InitialRangeOverlays ?? [])
        {
            SetRangeOverlaySet(registration.Id, registration.Set);
            SetRangeOverlayVisibility(registration.Id, registration.IsVisible);
        }

        ConfigureSymmetricValueTicks(20);

        if (data.MarkerSource is not null)
        {
            AddMarkerLines(data.MarkerSource);
        }

        CursorLine = AddTimeSeriesCursorLine();
    }

    protected static double GetAirtimeLabelY(RecordedTimeSeriesValueRange? valueRange)
    {
        if (valueRange is not { } range)
        {
            return 0;
        }

        return range.Minimum + (range.Maximum - range.Minimum) * AirtimeLabelVisualInsetFraction;
    }

    private static double EstimateLabelWidthPixels(string content) =>
        content.Length * RecordedTimeRangeOverlayFactory.AirtimeLabelFontSize * AirtimeLabelAverageCharacterWidthFactor;

    private RangeOverlayRenderState GetOrCreateRangeOverlayState(string id)
    {
        if (rangeOverlayStates.TryGetValue(id, out var state))
        {
            return state;
        }

        state = new RangeOverlayRenderState(id);
        rangeOverlayStates.Add(id, state);
        return state;
    }

    private void ApplyRangeOverlaySet(RangeOverlayRenderState state)
    {
        if (state.Set is not { } set)
        {
            ClearRangeOverlaySet(state.Id);
            return;
        }

        for (var index = 0; index < set.Ranges.Count; index++)
        {
            var range = set.Ranges[index];
            var span = index < state.Spans.Count
                ? state.Spans[index]
                : AddRangeOverlaySpan(state);
            ConfigureRangeOverlaySpan(span, range, set.Style, state.IsVisible);
        }

        for (var index = set.Ranges.Count; index < state.Spans.Count; index++)
        {
            state.Spans[index].IsVisible = false;
        }

        state.ActiveRangeCount = set.Ranges.Count;
        ApplyRangeOverlayLabels(state, set);
    }

    private HorizontalSpan AddRangeOverlaySpan(RangeOverlayRenderState state)
    {
        var span = Plot.Add.HorizontalSpan(0, 0);
        span.EnableAutoscale = false;
        span.IsVisible = false;
        state.Spans.Add(span);
        return span;
    }

    private static void ConfigureRangeOverlaySpan(
        HorizontalSpan span,
        RecordedTimeRangeOverlay range,
        RecordedTimeRangeOverlayStyle style,
        bool isVisible)
    {
        span.X1 = range.StartSeconds;
        span.X2 = range.EndSeconds;
        span.FillColor = style.FillColor;
        span.LineStyle.Color = style.OutlineColor;
        span.LineStyle.Width = style.OutlineWidth;
        span.EnableAutoscale = false;
        span.IsVisible = isVisible;
    }

    private void ApplyRangeOverlayLabels(
        RangeOverlayRenderState state,
        RecordedTimeRangeOverlaySet set)
    {
        if (set.LabelOptions is not { } labelOptions)
        {
            state.ActiveLabelCount = 0;
            HideRangeOverlayLabels(state);
            return;
        }

        var labelIndex = 0;
        foreach (var range in set.Ranges)
        {
            if (string.IsNullOrWhiteSpace(range.Label))
            {
                continue;
            }

            var label = labelIndex < state.Labels.Count
                ? state.Labels[labelIndex]
                : AddRangeOverlayLabel(state);
            ConfigureRangeOverlayLabel(label, range, labelOptions, state.IsVisible);
            labelIndex++;
        }

        state.ActiveLabelCount = labelIndex;
        for (var index = labelIndex; index < state.Labels.Count; index++)
        {
            state.Labels[index].Text.IsVisible = false;
        }
    }

    private RangeOverlayLabel AddRangeOverlayLabel(RangeOverlayRenderState state)
    {
        var text = AddLabel(string.Empty, 0, 0, 0, 0, Alignment.LowerCenter);
        text.IsVisible = false;
        var label = new RangeOverlayLabel(text);
        state.Labels.Add(label);
        return label;
    }

    private static void ConfigureRangeOverlayLabel(
        RangeOverlayLabel label,
        RecordedTimeRangeOverlay range,
        RecordedTimeRangeOverlayLabelOptions labelOptions,
        bool isVisible)
    {
        var duration = range.EndSeconds - range.StartSeconds;
        var center = range.StartSeconds + duration / 2;
        label.Text.LabelText = range.Label ?? string.Empty;
        label.Text.Location = new Coordinates(center, labelOptions.Y);
        label.Text.LabelFontSize = (float)labelOptions.FontSize;
        label.Text.IsVisible = isVisible;
        label.CenterSeconds = center;
        label.DurationSeconds = duration;
        label.WidthPixels = EstimateLabelWidthPixels(range.Label ?? string.Empty);
    }

    private static void HideRangeOverlayLabels(RangeOverlayRenderState state)
    {
        foreach (var label in state.Labels)
        {
            label.Text.IsVisible = false;
        }
    }

    private static void UpdateRangeOverlayLabelsForVisibility(RangeOverlayRenderState state)
    {
        if (state.Set?.LabelOptions is null)
        {
            HideRangeOverlayLabels(state);
            return;
        }

        for (var index = 0; index < state.Labels.Count; index++)
        {
            state.Labels[index].Text.IsVisible = state.IsVisible && index < state.ActiveLabelCount;
        }

        if (!state.IsVisible)
        {
            HideRangeOverlayLabels(state);
        }
    }

    private PreparedTimeSeries? PrepareSeries(RecordedTimeSeries series)
    {
        return series.Values switch
        {
            SampledValues sampledValues => PrepareSampledSeries(series, sampledValues),
            ExplicitValues explicitValues => PrepareExplicitSeries(series, explicitValues),
            _ => null
        };
    }

    private PreparedTimeSeries? PrepareSampledSeries(RecordedTimeSeries series, SampledValues values)
    {
        if (values.Samples.Length == 0)
        {
            return null;
        }

        var (displaySamples, step) = PrepareDisplaySignal(values.Samples, values.SampleRate);
        var cursorReadoutSeries = CursorReadoutSeries.FromRegularSamples(
            series.Label,
            series.Unit,
            series.Color,
            displaySamples,
            step,
            cursorDurationSeconds,
            series.Format,
            series.CursorValueFormatter);

        return PreparedTimeSeries.FromSampledValues(series, displaySamples, step, cursorReadoutSeries);
    }

    private PreparedTimeSeries? PrepareExplicitSeries(RecordedTimeSeries series, ExplicitValues values)
    {
        if (values.XValues.Length < 2 || values.YValues.Length < 2)
        {
            return null;
        }

        var count = Math.Min(values.XValues.Length, values.YValues.Length);
        if (count < 2)
        {
            return null;
        }

        var xValues = values.XValues.Length == count ? values.XValues : values.XValues.Take(count).ToArray();
        var sourceYValues = values.YValues.Length == count ? values.YValues : values.YValues.Take(count).ToArray();
        var yValues = TelemetryDisplaySmoothing.ApplyIrregular(xValues, sourceYValues, SmoothingLevel);
        var cursorReadoutSeries = CursorReadoutSeries.FromScatterSamples(
            series.Label,
            series.Unit,
            series.Color,
            xValues,
            yValues,
            series.Format,
            series.CursorValueFormatter);

        return PreparedTimeSeries.FromExplicitValues(series, xValues, yValues, cursorReadoutSeries);
    }

    private static RecordedTimeSeriesValueRange GetValueRange(IReadOnlyList<PreparedTimeSeries> series)
    {
        var minimum = series.Min(item => item.MinimumY);
        var maximum = series.Max(item => item.MaximumY);
        var span = maximum - minimum;
        var padding = Math.Max(Math.Abs(span) * 0.05, 1.0);

        return new RecordedTimeSeriesValueRange(minimum - padding, maximum + padding);
    }

    private sealed class PreparedTimeSeries
    {
        private PreparedTimeSeries(
            RecordedTimeSeries source,
            double[] xValues,
            double[] yValues,
            double? step,
            CursorReadoutSeries cursorSeries)
        {
            Source = source;
            XValues = xValues;
            YValues = yValues;
            Step = step;
            CursorSeries = cursorSeries;
            MinimumY = yValues.Min();
            MaximumY = yValues.Max();
        }

        public RecordedTimeSeries Source { get; }
        public double[] XValues { get; }
        public double[] YValues { get; }
        public double? Step { get; }
        public CursorReadoutSeries CursorSeries { get; }
        public double MinimumY { get; }
        public double MaximumY { get; }

        public static PreparedTimeSeries FromSampledValues(
            RecordedTimeSeries source,
            double[] yValues,
            double step,
            CursorReadoutSeries cursorSeries)
        {
            return new PreparedTimeSeries(source, [], yValues, step, cursorSeries);
        }

        public static PreparedTimeSeries FromExplicitValues(
            RecordedTimeSeries source,
            double[] xValues,
            double[] yValues,
            CursorReadoutSeries cursorSeries)
        {
            return new PreparedTimeSeries(source, xValues, yValues, null, cursorSeries);
        }

        public IPlottable AddToPlot(Plot plot)
        {
            if (Step is { } step)
            {
                var signal = plot.Add.Signal(YValues, step, Source.Color);
                signal.Axes.XAxis = plot.Axes.Bottom;
                signal.Axes.YAxis = plot.Axes.Left;
                signal.LineWidth = Source.LineWidth;
                signal.LegendText = Source.Label;
                return signal;
            }

            var scatter = plot.Add.Scatter(XValues, YValues);
            scatter.Color = Source.Color;
            scatter.LineWidth = Source.LineWidth;
            scatter.LegendText = Source.Label;
            scatter.MarkerStyle.IsVisible = false;
            return scatter;
        }
    }

    private sealed record PlottableCursorReadoutSeries(IPlottable Plottable, CursorReadoutSeries CursorSeries);

    private sealed class RangeOverlayRenderState(string id)
    {
        public string Id { get; } = id;
        public RecordedTimeRangeOverlaySet? Set { get; set; }
        public bool IsVisible { get; set; }
        public int ActiveRangeCount { get; set; }
        public int ActiveLabelCount { get; set; }
        public List<HorizontalSpan> Spans { get; } = [];
        public List<RangeOverlayLabel> Labels { get; } = [];
    }

    private sealed class RangeOverlayLabel(Text text)
    {
        public Text Text { get; } = text;
        public double CenterSeconds { get; set; }
        public double DurationSeconds { get; set; }
        public double WidthPixels { get; set; }
    }
}
