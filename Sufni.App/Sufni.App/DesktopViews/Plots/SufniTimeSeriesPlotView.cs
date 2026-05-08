using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Sufni.App.Models;
using Sufni.App.Plots;
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
    private Point graphClickStartPoint;
    private double selectionStartSeconds;
    private double selectionEndSeconds;

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

    protected virtual TelemetryData? MarkerSource => null;

    protected SufniTimeSeriesPlotView()
    {
        PropertyChanged += (_, e) =>
        {
            switch (e.Property.Name)
            {
                case nameof(SmoothingLevel):
                case nameof(HideRightAxis):
                    RequestReload();
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
                UpdateCursor(args);

                var workspace = GraphWorkspace;
                if (!IsLeftButtonPressed(args) || workspace is null || TimelineDurationSeconds is not > 0)
                {
                    return;
                }

                var point = args.GetPosition(PlotControl);
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
                    workspace.SetAnalysisRangeBoundaryFromMarker(markerSeconds);
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
                isGraphClickCandidate = false;
            }
        };
        PlotControl.PointerExited += (_, _) => HideCursorReadout();

        PlotControl.AddHandler<PointerReleasedEventArgs>(
            InputElement.PointerReleasedEvent,
            (_, args) =>
            {
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
                    GraphWorkspace?.ClearAnalysisRange();
                }

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
    }

    protected abstract bool CanLoadPlotData { get; }
    protected abstract void LoadPlotData(TelemetryPlot plotModel);

    protected virtual void OnAnalysisRangeChanged()
    {
        if (plot is RecordedTimeSeriesPlot recordedPlot && IsPlotReady)
        {
            recordedPlot.SetAnalysisRange(AnalysisRange);
            RefreshPlot();
        }
    }

    private bool IsLeftButtonPressed(PointerEventArgs args)
        => args.GetCurrentPoint(PlotControl).Properties.IsLeftButtonPressed;

    private double GetClampedTimeSeconds(PointerEventArgs args)
    {
        return TryGetTimelineSeconds(args, out var seconds) ? seconds : 0;
    }

    private void SetPreviewRange(double? startSeconds, double? endSeconds)
    {
        if (plot is RecordedTimeSeriesPlot recordedPlot)
        {
            recordedPlot.SetPreviewRange(startSeconds, endSeconds);
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
        var delta = point - graphClickStartPoint;
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
        RefreshPlot();
    }
}
