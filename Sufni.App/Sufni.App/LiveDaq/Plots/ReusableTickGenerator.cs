using ScottPlot;

namespace Sufni.App.LiveDaq.Plots;

internal sealed class ReusableTickGenerator(ITickGenerator inner) : ITickGenerator
{
    private CoordinateRange cachedRange;
    private float cachedLength;
    private bool cachedVertical;
    private bool hasCachedTicks;

    public Tick[] Ticks => inner.Ticks;

    public int MaxTickCount
    {
        get => inner.MaxTickCount;
        set => inner.MaxTickCount = value;
    }

    public void Regenerate(
        CoordinateRange range,
        Edge edge,
        PixelLength size,
        Paint paint,
        LabelStyle labelStyle)
    {
        var vertical = edge.IsVertical();
        if (hasCachedTicks &&
            cachedRange == range &&
            cachedLength == size.Length &&
            cachedVertical == vertical)
        {
            return;
        }

        inner.Regenerate(range, edge, size, paint, labelStyle);
        cachedRange = range;
        cachedLength = size.Length;
        cachedVertical = vertical;
        hasCachedTicks = true;
    }
}
