using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Sufni.Telemetry;
using ScottPlotPixel = ScottPlot.Pixel;
using Sufni.App.ExtensionHost.Contracts.Plots;
using Sufni.App.ExtensionHost.Contracts.Presentation;

using Sufni.App.Acquisition.Models;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Plots;
using Sufni.App.Shared.Plots;
using Sufni.App.Theming;
using Sufni.App.Sessions.Plots.Views.Plots;
using Sufni.App.Shared.Common;
using Sufni.App.Shared.Views.Input;
using Sufni.App.Infrastructure.Theming;
namespace Sufni.App.Shared.Views.Plots;

public abstract class SufniTimeSeriesPlotView : SufniTimelinePlotView
{
    private const double ClickMovementThresholdPixels = 4;
    private const double MarkerHitThresholdPixels = 8;

    private TelemetryPlot? plot;
    private bool hasPendingLoad;
    private bool loadFlushQueued;
    private bool isSelectingAnalysisRange;
    private bool isPlotClickCandidate;
    private bool isPlaybackStopClickCandidate;
    private bool suppressLegendTogglePointerRelease;
    private bool isAttachedToVisualTree;
    private Point plotClickStartPoint;
    private Point playbackStopClickStartPoint;
    private double selectionStartSeconds;
    private double selectionEndSeconds;
    private readonly HashSet<string> appliedTimeRangeOverlayIds = new(StringComparer.Ordinal);
    private IDisposable? touchContextMenuLongPress;
    private IDisposable? pendingPlotClickEffects;
    private Point touchContextMenuLongPressStartPoint;
    private TopLevel? keyDownTopLevel;

    protected TelemetryPlot PlotModel => plot!;
    protected bool HasPlotModel => plot is not null;
    protected override TelemetryPlot? TimelinePlot => plot;
    public bool IsPlotReady => isAttachedToVisualTree && plot is not null && HasPlotControl;

    public static readonly StyledProperty<string?> SignalRowIdProperty =
        AvaloniaProperty.Register<SufniTimeSeriesPlotView, string?>(nameof(SignalRowId));

    public string? SignalRowId
    {
        get => GetValue(SignalRowIdProperty);
        set => SetValue(SignalRowIdProperty, value);
    }

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

    public static readonly StyledProperty<IReadOnlyList<RecordedTimeRangeOverlaySetRegistration>?> TimeRangeOverlaysProperty =
        AvaloniaProperty.Register<SufniTimeSeriesPlotView, IReadOnlyList<RecordedTimeRangeOverlaySetRegistration>?>(
            nameof(TimeRangeOverlays));

    public IReadOnlyList<RecordedTimeRangeOverlaySetRegistration>? TimeRangeOverlays
    {
        get => GetValue(TimeRangeOverlaysProperty);
        set => SetValue(TimeRangeOverlaysProperty, value);
    }

    public static readonly StyledProperty<IReadOnlyList<TelemetryHighlightRange>?> AnalysisSelectionHighlightRangesProperty =
        AvaloniaProperty.Register<SufniTimeSeriesPlotView, IReadOnlyList<TelemetryHighlightRange>?>(
            nameof(AnalysisSelectionHighlightRanges));

    public IReadOnlyList<TelemetryHighlightRange>? AnalysisSelectionHighlightRanges
    {
        get => GetValue(AnalysisSelectionHighlightRangesProperty);
        set => SetValue(AnalysisSelectionHighlightRangesProperty, value);
    }

    public static readonly StyledProperty<bool> ShowAnalysisSelectionProperty =
        AvaloniaProperty.Register<SufniTimeSeriesPlotView, bool>(nameof(ShowAnalysisSelection));

    public bool ShowAnalysisSelection
    {
        get => GetValue(ShowAnalysisSelectionProperty);
        set => SetValue(ShowAnalysisSelectionProperty, value);
    }

    public static readonly StyledProperty<IReadOnlyList<TelemetryPlotContextMenuAction>?> AdditionalContextMenuActionsProperty =
        AvaloniaProperty.Register<SufniTimeSeriesPlotView, IReadOnlyList<TelemetryPlotContextMenuAction>?>(
            nameof(AdditionalContextMenuActions));

    public IReadOnlyList<TelemetryPlotContextMenuAction>? AdditionalContextMenuActions
    {
        get => GetValue(AdditionalContextMenuActionsProperty);
        set => SetValue(AdditionalContextMenuActionsProperty, value);
    }

    public static readonly StyledProperty<IRecordedSessionSignalsWorkspace?> SignalsWorkspaceProperty =
        AvaloniaProperty.Register<SufniTimeSeriesPlotView, IRecordedSessionSignalsWorkspace?>(nameof(SignalsWorkspace));

    public IRecordedSessionSignalsWorkspace? SignalsWorkspace
    {
        get => GetValue(SignalsWorkspaceProperty);
        set => SetValue(SignalsWorkspaceProperty, value);
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

                case nameof(ShowAnalysisSelection):
                    ApplyAnalysisSelectionVisibility(refresh: true);
                    break;

                case nameof(AnalysisSelectionHighlightRanges):
                    ApplyAnalysisSelectionRanges(refresh: true);
                    break;

                case nameof(PlotFigureBackground):
                case nameof(PlotDataBackground):
                    ApplyPlotBackgroundColors();
                    break;

                case nameof(AnalysisRange):
                    OnAnalysisRangeChanged();
                    break;

                case nameof(TimeRangeOverlays):
                    ApplyTimeRangeOverlays(refresh: true);
                    break;

                case nameof(IsVisible):
                    TryApplyPendingLoad();
                    break;
            }
        };

        EffectiveViewportChanged += (_, _) => TryApplyPendingLoad();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        isAttachedToVisualTree = true;
        base.OnAttachedToVisualTree(e);

        keyDownTopLevel = TopLevel.GetTopLevel(this);
        keyDownTopLevel?.AddHandler<KeyEventArgs>(
            KeyDownEvent,
            OnTopLevelKeyDown,
            RoutingStrategies.Bubble);
        TryApplyPendingLoad();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        isAttachedToVisualTree = false;
        keyDownTopLevel?.RemoveHandler(KeyDownEvent, OnTopLevelKeyDown);
        keyDownTopLevel = null;
        CancelPendingPlotClickEffects();

        base.OnDetachedFromVisualTree(e);
    }

    private void OnTopLevelKeyDown(object? sender, KeyEventArgs args)
    {
        // Space toggles timeline playback only while the pointer hovers this
        // plot and a cursor is published; key input belonging to text-editing
        // controls must keep typing spaces.
        if (args.Handled ||
            args.Key != Key.Space ||
            args.KeyModifiers != KeyModifiers.None ||
            args.Source is TextBox)
        {
            return;
        }

        if (!IsPlotReady ||
            !PlotControl.IsPointerOver ||
            Timeline is not { NormalizedCursorPosition: not null } timeline)
        {
            return;
        }

        timeline.RequestPlaybackToggle();
        args.Handled = true;
    }

    protected void SetPlotModel(TelemetryPlot plotModel)
    {
        plot = plotModel;
        TryApplyPendingLoad();
    }

    protected void RequestReload()
    {
        hasPendingLoad = true;
        QueuePendingLoad();
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
        if (!IsPlotReady)
        {
            return;
        }

        plot!.SetCursorPosition(position);
        RefreshPlot();
    }

    public void SetCursorPositionWithReadout(double position)
    {
        if (!IsPlotReady)
        {
            return;
        }

        plot!.SetCursorPositionWithReadout(position);
        RefreshPlot();
    }

    public void HideCursorReadout()
    {
        if (!IsPlotReady)
        {
            return;
        }

        plot!.HideCursorReadout();
        RefreshPlot();
    }

    protected void InitializeCursorReadoutInteractions()
    {
        InstallTelemetryPlotContextMenu();

        void UpdateCursor(PointerEventArgs args)
        {
            SetCursorPositionWithReadoutFromPointer(args);
        }

        PlotControl.AddHandler<PointerPressedEventArgs>(
            InputElement.PointerPressedEvent,
            (_, args) =>
            {
                if (args.ClickCount > 1)
                {
                    CancelPendingPlotClickEffects();
                    CancelTouchContextMenuLongPress();
                    isPlaybackStopClickCandidate = false;
                    isPlotClickCandidate = false;
                    return;
                }

                // Playback must stop on a plain click only; a drag that pans
                // or zooms the viewport keeps it running, so the stop request
                // is deferred to the release and cancelled on movement.
                if (IsPrimaryPointerPressed(args))
                {
                    isPlaybackStopClickCandidate = true;
                    playbackStopClickStartPoint = args.GetPosition(PlotControl);
                }

                if (TryShowMobileTelemetryPlotContextMenu(args))
                {
                    return;
                }

                if (TryToggleInteractiveLegend(args))
                {
                    return;
                }

                UpdateCursor(args);

                var workspace = SignalsWorkspace;
                if (!IsPrimaryPointerPressed(args) || workspace is null || TimelineDurationSeconds is not > 0)
                {
                    return;
                }

                var point = args.GetPosition(PlotControl);
                if (UsesTouchContextMenuLongPress())
                {
                    StartTouchContextMenuLongPress(point);
                    isPlotClickCandidate = true;
                    plotClickStartPoint = point;
                    return;
                }

                if (args.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    isSelectingAnalysisRange = true;
                    isPlotClickCandidate = false;
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
                    isPlotClickCandidate = false;
                    args.Handled = true;
                    return;
                }

                isPlotClickCandidate = true;
                plotClickStartPoint = point;
            },
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

        PlotControl.PointerMoved += (_, args) =>
        {
            UpdateCursor(args);
            if (isPlaybackStopClickCandidate && HasExceededPlaybackStopClickMovement(args))
            {
                isPlaybackStopClickCandidate = false;
            }

            if (isSelectingAnalysisRange)
            {
                selectionEndSeconds = GetClampedTimeSeconds(args);
                SetPreviewRange(selectionStartSeconds, selectionEndSeconds);
                args.Handled = true;
                RefreshPlot();
                return;
            }

            if (isPlotClickCandidate && HasExceededClickMovement(args))
            {
                CancelTouchContextMenuLongPress();
                isPlotClickCandidate = false;
            }
        };
        PlotControl.PointerExited += (_, _) => HideCursorReadout();

        PlotControl.AddHandler<PointerReleasedEventArgs>(
            InputElement.PointerReleasedEvent,
            (_, args) =>
            {
                var stopPlayback = false;

                if (isPlaybackStopClickCandidate)
                {
                    isPlaybackStopClickCandidate = false;
                    if (!HasExceededPlaybackStopClickMovement(args))
                    {
                        stopPlayback = true;
                    }
                }

                if (suppressLegendTogglePointerRelease)
                {
                    suppressLegendTogglePointerRelease = false;
                    SchedulePlotClickEffectsIfNeeded(args, stopPlayback, clearSelection: false);
                    args.Handled = true;
                    return;
                }

                UpdateCursor(args);
                if (isSelectingAnalysisRange)
                {
                    CompleteSelection(selectionEndSeconds);
                    UpdateTimelineRange();
                    args.Pointer.Capture(null);
                    SchedulePlotClickEffectsIfNeeded(args, stopPlayback, clearSelection: false);
                    args.Handled = true;
                    return;
                }

                var clearSelection = isPlotClickCandidate && !HasExceededClickMovement(args);
                SchedulePlotClickEffectsIfNeeded(args, stopPlayback, clearSelection);
                CancelTouchContextMenuLongPress();
                isPlotClickCandidate = false;
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

            isPlotClickCandidate = false;
            isPlaybackStopClickCandidate = false;
            CancelTouchContextMenuLongPress();
            PlotControl.Cursor = Cursor.Default;
            SetPreviewRange(null, null);
            RefreshPlot();
        };
    }

    protected void SetCursorPositionWithReadoutFromPointer(PointerEventArgs args)
    {
        // While timeline playback drives the cursor, pointer moves must not
        // fight it; a primary press stops playback first, so click-to-place
        // still works.
        if (Timeline is { IsPlaybackActive: true })
        {
            return;
        }

        if (!TryGetTimelineSeconds(args, out var seconds) ||
            TimelineDurationSeconds is not { } duration ||
            duration <= 0)
        {
            return;
        }

        Timeline?.SetCursorPosition(seconds / duration);
        if (!IsPlotReady)
        {
            return;
        }

        plot!.SetCursorPositionWithReadout(seconds);
        RefreshPlot();
    }

    protected bool TryGetTimelineSeconds(PointerEventArgs args, out double seconds)
    {
        seconds = default;
        if (!IsPlotReady || TimelineDurationSeconds is not { } duration || duration <= 0)
        {
            return false;
        }

        var point = args.GetPosition(PlotControl);
        var coords = PlotControl.Plot.GetCoordinates((float)point.X, (float)point.Y);
        return TelemetryTimeRange.TryClampBoundary(coords.X, duration, out seconds);
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
        if (!IsPlotReady)
        {
            return;
        }

        var (figure, data) = ResolvePlotBackgrounds();
        plot!.SetBackgroundColors(figure, data);
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
        if (!IsPlotReady)
        {
            return;
        }

        plot!.ApplyTheme(theme);
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
        if (plot is not RecordedTimeSeriesPlot recordedPlot || !IsPlotReady)
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

    private void ApplyAnalysisSelectionVisibility(bool refresh)
    {
        if (plot is not RecordedTimeSeriesPlot recordedPlot || !IsPlotReady)
        {
            return;
        }

        recordedPlot.SetRangeOverlayVisibility(RecordedTimeRangeOverlayIds.AnalysisSelection, ShowAnalysisSelection);
        if (refresh)
        {
            RefreshPlot();
        }
    }

    private void ApplyAnalysisSelectionRanges(bool refresh)
    {
        if (plot is not RecordedTimeSeriesPlot recordedPlot || !IsPlotReady)
        {
            return;
        }

        var ranges = AnalysisSelectionHighlightRanges ?? [];
        if (ranges.Count == 0)
        {
            recordedPlot.ClearRangeOverlaySet(RecordedTimeRangeOverlayIds.AnalysisSelection);
        }
        else
        {
            var registration = RecordedTimeRangeOverlayFactory.CreateAnalysisSelectionRegistration(
                ranges,
                CurrentTheme.Plot,
                ShowAnalysisSelection);
            recordedPlot.SetRangeOverlaySet(registration.Id, registration.Set);
            recordedPlot.SetRangeOverlayVisibility(registration.Id, registration.IsVisible);
        }

        if (refresh)
        {
            RefreshPlot();
        }
    }

    private bool IsPrimaryPointerPressed(PointerEventArgs args)
    {
        return PointerGesture.IsPrimaryPressed(args, PlotControl);
    }

    protected virtual IDisposable ScheduleTouchContextMenuLongPress(Action callback)
    {
        return PeriodicUiTimer.ScheduleOnce(PointerGesture.AnalysisLongPressDelay, callback);
    }

    protected virtual IDisposable ScheduleDeferredPlotClickEffects(TimeSpan delay, Action callback)
    {
        return PeriodicUiTimer.ScheduleOnce(delay, callback);
    }

    private TimeSpan GetDoubleTapCancelWindow(PointerEventArgs args)
    {
        return Application.Current?.PlatformSettings?.GetDoubleTapTime(args.Pointer.Type)
               ?? TimeSpan.FromMilliseconds(300);
    }

    private void CancelPendingPlotClickEffects()
    {
        pendingPlotClickEffects?.Dispose();
        pendingPlotClickEffects = null;
    }

    private void SchedulePlotClickEffectsIfNeeded(PointerEventArgs args, bool stopPlayback, bool clearSelection)
    {
        if (!stopPlayback && !clearSelection)
        {
            return;
        }

        CancelPendingPlotClickEffects();
        var timeline = Timeline;
        var signalsWorkspace = SignalsWorkspace;
        var deferredCursorSeconds = default(double?);
        var deferredCursorPosition = default(double?);
        if (stopPlayback
            && timeline?.IsPlaybackActive == true
            && TimelineDurationSeconds is { } duration
            && duration > 0
            && TryGetTimelineSeconds(args, out var seconds))
        {
            deferredCursorSeconds = seconds;
            deferredCursorPosition = seconds / duration;
        }

        pendingPlotClickEffects = ScheduleDeferredPlotClickEffects(
            GetDoubleTapCancelWindow(args),
            () =>
            {
                pendingPlotClickEffects = null;
                if (stopPlayback)
                {
                    timeline?.RequestPlaybackStop();
                    if (timeline?.IsPlaybackActive == false
                        && deferredCursorSeconds is { } cursorSeconds
                        && deferredCursorPosition is { } cursorPosition)
                    {
                        timeline.SetCursorPosition(cursorPosition);
                        if (IsPlotReady)
                        {
                            plot!.SetCursorPositionWithReadout(cursorSeconds);
                            RefreshPlot();
                        }
                    }
                }

                if (clearSelection)
                {
                    signalsWorkspace?.ClearAnalysisRange();
                }
            });
    }

    private double GetClampedTimeSeconds(PointerEventArgs args)
    {
        return TryGetTimelineSeconds(args, out var seconds) ? seconds : 0;
    }

    private static bool UsesTouchContextMenuLongPress()
    {
        return PointerGesture.SupportsTouchLongPressContextMenu();
    }

    private void StartTouchContextMenuLongPress(Point startPoint)
    {
        CancelTouchContextMenuLongPress();
        touchContextMenuLongPressStartPoint = startPoint;
        touchContextMenuLongPress = ScheduleTouchContextMenuLongPress(CompleteTouchContextMenuLongPress);
    }

    private void CancelTouchContextMenuLongPress()
    {
        touchContextMenuLongPress?.Dispose();
        touchContextMenuLongPress = null;
    }

    private void InstallTelemetryPlotContextMenu()
    {
        PlotControl.Menu = CreateTelemetryPlotContextMenu(TryCreateContextMenuContext, GetContextMenuActions);
    }

    protected virtual ScottPlot.IPlotMenu CreateTelemetryPlotContextMenu(
        Func<ScottPlotPixel, TelemetryPlotContextMenuContext?> createContext,
        Func<TelemetryPlotContextMenuContext, IReadOnlyList<TelemetryPlotContextMenuAction>> getActions)
    {
        return new TelemetryPlotContextMenu(PlotControl, createContext, getActions);
    }

    private TelemetryPlotContextMenuContext? TryCreateContextMenuContext(ScottPlotPixel pixel)
    {
        if (string.IsNullOrWhiteSpace(SignalRowId) ||
            SignalsWorkspace is null ||
            !IsPlotReady ||
            TimelineDurationSeconds is not { } duration ||
            !double.IsFinite(duration) ||
            duration <= 0 ||
            !IsContextMenuPixelInDataArea(pixel))
        {
            return null;
        }

        var coordinates = PlotControl.Plot.GetCoordinates(pixel.X, pixel.Y);
        if (!TelemetryTimeRange.TryClampBoundary(coordinates.X, duration, out var seconds))
        {
            return null;
        }

        return new TelemetryPlotContextMenuContext(SignalRowId, seconds, duration, AnalysisRange);
    }

    private IReadOnlyList<TelemetryPlotContextMenuAction> GetContextMenuActions(TelemetryPlotContextMenuContext context)
    {
        var workspaceActions = SignalsWorkspace?.SignalPlotContextMenuActionsBySignalRowId.TryGetValue(context.RowId, out var actions) == true
            ? actions
            : [];
        return AdditionalContextMenuActions is { Count: > 0 } additionalActions
            ? workspaceActions.Concat(additionalActions).ToArray()
            : workspaceActions;
    }

    private bool TryShowMobileTelemetryPlotContextMenu(PointerEventArgs args)
    {
        if (!PointerGesture.SupportsTouchLongPressContextMenu() ||
            !PointerGesture.IsSecondaryPressed(args, PlotControl))
        {
            return false;
        }

        var point = args.GetPosition(PlotControl);
        if (!TryShowTelemetryPlotContextMenu(point))
        {
            return false;
        }

        args.Handled = true;
        return true;
    }

    private bool TryShowTelemetryPlotContextMenu(Point point)
    {
        if (!IsPlotReady)
        {
            return false;
        }

        var pixel = PlotControl.ToScottPlotPixel(point);
        if (!IsContextMenuPixelInDataArea(pixel))
        {
            return false;
        }

        CancelTouchContextMenuLongPress();
        isPlotClickCandidate = false;
        if (PlotControl.Menu is not { } menu)
        {
            return false;
        }

        menu.ShowContextMenu(pixel);
        return true;
    }

    private bool IsContextMenuPixelInDataArea(ScottPlotPixel pixel)
    {
        var dataRect = PlotControl.Plot.LastRender.DataRect;
        if (!dataRect.HasArea)
        {
            return false;
        }

        var left = Math.Min(dataRect.Left, dataRect.Right);
        var right = Math.Max(dataRect.Left, dataRect.Right);
        var top = Math.Min(dataRect.Top, dataRect.Bottom);
        var bottom = Math.Max(dataRect.Top, dataRect.Bottom);

        return pixel.X >= left && pixel.X <= right && pixel.Y >= top && pixel.Y <= bottom;
    }

    private bool TryToggleInteractiveLegend(PointerEventArgs args)
    {
        if (!IsPlotReady || !IsPrimaryPointerPressed(args))
        {
            return false;
        }

        var point = args.GetPosition(PlotControl);
        var pixel = PlotControl.ToScottPlotPixel(point);
        var plotSize = PlotControl.GetScottPlotPixelSize();
        if (!plot!.TryToggleInteractiveLegendAt(pixel, plotSize))
        {
            return false;
        }

        CancelTouchContextMenuLongPress();
        isSelectingAnalysisRange = false;
        isPlotClickCandidate = false;
        suppressLegendTogglePointerRelease = true;
        PlotControl.Cursor = Cursor.Default;
        SetPreviewRange(null, null);
        plot!.HideCursorReadout();
        args.Pointer.Capture(null);
        args.Handled = true;
        RefreshPlot();
        return true;
    }

    private void CompleteTouchContextMenuLongPress()
    {
        CancelTouchContextMenuLongPress();
        if (SignalsWorkspace is null ||
            TimelineDurationSeconds is not { } duration ||
            duration <= 0 ||
            !IsPlotReady)
        {
            return;
        }

        TryShowTelemetryPlotContextMenu(touchContextMenuLongPressStartPoint);
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

    private bool HasExceededPlaybackStopClickMovement(PointerEventArgs args)
    {
        var delta = args.GetPosition(PlotControl) - playbackStopClickStartPoint;
        return Math.Abs(delta.X) > ClickMovementThresholdPixels ||
               Math.Abs(delta.Y) > ClickMovementThresholdPixels;
    }

    private bool HasExceededClickMovement(PointerEventArgs args)
    {
        var point = args.GetPosition(PlotControl);
        var startPoint = touchContextMenuLongPress is not null
            ? touchContextMenuLongPressStartPoint
            : plotClickStartPoint;
        var delta = point - startPoint;
        return Math.Abs(delta.X) > ClickMovementThresholdPixels ||
               Math.Abs(delta.Y) > ClickMovementThresholdPixels;
    }

    private void CompleteSelection(double endSeconds)
    {
        isSelectingAnalysisRange = false;
        PlotControl.Cursor = Cursor.Default;
        SetPreviewRange(null, null);

        if (SignalsWorkspace is null || TimelineDurationSeconds is not { } duration || duration <= 0)
        {
            return;
        }

        if (TelemetryTimeRange.TryCreateClamped(
                selectionStartSeconds,
                endSeconds,
                duration,
                out var range))
        {
            SignalsWorkspace.SetAnalysisRange(range.StartSeconds, range.EndSeconds);
        }

        RefreshPlot();
    }

    private bool CanLoadNow()
    {
        return IsPlotReady;
    }

    private void TryApplyPendingLoad()
    {
        // Applies a pending load synchronously when conditions allow (plot
        // attached and ready). This is the attach / layout / plot-creation path,
        // so a freshly opened plot paints with its data in the same UI turn
        // rather than flashing empty first.
        if (!hasPendingLoad || !CanLoadNow())
        {
            return;
        }

        hasPendingLoad = false;
        LoadIntoPlot();
    }

    private void QueuePendingLoad()
    {
        // RequestReload() routes here so a reload is coalesced onto a later
        // dispatcher turn rather than run inline. A reload can be triggered as a
        // side effect of visual-tree teardown — the inherited ActualThemeVariant
        // or the DataContext reverting to its default while the plot is being
        // unparented raises property changes that reach the theme subscription
        // and bound-property handlers. Running the load inline there would
        // recompute heavy telemetry/analysis synchronously on the closing UI
        // turn. Deferring lets the load run after teardown has finished, where
        // CanLoadNow() is false and it no-ops. The synchronous attach/layout
        // path (TryApplyPendingLoad) still flushes any load already pending.
        if (!hasPendingLoad || loadFlushQueued)
        {
            return;
        }

        loadFlushQueued = true;
        Dispatcher.UIThread.Post(FlushPendingLoad, DispatcherPriority.Render);
    }

    private void FlushPendingLoad()
    {
        loadFlushQueued = false;
        if (!hasPendingLoad || !CanLoadNow())
        {
            return;
        }

        hasPendingLoad = false;
        LoadIntoPlot();
    }

    private void LoadIntoPlot()
    {
        if (!IsPlotReady)
        {
            return;
        }

        var plotModel = plot!;
        if (!CanLoadPlotData)
        {
            plotModel.Clear();
            RefreshPlot();
            return;
        }

        ApplyPlotOptions(plotModel);
        plotModel.Clear();
        LoadPlotData(plotModel);
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

        ApplyTimeRangeOverlays(refresh: false);
        ApplyAnalysisSelectionRanges(refresh: false);
        ApplyAnalysisSelectionVisibility(refresh: false);
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

    private void ApplyTimeRangeOverlays(bool refresh)
    {
        if (plot is not RecordedTimeSeriesPlot recordedPlot || !IsPlotReady)
        {
            return;
        }

        var registrations = TimeRangeOverlays ?? [];
        var activeIds = registrations
            .Select(registration => registration.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var staleId in appliedTimeRangeOverlayIds.Where(id => !activeIds.Contains(id)).ToArray())
        {
            recordedPlot.ClearRangeOverlaySet(staleId);
            appliedTimeRangeOverlayIds.Remove(staleId);
        }

        foreach (var registration in registrations)
        {
            recordedPlot.SetRangeOverlaySet(registration.Id, registration.Set);
            recordedPlot.SetRangeOverlayVisibility(registration.Id, registration.IsVisible);
            appliedTimeRangeOverlayIds.Add(registration.Id);
        }

        if (refresh)
        {
            RefreshPlot();
        }
    }
}
