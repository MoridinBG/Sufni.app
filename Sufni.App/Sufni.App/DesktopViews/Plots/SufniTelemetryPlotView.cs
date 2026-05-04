using Avalonia;
using Sufni.App.Models;
using Sufni.App.Plots;
using Sufni.Telemetry;

namespace Sufni.App.DesktopViews.Plots;

public abstract class SufniTelemetryPlotView : SufniTimelinePlotView
{
    private TelemetryPlot? plot;
    private bool hasPendingTelemetryLoad;

    protected TelemetryPlot PlotModel => plot!;
    protected bool HasPlotModel => plot is not null;
    protected override TelemetryPlot? TimelinePlot => plot;
    protected override double? TimelineDurationSeconds => Telemetry?.Metadata.Duration;
    public bool IsPlotReady => plot is not null && HasPlotControl;

    public static readonly StyledProperty<TelemetryData?> TelemetryProperty =
        AvaloniaProperty.Register<SufniTelemetryPlotView, TelemetryData?>(nameof(Telemetry));

    public TelemetryData? Telemetry
    {
        get => GetValue(TelemetryProperty);
        set => SetValue(TelemetryProperty, value);
    }

    public static readonly StyledProperty<int?> MaximumDisplayHzProperty =
        AvaloniaProperty.Register<SufniTelemetryPlotView, int?>(nameof(MaximumDisplayHz));

    public int? MaximumDisplayHz
    {
        get => GetValue(MaximumDisplayHzProperty);
        set => SetValue(MaximumDisplayHzProperty, value);
    }

    public static readonly StyledProperty<PlotSmoothingLevel> SmoothingLevelProperty =
        AvaloniaProperty.Register<SufniTelemetryPlotView, PlotSmoothingLevel>(nameof(SmoothingLevel));

    public PlotSmoothingLevel SmoothingLevel
    {
        get => GetValue(SmoothingLevelProperty);
        set => SetValue(SmoothingLevelProperty, value);
    }

    public static readonly StyledProperty<TelemetryTimeRange?> AnalysisRangeProperty =
        AvaloniaProperty.Register<SufniTelemetryPlotView, TelemetryTimeRange?>(nameof(AnalysisRange));

    public TelemetryTimeRange? AnalysisRange
    {
        get => GetValue(AnalysisRangeProperty);
        set => SetValue(AnalysisRangeProperty, value);
    }

    protected SufniTelemetryPlotView()
    {
        // Populate the ScottPlot plot when the Telemetry property is set.
        PropertyChanged += (_, e) =>
        {
            switch (e.Property.Name)
            {
                case nameof(Telemetry):
                    if (e.NewValue is not TelemetryData telemetryData)
                    {
                        hasPendingTelemetryLoad = false;
                        return;
                    }

                    if (!CanLoadTelemetryNow())
                    {
                        hasPendingTelemetryLoad = true;
                        return;
                    }

                    hasPendingTelemetryLoad = false;
                    LoadTelemetryIntoPlot(telemetryData);
                    break;

                case nameof(IsVisible):
                    TryApplyPendingTelemetryLoad();
                    break;

                case nameof(MaximumDisplayHz):
                case nameof(SmoothingLevel):
                    if (Telemetry is not null)
                    {
                        hasPendingTelemetryLoad = true;
                        TryApplyPendingTelemetryLoad();
                    }
                    break;

                case nameof(AnalysisRange):
                    OnAnalysisRangeChanged();
                    break;
            }

            RefreshPlot();
        };

        // When the plot becomes visible again (tab switch), apply any deferred telemetry load.
        // EffectiveViewportChanged fires when the control's effective visibility changes,
        // including when a parent's IsVisible is toggled.
        EffectiveViewportChanged += (_, _) =>
        {
            TryApplyPendingTelemetryLoad();
        };
    }

    protected void SetPlotModel(TelemetryPlot plotModel)
    {
        plot = plotModel;
        TryApplyPendingTelemetryLoad();
    }

    protected void ReloadTelemetry()
    {
        if (Telemetry is null)
        {
            return;
        }

        if (!CanLoadTelemetryNow())
        {
            hasPendingTelemetryLoad = true;
            return;
        }

        LoadTelemetryIntoPlot(Telemetry);
    }

    public void SetCursorPosition(double position)
    {
        plot?.SetCursorPosition(position);
        RefreshPlot();
    }

    public void SetCursorPositionWithReadout(double position)
    {
        plot?.SetCursorPositionWithReadout(position);
        RefreshPlot();
    }

    public void HideCursorReadout()
    {
        plot?.HideCursorReadout();
        RefreshPlot();
    }

    public void LinkXAxisWith(SufniTelemetryPlotView other)
    {
        if (!IsPlotReady || !other.IsPlotReady)
        {
            return;
        }

        PlotControl.Plot.Axes.Link(other.PlotControl, x: true, y: false);
        other.PlotControl.Plot.Axes.Link(PlotControl, x: true, y: false);
    }

    private void LoadTelemetryIntoPlot(TelemetryData telemetryData)
    {
        if (plot is null || !HasPlotControl)
        {
            return;
        }

        plot.MaximumDisplayHz = MaximumDisplayHz;
        plot.SmoothingLevel = SmoothingLevel;
        plot.AnalysisRange = AnalysisRange;
        plot.Clear();
        plot.LoadTelemetryData(telemetryData);
        ApplyTimelineCursor();
        RefreshPlot();
    }

    protected virtual void OnAnalysisRangeChanged()
    {
        if (Telemetry is null)
        {
            return;
        }

        if (!CanLoadTelemetryNow())
        {
            hasPendingTelemetryLoad = true;
            return;
        }

        LoadTelemetryIntoPlot(Telemetry);
    }

    private bool CanLoadTelemetryNow()
    {
        return plot is not null && HasPlotControl && IsEffectivelyVisible;
    }

    private void TryApplyPendingTelemetryLoad()
    {
        if (!hasPendingTelemetryLoad || !CanLoadTelemetryNow() || Telemetry is not { } data)
        {
            return;
        }

        hasPendingTelemetryLoad = false;
        LoadTelemetryIntoPlot(data);
    }
}
