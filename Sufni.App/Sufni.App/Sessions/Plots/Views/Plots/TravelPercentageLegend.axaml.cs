using Avalonia.Controls;

using Sufni.App.Theming;
using Sufni.App.Infrastructure.Theming;
namespace Sufni.App.Sessions.Plots.Views.Plots;

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
