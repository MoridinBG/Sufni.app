using ScottPlot;

namespace Sufni.App.Shared.Plots;

internal interface IPointerReadoutTarget
{
    double GetDistanceSquared(Coordinates pointer, Plot plot);

    CursorReadout ToCursorReadout(Coordinates pointer, Plot plot);
}
