using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Threading;
using Sufni.App.Plots;
using Sufni.App.SessionGraphs;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.ViewModels.Editors;
using Sufni.App.Views.Plots;

namespace Sufni.App.DesktopViews.Plots;

public abstract class LiveGraphPlotDesktopViewBase : SufniPlotView
{
    private const int DefaultPendingSampleMargin = 512;
    private const int PendingSampleMarginStep = 128;

    private readonly DispatcherTimer uiRefreshTimer;
    private readonly object pendingGraphBatchesGate = new();
    private IDisposable? graphBatchesSubscription;
    private bool applyingTimelineRange;
    private long lastRevision;
    private int pendingSampleMargin = DefaultPendingSampleMargin;
    private PendingGraphBatchBuffer pendingGraphBatches = new();

    public LiveStreamingPlotBase? Plot { get; protected set; }

    public static readonly StyledProperty<IObservable<LiveGraphBatch>?> GraphBatchesProperty =
        AvaloniaProperty.Register<LiveGraphPlotDesktopViewBase, IObservable<LiveGraphBatch>?>(nameof(GraphBatches));

    public IObservable<LiveGraphBatch>? GraphBatches
    {
        get => GetValue(GraphBatchesProperty);
        set => SetValue(GraphBatchesProperty, value);
    }

    public static readonly StyledProperty<SessionTimelineLinkViewModel?> TimelineProperty =
        AvaloniaProperty.Register<LiveGraphPlotDesktopViewBase, SessionTimelineLinkViewModel?>(nameof(Timeline));

    public SessionTimelineLinkViewModel? Timeline
    {
        get => GetValue(TimelineProperty);
        set => SetValue(TimelineProperty, value);
    }

    public static readonly StyledProperty<double?> MinimumYProperty =
        AvaloniaProperty.Register<LiveGraphPlotDesktopViewBase, double?>(nameof(MinimumY));

    public double? MinimumY
    {
        get => GetValue(MinimumYProperty);
        set => SetValue(MinimumYProperty, value);
    }

    public static readonly StyledProperty<double?> MaximumYProperty =
        AvaloniaProperty.Register<LiveGraphPlotDesktopViewBase, double?>(nameof(MaximumY));

    public double? MaximumY
    {
        get => GetValue(MaximumYProperty);
        set => SetValue(MaximumYProperty, value);
    }

    protected LiveGraphPlotDesktopViewBase()
    {
        uiRefreshTimer = CreateUiRefreshTimer();

        PropertyChanged += (_, e) =>
        {
            switch (e.Property.Name)
            {
                case nameof(GraphBatches):
                    graphBatchesSubscription?.Dispose();
                    graphBatchesSubscription = null;
                    ClearPendingGraphBatches();
                    EnsureGraphBatchSubscription();
                    break;

                case nameof(Timeline):
                    if (e.OldValue is SessionTimelineLinkViewModel oldTimeline)
                    {
                        oldTimeline.PropertyChanged -= OnTimelineChanged;
                        oldTimeline.VisibleRangeChanged -= OnTimelineVisibleRangeChanged;
                    }

                    if (e.NewValue is SessionTimelineLinkViewModel newTimeline)
                    {
                        newTimeline.PropertyChanged += OnTimelineChanged;
                        newTimeline.VisibleRangeChanged += OnTimelineVisibleRangeChanged;
                        ApplyTimelineRange();
                    }
                    break;

                case nameof(MinimumY):
                case nameof(MaximumY):
                    ApplyConfiguredVerticalLimits();
                    break;
            }
        };

        AttachedToVisualTree += (_, _) =>
        {
            uiRefreshTimer.Start();
            EnsureGraphBatchSubscription();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            graphBatchesSubscription?.Dispose();
            graphBatchesSubscription = null;
            uiRefreshTimer.Stop();
            ClearPendingGraphBatches();
        };
    }

    protected void InitializeInteractions()
    {
        if (!HasPlotControl)
        {
            return;
        }

        var plotControl = PlotControl;

        plotControl.PointerMoved += (_, args) =>
        {
            if (Plot is null)
            {
                return;
            }

            var point = args.GetPosition(plotControl);
            var coords = plotControl.Plot.GetCoordinates((float)point.X, (float)point.Y);
            var normalizedTime = Plot.CoordinateToNormalizedTime(coords.X);
            if (normalizedTime is null)
            {
                return;
            }

            Timeline?.SetCursorPosition(normalizedTime);
            Plot.SetCursorFromNormalized(normalizedTime);
            RefreshPlot();
        };

        plotControl.PointerReleased += (_, _) => UpdateTimelineRange();
        plotControl.PointerWheelChanged += (_, _) => UpdateTimelineRange();
    }

    protected abstract void ApplyGraphBatch(LiveGraphBatch batch);

    protected void ApplyConfiguredVerticalLimits()
    {
        if (Plot is null)
        {
            return;
        }

        if (MinimumY is null && MaximumY is null)
        {
            return;
        }

        Plot.SetVerticalLimits(MinimumY ?? 0, MaximumY ?? 1);
    }

    private void HandleGraphBatch(LiveGraphBatch batch)
    {
        lock (pendingGraphBatchesGate)
        {
            pendingGraphBatches.Enqueue(batch, GetPendingSampleLimit());
        }
    }

    private void EnsureGraphBatchSubscription()
    {
        if (graphBatchesSubscription is not null || GraphBatches is not IObservable<LiveGraphBatch> graphBatches)
        {
            return;
        }

        graphBatchesSubscription = graphBatches.Subscribe(HandleGraphBatch);
    }

    internal void FlushPendingGraphBatches()
    {
        if (Plot is null || !HasPlotControl)
        {
            return;
        }

        List<LiveGraphBatch> batches;
        lock (pendingGraphBatchesGate)
        {
            if (!pendingGraphBatches.HasContent)
            {
                return;
            }

            batches = pendingGraphBatches.Take();
        }

        var didApplyBatch = false;
        var didReset = false;
        var stopwatch = Stopwatch.StartNew();
        foreach (var batch in batches)
        {
            if (batch.Revision < lastRevision)
            {
                continue;
            }

            if (IsResetBatch(batch))
            {
                Plot.Reset();
                didReset = true;
            }
            else
            {
                ApplyGraphBatch(batch);
            }

            lastRevision = batch.Revision;
            didApplyBatch = true;
        }

        if (!didApplyBatch)
        {
            return;
        }

        Plot.SetCursorFromNormalized(Timeline?.NormalizedCursorPosition);
        RefreshPlot();
        if (didReset)
        {
            UpdateTimelineRange();
        }

        stopwatch.Stop();
        AdjustPendingSampleMargin(stopwatch.Elapsed);
    }

    private DispatcherTimer CreateUiRefreshTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(SessionGraphSettings.LiveGraphRefreshIntervalMs)
        };
        timer.Tick += (_, _) => FlushPendingGraphBatches();
        return timer;
    }

    private void ClearPendingGraphBatches()
    {
        lock (pendingGraphBatchesGate)
        {
            pendingGraphBatches.Clear();
        }
    }

    private int GetPendingSampleLimit()
    {
        return Math.Max(1, (Plot?.SampleCapacity ?? 2048) + pendingSampleMargin);
    }

    private void AdjustPendingSampleMargin(TimeSpan flushDuration)
    {
        var refreshInterval = TimeSpan.FromMilliseconds(SessionGraphSettings.LiveGraphRefreshIntervalMs);
        lock (pendingGraphBatchesGate)
        {
            if (flushDuration > refreshInterval && pendingSampleMargin > 0)
            {
                pendingSampleMargin = Math.Max(0, pendingSampleMargin - PendingSampleMarginStep);
            }
            else if (flushDuration < refreshInterval / 2 && pendingSampleMargin < DefaultPendingSampleMargin)
            {
                pendingSampleMargin = Math.Min(DefaultPendingSampleMargin, pendingSampleMargin + PendingSampleMarginStep);
            }
        }
    }

    private void OnTimelineChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Plot is null)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(SessionTimelineLinkViewModel.NormalizedCursorPosition):
                Plot.SetCursorFromNormalized(Timeline?.NormalizedCursorPosition);
                RefreshPlot();
                break;
        }
    }

    private void OnTimelineVisibleRangeChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(Timeline?.VisibleRangeChangeSource, this))
        {
            return;
        }

        ApplyTimelineRange();
    }

    protected override void OnViewportChanged() => UpdateTimelineRange();

    private void UpdateTimelineRange()
    {
        if (Plot is null || Timeline is null || applyingTimelineRange)
        {
            return;
        }

        var (start, end) = Plot.GetNormalizedVisibleRange();
        Timeline.SetVisibleRange(start, end, this);
    }

    private void ApplyTimelineRange()
    {
        if (Plot is null || Timeline is null || !HasPlotControl || applyingTimelineRange)
        {
            return;
        }

        applyingTimelineRange = true;
        try
        {
            Plot.ApplyVisibleRange(Timeline.VisibleRangeStart, Timeline.VisibleRangeEnd);
            RefreshPlot();
        }
        finally
        {
            applyingTimelineRange = false;
        }
    }

    private static bool IsResetBatch(LiveGraphBatch batch)
    {
        return batch.TravelTimes.Count == 0
            && batch.FrontTravel.Count == 0
            && batch.RearTravel.Count == 0
            && batch.VelocityTimes.Count == 0
            && batch.FrontVelocity.Count == 0
            && batch.RearVelocity.Count == 0
            && batch.ImuTimes.Count == 0
            && batch.ImuMagnitudes.Count == 0;
    }

    private sealed class PendingGraphBatchBuffer
    {
        private LiveGraphBatch? resetBatch;
        private MutableGraphBatch? pendingBatch;

        public bool HasContent => resetBatch is not null || pendingBatch?.HasContent == true;

        public void Enqueue(LiveGraphBatch batch, int sampleLimit)
        {
            if (IsResetBatch(batch))
            {
                resetBatch = batch;
                pendingBatch = null;
                return;
            }

            pendingBatch ??= new MutableGraphBatch();
            pendingBatch.Append(batch);
            pendingBatch.TrimToNewest(sampleLimit);
        }

        public List<LiveGraphBatch> Take()
        {
            var batches = new List<LiveGraphBatch>(2);
            if (resetBatch is not null)
            {
                batches.Add(resetBatch);
                resetBatch = null;
            }

            if (pendingBatch?.HasContent == true)
            {
                batches.Add(pendingBatch.ToBatch());
                pendingBatch = null;
            }

            return batches;
        }

        public void Clear()
        {
            resetBatch = null;
            pendingBatch = null;
        }
    }

    private sealed class MutableGraphBatch
    {
        private readonly List<double> travelTimes = [];
        private readonly List<double> frontTravel = [];
        private readonly List<double> rearTravel = [];
        private readonly List<double> velocityTimes = [];
        private readonly List<double> frontVelocity = [];
        private readonly List<double> rearVelocity = [];
        private readonly Dictionary<LiveImuLocation, List<double>> imuTimes = [];
        private readonly Dictionary<LiveImuLocation, List<double>> imuMagnitudes = [];

        private long revision;

        public bool HasContent => travelTimes.Count > 0
            || frontTravel.Count > 0
            || rearTravel.Count > 0
            || velocityTimes.Count > 0
            || frontVelocity.Count > 0
            || rearVelocity.Count > 0
            || HasDictionaryContent(imuTimes)
            || HasDictionaryContent(imuMagnitudes);

        public void Append(LiveGraphBatch batch)
        {
            revision = Math.Max(revision, batch.Revision);
            travelTimes.AddRange(batch.TravelTimes);
            frontTravel.AddRange(batch.FrontTravel);
            rearTravel.AddRange(batch.RearTravel);
            velocityTimes.AddRange(batch.VelocityTimes);
            frontVelocity.AddRange(batch.FrontVelocity);
            rearVelocity.AddRange(batch.RearVelocity);
            AppendDictionary(imuTimes, batch.ImuTimes);
            AppendDictionary(imuMagnitudes, batch.ImuMagnitudes);
        }

        public void TrimToNewest(int sampleLimit)
        {
            TrimListToNewest(travelTimes, sampleLimit);
            TrimListToNewest(frontTravel, sampleLimit);
            TrimListToNewest(rearTravel, sampleLimit);
            TrimListToNewest(velocityTimes, sampleLimit);
            TrimListToNewest(frontVelocity, sampleLimit);
            TrimListToNewest(rearVelocity, sampleLimit);
            TrimDictionaryToNewest(imuTimes, sampleLimit);
            TrimDictionaryToNewest(imuMagnitudes, sampleLimit);
        }

        public LiveGraphBatch ToBatch()
        {
            return new LiveGraphBatch(
                Revision: revision,
                TravelTimes: travelTimes.ToArray(),
                FrontTravel: frontTravel.ToArray(),
                RearTravel: rearTravel.ToArray(),
                VelocityTimes: velocityTimes.ToArray(),
                FrontVelocity: frontVelocity.ToArray(),
                RearVelocity: rearVelocity.ToArray(),
                ImuTimes: CloneDictionary(imuTimes),
                ImuMagnitudes: CloneDictionary(imuMagnitudes));
        }

        private static void AppendDictionary(
            Dictionary<LiveImuLocation, List<double>> destination,
            IReadOnlyDictionary<LiveImuLocation, IReadOnlyList<double>> source)
        {
            foreach (var entry in source)
            {
                if (!destination.TryGetValue(entry.Key, out var values))
                {
                    values = [];
                    destination[entry.Key] = values;
                }

                values.AddRange(entry.Value);
            }
        }

        private static Dictionary<LiveImuLocation, IReadOnlyList<double>> CloneDictionary(
            Dictionary<LiveImuLocation, List<double>> source)
        {
            var clone = new Dictionary<LiveImuLocation, IReadOnlyList<double>>(source.Count);
            foreach (var entry in source)
            {
                clone[entry.Key] = entry.Value.ToArray();
            }

            return clone;
        }

        private static void TrimDictionaryToNewest(Dictionary<LiveImuLocation, List<double>> values, int sampleLimit)
        {
            foreach (var entry in values)
            {
                TrimListToNewest(entry.Value, sampleLimit);
            }
        }

        private static void TrimListToNewest(List<double> values, int sampleLimit)
        {
            var excess = values.Count - sampleLimit;
            if (excess > 0)
            {
                values.RemoveRange(0, excess);
            }
        }

        private static bool HasDictionaryContent(Dictionary<LiveImuLocation, List<double>> values)
        {
            foreach (var entry in values)
            {
                if (entry.Value.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
