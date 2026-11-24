using Avalonia;
using Avalonia.Controls;

namespace Sufni.App.Sessions.Signals.Views.Controls;

public partial class RecordedSignalRowsView : UserControl
{
    public static readonly StyledProperty<bool> CompactTerrainRowProperty =
        AvaloniaProperty.Register<RecordedSignalRowsView, bool>(nameof(CompactTerrainRow));

    public bool CompactTerrainRow { get => GetValue(CompactTerrainRowProperty); set => SetValue(CompactTerrainRowProperty, value); }

    public static readonly DirectProperty<RecordedSignalRowsView, double> TerrainPlotHeightProperty =
        AvaloniaProperty.RegisterDirect<RecordedSignalRowsView, double>(nameof(TerrainPlotHeight), view => view.TerrainPlotHeight);

    /// <summary>The lanes, plus one legend line per lane unless the compact header holds the legends.</summary>
    public double TerrainPlotHeight => CompactTerrainRow ? 112 : 164;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CompactTerrainRowProperty)
            RaisePropertyChanged(TerrainPlotHeightProperty, change.GetOldValue<bool>() ? 112 : 164, TerrainPlotHeight);
    }

    public static readonly StyledProperty<bool> HideRightAxisProperty =
        AvaloniaProperty.Register<RecordedSignalRowsView, bool>(nameof(HideRightAxis));

    public static readonly StyledProperty<int?> MaximumDisplayHzProperty =
        AvaloniaProperty.Register<RecordedSignalRowsView, int?>(nameof(MaximumDisplayHz));

    public bool HideRightAxis
    {
        get => GetValue(HideRightAxisProperty);
        set => SetValue(HideRightAxisProperty, value);
    }

    public int? MaximumDisplayHz
    {
        get => GetValue(MaximumDisplayHzProperty);
        set => SetValue(MaximumDisplayHzProperty, value);
    }

    public RecordedSignalRowsView()
    {
        InitializeComponent();
    }
}
