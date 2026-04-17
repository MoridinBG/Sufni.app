using System.Linq;
using ScottPlot;
using ScottPlot.TickGenerators;
using Sufni.Kinematics;

namespace Sufni.App.Plots;

public class LeverageRatioPlot(Plot plot) : SufniPlot(plot)
{
    public void Reset()
    {
        Plot.Clear();
        Plot.Axes.Title.Label.Text = string.Empty;
        Plot.Axes.SetLimits(0, 1, 0, 1);
    }

    public void LoadLeverageRatioData(CoordinateList leverageRatioData)
    {
        Plot.Axes.Title.Label.Text = "Leverage ratio";
        Plot.Layout.Fixed(new PixelPadding(30, 10, 40, 40));

        var travelMax = leverageRatioData.X[^1];
        var leverageRatioMin = leverageRatioData.Y.Min() * 0.98;
        var leverageRatioMax = leverageRatioData.Y.Max() * 1.02;
        Plot.Axes.SetLimits(0, travelMax, leverageRatioMin, leverageRatioMax);
        Plot.Axes.Bottom.TickGenerator = new NumericFixedInterval(20);

        var scatter = Plot.Add.Scatter(leverageRatioData.X, leverageRatioData.Y);
        scatter.LineWidth = 2.0f;
        scatter.LineStyle.Color = Color.FromHex("#ffffbf"); // Spectral11 #5
        scatter.MarkerStyle.IsVisible = false;
    }
}