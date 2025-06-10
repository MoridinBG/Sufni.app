using Avalonia.Controls;

namespace Sufni.App.DesktopViews.Controls;

public partial class DeletableListItemButton : UserControl
{
    public DeletableListItemButton()
    {
        InitializeComponent();

        PropertyChanged += (_, e) =>
        {
            if (e.Property.Name != nameof(IsEnabled)) return;

            var css = IsEnabled ? ".default { fill: #bf312d; }" : ".default { fill: #f0f0f0; }";
            DeleteButton.SetCurrentValue(Avalonia.Svg.Skia.Svg.CssProperty, css);
        };
    }
}