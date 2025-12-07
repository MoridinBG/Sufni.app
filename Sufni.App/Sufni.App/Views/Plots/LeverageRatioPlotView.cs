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
            if (e.NewValue is null || AvaPlot is null || Plot is null) return;
            
            switch (e.Property.Name)
            {
                case nameof(LeverageRatioData):
                    Plot.Plot.Clear();
                    Plot.LoadLeverageRatioData((CoordinateList)e.NewValue);
                    break;
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