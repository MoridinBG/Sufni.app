using System.Linq;
using ScottPlot;
using Sufni.App.Models.Telemetry;

namespace Sufni.App.Plots;

public class LeverageRatioPlot(Plot plot) : SufniPlot(plot)
{
    public void LoadLeverageRatioData(LeverageRatioData data)
    {
        Plot.Layout.Fixed(new PixelPadding(60, 3, 40, 10));
        Plot.Axes.Bottom.Label.Text = "Rear Wheel Travel (mm)";
        Plot.Axes.Left.Label.Text = "Leverage Ratio";
        Plot.Axes.Left.Label.OffsetX = -10;

        if (data.WheelTravel.Count == 0)
        {
            return;
        }

        var lr = Plot.Add.Scatter(data.WheelTravel, data.LeverageRatio);
        lr.MarkerStyle.IsVisible = false;
        lr.LineStyle.Color = Color.FromHex("#ffffbf");
        lr.LineStyle.Width = 2;

        Plot.Axes.SetLimits(data.WheelTravel.Min(), data.WheelTravel.Max(),
            data.LeverageRatio.Min(), data.LeverageRatio.Max());
    }
}