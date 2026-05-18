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
    float LineWidth = 2.0f);

public readonly record struct RecordedTimeSeriesValueRange(double Minimum, double Maximum);

public sealed record RecordedTimeSeriesData(
    string Title,
    string EmptyMessage,
    double DurationSeconds,
    IReadOnlyList<RecordedTimeSeries> Series,
    RecordedTimeSeriesValueRange? ValueRange = null,
    TelemetryData? MarkerSource = null,
    bool ShowLegendWhenSingleSource = false);

public abstract class RecordedTimeSeriesPlot(Plot plot, SufniTheme? theme = null) : TelemetryPlot(plot, theme)
{
    private readonly List<CursorReadoutSeries> cursorSeries = [];
    private HorizontalSpan? selectedSpan;
    private HorizontalSpan? previewSpan;
    private double cursorDurationSeconds;

    public VerticalLine? CursorLine { get; protected set; }

    public void SetAnalysisRange(TelemetryTimeRange? range)
    {
        selectedSpan = SetSpan(selectedSpan, range?.StartSeconds, range?.EndSeconds,
            PlotTheme.AnalysisRange.SelectedFill.ToScottPlotColor());
    }

    public void SetPreviewRange(double? startSeconds, double? endSeconds)
    {
        previewSpan = SetSpan(previewSpan, startSeconds, endSeconds,
            PlotTheme.AnalysisRange.PreviewFill.ToScottPlotColor());
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
        return CreateCursorReadout(position, cursorDurationSeconds, cursorSeries);
    }

    protected void LoadTimeSeries(RecordedTimeSeriesData data)
    {
        ResetCursorReadout();
        CursorLine = null;
        selectedSpan = null;
        previewSpan = null;
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

        foreach (var series in preparedSeries)
        {
            cursorSeries.Add(series.CursorSeries);
            series.AddToPlot(Plot);
        }

        if (preparedSeries.Length > 1 || data.ShowLegendWhenSingleSource)
        {
            ShowSourceLegend();
        }

        var valueRange = data.ValueRange ?? GetValueRange(preparedSeries);
        Plot.Axes.SetLimits(0, data.DurationSeconds, valueRange.Minimum, valueRange.Maximum);
        AddMirroredTimeSeriesAxisRules(0, data.DurationSeconds, valueRange.Minimum, valueRange.Maximum);

        AddTimeSeriesOverlays(data);

        SetAnalysisRange(AnalysisRange);
        SetPreviewRange(null, null);

        ConfigureSymmetricValueTicks(20);

        if (data.MarkerSource is not null)
        {
            AddMarkerLines(data.MarkerSource);
        }

        CursorLine = AddTimeSeriesCursorLine();
    }

    protected virtual void AddTimeSeriesOverlays(RecordedTimeSeriesData data)
    {
    }

    private HorizontalSpan? SetSpan(HorizontalSpan? span, double? startSeconds, double? endSeconds, Color color)
    {
        if (startSeconds is null || endSeconds is null)
        {
            if (span is not null)
            {
                span.IsVisible = false;
            }

            return span;
        }

        var start = Math.Min(startSeconds.Value, endSeconds.Value);
        var end = Math.Max(startSeconds.Value, endSeconds.Value);
        if (span is null)
        {
            span = Plot.Add.HorizontalSpan(start, end);
            span.FillColor = color;
            span.LineStyle.Width = 0;
            span.EnableAutoscale = false;
        }
        else
        {
            span.X1 = start;
            span.X2 = end;
            span.FillColor = color;
        }

        span.IsVisible = true;
        return span;
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
            series.Format);

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
            series.Format);

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

        public void AddToPlot(Plot plot)
        {
            if (Step is { } step)
            {
                var signal = plot.Add.Signal(YValues, step, Source.Color);
                signal.Axes.XAxis = plot.Axes.Bottom;
                signal.Axes.YAxis = plot.Axes.Left;
                signal.LineWidth = Source.LineWidth;
                signal.LegendText = Source.Label;
                return;
            }

            var scatter = plot.Add.Scatter(XValues, YValues);
            scatter.Color = Source.Color;
            scatter.LineWidth = Source.LineWidth;
            scatter.LegendText = Source.Label;
            scatter.MarkerStyle.IsVisible = false;
        }
    }
}
