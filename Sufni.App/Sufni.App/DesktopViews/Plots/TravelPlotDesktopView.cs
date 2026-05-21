using System;
using Sufni.App.Plots;

namespace Sufni.App.DesktopViews.Plots;

public class TravelPlotDesktopView : SufniTelemetryPlotView
{
    private TravelPlot? travelPlot;

    protected override void CreatePlot()
    {
        travelPlot = new TravelPlot(PlotControl.Plot, CurrentTheme);
        SetPlotModel(travelPlot);
        InitializeCursorReadoutInteractions();
    }

    protected override void OnViewportChanged()
    {
        base.OnViewportChanged();
        UpdateAirtimeLabelVisibility(refresh: true);
    }

    protected override void OnPlotDataLoaded()
    {
        UpdateAirtimeLabelVisibility(refresh: false);
    }

    private void UpdateAirtimeLabelVisibility(bool refresh)
    {
        if (travelPlot is null || !HasPlotControl)
        {
            return;
        }

        var limits = PlotControl.Plot.Axes.GetLimits();
        var dataRect = PlotControl.Plot.LastRender.DataRect;
        double dataAreaWidthPixels = Math.Abs(dataRect.Right - dataRect.Left);
        if (dataAreaWidthPixels <= 0)
        {
            dataAreaWidthPixels = PlotControl.Bounds.Width;
        }

        travelPlot.UpdateAirtimeLabelVisibility(
            limits.Left,
            limits.Right,
            dataAreaWidthPixels);
        if (refresh)
        {
            RefreshPlot();
        }
    }
}
