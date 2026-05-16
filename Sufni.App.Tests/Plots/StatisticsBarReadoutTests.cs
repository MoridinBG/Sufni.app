using System.Globalization;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Plots;
using Sufni.App.Tests.Infrastructure;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Plots;

public class StatisticsBarReadoutTests
{
    [Fact]
    public void TravelHistogram_SetPointerPositionWithReadout_ShowsClosestBarValue()
    {
        var telemetry = TestTelemetryData.Create(frontPresent: true, rearPresent: true);
        var data = TelemetryStatistics.CalculateTravelHistogram(telemetry, SuspensionType.Front);
        var index = data.Values.FindIndex(value => value > 0);
        var plot = new Plot();
        var sut = new TravelHistogramPlot(plot, SuspensionType.Front);

        sut.LoadTelemetryData(telemetry);
        sut.SetPointerPositionWithReadout(data.Values[index] / 2.0, data.Bins[index]);

        var tooltip = Assert.Single(plot.PlottableList.OfType<Tooltip>());
        Assert.True(tooltip.IsVisible);
        Assert.Contains("Axle position", tooltip.LabelText);
        Assert.Contains(FormatLine("Time", data.Values[index], "%"), tooltip.LabelText);
    }

    [Fact]
    public void StrokeLengthHistogram_SetPointerPositionWithReadout_ShowsClosestVerticalBarValue()
    {
        var telemetry = TestTelemetryData.Create(frontPresent: true, rearPresent: true);
        var data = TelemetryStatistics.CalculateStrokeLengthHistogram(telemetry, SuspensionType.Front, BalanceType.Compression);
        var index = data.Values.FindIndex(value => value > 0);
        var plot = new Plot();
        var sut = new StrokeLengthHistogramPlot(plot, SuspensionType.Front, BalanceType.Compression);

        sut.LoadTelemetryData(telemetry);
        sut.SetPointerPositionWithReadout(data.Bins[index], data.Values[index] / 2.0);

        var tooltip = Assert.Single(plot.PlottableList.OfType<Tooltip>());
        Assert.True(tooltip.IsVisible);
        Assert.Contains("Stroke length", tooltip.LabelText);
        Assert.Contains(FormatLine("Strokes", data.Values[index], "%"), tooltip.LabelText);
    }

    [Fact]
    public void VelocityHistogram_SetPointerPositionWithReadout_ShowsClosestStackedSegmentValue()
    {
        var telemetry = TestTelemetryData.Create(frontPresent: true, rearPresent: true);
        var data = TelemetryStatistics.CalculateVelocityHistogram(telemetry, SuspensionType.Front);
        var (velocityIndex, travelIndex) = FindStackedValue(data);
        var value = data.Values[velocityIndex][travelIndex];
        var valueBase = data.Values[velocityIndex].Take(travelIndex).Sum();
        var plot = new Plot();
        var sut = new VelocityHistogramPlot(plot, SuspensionType.Front);

        sut.LoadTelemetryData(telemetry);
        sut.SetPointerPositionWithReadout(valueBase + value / 2.0, data.Bins[velocityIndex]);

        var tooltip = Assert.Single(plot.PlottableList.OfType<Tooltip>());
        Assert.True(tooltip.IsVisible);
        Assert.Contains("Velocity", tooltip.LabelText);
        Assert.Contains($"Travel: {travelIndex * 10}-{(travelIndex + 1) * 10} %", tooltip.LabelText);
        Assert.Contains(FormatLine("Time", value, "%"), tooltip.LabelText);
    }

    [Fact]
    public void TravelHistogram_SetPointerPositionWithReadout_KeepsTooltipAnchorInsideDataArea()
    {
        var telemetry = TestTelemetryData.Create(frontPresent: true, rearPresent: true);
        var data = TelemetryStatistics.CalculateTravelHistogram(telemetry, SuspensionType.Front);
        var index = data.Values.FindIndex(value => value > 0);
        var plot = new Plot();
        var sut = new TravelHistogramPlot(plot, SuspensionType.Front);

        sut.LoadTelemetryData(telemetry);
        plot.GetSvgXml(500, 320);
        var dataRect = plot.LastRender.DataRect;

        sut.SetPointerPositionWithReadout(data.Values[index] / 2.0, data.Bins[index]);

        var tooltip = Assert.Single(plot.PlottableList.OfType<Tooltip>());
        var labelPixel = plot.GetPixel(tooltip.LabelLocation);
        Assert.InRange(labelPixel.X, Math.Min(dataRect.Left, dataRect.Right), Math.Max(dataRect.Left, dataRect.Right));
        Assert.InRange(labelPixel.Y, Math.Min(dataRect.Top, dataRect.Bottom), Math.Max(dataRect.Top, dataRect.Bottom));
    }

    [Fact]
    public void TravelHistogram_SetPointerPositionWithReadout_PositionsTooltipNearPointer()
    {
        var telemetry = TestTelemetryData.Create(frontPresent: true, rearPresent: true);
        var data = TelemetryStatistics.CalculateTravelHistogram(telemetry, SuspensionType.Front);
        var index = data.Values.FindIndex(value => value > 0);
        var plot = new Plot();
        var sut = new TravelHistogramPlot(plot, SuspensionType.Front);

        sut.LoadTelemetryData(telemetry);
        plot.GetSvgXml(500, 320);
        var dataRect = plot.LastRender.DataRect;
        var pointer = new Coordinates(data.Values[index] / 2.0, data.Bins[index]);
        var pointerPixel = plot.GetPixel(pointer);

        sut.SetPointerPositionWithReadout(pointer.X, pointer.Y);

        var tooltip = Assert.Single(plot.PlottableList.OfType<Tooltip>());
        var labelPixel = plot.GetPixel(tooltip.LabelLocation);
        Assert.True(
            Math.Abs(labelPixel.X - pointerPixel.X) < dataRect.Width / 2.0,
            "The statistics tooltip should follow the pointer instead of pinning to a graph edge.");
    }

    private static (int VelocityIndex, int TravelIndex) FindStackedValue(StackedHistogramData data)
    {
        for (var velocityIndex = 0; velocityIndex < data.Values.Count; velocityIndex++)
        {
            for (var travelIndex = 0; travelIndex < data.Values[velocityIndex].Length; travelIndex++)
            {
                if (data.Values[velocityIndex][travelIndex] > 0)
                {
                    return (velocityIndex, travelIndex);
                }
            }
        }

        throw new InvalidOperationException("Expected a non-empty stacked histogram.");
    }

    private static string FormatLine(string label, double value, string unit)
    {
        var formatted = value.ToString("0.##", CultureInfo.InvariantCulture);
        return $"{label}: {formatted} {unit}";
    }
}
