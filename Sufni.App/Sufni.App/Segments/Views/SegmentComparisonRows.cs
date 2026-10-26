using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
using Sufni.App.Segments.ViewModels;
using Sufni.App.Shared.Views.Controls;
using Sufni.App.Shared.Views.Overlays;

namespace Sufni.App.Segments.Views;

/// <summary>
/// Shows segment comparison rows as the first, collapsed child rows of a signal
/// row. The rows have no row id, so the layout preferences do not store them.
/// </summary>
public static class SegmentComparisonRows
{
    public static readonly AttachedProperty<IReadOnlyList<SegmentComparisonRowViewModel>?> RowsProperty =
        AvaloniaProperty.RegisterAttached<SignalRow, IReadOnlyList<SegmentComparisonRowViewModel>?>(
            "Rows",
            typeof(SegmentComparisonRows));

    public static readonly AttachedProperty<IRecordedSessionTimeline?> TimelineProperty =
        AvaloniaProperty.RegisterAttached<SignalRow, IRecordedSessionTimeline?>(
            "Timeline",
            typeof(SegmentComparisonRows));

    static SegmentComparisonRows()
    {
        RowsProperty.Changed.AddClassHandler<SignalRow>((row, _) => UpdateRows(row));
        TimelineProperty.Changed.AddClassHandler<SignalRow>((row, _) => UpdateTimeline(row));
    }

    public static void SetRows(SignalRow row, IReadOnlyList<SegmentComparisonRowViewModel>? value) =>
        row.SetValue(RowsProperty, value);

    public static IReadOnlyList<SegmentComparisonRowViewModel>? GetRows(SignalRow row) =>
        row.GetValue(RowsProperty);

    public static void SetTimeline(SignalRow row, IRecordedSessionTimeline? value) =>
        row.SetValue(TimelineProperty, value);

    public static IRecordedSessionTimeline? GetTimeline(SignalRow row) =>
        row.GetValue(TimelineProperty);

    private static void UpdateRows(SignalRow row)
    {
        var existing = row.ChildRows
            .Where(child => child.PlotContent is SegmentComparisonPlotView)
            .ToDictionary(child => ((SegmentComparisonPlotView)child.PlotContent!).DataContext!);
        foreach (var child in existing.Values)
        {
            row.ChildRows.Remove(child);
        }

        var index = 0;
        foreach (var comparisonRow in GetRows(row) ?? [])
        {
            var child = existing.TryGetValue(comparisonRow, out var kept)
                ? kept
                : CreateRow(comparisonRow, GetTimeline(row));
            row.ChildRows.Insert(index++, child);
        }
    }

    private static void UpdateTimeline(SignalRow row)
    {
        foreach (var child in row.ChildRows)
        {
            if (child.PlotContent is SegmentComparisonPlotView plotView)
            {
                plotView.Timeline = GetTimeline(row);
            }
        }
    }

    private static SignalRow CreateRow(SegmentComparisonRowViewModel comparisonRow, IRecordedSessionTimeline? timeline) =>
        new()
        {
            Title = comparisonRow.Title,
            TitleToolTip = SegmentComparisonRowViewModel.MatchErrorToolTip,
            HeaderActions = comparisonRow.HeaderActions,
            PlotContent = new SegmentComparisonPlotView { DataContext = comparisonRow, Timeline = timeline },
            PlaceholderContent = new SurfacePlaceholderCard { Title = comparisonRow.Title },
            IsExpanded = false,
        };
}
