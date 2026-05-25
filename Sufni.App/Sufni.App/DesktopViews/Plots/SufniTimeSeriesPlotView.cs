using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Sufni.App.Behaviors;
using Sufni.App.Models;
using Sufni.App.Plots;
using Sufni.App.Theming;
using Sufni.App.ViewModels.Editors;
using Sufni.Telemetry;

namespace Sufni.App.DesktopViews.Plots;

public abstract class SufniTimeSeriesPlotView : SufniTimelinePlotView
{
    private const double ClickMovementThresholdPixels = 4;
    private const double MarkerHitThresholdPixels = 8;

    private TelemetryPlot? plot;
    private bool hasPendingLoad;
    private bool isSelectingAnalysisRange;
    private bool isGraphClickCandidate;
    private bool suppressGraphClickClear;
    private bool suppressLegendTogglePointerRelease;
    private Point graphClickStartPoint;
    private double selectionStartSeconds;
    private double selectionEndSeconds;
    private IDisposable? mobileAnalysisRangeLongPress;
    private Point mobileAnalysisRangeLongPressStartPoint;
    private double mobileAnalysisRangeLongPressSeconds;

    protected TelemetryPlot PlotModel => plot!;
    protected bool HasPlotModel => plot is not null;
    protected override TelemetryPlot? TimelinePlot => plot;
    public bool IsPlotReady => plot is not null && HasPlotControl;

    public static readonly StyledProperty<PlotSmoothingLevel> SmoothingLevelProperty =
        AvaloniaProperty.Register<SufniTimeSeriesPlotView, PlotSmoothingLevel>(nameof(SmoothingLevel));

    public PlotSmoothingLevel SmoothingLevel
    {
        get => GetValue(SmoothingLevelProperty);
        set => SetValue(SmoothingLevelProperty, value);
    }

    public static readonly StyledProperty<bool> HideRightAxisProperty =
        AvaloniaProperty.Register<SufniTimeSeriesPlotView, bool>(nameof(HideRightAxis));

    public bool HideRightAxis
    {
        get => GetValue(HideRightAxisProperty);
        set => SetValue(HideRightAxisProperty, value);
    }

    public static readonly StyledProperty<bool> ShowAirtimeProperty =
        AvaloniaProperty.Register<SufniTimeSeriesPlotView, bool>(nameof(ShowAirtime));

    public bool ShowAirtime
    {
        get => GetValue(ShowAirtimeProperty);
        set => SetValue(ShowAirtimeProperty, value);
    }

    public static readonly StyledProperty<TelemetryTimeRange?> AnalysisRangeProperty =
        AvaloniaProperty.Register<SufniTimeSeriesPlotView, TelemetryTimeRange?>(nameof(AnalysisRange));

    public TelemetryTimeRange? AnalysisRange
    {
        get => GetValue(AnalysisRangeProperty);
        set => SetValue(AnalysisRangeProperty, value);
    }

    public static readonly StyledProperty<IRecordedSessionGraphWorkspace?> GraphWorkspaceProperty =
        AvaloniaProperty.Register<SufniTimeSeriesPlotView, IRecordedSessionGraphWorkspace?>(nameof(GraphWorkspace));

    public IRecordedSessionGraphWorkspace? GraphWorkspace
    {
        get => GetValue(GraphWorkspaceProperty);
        set => SetValue(GraphWorkspaceProperty, value);
    }

    public static readonly StyledProperty<TelemetrySourceVisibilityStore?> SourceVisibilityProperty =
        AvaloniaProperty.Register<SufniTimeSeriesPlotView, TelemetrySourceVisibilityStore?>(nameof(SourceVisibility));

    public TelemetrySourceVisibilityStore? SourceVisibility
    {
        get => GetValue(SourceVisibilityProperty);
        set => SetValue(SourceVisibilityProperty, value);
    }

    protected virtual TelemetryData? MarkerSource => null;

    protected SufniTimeSeriesPlotView()
    {
        PropertyChanged += (_, e) =>
        {
            switch (e.Property.Name)
            {
                case nameof(SmoothingLevel):
                case nameof(HideRightAxis):
                case nameof(SourceVisibility):
                    RequestReload();
                    break;

                case nameof(ShowAirtime):
                    ApplyAirtimeVisibility(refresh: true);
                    break;

                case nameof(PlotFigureBackground):
                case nameof(PlotDataBackground):
                    ApplyPlotBackgroundColors();
                    break;

                case nameof(AnalysisRange):
                    OnAnalysisRangeChanged();
                    break;

                case nameof(IsVisible):
                    TryApplyPendingLoad();
                    break;
            }
        };

        EffectiveViewportChanged += (_, _) => TryApplyPendingLoad();
    }

    protected void SetPlotModel(TelemetryPlot plotModel)
    {
        plot = plotModel;
        TryApplyPendingLoad();
    }

    protected void RequestReload()
    {
        hasPendingLoad = true;
        TryApplyPendingLoad();
    }

    protected void ReloadPlot()
    {
        if (!CanLoadNow())
        {
            hasPendingLoad = true;
            return;
        }

        LoadIntoPlot();
    }

    public void SetCursorPosition(double position)
    {
        plot?.SetCursorPosition(position);
        RefreshPlot();
    }

    public void SetCursorPositionWithReadout(double position)
    {
        plot?.SetCursorPositionWithReadout(position);
        RefreshPlot();
    }

    public void HideCursorReadout()
    {
        plot?.HideCursorReadout();
        RefreshPlot();
    }

    protected void InitializeCursorReadoutInteractions()
    {
        void UpdateCursor(PointerEventArgs args)
        {
            SetCursorPositionWithReadoutFromPointer(args);
        }

        PlotControl.AddHandler<PointerPressedEventArgs>(
            InputElement.PointerPressedEvent,
            (_, args) =>
            {
                if (TryToggleInteractiveLegend(args))
                {
                    return;
                }

                UpdateCursor(args);

                var workspace = GraphWorkspace;
                if (!IsPrimaryPointerPressed(args) || workspace is null || TimelineDurationSeconds is not > 0)
                {
                    return;
                }

                var point = args.GetPosition(PlotControl);
                if (UsesMobileAnalysisRangeGestures())
                {
                    StartMobileAnalysisRangeLongPress(args, point);
                    isGraphClickCandidate = true;
                    graphClickStartPoint = point;
                    return;
                }

                if (args.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    isSelectingAnalysisRange = true;
                    isGraphClickCandidate = false;
                    selectionStartSeconds = GetClampedTimeSeconds(args);
                    selectionEndSeconds = selectionStartSeconds;
                    SetPreviewRange(selectionStartSeconds, selectionEndSeconds);
                    PlotControl.Cursor = new Cursor(StandardCursorType.Cross);
                    args.Pointer.Capture(PlotControl);
                    args.Handled = true;
                    RefreshPlot();
                    return;
                }

                if (TryGetHitMarkerSeconds(args, out var markerSeconds))
                {
                    workspace.SetAnalysisRangeBoundary(markerSeconds);
                    isGraphClickCandidate = false;
                    args.Handled = true;
                    return;
                }

                isGraphClickCandidate = true;
                graphClickStartPoint = point;
            },
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

        PlotControl.PointerMoved += (_, args) =>
        {
            UpdateCursor(args);
            if (isSelectingAnalysisRange)
            {
                selectionEndSeconds = GetClampedTimeSeconds(args);
                SetPreviewRange(selectionStartSeconds, selectionEndSeconds);
                args.Handled = true;
                RefreshPlot();
                return;
            }

            if (isGraphClickCandidate && HasExceededClickMovement(args))
            {
                CancelMobileAnalysisRangeLongPress();
                isGraphClickCandidate = false;
            }
        };
        PlotControl.PointerExited += (_, _) => HideCursorReadout();

        PlotControl.AddHandler<PointerReleasedEventArgs>(
            InputElement.PointerReleasedEvent,
            (_, args) =>
            {
                if (suppressLegendTogglePointerRelease)
                {
                    suppressLegendTogglePointerRelease = false;
                    args.Handled = true;
                    return;
                }

                UpdateCursor(args);
                if (isSelectingAnalysisRange)
                {
                    CompleteSelection(selectionEndSeconds);
                    UpdateTimelineRange();
                    args.Pointer.Capture(null);
                    args.Handled = true;
                    return;
                }

                if (isGraphClickCandidate && !HasExceededClickMovement(args))
                {
                    if (!suppressGraphClickClear)
                    {
                        GraphWorkspace?.ClearAnalysisRange();
                    }
                }

                CancelMobileAnalysisRangeLongPress();
                suppressGraphClickClear = false;
                isGraphClickCandidate = false;
                UpdateTimelineRange();
            },
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

        PlotControl.PointerWheelChanged += (_, _) => UpdateTimelineRange();
        PlotControl.PointerCaptureLost += (_, _) =>
        {
            if (isSelectingAnalysisRange)
            {
                CompleteSelection(selectionEndSeconds);
                UpdateTimelineRange();
            }

            isGraphClickCandidate = false;
            suppressGraphClickClear = false;
            CancelMobileAnalysisRangeLongPress();
            PlotControl.Cursor = Cursor.Default;
            SetPreviewRange(null, null);
            RefreshPlot();
        };
    }

    protected void SetCursorPositionWithReadoutFromPointer(PointerEventArgs args)
    {
        if (!TryGetTimelineSeconds(args, out var seconds) ||
            TimelineDurationSeconds is not { } duration ||
            duration <= 0)
        {
            return;
        }

        Timeline?.SetCursorPosition(seconds / duration);
        plot?.SetCursorPositionWithReadout(seconds);
        RefreshPlot();
    }

    protected bool TryGetTimelineSeconds(PointerEventArgs args, out double seconds)
    {
        seconds = default;
        if (!HasPlotControl || TimelineDurationSeconds is not { } duration || duration <= 0)
        {
            return false;
        }

        var point = args.GetPosition(PlotControl);
        var coords = PlotControl.Plot.GetCoordinates((float)point.X, (float)point.Y);
        seconds = Math.Clamp(coords.X, 0.0, duration);
        return true;
    }

    protected virtual void ApplyPlotOptions(TelemetryPlot plotModel)
    {
        plotModel.SmoothingLevel = SmoothingLevel;
        plotModel.HideRightAxis = HideRightAxis;
        plotModel.AnalysisRange = AnalysisRange;
        plotModel.SourceVisibility = SourceVisibility;
        var (figure, data) = ResolvePlotBackgrounds();
        plotModel.SetBackgroundColors(figure, data);
    }

    private void ApplyPlotBackgroundColors()
    {
        if (plot is null)
        {
            return;
        }

        var (figure, data) = ResolvePlotBackgrounds();
        plot.SetBackgroundColors(figure, data);
        RefreshPlot();
    }

    private (ScottPlot.Color Figure, ScottPlot.Color Data) ResolvePlotBackgrounds()
    {
        var plotTheme = CurrentTheme.Plot.Root;
        var figure = IsSet(PlotFigureBackgroundProperty)
            ? PlotFigureBackground.ToScottPlotColor()
            : plotTheme.Figure.ToScottPlotColor();
        var data = IsSet(PlotDataBackgroundProperty)
            ? PlotDataBackground.ToScottPlotColor()
            : plotTheme.Data.ToScottPlotColor();
        return (figure, data);
    }

    protected override void OnThemeChanged(SufniTheme theme)
    {
        if (plot is null)
        {
            return;
        }

        plot.ApplyTheme(theme);
        // Marker / span / readout colors that are baked at LoadTelemetryData time
        // need a full reload to repaint with the new palette.
        RequestReload();
    }

    protected abstract bool CanLoadPlotData { get; }
    protected abstract void LoadPlotData(TelemetryPlot plotModel);

    protected virtual void OnAnalysisRangeChanged()
    {
        if (plot is RecordedTimeSeriesPlot recordedPlot && IsPlotReady)
        {
            ApplyAnalysisRange(recordedPlot);
            RefreshPlot();
        }
    }

    protected override void OnViewportChanged()
    {
        base.OnViewportChanged();
        ApplyAirtimeVisibility(refresh: true);
    }

    private void ApplyAirtimeVisibility(bool refresh)
    {
        if (plot is not RecordedTimeSeriesPlot recordedPlot || !HasPlotControl)
        {
            return;
        }

        var limits = PlotControl.Plot.Axes.GetLimits();
        var dataRect = PlotControl.Plot.LastRender.DataRect;
        double dataAreaWidthPixels = Math.Abs(dataRect.Right - dataRect.Left);
        if (dataAreaWidthPixels <= 0)
        {
            dataAreaWidthPixels = PlotControl.Bounds.Width;
        }

        recordedPlot.SetRangeOverlayVisibility(RecordedTimeRangeOverlayIds.Airtime, ShowAirtime);
        recordedPlot.UpdateRangeOverlayLabelVisibility(
            RecordedTimeRangeOverlayIds.Airtime,
            limits.Left,
            limits.Right,
            dataAreaWidthPixels);
        if (refresh)
        {
            RefreshPlot();
        }
    }

    private bool IsPrimaryPointerPressed(PointerEventArgs args)
    {
        var point = args.GetCurrentPoint(PlotControl);
        return point.Properties.IsLeftButtonPressed || args.Pointer.Type != PointerType.Mouse;
    }

    protected virtual IDisposable ScheduleMobileAnalysisRangeLongPress(Action callback)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            callback();
        };
        timer.Start();
        return new DispatcherTimerSubscription(timer);
    }

    private double GetClampedTimeSeconds(PointerEventArgs args)
    {
        return TryGetTimelineSeconds(args, out var seconds) ? seconds : 0;
    }

    private static bool UsesMobileAnalysisRangeGestures()
    {
        return App.Current?.IsDesktop == false;
    }

    private void StartMobileAnalysisRangeLongPress(PointerEventArgs args, Point startPoint)
    {
        CancelMobileAnalysisRangeLongPress();
        suppressGraphClickClear = false;
        mobileAnalysisRangeLongPressStartPoint = startPoint;
        mobileAnalysisRangeLongPressSeconds = GetClampedTimeSeconds(args);
        mobileAnalysisRangeLongPress = ScheduleMobileAnalysisRangeLongPress(CompleteMobileAnalysisRangeLongPress);
    }

    private void CancelMobileAnalysisRangeLongPress()
    {
        mobileAnalysisRangeLongPress?.Dispose();
        mobileAnalysisRangeLongPress = null;
    }

    private bool TryToggleInteractiveLegend(PointerEventArgs args)
    {
        if (plot is null || !HasPlotControl || !IsPrimaryPointerPressed(args))
        {
            return false;
        }

        var point = args.GetPosition(PlotControl);
        var pixel = PlotControl.ToScottPlotPixel(point);
        var plotSize = PlotControl.GetScottPlotPixelSize();
        if (!plot.TryToggleInteractiveLegendAt(pixel, plotSize))
        {
            return false;
        }

        CancelMobileAnalysisRangeLongPress();
        isSelectingAnalysisRange = false;
        isGraphClickCandidate = false;
        suppressGraphClickClear = false;
        suppressLegendTogglePointerRelease = true;
        PlotControl.Cursor = Cursor.Default;
        SetPreviewRange(null, null);
        plot.HideCursorReadout();
        args.Pointer.Capture(null);
        args.Handled = true;
        RefreshPlot();
        return true;
    }

    private void CompleteMobileAnalysisRangeLongPress()
    {
        CancelMobileAnalysisRangeLongPress();
        if (GraphWorkspace is null ||
            TimelineDurationSeconds is not { } duration ||
            duration <= 0 ||
            !IsPlotReady)
        {
            return;
        }

        GraphWorkspace.SetAnalysisRangeBoundary(mobileAnalysisRangeLongPressSeconds);
        RaiseEvent(new RoutedEventArgs(HapticFeedbackBehavior.LongPressFeedbackRequestedEvent));
        suppressGraphClickClear = true;
        isGraphClickCandidate = false;
        RefreshPlot();
    }

    private void SetPreviewRange(double? startSeconds, double? endSeconds)
    {
        if (plot is RecordedTimeSeriesPlot recordedPlot)
        {
            if (startSeconds is null || endSeconds is null)
            {
                recordedPlot.ClearRangeOverlaySet(RecordedTimeRangeOverlayIds.PreviewRange);
                return;
            }

            var registration = RecordedTimeRangeOverlayFactory.CreatePreviewRangeRegistration(
                startSeconds.Value,
                endSeconds.Value,
                CurrentTheme.Plot);
            recordedPlot.SetRangeOverlaySet(registration.Id, registration.Set);
            recordedPlot.SetRangeOverlayVisibility(registration.Id, registration.IsVisible);
        }
    }

    private bool TryGetHitMarkerSeconds(PointerEventArgs args, out double markerSeconds)
    {
        markerSeconds = default;
        var telemetry = MarkerSource;
        if (telemetry is null || telemetry.Markers.Length == 0)
        {
            return false;
        }

        var pointerSeconds = GetClampedTimeSeconds(args);
        var limits = PlotControl.Plot.Axes.GetLimits();
        var secondsPerPixel = Math.Abs(limits.Right - limits.Left) / Math.Max(1, PlotControl.Bounds.Width);
        var thresholdSeconds = Math.Max(0.02, secondsPerPixel * MarkerHitThresholdPixels);

        return RecordedTimeSeriesMarkerHitTester.TryGetHitMarkerSeconds(
            telemetry,
            pointerSeconds,
            thresholdSeconds,
            out markerSeconds);
    }

    private bool HasExceededClickMovement(PointerEventArgs args)
    {
        var point = args.GetPosition(PlotControl);
        var startPoint = mobileAnalysisRangeLongPress is not null
            ? mobileAnalysisRangeLongPressStartPoint
            : graphClickStartPoint;
        var delta = point - startPoint;
        return Math.Abs(delta.X) > ClickMovementThresholdPixels ||
               Math.Abs(delta.Y) > ClickMovementThresholdPixels;
    }

    private void CompleteSelection(double endSeconds)
    {
        isSelectingAnalysisRange = false;
        PlotControl.Cursor = Cursor.Default;
        SetPreviewRange(null, null);

        if (GraphWorkspace is null || TimelineDurationSeconds is not { } duration || duration <= 0)
        {
            return;
        }

        if (TelemetryTimeRange.TryCreateClamped(
                selectionStartSeconds,
                endSeconds,
                duration,
                out var range))
        {
            GraphWorkspace.SetAnalysisRange(range.StartSeconds, range.EndSeconds);
        }

        RefreshPlot();
    }

    private sealed class DispatcherTimerSubscription(DispatcherTimer timer) : IDisposable
    {
        public void Dispose() => timer.Stop();
    }

    private bool CanLoadNow()
    {
        return plot is not null && HasPlotControl;
    }

    private void TryApplyPendingLoad()
    {
        if (!hasPendingLoad || !CanLoadNow())
        {
            return;
        }

        hasPendingLoad = false;
        LoadIntoPlot();
    }

    private void LoadIntoPlot()
    {
        if (plot is null || !HasPlotControl)
        {
            return;
        }

        if (!CanLoadPlotData)
        {
            plot.Clear();
            RefreshPlot();
            return;
        }

        ApplyPlotOptions(plot);
        plot.Clear();
        LoadPlotData(plot);
        ApplyTimelineCursor();
        ApplyTimelineRange();
        OnPlotDataLoaded();
        RefreshPlot();
    }

    protected virtual void OnPlotDataLoaded()
    {
        if (plot is RecordedTimeSeriesPlot recordedPlot)
        {
            ApplyAnalysisRange(recordedPlot);
        }

        ApplyAirtimeVisibility(refresh: false);
    }

    private void ApplyAnalysisRange(RecordedTimeSeriesPlot recordedPlot)
    {
        if (AnalysisRange is not { } range)
        {
            recordedPlot.ClearRangeOverlaySet(RecordedTimeRangeOverlayIds.AnalysisRange);
            return;
        }

        var registration = RecordedTimeRangeOverlayFactory.CreateAnalysisRangeRegistration(range, CurrentTheme.Plot);
        recordedPlot.SetRangeOverlaySet(registration.Id, registration.Set);
        recordedPlot.SetRangeOverlayVisibility(registration.Id, registration.IsVisible);
    }
}
