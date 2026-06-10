using Avalonia.Controls;
using Sufni.App.Theming;

namespace Sufni.App.DesktopViews.Plots;

public partial class TravelPercentageLegend : UserControl
{
    public TravelPercentageLegend()
    {
        InitializeComponent();

        foreach (var color in SufniThemes.TravelZoneRamp)
        {
            PaletteGrid.Children.Add(new Border
            {
                Background = color.ToBrush()
            });
        }
    }
}
