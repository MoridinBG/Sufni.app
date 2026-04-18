using Avalonia.Controls;
using Avalonia.Media;
using Sufni.App.Plots;

namespace Sufni.App.DesktopViews.Plots;

public partial class TravelPercentageLegend : UserControl
{
    public TravelPercentageLegend()
    {
        InitializeComponent();

        foreach (var color in TravelZonePalette.HexColors)
        {
            PaletteGrid.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.Parse(color))
            });
        }
    }
}