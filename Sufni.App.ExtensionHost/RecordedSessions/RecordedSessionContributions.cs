using System.Collections.Generic;
using Sufni.App.Plots;
using Sufni.App.Presentation;
using Sufni.App.ViewModels.Editors;
using Sufni.App.Views.Controls;

namespace Sufni.App.ExtensionHost.RecordedSessions;

public interface IRecordedSessionContribution : IExtensionContribution;

public enum RecordedSessionToolbarZone
{
    Leading,
    Trailing,
}

public sealed record RecordedSessionToolbarContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    RecordedSessionToolbarZone Zone,
    IRecordedSessionToolbarContributionViewModel ViewModel) : IRecordedSessionContribution;

public sealed record RecordedSessionPageContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    string DisplayName,
    IRecordedSessionPageContributionViewModel ViewModel,
    int RequestedIndex) : IRecordedSessionContribution;

public sealed record RecordedSessionMediaPaneContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    IRecordedSessionMediaPaneContributionViewModel ViewModel) : IRecordedSessionContribution;

public sealed record RecordedSessionMapOverlayContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    IReadOnlyList<RecordedSessionMapLineOverlay> Lines,
    IReadOnlyList<RecordedSessionMapPointOverlay> Points) : IRecordedSessionContribution;

public sealed record RecordedSessionMapCoordinate(
    double Latitude,
    double Longitude);

public sealed record RecordedSessionMapLineOverlay(
    IReadOnlyList<RecordedSessionMapCoordinate> Points,
    RecordedSessionMapLineStyle Style,
    string? Label = null);

public sealed record RecordedSessionMapPointOverlay(
    RecordedSessionMapCoordinate Coordinate,
    RecordedSessionMapPointStyle Style,
    string? Label = null);

public sealed record RecordedSessionMapColor(
    byte A,
    byte R,
    byte G,
    byte B);

public sealed record RecordedSessionMapLineStyle(
    RecordedSessionMapColor Color,
    double Width,
    double Opacity = 1.0);

public sealed record RecordedSessionMapPointStyle(
    RecordedSessionMapColor Fill,
    RecordedSessionMapColor Stroke,
    double Radius,
    double StrokeWidth = 1.0,
    double Opacity = 1.0);

public sealed record RecordedSessionStatisticsBannerContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    IRecordedSessionStatisticsBannerContributionViewModel ViewModel) : IRecordedSessionContribution;

public sealed record RecordedSessionStatisticsOverlayContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    RecordedSessionStatisticsPlotTarget TargetPlot,
    IRecordedSessionStatisticsOverlayContributionViewModel? ViewModel,
    RecordedSessionStatisticsPlotOverlayDescriptor? Overlay) : IRecordedSessionContribution;

public sealed record RecordedSessionStatisticsPlotOverlayDescriptor(
    IReadOnlyList<RecordedSessionPlotLineOverlay> Lines,
    IReadOnlyList<RecordedSessionPlotBandOverlay> Bands,
    IReadOnlyList<RecordedSessionPlotLabelOverlay> Labels);

public sealed record RecordedSessionPlotLineOverlay(
    double X1,
    double Y1,
    double X2,
    double Y2,
    RecordedSessionPlotOverlayStyle Style,
    string? Label = null,
    RecordedSessionPlotLinePlacement Placement = RecordedSessionPlotLinePlacement.Coordinates);

public enum RecordedSessionPlotLinePlacement
{
    Coordinates,
    PlotHorizontal,
}

public sealed record RecordedSessionPlotBandOverlay(
    double X1,
    double X2,
    RecordedSessionPlotOverlayStyle Style,
    string? Label = null);

public sealed record RecordedSessionPlotLabelOverlay(
    double X,
    double Y,
    string Text,
    RecordedSessionPlotLabelStyle Style,
    RecordedSessionPlotLabelPlacement Placement = RecordedSessionPlotLabelPlacement.Coordinates);

public enum RecordedSessionPlotLabelPlacement
{
    Coordinates,
    PlotRightEdge,
}

public sealed record RecordedSessionPlotLabelStyle(
    RecordedSessionMapColor TextColor,
    RecordedSessionMapColor? BackgroundColor,
    double FontSize,
    RecordedSessionPlotLabelAnchor Anchor);

public enum RecordedSessionPlotLabelAnchor
{
    Center,
    Left,
    Right,
    Top,
    Bottom,
}

public sealed record RecordedSessionPlotOverlayStyle(
    RecordedSessionMapColor Color,
    double Width = 1.0,
    double Opacity = 1.0);

public sealed record RecordedSessionStatisticsMetricContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    RecordedSessionStatisticsMetricTarget TargetMetric,
    string DisplayValue,
    string? DeltaValue,
    RecordedSessionMetricTone Tone) : IRecordedSessionContribution
{
    public bool HasDeltaValue => !string.IsNullOrWhiteSpace(DeltaValue);
    public bool IsPositiveTone => Tone == RecordedSessionMetricTone.Positive;
    public bool IsNegativeTone => Tone == RecordedSessionMetricTone.Negative;
    public bool IsAccentTone => Tone == RecordedSessionMetricTone.Accent;
}

public enum RecordedSessionMetricTone
{
    Default,
    Positive,
    Negative,
    Accent,
}

public enum RecordedSessionStatisticsMetricTarget
{
    FrontHscPercentage,
    FrontHsrPercentage,
    FrontLscPercentage,
    FrontLsrPercentage,
    RearHscPercentage,
    RearHsrPercentage,
    RearLscPercentage,
    RearLsrPercentage,
}

public sealed record RecordedSessionListIndicatorContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    IRecordedSessionListIndicatorContributionViewModel ViewModel) : IRecordedSessionContribution;

public sealed record RecordedSessionListActionContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    IRecordedSessionListActionContributionViewModel ViewModel) : IRecordedSessionContribution;

public sealed record RecordedSessionPlotContextMenuContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    RecordedSessionBuiltInGraphRow TargetRow,
    TelemetryPlotContextMenuAction Action) : IRecordedSessionContribution;

public sealed record RecordedSessionPlotRowActionContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    RecordedSessionGraphRowTarget TargetRow,
    TelemetryPlotRowAction Action) : IRecordedSessionContribution;

public sealed record RecordedSessionHostedGraphRowContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    RecordedSessionBuiltInGraphRow ParentRow,
    RecordedSessionGraphRowTarget RowTarget,
    string Title,
    SurfacePresentationState PresentationState,
    IRecordedSessionHostedGraphRowContributionViewModel ViewModel,
    bool IsInitiallyExpanded) : IRecordedSessionContribution
{
    public object? TitleToolTip { get; init; }
}

public sealed record RecordedSessionTimeRangeOverlayContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    RecordedSessionGraphRowTarget TargetRow,
    RecordedTimeRangeOverlaySetRegistration Registration) : IRecordedSessionContribution;
