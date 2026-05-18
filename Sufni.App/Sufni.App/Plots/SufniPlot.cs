using System;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Theming;

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
    protected Plot Plot { get; }

    protected SufniPlotTheme PlotTheme { get; private set; } = SufniThemes.Fallback.Plot;

    protected enum LabelLinePosition
    {
        Below,
        Above
    }

    protected SufniPlot(Plot plot, SufniTheme? theme = null)
    {
        Plot = plot;
        ApplyTheme(theme ?? SufniThemes.Fallback);
    }

    public Color DefaultFigureBackgroundColor => PlotTheme.Root.Figure.ToScottPlotColor();
    public Color DefaultDataBackgroundColor => PlotTheme.Root.Data.ToScottPlotColor();

    public virtual void ApplyTheme(SufniTheme theme)
    {
        PlotTheme = theme.Plot;

        SetBackgroundColors(
            PlotTheme.Root.Figure.ToScottPlotColor(),
            PlotTheme.Root.Data.ToScottPlotColor());
        Plot.Grid.MajorLineColor = PlotTheme.Grid.Major.ToScottPlotColor();
        Plot.Grid.MinorLineColor = PlotTheme.Grid.Minor.ToScottPlotColor();
        Plot.Axes.Color(PlotTheme.Axis.Line.ToScottPlotColor());

        Plot.Axes.Title.Label.FontSize = 14;
        Plot.Axes.Title.Label.OffsetY = -5;
        Plot.Axes.Title.Label.ForeColor = PlotTheme.Axis.Label.ToScottPlotColor();

        Plot.Axes.Left.Label.ForeColor = PlotTheme.Axis.Label.ToScottPlotColor();
        Plot.Axes.Left.Label.Bold = false;
        Plot.Axes.Left.Label.FontSize = 14;

        Plot.Axes.Left.TickLabelStyle.ForeColor = PlotTheme.Axis.Tick.ToScottPlotColor();
        Plot.Axes.Left.TickLabelStyle.Bold = false;
        Plot.Axes.Left.TickLabelStyle.FontSize = 12;
        Plot.Axes.Left.MajorTickStyle.Length = 0;
        Plot.Axes.Left.MinorTickStyle.Length = 0;
        Plot.Axes.Left.MajorTickStyle.Width = 0;
        Plot.Axes.Left.MinorTickStyle.Width = 0;

        Plot.Axes.Bottom.Label.ForeColor = PlotTheme.Axis.Label.ToScottPlotColor();
        Plot.Axes.Bottom.Label.Bold = false;
        Plot.Axes.Bottom.Label.FontSize = 14;

        Plot.Axes.Bottom.TickLabelStyle.ForeColor = PlotTheme.Axis.Tick.ToScottPlotColor();
        Plot.Axes.Bottom.TickLabelStyle.Bold = false;
        Plot.Axes.Bottom.TickLabelStyle.FontSize = 12;
        Plot.Axes.Bottom.MajorTickStyle.Length = 0;
        Plot.Axes.Bottom.MinorTickStyle.Length = 0;
        Plot.Axes.Bottom.MajorTickStyle.Width = 0;
        Plot.Axes.Bottom.MinorTickStyle.Width = 0;
        Plot.Axes.Bottom.TickLabelStyle.OffsetY = 5;
    }

    public virtual void Clear()
    {
        Plot.Clear();
        Plot.Axes.Rules.Clear();
    }

    public void SetBackgroundColors(Color figureBackground, Color dataBackground)
    {
        Plot.FigureBackground.Color = figureBackground;
        Plot.DataBackground.Color = dataBackground;
    }

    public string GetSvgXml(int width, int height) => Plot.GetSvgXml(width, height);

    protected void SetAxisLabels(string bottom, string left)
    {
        Plot.Axes.Bottom.Label.Text = bottom;
        Plot.Axes.Left.Label.Text = left;
    }

    protected void AddLabel(string content, double x, double y, int xoffset, int yoffset, Alignment alignment = Alignment.LowerLeft)
    {
        var text = Plot.Add.Text(content, x, y);
        text.LabelFontColor = PlotTheme.InPlotLabelText.ToScottPlotColor();
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

        var limits = Plot.Axes.GetLimits();
        var xInset = Math.Abs(limits.Right - limits.Left) * 0.015;
        var text = Plot.Add.Text(content, limits.Right - xInset, position);
        text.LabelFontColor = PlotTheme.InPlotLabelText.ToScottPlotColor();
        text.LabelFontSize = 13;
        text.LabelAlignment = linePosition == LabelLinePosition.Above ? Alignment.UpperRight : Alignment.LowerRight;
        text.LabelOffsetX = -10;
        text.LabelOffsetY = yoffset;

        //XXX: Workaround for https://github.com/ScottPlot/ScottPlot/issues/4650
        //Plot.Add.HorizontalLine(position, 1f, PlotTheme.ReferenceLine.ToScottPlotColor(), LinePattern.Dotted);
        FixedHorizontalLine line = new()
        {
            LineWidth = 1f,
            LineColor = PlotTheme.ReferenceLine.ToScottPlotColor(),
            LinePattern = LinePattern.DenselyDashed,
            Y = position
        };
        Plot.PlottableList.Add(line);
    }
}
