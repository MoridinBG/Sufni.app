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
    public ObservableCollection<RecordedSessionToolbarCommandContribution> SignalToolbarCommands { get; } = CreateCollection<RecordedSessionToolbarCommandContribution>();
    public ObservableCollection<RecordedSessionToolbarViewContribution> SignalToolbarViews { get; } = CreateCollection<RecordedSessionToolbarViewContribution>();
    public ObservableCollection<RecordedSessionPageContribution> Pages { get; } = CreateCollection<RecordedSessionPageContribution>();
    public ObservableCollection<RecordedSessionMediaPaneContribution> MediaPanes { get; } = CreateCollection<RecordedSessionMediaPaneContribution>();
    public ObservableCollection<RecordedSessionMapOverlayContribution> MapOverlays { get; } = CreateCollection<RecordedSessionMapOverlayContribution>();
    public ObservableCollection<RecordedSessionAnalysisBannerContribution> AnalysisBanners { get; } = CreateCollection<RecordedSessionAnalysisBannerContribution>();
    public ObservableCollection<RecordedSessionAnalysisTabContribution> AnalysisTabs { get; } = CreateCollection<RecordedSessionAnalysisTabContribution>();
    public ObservableCollection<RecordedSessionAnalysisOverlayContribution> AnalysisOverlays { get; } = CreateCollection<RecordedSessionAnalysisOverlayContribution>();
    public ObservableCollection<RecordedSessionAnalysisMetricContribution> AnalysisMetrics { get; } = CreateCollection<RecordedSessionAnalysisMetricContribution>();
    public ObservableCollection<RecordedSessionListIndicatorContribution> SessionListIndicators { get; } = CreateCollection<RecordedSessionListIndicatorContribution>();
    public ObservableCollection<RecordedSessionListActionContribution> SessionListActions { get; } = CreateCollection<RecordedSessionListActionContribution>();
    public ObservableCollection<RecordedSessionSignalPlotContextMenuContribution> SignalPlotContextMenuActions { get; } = CreateCollection<RecordedSessionSignalPlotContextMenuContribution>();
    public ObservableCollection<RecordedSessionSignalRowActionContribution> SignalRowHeaderActions { get; } = CreateCollection<RecordedSessionSignalRowActionContribution>();
    public ObservableCollection<RecordedSessionHostedSignalRowContribution> HostedSignalRows { get; } = CreateCollection<RecordedSessionHostedSignalRowContribution>();
    public ObservableCollection<RecordedSessionTimeRangeOverlayContribution> SignalTimeRangeOverlays { get; } = CreateCollection<RecordedSessionTimeRangeOverlayContribution>();

    private static ObservableCollection<T> CreateCollection<T>() => new RecordedSessionExtensionSlotCollection<T>();

    public IDisposable SubscribeToChanges(Action changed)
    {
        ArgumentNullException.ThrowIfNull(changed);

        return new RecordedSessionExtensionSlotChangeSubscription(
            [
                Subscribe(SignalToolbarCommands, changed),
                Subscribe(SignalToolbarViews, changed),
                Subscribe(Pages, changed),
                Subscribe(MediaPanes, changed),
                Subscribe(MapOverlays, changed),
                Subscribe(AnalysisBanners, changed),
                Subscribe(AnalysisTabs, changed),
                Subscribe(AnalysisOverlays, changed),
                Subscribe(AnalysisMetrics, changed),
                Subscribe(SessionListIndicators, changed),
                Subscribe(SessionListActions, changed),
                Subscribe(SignalPlotContextMenuActions, changed),
                Subscribe(SignalRowHeaderActions, changed),
                Subscribe(HostedSignalRows, changed),
                Subscribe(SignalTimeRangeOverlays, changed),
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
