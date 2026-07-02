namespace Sufni.App.Infrastructure;

public sealed class PlotZoomState : IPlotZoomState
{
    private IPlotZoomSurface? surface;

    public void SetSurface(IPlotZoomSurface? surface)
    {
        this.surface = surface;
    }

    public bool TryCollapse() => surface?.TryCollapseZoom() == true;
}
