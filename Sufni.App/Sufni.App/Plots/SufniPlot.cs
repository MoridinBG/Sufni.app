using System;
using ScottPlot;
using ScottPlot.Plottables;

namespace Sufni.App.Plots;

//XXX Workaround for https://github.com/ScottPlot/ScottPlot/issues/4650
public class FixedHorizontalLine : HorizontalLine
{
    public override void Render(RenderPack rp)
    {
        if (!IsVisible) // || !Axes.YAxis.Range.Contains(Y))
            return;

        Coordinates pt1 = new(Math.Max(Minimum, Axes.XAxis.Min), Y);
        Coordinates pt2 = new(Math.Min(Maximum, Axes.XAxis.Max), Y);
        CoordinateLine line = new(pt1, pt2);
        var pxLine = Axes.GetPixelLine(line);
        LineStyle.Render(rp.Canvas, pxLine, rp.Paint);
    }
}

public class SufniPlot
{
    public Plot Plot;

    protected enum LabelLinePosition
    {
        Below,
        Above
    }

    protected SufniPlot(Plot plot)
    {
        Plot = plot;

        Plot.FigureBackground.Color = Color.FromHex("#15191C");
        Plot.DataBackground.Color = Color.FromHex("#20262B");
        Plot.Grid.MajorLineColor = Color.FromHex("#505558");
        Plot.Grid.MinorLineColor = Color.FromHex("#505558");
        Plot.Axes.Color(Color.FromHex("#505558"));

        Plot.Axes.Title.Label.FontSize = 14;
        Plot.Axes.Title.Label.OffsetY = 5;
        Plot.Axes.Title.Label.ForeColor = Color.FromHex("#D0D0D0");

        Plot.Axes.Left.Label.ForeColor = Color.FromHex("#D0D0D0");
        Plot.Axes.Left.Label.Bold = false;
        Plot.Axes.Left.Label.FontSize = 14;

        Plot.Axes.Left.TickLabelStyle.ForeColor = Color.FromHex("#D0D0D0");
        Plot.Axes.Left.TickLabelStyle.Bold = false;
        Plot.Axes.Left.TickLabelStyle.FontSize = 12;
        Plot.Axes.Left.MajorTickStyle.Length = 0;
        Plot.Axes.Left.MinorTickStyle.Length = 0;
        Plot.Axes.Left.MajorTickStyle.Width = 0;
        Plot.Axes.Left.MinorTickStyle.Width = 0;

        Plot.Axes.Bottom.Label.ForeColor = Color.FromHex("#D0D0D0");
        Plot.Axes.Bottom.Label.Bold = false;
        Plot.Axes.Bottom.Label.FontSize = 14;

        Plot.Axes.Bottom.TickLabelStyle.ForeColor = Color.FromHex("#D0D0D0");
        Plot.Axes.Bottom.TickLabelStyle.Bold = false;
        Plot.Axes.Bottom.TickLabelStyle.FontSize = 12;
        Plot.Axes.Bottom.MajorTickStyle.Length = 0;
        Plot.Axes.Bottom.MinorTickStyle.Length = 0;
        Plot.Axes.Bottom.MajorTickStyle.Width = 0;
        Plot.Axes.Bottom.MinorTickStyle.Width = 0;
    }

    protected void AddLabel(string content, double x, double y, int xoffset, int yoffset, Alignment alignment = Alignment.LowerLeft)
    {
        var text = Plot.Add.Text(content, x, y);
        text.LabelFontColor = Color.FromHex("#fefefe");
        text.LabelFontSize = 13;
        text.LabelAlignment = alignment;
        text.LabelOffsetX = xoffset;
        text.LabelOffsetY = yoffset;
    }

    protected void AddLabelWithHorizontalLine(string content, double position, LabelLinePosition linePosition)
    {
        var yoffset = linePosition switch
        {
            LabelLinePosition.Above => 5,
            LabelLinePosition.Below => -5,
            _ => 0
        };

        var text = Plot.Add.Text(content, Plot.Axes.GetLimits().Right, position);
        text.LabelFontColor = Color.FromHex("#fefefe");
        text.LabelFontSize = 13;
        text.LabelAlignment = linePosition == LabelLinePosition.Above ? Alignment.UpperRight : Alignment.LowerRight;
        text.LabelOffsetX = -10;
        text.LabelOffsetY = yoffset;

        //XXX: Workaround for https://github.com/ScottPlot/ScottPlot/issues/4650
        //Plot.Add.HorizontalLine(position, 1f, Color.FromHex("#dddddd"), LinePattern.Dotted);
        FixedHorizontalLine line = new()
        {
            LineWidth = 1f,
            LineColor = Color.FromHex("#dddddd"),
            LinePattern = LinePattern.DenselyDashed,
            Y = position
        };
        Plot.PlottableList.Add(line);
    }
}