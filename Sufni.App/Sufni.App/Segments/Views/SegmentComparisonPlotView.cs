using System.ComponentModel;
using Sufni.App.Segments.Plots;
using Sufni.App.Segments.ViewModels;
using Sufni.App.Shared.Plots;
using Sufni.App.Shared.Views.Plots;

namespace Sufni.App.Segments.Views;

/// <summary>
/// Plot of a <see cref="SegmentComparisonRowViewModel"/> data context. It has no
/// signals workspace, so analysis-range selection and the context menu stay inactive.
/// </summary>
public sealed class SegmentComparisonPlotView : SufniTimeSeriesPlotView
{
    private SegmentComparisonRowViewModel? row;

    public SegmentComparisonPlotView()
    {
        DataContextChanged += (_, _) => BindRow(DataContext as SegmentComparisonRowViewModel);
    }

    protected override double? TimelineDurationSeconds => row?.Comparison.SourceDurationSeconds;
    protected override bool CanLoadPlotData => row is { Series.Count: > 0 };

    protected override void CreatePlot()
    {
        SetPlotModel(new SegmentComparisonPlot(PlotControl.Plot, CurrentTheme));
        InitializeCursorReadoutInteractions();
    }

    protected override void LoadPlotData(TelemetryPlot plotModel)
    {
        if (plotModel is SegmentComparisonPlot plot && row is not null)
        {
            plot.LoadRow(row);
        }
    }

    private void BindRow(SegmentComparisonRowViewModel? next)
    {
        if (ReferenceEquals(row, next))
        {
            return;
        }

        if (row is not null)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
        }

        row = next;
        if (row is not null)
        {
            row.PropertyChanged += OnRowPropertyChanged;
        }

        ShowAirtime = row?.ShowAirtime ?? false;
        RequestReload();
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SegmentComparisonRowViewModel.ShowAirtime))
        {
            ShowAirtime = row?.ShowAirtime ?? false;
        }
    }
}
