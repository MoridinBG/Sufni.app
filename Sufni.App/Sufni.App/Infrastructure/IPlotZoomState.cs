namespace Sufni.App.Infrastructure;

public interface IPlotZoomSurface
{
    bool IsZoomOpen { get; }
    bool TryCollapseZoom();
}

public interface IPlotZoomState
{
    void SetSurface(IPlotZoomSurface? surface);
    bool TryCollapse();
}
