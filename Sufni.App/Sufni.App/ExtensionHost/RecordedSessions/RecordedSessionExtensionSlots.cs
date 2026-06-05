using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace Sufni.App.ExtensionHost.RecordedSessions;

public sealed class RecordedSessionExtensionSlots
{
    public ObservableCollection<RecordedSessionToolbarContribution> GraphToolbarActions { get; } = CreateCollection<RecordedSessionToolbarContribution>();
    public ObservableCollection<RecordedSessionPageContribution> Pages { get; } = CreateCollection<RecordedSessionPageContribution>();
    public ObservableCollection<RecordedSessionMediaPaneContribution> MediaPanes { get; } = CreateCollection<RecordedSessionMediaPaneContribution>();
    public ObservableCollection<RecordedSessionMapOverlayContribution> MapOverlays { get; } = CreateCollection<RecordedSessionMapOverlayContribution>();
    public ObservableCollection<RecordedSessionStatisticsBannerContribution> StatisticsBanners { get; } = CreateCollection<RecordedSessionStatisticsBannerContribution>();
    public ObservableCollection<RecordedSessionStatisticsOverlayContribution> StatisticsOverlays { get; } = CreateCollection<RecordedSessionStatisticsOverlayContribution>();
    public ObservableCollection<RecordedSessionStatisticsMetricContribution> StatisticsMetrics { get; } = CreateCollection<RecordedSessionStatisticsMetricContribution>();
    public ObservableCollection<RecordedSessionListIndicatorContribution> SessionListIndicators { get; } = CreateCollection<RecordedSessionListIndicatorContribution>();
    public ObservableCollection<RecordedSessionListActionContribution> SessionListActions { get; } = CreateCollection<RecordedSessionListActionContribution>();
    public ObservableCollection<RecordedSessionPlotContextMenuContribution> PlotContextMenuActions { get; } = CreateCollection<RecordedSessionPlotContextMenuContribution>();
    public ObservableCollection<RecordedSessionPlotRowActionContribution> PlotRowHeaderActions { get; } = CreateCollection<RecordedSessionPlotRowActionContribution>();
    public ObservableCollection<RecordedSessionHostedGraphRowContribution> HostedGraphRows { get; } = CreateCollection<RecordedSessionHostedGraphRowContribution>();
    public ObservableCollection<RecordedSessionTimeRangeOverlayContribution> TimeRangeOverlays { get; } = CreateCollection<RecordedSessionTimeRangeOverlayContribution>();

    private static ObservableCollection<T> CreateCollection<T>() => new RecordedSessionExtensionSlotCollection<T>();
}

public static class RecordedSessionExtensionSlotCollectionExtensions
{
    public static void ReplaceWith<T>(this ObservableCollection<T> collection, IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(items);

        if (collection is IRecordedSessionExtensionSlotCollection<T> batchedCollection)
        {
            batchedCollection.ReplaceWith(items);
            return;
        }

        var replacement = items.ToArray();
        collection.Clear();
        foreach (var item in replacement)
        {
            collection.Add(item);
        }
    }
}

internal interface IRecordedSessionExtensionSlotCollection<T>
{
    void ReplaceWith(IEnumerable<T> items);
}

internal sealed class RecordedSessionExtensionSlotCollection<T>
    : ObservableCollection<T>, IRecordedSessionExtensionSlotCollection<T>
{
    public void ReplaceWith(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var replacement = items.ToArray();
        if (Count == replacement.Length && this.SequenceEqual(replacement))
        {
            return;
        }

        CheckReentrancy();
        Items.Clear();
        foreach (var item in replacement)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
