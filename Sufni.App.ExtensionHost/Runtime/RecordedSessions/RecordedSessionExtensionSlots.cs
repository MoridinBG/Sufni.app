using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

using Sufni.App.ExtensionHost.Contracts.RecordedSessions;

namespace Sufni.App.ExtensionHost.Runtime.RecordedSessions;

public sealed class RecordedSessionExtensionSlots
{
    public ObservableCollection<RecordedSessionToolbarCommandContribution> GraphToolbarCommands { get; } = CreateCollection<RecordedSessionToolbarCommandContribution>();
    public ObservableCollection<RecordedSessionToolbarViewContribution> GraphToolbarViews { get; } = CreateCollection<RecordedSessionToolbarViewContribution>();
    public ObservableCollection<RecordedSessionPageContribution> Pages { get; } = CreateCollection<RecordedSessionPageContribution>();
    public ObservableCollection<RecordedSessionMediaPaneContribution> MediaPanes { get; } = CreateCollection<RecordedSessionMediaPaneContribution>();
    public ObservableCollection<RecordedSessionMapOverlayContribution> MapOverlays { get; } = CreateCollection<RecordedSessionMapOverlayContribution>();
    public ObservableCollection<RecordedSessionStatisticsBannerContribution> StatisticsBanners { get; } = CreateCollection<RecordedSessionStatisticsBannerContribution>();
    public ObservableCollection<RecordedSessionStatisticsTabContribution> StatisticsTabs { get; } = CreateCollection<RecordedSessionStatisticsTabContribution>();
    public ObservableCollection<RecordedSessionStatisticsOverlayContribution> StatisticsOverlays { get; } = CreateCollection<RecordedSessionStatisticsOverlayContribution>();
    public ObservableCollection<RecordedSessionStatisticsMetricContribution> StatisticsMetrics { get; } = CreateCollection<RecordedSessionStatisticsMetricContribution>();
    public ObservableCollection<RecordedSessionListIndicatorContribution> SessionListIndicators { get; } = CreateCollection<RecordedSessionListIndicatorContribution>();
    public ObservableCollection<RecordedSessionListActionContribution> SessionListActions { get; } = CreateCollection<RecordedSessionListActionContribution>();
    public ObservableCollection<RecordedSessionPlotContextMenuContribution> PlotContextMenuActions { get; } = CreateCollection<RecordedSessionPlotContextMenuContribution>();
    public ObservableCollection<RecordedSessionPlotRowActionContribution> PlotRowHeaderActions { get; } = CreateCollection<RecordedSessionPlotRowActionContribution>();
    public ObservableCollection<RecordedSessionHostedGraphRowContribution> HostedGraphRows { get; } = CreateCollection<RecordedSessionHostedGraphRowContribution>();
    public ObservableCollection<RecordedSessionTimeRangeOverlayContribution> TimeRangeOverlays { get; } = CreateCollection<RecordedSessionTimeRangeOverlayContribution>();

    private static ObservableCollection<T> CreateCollection<T>() => new RecordedSessionExtensionSlotCollection<T>();

    public IDisposable SubscribeToChanges(Action changed)
    {
        ArgumentNullException.ThrowIfNull(changed);

        return new RecordedSessionExtensionSlotChangeSubscription(
            [
                Subscribe(GraphToolbarCommands, changed),
                Subscribe(GraphToolbarViews, changed),
                Subscribe(Pages, changed),
                Subscribe(MediaPanes, changed),
                Subscribe(MapOverlays, changed),
                Subscribe(StatisticsBanners, changed),
                Subscribe(StatisticsTabs, changed),
                Subscribe(StatisticsOverlays, changed),
                Subscribe(StatisticsMetrics, changed),
                Subscribe(SessionListIndicators, changed),
                Subscribe(SessionListActions, changed),
                Subscribe(PlotContextMenuActions, changed),
                Subscribe(PlotRowHeaderActions, changed),
                Subscribe(HostedGraphRows, changed),
                Subscribe(TimeRangeOverlays, changed),
            ]);
    }

    private static IDisposable Subscribe<T>(ObservableCollection<T> collection, Action changed)
    {
        NotifyCollectionChangedEventHandler handler = (_, _) => changed();
        collection.CollectionChanged += handler;
        return new RecordedSessionExtensionSlotChangeSubscription(
            () => collection.CollectionChanged -= handler);
    }

    private sealed class RecordedSessionExtensionSlotChangeSubscription : IDisposable
    {
        private Action? disposeAction;
        private IReadOnlyList<IDisposable>? subscriptions;

        public RecordedSessionExtensionSlotChangeSubscription(Action dispose)
        {
            disposeAction = dispose;
        }

        public RecordedSessionExtensionSlotChangeSubscription(IReadOnlyList<IDisposable> subscriptions)
        {
            this.subscriptions = subscriptions;
        }

        public void Dispose()
        {
            if (disposeAction is { } action)
            {
                disposeAction = null;
                action();
            }

            if (subscriptions is { } currentSubscriptions)
            {
                subscriptions = null;
                foreach (var subscription in currentSubscriptions)
                {
                    subscription.Dispose();
                }
            }
        }
    }
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
