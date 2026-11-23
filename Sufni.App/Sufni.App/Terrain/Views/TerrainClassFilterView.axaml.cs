using Avalonia;
using Avalonia.Controls;

namespace Sufni.App.Terrain.Views;

public partial class TerrainClassFilterView : UserControl
{
    public static readonly StyledProperty<bool> CompactProperty =
        AvaloniaProperty.Register<TerrainClassFilterView, bool>(nameof(Compact));
    public bool Compact { get => GetValue(CompactProperty); set => SetValue(CompactProperty, value); }

    public TerrainClassFilterView() => InitializeComponent();
}
