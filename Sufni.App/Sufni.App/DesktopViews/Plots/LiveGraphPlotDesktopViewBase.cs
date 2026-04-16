using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Threading;
using Sufni.App.Plots;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.ViewModels.Editors;
using Sufni.App.Views.Plots;

namespace Sufni.App.DesktopViews.Plots;

public abstract class LiveGraphPlotDesktopViewBase : SufniPlotView
{
    private readonly DispatcherTimer uiRefreshTimer;
    private readonly object pendingGraphBatchesGate = new();
    private IDisposable? graphBatchesSubscription;
    private bool applyingTimelineRange;
    private long lastRevision;
    private List<LiveGraphBatch> pendingGraphBatches = [];

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

    protected LiveGraphPlotDesktopViewBase()
    {
        uiRefreshTimer = CreateUiRefreshTimer();

        PropertyChanged += (_, e) =>
        {
            switch (e.Property.Name)
            {
                case nameof(GraphBatches):
                    graphBatchesSubscription?.Dispose();
                    ClearPendingGraphBatches();
                    if (e.NewValue is IObservable<LiveGraphBatch> graphBatches)
                    {
                        graphBatchesSubscription = graphBatches.Subscribe(HandleGraphBatch);
                    }
                    break;

                case nameof(Timeline):
                    if (e.OldValue is SessionTimelineLinkViewModel oldTimeline)
                    {
                        oldTimeline.PropertyChanged -= OnTimelineChanged;
                    }

                    if (e.NewValue is SessionTimelineLinkViewModel newTimeline)
                    {
                        newTimeline.PropertyChanged += OnTimelineChanged;
                        ApplyTimelineRange();
                    }
                    break;
            }
        };

        AttachedToVisualTree += (_, _) => uiRefreshTimer.Start();
        DetachedFromVisualTree += (_, _) =>
        {
            graphBatchesSubscription?.Dispose();
            uiRefreshTimer.Stop();
            ClearPendingGraphBatches();
        };
    }

    protected void InitializeInteractions()
    {
        if (AvaPlot is null)
        {
            return;
        }

        AvaPlot.PointerMoved += (_, args) =>
        {
            if (Plot is null)
            {
                return;
            }

            var point = args.GetPosition(AvaPlot);
            var coords = AvaPlot.Plot.GetCoordinates((float)point.X, (float)point.Y);
            var normalizedTime = Plot.CoordinateToNormalizedTime(coords.X);
            if (normalizedTime is null)
            {
                return;
            }

            Timeline?.SetCursorPosition(normalizedTime);
            Plot.SetCursorFromNormalized(normalizedTime);
            AvaPlot.Refresh();
        };

        AvaPlot.PointerReleased += (_, _) => UpdateTimelineRange();
        AvaPlot.PointerWheelChanged += (_, _) => UpdateTimelineRange();
    }

    protected abstract void ApplyGraphBatch(LiveGraphBatch batch);

    private void HandleGraphBatch(LiveGraphBatch batch)
    {
        lock (pendingGraphBatchesGate)
        {
            pendingGraphBatches.Add(batch);
        }
    }

    private void FlushPendingGraphBatches()
    {
        if (Plot is null || AvaPlot is null)
        {
            return;
        }

        List<LiveGraphBatch> batches;
        lock (pendingGraphBatchesGate)
        {
            if (pendingGraphBatches.Count == 0)
            {
                return;
            }

            batches = pendingGraphBatches;
            pendingGraphBatches = [];
        }

        var didApplyBatch = false;
        var didReset = false;
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
        AvaPlot.Refresh();
        if (didReset)
        {
            UpdateTimelineRange();
        }
    }

    private DispatcherTimer CreateUiRefreshTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(LiveSessionRefreshCadence.UiRefreshIntervalMs)
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
                AvaPlot?.Refresh();
                break;

            case nameof(SessionTimelineLinkViewModel.VisibleRangeStart):
            case nameof(SessionTimelineLinkViewModel.VisibleRangeEnd):
                ApplyTimelineRange();
                break;
        }
    }

    private void UpdateTimelineRange()
    {
        if (Plot is null || Timeline is null || applyingTimelineRange)
        {
            return;
        }

        var (start, end) = Plot.GetNormalizedVisibleRange();
        Timeline.SetVisibleRange(start, end);
    }

    private void ApplyTimelineRange()
    {
        if (Plot is null || Timeline is null || AvaPlot is null || applyingTimelineRange)
        {
            return;
        }

        applyingTimelineRange = true;
        try
        {
            Plot.ApplyVisibleRange(Timeline.VisibleRangeStart, Timeline.VisibleRangeEnd);
            AvaPlot.Refresh();
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
}
