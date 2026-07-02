namespace Sufni.App.Shared.Plots;

using ScottPlot;

public static class PlotSettings
{
    // Controls how often live signal plot controls flush queued stream samples to the visible plots.
    public const int LiveSignalRefreshIntervalMs = 33;

    // Controls how often the live session view projects the latest stream/control state into UI-bound properties.
    public const int LiveUiRefreshIntervalMs = 33;

    // Controls the minimum delay between full live-session analysis rebuilds from the accumulated capture.
    public const int LiveAnalysisRefreshIntervalMs = 1500;

    // Caps the display sample rate used by recorded session signal plots on mobile views.
    public const int RecordedMobileMaximumDisplayHz = 100;

    public const float TimeSeriesPlotChromePadding = 40;
    public const float TimeSeriesPlotBottomChromePadding = 24;
    public const float TimeSeriesPlotTopChromePadding = 12;
    public const float MobileSignalOuterBleed = 8;

    public static PixelPadding CreateTimeSeriesPlotPadding(bool reserveRightChrome)
    {
        var rightPadding = reserveRightChrome
            ? TimeSeriesPlotChromePadding
            : MobileSignalOuterBleed;

        return new PixelPadding(
            TimeSeriesPlotChromePadding,
            rightPadding,
            TimeSeriesPlotBottomChromePadding,
            TimeSeriesPlotTopChromePadding);
    }
}
