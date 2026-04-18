using System.Diagnostics;
using Avalonia;
using Sufni.App.Plots;
using Sufni.Kinematics;

namespace Sufni.App.Views.Plots;

public class LeverageRatioPlotView : SufniPlotView
{
    private LeverageRatioPlot? plot;

    public static readonly StyledProperty<CoordinateList?> LeverageRatioDataProperty =
        AvaloniaProperty.Register<LeverageRatioPlotView, CoordinateList?>(nameof(LeverageRatioData));

    public CoordinateList? LeverageRatioData
    {
        get => GetValue(LeverageRatioDataProperty);
        set => SetValue(LeverageRatioDataProperty, value);
    }

    public LeverageRatioPlotView()
    {
        // Populate the ScottPlot plot when the Telemetry property is set.
        PropertyChanged += (_, e) =>
        {
            if (e.Property.Name != nameof(LeverageRatioData))
            {
                return;
            }

            if (!HasPlotControl || plot is null)
            {
                return;
            }

            if (e.NewValue is CoordinateList leverageRatioData)
            {
                plot.Clear();
                plot.LoadLeverageRatioData(leverageRatioData);
            }
            else
            {
                plot.Reset();
            }

            RefreshPlot();
        };
    }

    protected override void CreatePlot()
    {
        plot = new LeverageRatioPlot(PlotControl.Plot);

        if (LeverageRatioData is CoordinateList leverageRatioData)
        {
            plot.LoadLeverageRatioData(leverageRatioData);
        }
        else
        {
            plot.Reset();
        }
    }
}