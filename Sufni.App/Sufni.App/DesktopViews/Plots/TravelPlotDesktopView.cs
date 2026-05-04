using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Sufni.App.Plots;
using Sufni.App.ViewModels.Editors;
using Sufni.Telemetry;

namespace Sufni.App.DesktopViews.Plots;

public class TravelPlotDesktopView : SufniTelemetryPlotView
{
    private const double ClickMovementThresholdPixels = 4;
    private const double MarkerHitThresholdPixels = 8;
    private bool isSelectingAnalysisRange;
    private bool isGraphClickCandidate;
    private Point graphClickStartPoint;
    private double selectionStartSeconds;
    private double selectionEndSeconds;

    public static readonly StyledProperty<SufniTelemetryPlotView> VelocityPlotViewProperty =
        AvaloniaProperty.Register<TravelPlotDesktopView, SufniTelemetryPlotView>(nameof(VelocityPlotView));

    public SufniTelemetryPlotView VelocityPlotView
    {
        get => GetValue(VelocityPlotViewProperty);
        set => SetValue(VelocityPlotViewProperty, value);
    }

    public static readonly StyledProperty<SufniTelemetryPlotView> ImuPlotViewProperty =
        AvaloniaProperty.Register<TravelPlotDesktopView, SufniTelemetryPlotView>(nameof(ImuPlotView));

    public SufniTelemetryPlotView ImuPlotView
    {
        get => GetValue(ImuPlotViewProperty);
        set => SetValue(ImuPlotViewProperty, value);
    }

    protected override void CreatePlot()
    {
        SetPlotModel(new TravelPlot(PlotControl.Plot));

        void UpdateCursor(Avalonia.Input.PointerEventArgs args)
        {
            var point = args.GetPosition(PlotControl);
            var coords = PlotControl.Plot.GetCoordinates((float)point.X, (float)point.Y);
            if (Telemetry is null || Telemetry.Metadata.Duration <= 0) return;

            var normalizedCursorPosition = Math.Clamp(coords.X / Telemetry.Metadata.Duration, 0.0, 1.0);
            Timeline?.SetCursorPosition(normalizedCursorPosition);

            SetCursorPositionWithReadout(coords.X);
        }

        PlotControl.AddHandler<PointerPressedEventArgs>(
            InputElement.PointerPressedEvent,
            (_, args) =>
            {
                UpdateCursor(args);
                if (!IsLeftButtonPressed(args) || Telemetry is null || Telemetry.Metadata.Duration <= 0)
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
                    (PlotModel as TravelPlot)?.SetPreviewRange(selectionStartSeconds, selectionEndSeconds);
                    PlotControl.Cursor = new Cursor(StandardCursorType.Cross);
                    args.Pointer.Capture(PlotControl);
                    args.Handled = true;
                    RefreshPlot();
                    return;
                }

                if (TryGetHitMarkerSeconds(args, out var markerSeconds))
                {
                    GraphWorkspace?.SetAnalysisRangeBoundaryFromMarker(markerSeconds);
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
                (PlotModel as TravelPlot)?.SetPreviewRange(selectionStartSeconds, selectionEndSeconds);
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
            (PlotModel as TravelPlot)?.SetPreviewRange(null, null);
            RefreshPlot();
        };
    }

    protected override void OnAnalysisRangeChanged()
    {
        if (IsPlotReady && PlotModel is TravelPlot travelPlot)
        {
            travelPlot.SetAnalysisRange(AnalysisRange);
            RefreshPlot();
        }
    }

    private IRecordedSessionGraphWorkspace? GraphWorkspace => DataContext as IRecordedSessionGraphWorkspace;

    private bool IsLeftButtonPressed(PointerEventArgs args)
        => args.GetCurrentPoint(PlotControl).Properties.IsLeftButtonPressed;

    private double GetClampedTimeSeconds(PointerEventArgs args)
    {
        var telemetry = Telemetry;
        if (telemetry is null || telemetry.Metadata.Duration <= 0)
        {
            return 0;
        }

        var point = args.GetPosition(PlotControl);
        var coords = PlotControl.Plot.GetCoordinates((float)point.X, (float)point.Y);
        return Math.Clamp(coords.X, 0.0, telemetry.Metadata.Duration);
    }

    private bool TryGetHitMarkerSeconds(PointerEventArgs args, out double markerSeconds)
    {
        markerSeconds = default;
        var telemetry = Telemetry;
        if (telemetry is null || telemetry.Markers.Length == 0)
        {
            return false;
        }

        var pointerSeconds = GetClampedTimeSeconds(args);
        var limits = PlotControl.Plot.Axes.GetLimits();
        var secondsPerPixel = Math.Abs(limits.Right - limits.Left) / Math.Max(1, PlotControl.Bounds.Width);
        var thresholdSeconds = Math.Max(0.02, secondsPerPixel * MarkerHitThresholdPixels);

        foreach (var marker in telemetry.Markers)
        {
            if (double.IsNaN(marker.TimestampOffset) || double.IsInfinity(marker.TimestampOffset))
            {
                continue;
            }

            var markerSecondsCandidate = telemetry.Metadata.Duration > 0
                ? Math.Clamp(marker.TimestampOffset, 0, telemetry.Metadata.Duration)
                : marker.TimestampOffset;
            if (Math.Abs(markerSecondsCandidate - pointerSeconds) <= thresholdSeconds)
            {
                markerSeconds = markerSecondsCandidate;
                return true;
            }
        }

        return false;
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
        (PlotModel as TravelPlot)?.SetPreviewRange(null, null);

        if (Telemetry is null || Telemetry.Metadata.Duration <= 0)
        {
            return;
        }

        if (TelemetryTimeRange.TryCreateClamped(
                selectionStartSeconds,
                endSeconds,
                Telemetry.Metadata.Duration,
                out var range))
        {
            GraphWorkspace?.SetAnalysisRange(range.StartSeconds, range.EndSeconds);
        }

        RefreshPlot();
    }
}
