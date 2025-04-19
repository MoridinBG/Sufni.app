using System.Diagnostics;
using Avalonia;
using Sufni.App.Models.Telemetry;
using Sufni.App.Plots;

namespace Sufni.App.Views.Plots;

public class LeverageRatioPlotView : SufniPlotView
{
    private LeverageRatioPlot? plot;
    
    #region Styled properties

    public static readonly StyledProperty<LeverageRatioData> LevarageRatioDataProperty =
        AvaloniaProperty.Register<LeverageRatioPlotView, LeverageRatioData>("LevarageRatioData");

    public LeverageRatioData LevarageRatioData
    {
        get => GetValue(LevarageRatioDataProperty);
        set => SetValue(LevarageRatioDataProperty, value);
    }

    #endregion
    
    public LeverageRatioPlotView()
    {
        PropertyChanged += (_, e) =>
        {
            if (e.NewValue is null || AvaPlot is null || plot is null) return;
            
            switch (e.Property.Name)
            {
                case nameof(LevarageRatioData):
                    AvaPlot.Plot.Clear();
                    plot.LoadLeverageRatioData((LeverageRatioData)e.NewValue);
                    break;
            }

            AvaPlot.Refresh();
        };
    }

    protected override void CreatePlot()
    {
        Debug.Assert(AvaPlot != null, nameof(AvaPlot) + " != null");
        plot = new LeverageRatioPlot(AvaPlot.Plot);
    }
}