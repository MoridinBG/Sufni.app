using Avalonia;
using Avalonia.Controls;

namespace Sufni.App.Terrain.Views;

/// <summary>The terrain scope with its selected distance; its flyout picks a preset and, when asked, legend classes.</summary>
public partial class TerrainScopeChipView : UserControl
{
    public static readonly StyledProperty<bool> ShowLegendsProperty =
        AvaloniaProperty.Register<TerrainScopeChipView, bool>(nameof(ShowLegends));

    public bool ShowLegends { get => GetValue(ShowLegendsProperty); set => SetValue(ShowLegendsProperty, value); }

    public TerrainScopeChipView() => InitializeComponent();
}
