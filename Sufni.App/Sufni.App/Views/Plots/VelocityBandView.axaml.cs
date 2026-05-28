using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Sufni.App.Behaviors;
using Sufni.App.SessionDetails;
using Sufni.App.ViewModels.Editors;
using Sufni.Telemetry;

namespace Sufni.App.Views.Plots;

public class VelocityBandView : TemplatedControl
{
    private const double ZoneTopOffset = 40.0;
    private const double ZoneBottomOffset = 55.0;
    private const double GuideLabelTopOffset = 24.0;
    private const double GuideLabelMinimumTop = 2.0;
    private const double ClickMovementThresholdPixels = 4.0;
    private const double GuideEndX = 10000.0;
    private const double VelocityLimit = SessionDampingSettings.VelocityHistogramLimitMmPerSecond;

    private static readonly GridLength highSpeedZoneLength = CreateZoneLength(
        SessionDampingSettings.VelocityHistogramLimitMmPerSecond -
        SessionDampingSettings.HighSpeedThresholdMmPerSecond);

    private static readonly GridLength lowSpeedZoneLength = CreateZoneLength(
        SessionDampingSettings.HighSpeedThresholdMmPerSecond);

    private Control? reboundHandle;
    private Control? compressionHandle;
    private DampingSpeedCircuit? activeCircuit;
    private double? activeGuideCutoff;
    private DampingSpeedCircuit? pendingMobileCircuit;
    private IDisposable? pendingMobileLongPress;
    private IPointer? pendingMobilePointer;
    private Point pointerStartPoint;
    private bool isDragging;

    private GridLength hsrZoneLength = highSpeedZoneLength;
    private GridLength lsrZoneLength = lowSpeedZoneLength;
    private GridLength lscZoneLength = lowSpeedZoneLength;
    private GridLength hscZoneLength = highSpeedZoneLength;
    private double reboundCutoff = DampingSpeedCutoffs.DefaultMmPerSecond;
    private double compressionCutoff = DampingSpeedCutoffs.DefaultMmPerSecond;
    private bool isDampingGuideVisible;
    private Point dampingGuideStartPoint;
    private Point dampingGuideEndPoint;
    private string dampingGuideLabel = string.Empty;
    private double dampingGuideLabelTop;

    public static readonly DirectProperty<VelocityBandView, GridLength> HsrZoneLengthProperty =
        AvaloniaProperty.RegisterDirect<VelocityBandView, GridLength>(
            nameof(HsrZoneLength),
            view => view.HsrZoneLength);

    public static readonly DirectProperty<VelocityBandView, GridLength> LsrZoneLengthProperty =
        AvaloniaProperty.RegisterDirect<VelocityBandView, GridLength>(
            nameof(LsrZoneLength),
            view => view.LsrZoneLength);

    public static readonly DirectProperty<VelocityBandView, GridLength> LscZoneLengthProperty =
        AvaloniaProperty.RegisterDirect<VelocityBandView, GridLength>(
            nameof(LscZoneLength),
            view => view.LscZoneLength);

    public static readonly DirectProperty<VelocityBandView, GridLength> HscZoneLengthProperty =
        AvaloniaProperty.RegisterDirect<VelocityBandView, GridLength>(
            nameof(HscZoneLength),
            view => view.HscZoneLength);

    public static readonly DirectProperty<VelocityBandView, double> ReboundCutoffProperty =
        AvaloniaProperty.RegisterDirect<VelocityBandView, double>(
            nameof(ReboundCutoff),
            view => view.ReboundCutoff);

    public static readonly DirectProperty<VelocityBandView, double> CompressionCutoffProperty =
        AvaloniaProperty.RegisterDirect<VelocityBandView, double>(
            nameof(CompressionCutoff),
            view => view.CompressionCutoff);

    public static readonly DirectProperty<VelocityBandView, bool> IsDampingGuideVisibleProperty =
        AvaloniaProperty.RegisterDirect<VelocityBandView, bool>(
            nameof(IsDampingGuideVisible),
            view => view.IsDampingGuideVisible);

    public static readonly DirectProperty<VelocityBandView, Point> DampingGuideStartPointProperty =
        AvaloniaProperty.RegisterDirect<VelocityBandView, Point>(
            nameof(DampingGuideStartPoint),
            view => view.DampingGuideStartPoint);

    public static readonly DirectProperty<VelocityBandView, Point> DampingGuideEndPointProperty =
        AvaloniaProperty.RegisterDirect<VelocityBandView, Point>(
            nameof(DampingGuideEndPoint),
            view => view.DampingGuideEndPoint);

    public static readonly DirectProperty<VelocityBandView, string> DampingGuideLabelProperty =
        AvaloniaProperty.RegisterDirect<VelocityBandView, string>(
            nameof(DampingGuideLabel),
            view => view.DampingGuideLabel);

    public static readonly DirectProperty<VelocityBandView, double> DampingGuideLabelTopProperty =
        AvaloniaProperty.RegisterDirect<VelocityBandView, double>(
            nameof(DampingGuideLabelTop),
            view => view.DampingGuideLabelTop);

    public GridLength HighSpeedZoneLength => highSpeedZoneLength;

    public GridLength LowSpeedZoneLength => lowSpeedZoneLength;

    public GridLength HsrZoneLength
    {
        get => hsrZoneLength;
        private set => SetAndRaise(HsrZoneLengthProperty, ref hsrZoneLength, value);
    }

    public GridLength LsrZoneLength
    {
        get => lsrZoneLength;
        private set => SetAndRaise(LsrZoneLengthProperty, ref lsrZoneLength, value);
    }

    public GridLength LscZoneLength
    {
        get => lscZoneLength;
        private set => SetAndRaise(LscZoneLengthProperty, ref lscZoneLength, value);
    }

    public GridLength HscZoneLength
    {
        get => hscZoneLength;
        private set => SetAndRaise(HscZoneLengthProperty, ref hscZoneLength, value);
    }

    public double ReboundCutoff
    {
        get => reboundCutoff;
        private set => SetAndRaise(ReboundCutoffProperty, ref reboundCutoff, value);
    }

    public double CompressionCutoff
    {
        get => compressionCutoff;
        private set => SetAndRaise(CompressionCutoffProperty, ref compressionCutoff, value);
    }

    public bool IsDampingGuideVisible
    {
        get => isDampingGuideVisible;
        private set => SetAndRaise(IsDampingGuideVisibleProperty, ref isDampingGuideVisible, value);
    }

    public Point DampingGuideStartPoint
    {
        get => dampingGuideStartPoint;
        private set => SetAndRaise(DampingGuideStartPointProperty, ref dampingGuideStartPoint, value);
    }

    public Point DampingGuideEndPoint
    {
        get => dampingGuideEndPoint;
        private set => SetAndRaise(DampingGuideEndPointProperty, ref dampingGuideEndPoint, value);
    }

    public string DampingGuideLabel
    {
        get => dampingGuideLabel;
        private set => SetAndRaise(DampingGuideLabelProperty, ref dampingGuideLabel, value);
    }

    public double DampingGuideLabelTop
    {
        get => dampingGuideLabelTop;
        private set => SetAndRaise(DampingGuideLabelTopProperty, ref dampingGuideLabelTop, value);
    }

    public static readonly StyledProperty<SuspensionType> SuspensionTypeProperty =
        AvaloniaProperty.Register<VelocityBandView, SuspensionType>(nameof(SuspensionType));

    public SuspensionType SuspensionType
    {
        get => GetValue(SuspensionTypeProperty);
        set => SetValue(SuspensionTypeProperty, value);
    }

    public static readonly StyledProperty<DampingSpeedCutoffs> DampingSpeedCutoffsProperty =
        AvaloniaProperty.Register<VelocityBandView, DampingSpeedCutoffs>(
            nameof(DampingSpeedCutoffs),
            DampingSpeedCutoffs.Default);

    public DampingSpeedCutoffs DampingSpeedCutoffs
    {
        get => GetValue(DampingSpeedCutoffsProperty);
        set => SetValue(DampingSpeedCutoffsProperty, value);
    }

    public static readonly StyledProperty<bool> CanEditDampingSpeedCutoffsProperty =
        AvaloniaProperty.Register<VelocityBandView, bool>(nameof(CanEditDampingSpeedCutoffs));

    public bool CanEditDampingSpeedCutoffs
    {
        get => GetValue(CanEditDampingSpeedCutoffsProperty);
        set => SetValue(CanEditDampingSpeedCutoffsProperty, value);
    }

    public static readonly StyledProperty<ISessionStatisticsWorkspace?> StatisticsWorkspaceProperty =
        AvaloniaProperty.Register<VelocityBandView, ISessionStatisticsWorkspace?>(nameof(StatisticsWorkspace));

    public ISessionStatisticsWorkspace? StatisticsWorkspace
    {
        get => GetValue(StatisticsWorkspaceProperty);
        set => SetValue(StatisticsWorkspaceProperty, value);
    }

    public static readonly StyledProperty<double?> HsrPercentageProperty = AvaloniaProperty.Register<VelocityBandView, double?>(
        "HsrPercentage");

    public double? HsrPercentage
    {
        get => GetValue(HsrPercentageProperty);
        set => SetValue(HsrPercentageProperty, value);
    }

    public static readonly StyledProperty<double?> LsrPercentageProperty = AvaloniaProperty.Register<VelocityBandView, double?>(
        "LsrPercentage");

    public double? LsrPercentage
    {
        get => GetValue(LsrPercentageProperty);
        set => SetValue(LsrPercentageProperty, value);
    }

    public static readonly StyledProperty<double?> LscPercentageProperty = AvaloniaProperty.Register<VelocityBandView, double?>(
        "LscPercentage");

    public double? LscPercentage
    {
        get => GetValue(LscPercentageProperty);
        set => SetValue(LscPercentageProperty, value);
    }

    public static readonly StyledProperty<double?> HscPercentageProperty = AvaloniaProperty.Register<VelocityBandView, double?>(
        "HscPercentage");

    public double? HscPercentage
    {
        get => GetValue(HscPercentageProperty);
        set => SetValue(HscPercentageProperty, value);
    }

    public VelocityBandView()
    {
        RefreshZoneLengths();
        RefreshGuide();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        DetachHandleEvents();
        base.OnApplyTemplate(e);

        reboundHandle = e.NameScope.Find<Control>("PART_ReboundHandle");
        compressionHandle = e.NameScope.Find<Control>("PART_CompressionHandle");
        AttachHandleEvents();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DampingSpeedCutoffsProperty ||
            change.Property == SuspensionTypeProperty)
        {
            RefreshZoneLengths();
            RefreshGuide();
        }
        else if (change.Property == BoundsProperty)
        {
            RefreshGuide();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (pendingMobileLongPress is not null && HasExceededClickMovement(e))
        {
            CancelPendingMobileLongPress();
            e.Handled = true;
            return;
        }

        if (!isDragging || activeCircuit is not { } circuit)
        {
            return;
        }

        PreviewFromPointer(e, circuit);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        CancelPendingMobileLongPress();
        if (!isDragging || activeCircuit is not { } circuit)
        {
            return;
        }

        var cutoff = GetCutoffFromPointer(e, circuit);
        isDragging = false;
        activeCircuit = null;
        activeGuideCutoff = null;
        RefreshGuide();
        e.Pointer.Capture(null);
        _ = CommitAsync(circuit, cutoff);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        CancelPendingMobileLongPress();
        if (!isDragging)
        {
            return;
        }

        isDragging = false;
        activeCircuit = null;
        activeGuideCutoff = null;
        StatisticsWorkspace?.CancelDampingSpeedCutoffPreview();
        RefreshGuide();
    }

    private void AttachHandleEvents()
    {
        if (reboundHandle is not null)
        {
            reboundHandle.PointerPressed += OnReboundHandlePointerPressed;
        }

        if (compressionHandle is not null)
        {
            compressionHandle.PointerPressed += OnCompressionHandlePointerPressed;
        }
    }

    private void DetachHandleEvents()
    {
        if (reboundHandle is not null)
        {
            reboundHandle.PointerPressed -= OnReboundHandlePointerPressed;
        }

        if (compressionHandle is not null)
        {
            compressionHandle.PointerPressed -= OnCompressionHandlePointerPressed;
        }
    }

    private void OnReboundHandlePointerPressed(object? sender, PointerPressedEventArgs e) =>
        BeginPointerPress(e, DampingSpeedCircuit.Rebound);

    private void OnCompressionHandlePointerPressed(object? sender, PointerPressedEventArgs e) =>
        BeginPointerPress(e, DampingSpeedCircuit.Compression);

    private void BeginPointerPress(PointerPressedEventArgs e, DampingSpeedCircuit circuit)
    {
        if (!CanStartDrag(e))
        {
            return;
        }

        pointerStartPoint = e.GetPosition(this);
        if (UsesMobileLongPress())
        {
            StartPendingMobileLongPress(e.Pointer, circuit);
        }
        else
        {
            StartDrag(e.Pointer, circuit);
            PreviewFromPointer(e, circuit);
        }

        e.Handled = true;
    }

    private bool CanStartDrag(PointerEventArgs e)
    {
        return CanEditDampingSpeedCutoffs &&
               StatisticsWorkspace is not null &&
               IsPrimaryPointerPressed(e);
    }

    private void StartPendingMobileLongPress(IPointer pointer, DampingSpeedCircuit circuit)
    {
        CancelPendingMobileLongPress();
        pendingMobilePointer = pointer;
        pendingMobileCircuit = circuit;
        var timer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = DampingSpeedCutoffs.MobileLongPressDelay,
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            CompleteMobileLongPress();
        };
        timer.Start();
        pendingMobileLongPress = new DispatcherTimerSubscription(timer);
    }

    private void CompleteMobileLongPress()
    {
        var pointer = pendingMobilePointer;
        var circuit = pendingMobileCircuit;
        CancelPendingMobileLongPress();

        if (pointer is null || circuit is null || !CanEditDampingSpeedCutoffs)
        {
            return;
        }

        RaiseEvent(new RoutedEventArgs(HapticFeedbackBehavior.LongPressFeedbackRequestedEvent));
        StartDrag(pointer, circuit.Value);
        PreviewCutoff(circuit.Value, GetCutoffFromPoint(pointerStartPoint, circuit.Value));
    }

    private void CancelPendingMobileLongPress()
    {
        pendingMobileLongPress?.Dispose();
        pendingMobileLongPress = null;
        pendingMobilePointer = null;
        pendingMobileCircuit = null;
    }

    private void StartDrag(IPointer pointer, DampingSpeedCircuit circuit)
    {
        activeCircuit = circuit;
        activeGuideCutoff = DampingSpeedCutoffs.Get(SuspensionType, circuit);
        isDragging = true;
        pointer.Capture(this);
        RefreshGuide();
    }

    private void PreviewFromPointer(PointerEventArgs e, DampingSpeedCircuit circuit)
    {
        PreviewCutoff(circuit, GetCutoffFromPointer(e, circuit));
    }

    private void PreviewCutoff(DampingSpeedCircuit circuit, double cutoff)
    {
        activeGuideCutoff = DampingSpeedCutoffs.RoundDragValue(cutoff);
        StatisticsWorkspace?.PreviewDampingSpeedCutoff(SuspensionType, circuit, cutoff);
        RefreshGuide();
    }

    private async Task CommitAsync(DampingSpeedCircuit circuit, double cutoff)
    {
        if (StatisticsWorkspace is null)
        {
            return;
        }

        await StatisticsWorkspace.CommitDampingSpeedCutoffAsync(SuspensionType, circuit, cutoff);
    }

    private double GetCutoffFromPointer(PointerEventArgs e, DampingSpeedCircuit circuit) =>
        GetCutoffFromPoint(e.GetPosition(this), circuit);

    private double GetCutoffFromPoint(Point point, DampingSpeedCircuit circuit)
    {
        var zoneHeight = GetZoneHeight();
        var zoneY = Math.Clamp(point.Y - ZoneTopOffset, 0, zoneHeight);
        var signedVelocity = zoneY / zoneHeight * VelocityLimit * 2.0 - VelocityLimit;
        return circuit == DampingSpeedCircuit.Rebound
            ? DampingSpeedCutoffs.Clamp(-signedVelocity)
            : DampingSpeedCutoffs.Clamp(signedVelocity);
    }

    private bool HasExceededClickMovement(PointerEventArgs e)
    {
        var delta = e.GetPosition(this) - pointerStartPoint;
        return Math.Abs(delta.X) > ClickMovementThresholdPixels ||
               Math.Abs(delta.Y) > ClickMovementThresholdPixels;
    }

    private bool IsPrimaryPointerPressed(PointerEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        return point.Properties.IsLeftButtonPressed || e.Pointer.Type != PointerType.Mouse;
    }

    private static bool UsesMobileLongPress() => App.Current?.IsDesktop == false;

    private void RefreshZoneLengths()
    {
        var side = DampingSpeedCutoffs.ForSide(SuspensionType);
        ReboundCutoff = DampingSpeedCutoffs.Clamp(side.ReboundMmPerSecond);
        CompressionCutoff = DampingSpeedCutoffs.Clamp(side.CompressionMmPerSecond);

        HsrZoneLength = CreateZoneLength(VelocityLimit - ReboundCutoff);
        LsrZoneLength = CreateZoneLength(ReboundCutoff);
        LscZoneLength = CreateZoneLength(CompressionCutoff);
        HscZoneLength = CreateZoneLength(VelocityLimit - CompressionCutoff);
    }

    private void RefreshGuide()
    {
        if (!isDragging || activeCircuit is null)
        {
            IsDampingGuideVisible = false;
            DampingGuideLabel = string.Empty;
            DampingGuideLabelTop = 0;
            return;
        }

        var cutoff = activeGuideCutoff ?? DampingSpeedCutoffs.Get(SuspensionType, activeCircuit.Value);
        var signedVelocity = activeCircuit == DampingSpeedCircuit.Rebound
            ? -cutoff
            : cutoff;
        var top = VelocityToTop(signedVelocity);
        DampingGuideStartPoint = new Point(0, top);
        DampingGuideEndPoint = new Point(GuideEndX, top);
        DampingGuideLabel = FormatGuideLabel(signedVelocity);
        DampingGuideLabelTop = GetGuideLabelTop(top);
        IsDampingGuideVisible = true;
    }

    private double VelocityToTop(double signedVelocity)
    {
        var zoneHeight = GetZoneHeight();
        var clamped = Math.Clamp(signedVelocity, -VelocityLimit, VelocityLimit);
        var ratio = (clamped + VelocityLimit) / (VelocityLimit * 2.0);
        return ZoneTopOffset + ratio * zoneHeight;
    }

    private double GetZoneHeight()
    {
        return Math.Max(1.0, Bounds.Height - ZoneTopOffset - ZoneBottomOffset);
    }

    private double GetGuideLabelTop(double guideTop)
    {
        var maxTop = Math.Max(GuideLabelMinimumTop, Bounds.Height - GuideLabelTopOffset);
        return Math.Clamp(guideTop - GuideLabelTopOffset, GuideLabelMinimumTop, maxTop);
    }

    private static string FormatGuideLabel(double signedVelocity) =>
        $"{signedVelocity.ToString("0", CultureInfo.CurrentCulture)} mm/s";

    private static GridLength CreateZoneLength(double value) =>
        new(Math.Max(0, value), GridUnitType.Star);

    private sealed class DispatcherTimerSubscription(DispatcherTimer timer) : IDisposable
    {
        public void Dispose() => timer.Stop();
    }
}
