using System.Collections.Generic;
using System.Linq;
using Sufni.App.Theming;

namespace Sufni.App.Plots;

public static class TravelZonePalette
{
    public static IReadOnlyList<string> HexColors { get; } =
        SufniThemes.TravelZoneRamp
            .Select(color => color.ToHexString())
            .ToArray();
}
