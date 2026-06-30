using System.Diagnostics;
using Avalonia;
using Sufni.Kinematics;

using Sufni.App.Bikes.Plots;
using Sufni.App.Shared.Views.Plots;
using Sufni.App.Theming;
namespace Sufni.App.Bikes.Views.Plots;

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
        plot = new LeverageRatioPlot(PlotControl.Plot, CurrentTheme);

        if (LeverageRatioData is CoordinateList leverageRatioData)
        {
            plot.LoadLeverageRatioData(leverageRatioData);
        }
        else
        {
            plot.Reset();
        }
    }

    protected override void OnThemeChanged(SufniTheme theme)
    {
        if (plot is null)
        {
            return;
        }

        plot.ApplyTheme(theme);
        plot.Clear();
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
