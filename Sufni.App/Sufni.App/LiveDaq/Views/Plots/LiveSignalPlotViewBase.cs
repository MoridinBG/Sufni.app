using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Input;

using Sufni.App.Acquisition.Models;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Plots;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
using Sufni.App.Shared.Views.Plots;
using Sufni.App.Shared.Views.Input;
using Sufni.App.Theming;
using Sufni.App.Shared.Common;
using Sufni.App.Shared.Plots;
using Sufni.App.Infrastructure.Theming;
namespace Sufni.App.LiveDaq.Views.Plots;

public abstract class LiveSignalPlotViewBase : SufniPlotView
{
    private const int DefaultPendingSampleMargin = 512;
    private const int PendingSampleMarginStep = 128;

    private IDisposable? uiRefreshTimer;
    private readonly System.Threading.Lock pendingSignalBatchesGate = new();
    private IDisposable? signalBatchesSubscription;
    private bool applyingTimelineRange;
    private long lastRevision;
    private int pendingSampleMargin = DefaultPendingSampleMargin;
    private PendingSignalBatchBuffer pendingSignalBatches = new();
    private LiveStreamingPlotBase? plot;
    private bool suppressLegendTogglePointerRelease;

    public LiveStreamingPlotBase? Plot
    {
        get => plot;
        protected set
        {
            plot = value;
            plot?.SourceVisibility = SourceVisibility;

            ApplyPlotBackgroundColors(refresh: false);
        }
    }

    public static readonly StyledProperty<IObservable<LiveSignalBatch>?> SignalBatchesProperty =
        AvaloniaProperty.Register<LiveSignalPlotViewBase, IObservable<LiveSignalBatch>?>(nameof(SignalBatches));

    public IObservable<LiveSignalBatch>? SignalBatches
    {
        get => GetValue(SignalBatchesProperty);
        set => SetValue(SignalBatchesProperty, value);
    }

    public static readonly StyledProperty<SessionTimelineLinkViewModel?> TimelineProperty =
        AvaloniaProperty.Register<LiveSignalPlotViewBase, SessionTimelineLinkViewModel?>(nameof(Timeline));

    public SessionTimelineLinkViewModel? Timeline
    {
        get => GetValue(TimelineProperty);
        set => SetValue(TimelineProperty, value);
    }

    public static readonly StyledProperty<double?> MinimumYProperty =
        AvaloniaProperty.Register<LiveSignalPlotViewBase, double?>(nameof(MinimumY));

    public double? MinimumY
    {
        get => GetValue(MinimumYProperty);
        set => SetValue(MinimumYProperty, value);
    }

    public static readonly StyledProperty<double?> MaximumYProperty =
        AvaloniaProperty.Register<LiveSignalPlotViewBase, double?>(nameof(MaximumY));

    public double? MaximumY
    {
        get => GetValue(MaximumYProperty);
        set => SetValue(MaximumYProperty, value);
    }

    public static readonly StyledProperty<PlotSmoothingLevel> SmoothingLevelProperty =
        AvaloniaProperty.Register<LiveSignalPlotViewBase, PlotSmoothingLevel>(nameof(SmoothingLevel));

    public PlotSmoothingLevel SmoothingLevel
    {
        get => GetValue(SmoothingLevelProperty);
        set => SetValue(SmoothingLevelProperty, value);
    }

    public static readonly StyledProperty<bool> HideRightAxisProperty =
        AvaloniaProperty.Register<LiveSignalPlotViewBase, bool>(nameof(HideRightAxis));

    public bool HideRightAxis
    {
        get => GetValue(HideRightAxisProperty);
        set => SetValue(HideRightAxisProperty, value);
    }

    public static readonly StyledProperty<TelemetrySourceVisibilityStore?> SourceVisibilityProperty =
        AvaloniaProperty.Register<LiveSignalPlotViewBase, TelemetrySourceVisibilityStore?>(nameof(SourceVisibility));

    public TelemetrySourceVisibilityStore? SourceVisibility
    {
        get => GetValue(SourceVisibilityProperty);
        set => SetValue(SourceVisibilityProperty, value);
    }

    protected LiveSignalPlotViewBase()
    {
        PropertyChanged += (_, e) =>
        {
            switch (e.Property.Name)
            {
                case nameof(SignalBatches):
                    signalBatchesSubscription?.Dispose();
                    signalBatchesSubscription = null;
                    ClearPendingSignalBatches();
                    EnsureSignalBatchSubscription();
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

                case nameof(SmoothingLevel):
                    ApplySmoothingLevel(resetExistingSamples: true);
                    break;

                case nameof(HideRightAxis):
                    Plot?.SetHideRightAxis(HideRightAxis);
                    RefreshPlot();
                    break;

                case nameof(SourceVisibility):
                    if (Plot is not null)
                    {
                        Plot.SourceVisibility = SourceVisibility;
                        RefreshPlot();
                    }
                    break;

                case nameof(PlotFigureBackground):
                case nameof(PlotDataBackground):
                    ApplyPlotBackgroundColors(refresh: true);
                    break;
            }
        };

        AttachedToVisualTree += (_, _) =>
        {
            uiRefreshTimer ??= PeriodicUiTimer.SchedulePeriodic(
                TimeSpan.FromMilliseconds(PlotSettings.LiveSignalRefreshIntervalMs),
                FlushPendingSignalBatches);
            EnsureSignalBatchSubscription();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            signalBatchesSubscription?.Dispose();
            signalBatchesSubscription = null;
            uiRefreshTimer?.Dispose();
            uiRefreshTimer = null;
            ClearPendingSignalBatches();
        };
    }

    protected void InitializeInteractions()
    {
        if (!HasPlotControl)
        {
            return;
        }

        var plotControl = PlotControl;

        void UpdateCursor(Avalonia.Input.PointerEventArgs args)
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
        }

        plotControl.PointerPressed += (_, args) =>
        {
            if (TryToggleInteractiveLegend(args))
            {
                return;
            }

            UpdateCursor(args);
        };
        plotControl.PointerMoved += (_, args) => UpdateCursor(args);

        plotControl.PointerReleased += (_, args) =>
        {
            if (suppressLegendTogglePointerRelease)
            {
                suppressLegendTogglePointerRelease = false;
                args.Handled = true;
                return;
            }

            UpdateTimelineRange();
        };
        plotControl.PointerWheelChanged += (_, _) => UpdateTimelineRange();
    }

    private bool TryToggleInteractiveLegend(PointerEventArgs args)
    {
        if (Plot is null || !HasPlotControl || !IsPrimaryPointerPressed(args))
        {
            return false;
        }

        var point = args.GetPosition(PlotControl);
        var pixel = PlotControl.ToScottPlotPixel(point);
        var plotSize = PlotControl.GetScottPlotPixelSize();
        if (!Plot.TryToggleInteractiveLegendAt(pixel, plotSize))
        {
            return false;
        }

        Plot.HideCursorReadout();
        suppressLegendTogglePointerRelease = true;
        args.Handled = true;
        RefreshPlot();
        return true;
    }

    private bool IsPrimaryPointerPressed(PointerEventArgs args)
    {
        return PointerGesture.IsPrimaryPressed(args, PlotControl);
    }

    protected abstract void ApplySignalBatch(LiveSignalBatch batch);

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

    protected void ApplySmoothingLevel(bool resetExistingSamples = false)
    {
        if (Plot is null || Plot.SmoothingLevel == SmoothingLevel)
        {
            return;
        }

        Plot.SmoothingLevel = SmoothingLevel;
        if (!resetExistingSamples)
        {
            return;
        }

        Plot.Reset();
        RefreshPlot();
        UpdateTimelineRange();
    }

    private void ApplyPlotBackgroundColors(bool refresh)
    {
        if (Plot is null)
        {
            return;
        }

        var plotTheme = CurrentTheme.Plot.Root;
        var figure = IsSet(PlotFigureBackgroundProperty)
            ? PlotFigureBackground.ToScottPlotColor()
            : plotTheme.Figure.ToScottPlotColor();
        var data = IsSet(PlotDataBackgroundProperty)
            ? PlotDataBackground.ToScottPlotColor()
            : plotTheme.Data.ToScottPlotColor();
        Plot.SetBackgroundColors(figure, data);
        if (refresh)
        {
            RefreshPlot();
        }
    }

    protected override void OnThemeChanged(SufniTheme theme)
    {
        Plot?.ApplyTheme(theme);
    }

    private void HandleSignalBatch(LiveSignalBatch batch)
    {
        lock (pendingSignalBatchesGate)
        {
            pendingSignalBatches.Enqueue(batch, GetPendingSampleLimit());
        }
    }

    private void EnsureSignalBatchSubscription()
    {
        if (signalBatchesSubscription is not null || SignalBatches is not IObservable<LiveSignalBatch> signalBatches)
        {
            return;
        }

        signalBatchesSubscription = signalBatches.Subscribe(HandleSignalBatch);
    }

    internal void FlushPendingSignalBatches()
    {
        if (Plot is null || !HasPlotControl)
        {
            return;
        }

        List<LiveSignalBatch> batches;
        lock (pendingSignalBatchesGate)
        {
            if (!pendingSignalBatches.HasContent)
            {
                return;
            }

            batches = pendingSignalBatches.Take();
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
                ApplySignalBatch(batch);
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

    private void ClearPendingSignalBatches()
    {
        lock (pendingSignalBatchesGate)
        {
            pendingSignalBatches.Clear();
        }
    }

    private int GetPendingSampleLimit()
    {
        return Math.Max(1, (Plot?.SampleCapacity ?? 2048) + pendingSampleMargin);
    }

    private void AdjustPendingSampleMargin(TimeSpan flushDuration)
    {
        var refreshInterval = TimeSpan.FromMilliseconds(PlotSettings.LiveSignalRefreshIntervalMs);
        lock (pendingSignalBatchesGate)
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

    private static bool IsResetBatch(LiveSignalBatch batch)
    {
        return batch.TravelTimes.Count == 0
            && batch.FrontTravel.Count == 0
            && batch.RearTravel.Count == 0
            && batch.VelocityTimes.Count == 0
            && batch.FrontVelocity.Count == 0
            && batch.RearVelocity.Count == 0
            && batch.ImuTimes.Count == 0
            && batch.ImuVibrationRms.Count == 0
            && batch.FramePitchRollTimes.Count == 0
            && batch.FramePitchDegrees.Count == 0
            && batch.FrameRollDegrees.Count == 0;
    }

    private sealed class PendingSignalBatchBuffer
    {
        private LiveSignalBatch? resetBatch;
        private MutableSignalBatch? pendingBatch;

        public bool HasContent => resetBatch is not null || pendingBatch?.HasContent == true;

        public void Enqueue(LiveSignalBatch batch, int sampleLimit)
        {
            if (IsResetBatch(batch))
            {
                resetBatch = batch;
                pendingBatch = null;
                return;
            }

            pendingBatch ??= new MutableSignalBatch();
            pendingBatch.Append(batch);
            pendingBatch.TrimToNewest(sampleLimit);
        }

        public List<LiveSignalBatch> Take()
        {
            var batches = new List<LiveSignalBatch>(2);
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

    private sealed class MutableSignalBatch
    {
        private readonly List<double> travelTimes = [];
        private readonly List<double> frontTravel = [];
        private readonly List<double> rearTravel = [];
        private readonly List<double> velocityTimes = [];
        private readonly List<double> frontVelocity = [];
        private readonly List<double> rearVelocity = [];
        private readonly Dictionary<LiveImuLocation, List<double>> imuTimes = [];
        private readonly Dictionary<LiveImuLocation, List<double>> imuVibrationRms = [];
        private readonly List<double> framePitchRollTimes = [];
        private readonly List<double> framePitchDegrees = [];
        private readonly List<double> frameRollDegrees = [];

        private long revision;

        public bool HasContent => travelTimes.Count > 0
            || frontTravel.Count > 0
            || rearTravel.Count > 0
            || velocityTimes.Count > 0
            || frontVelocity.Count > 0
            || rearVelocity.Count > 0
            || HasDictionaryContent(imuTimes)
            || HasDictionaryContent(imuVibrationRms)
            || framePitchRollTimes.Count > 0
            || framePitchDegrees.Count > 0
            || frameRollDegrees.Count > 0;

        public void Append(LiveSignalBatch batch)
        {
            revision = Math.Max(revision, batch.Revision);
            travelTimes.AddRange(batch.TravelTimes);
            frontTravel.AddRange(batch.FrontTravel);
            rearTravel.AddRange(batch.RearTravel);
            velocityTimes.AddRange(batch.VelocityTimes);
            frontVelocity.AddRange(batch.FrontVelocity);
            rearVelocity.AddRange(batch.RearVelocity);
            AppendDictionary(imuTimes, batch.ImuTimes);
            AppendDictionary(imuVibrationRms, batch.ImuVibrationRms);
            framePitchRollTimes.AddRange(batch.FramePitchRollTimes);
            framePitchDegrees.AddRange(batch.FramePitchDegrees);
            frameRollDegrees.AddRange(batch.FrameRollDegrees);
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
            TrimDictionaryToNewest(imuVibrationRms, sampleLimit);
            TrimListToNewest(framePitchRollTimes, sampleLimit);
            TrimListToNewest(framePitchDegrees, sampleLimit);
            TrimListToNewest(frameRollDegrees, sampleLimit);
        }

        public LiveSignalBatch ToBatch()
        {
            return new LiveSignalBatch(
                Revision: revision,
                TravelTimes: travelTimes.ToArray(),
                FrontTravel: frontTravel.ToArray(),
                RearTravel: rearTravel.ToArray(),
                VelocityTimes: velocityTimes.ToArray(),
                FrontVelocity: frontVelocity.ToArray(),
                RearVelocity: rearVelocity.ToArray(),
                ImuTimes: CloneDictionary(imuTimes),
                ImuVibrationRms: CloneDictionary(imuVibrationRms),
                FramePitchRollTimes: framePitchRollTimes.ToArray(),
                FramePitchDegrees: framePitchDegrees.ToArray(),
                FrameRollDegrees: frameRollDegrees.ToArray());
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
