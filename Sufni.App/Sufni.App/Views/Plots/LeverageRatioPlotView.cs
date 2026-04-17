using System.Diagnostics;
using Avalonia;
using Sufni.App.Plots;
using Sufni.Kinematics;

namespace Sufni.App.Views.Plots;

public class LeverageRatioPlotView : SufniPlotView
{
    public LeverageRatioPlot? Plot;

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

            if (AvaPlot is null || Plot is null)
            {
                return;
            }

            Plot.Plot.Clear();
            if (e.NewValue is CoordinateList leverageRatioData)
            {
                Plot.LoadLeverageRatioData(leverageRatioData);
            }
            else
            {
                Plot.Plot.Axes.AutoScale();
            }

            AvaPlot.Refresh();
        };
    }

    protected override void CreatePlot()
    {
        Debug.Assert(AvaPlot != null);
        Plot = new LeverageRatioPlot(AvaPlot.Plot);
    }
}