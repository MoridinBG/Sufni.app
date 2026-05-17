using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;

namespace Sufni.App.Views.Controls;

public sealed class TelemetryPlotsRoot : UserControl
{
    private readonly ScrollViewer scrollViewer;
    private readonly TelemetryPlotRowsPanel rowsPanel;

    public AvaloniaList<TelemetryPlotRow> Rows { get; } = [];

    public TelemetryPlotsRoot()
    {
        rowsPanel = new TelemetryPlotRowsPanel
        {
            Name = "RowsPanel",
        };
        scrollViewer = new ScrollViewer
        {
            Name = "RowsScrollViewer",
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            IsScrollChainingEnabled = false,
            Content = rowsPanel,
        };
        Content = scrollViewer;

        Rows.CollectionChanged += OnRowsChanged;
        RebuildRows();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        rowsPanel.ViewportHeightOverride = double.IsInfinity(availableSize.Height)
            ? null
            : availableSize.Height;
        return base.MeasureOverride(availableSize);
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildRows();
    }

    private void RebuildRows()
    {
        rowsPanel.Children.Clear();

        for (var i = 0; i < Rows.Count; i++)
        {
            var row = Rows[i];
            rowsPanel.Children.Add(row);

            if (i < Rows.Count - 1)
            {
                rowsPanel.Children.Add(new TelemetryBaseRowDivider
                {
                    TargetRow = row,
                });
            }
        }

        rowsPanel.InvalidateMeasure();
    }
}
