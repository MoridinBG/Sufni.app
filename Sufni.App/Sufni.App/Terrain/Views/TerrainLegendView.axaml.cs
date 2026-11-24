using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Sufni.App.Terrain.ViewModels;

namespace Sufni.App.Terrain.Views;

/// <summary>One line of toggleable terrain classes with their colour and distance.</summary>
public partial class TerrainLegendView : UserControl
{
    public static readonly StyledProperty<IReadOnlyList<TerrainScopeOption>?> OptionsProperty =
        AvaloniaProperty.Register<TerrainLegendView, IReadOnlyList<TerrainScopeOption>?>(nameof(Options));

    public IReadOnlyList<TerrainScopeOption>? Options { get => GetValue(OptionsProperty); set => SetValue(OptionsProperty, value); }

    public TerrainLegendView() => InitializeComponent();
}
