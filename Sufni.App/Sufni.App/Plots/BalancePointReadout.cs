using ScottPlot;

namespace Sufni.App.Plots;

internal sealed class BalancePointReadout : IPointerReadoutTarget
{
    private BalancePointReadout(
        double x,
        double y,
        string sideLabel,
        string xLabel,
        Color color)
    {
        X = x;
        Y = y;
        SideLabel = sideLabel;
        XLabel = xLabel;
        Color = color;
    }

    private double X { get; }
    private double Y { get; }
    private string SideLabel { get; }
    private string XLabel { get; }
    private Color Color { get; }

    public static BalancePointReadout FromPoint(
        double x,
        double y,
        string sideLabel,
        string xLabel,
        Color color)
    {
        return new BalancePointReadout(x, y, sideLabel, xLabel, color);
    }

    public double GetDistanceSquared(Coordinates pointer, Plot plot)
    {
        var point = new Coordinates(X, Y);
        var dataRect = plot.LastRender.DataRect;
        if (dataRect.Width > 0 && dataRect.Height > 0)
        {
            var pointerPixel = plot.GetPixel(pointer);
            var pointPixel = plot.GetPixel(point);
            var dx = pointerPixel.X - pointPixel.X;
            var dy = pointerPixel.Y - pointPixel.Y;
            return dx * dx + dy * dy;
        }

        var dxData = pointer.X - X;
        var dyData = pointer.Y - Y;
        return dxData * dxData + dyData * dyData;
    }

    public CursorReadout ToCursorReadout(Coordinates pointer, Plot plot)
    {
        return new CursorReadout(
            double.NaN,
            X,
            Y,
            [
                new CursorReadoutLine(XLabel, X, "%", Colors.LightGray, "0.#"),
                new CursorReadoutLine($"{SideLabel} peak speed", Y, "mm/s", Color, "0.#"),
            ],
            Header: null,
            KeepTooltipInsideDataArea: true);
    }
}
