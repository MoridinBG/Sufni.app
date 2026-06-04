using System.Collections.Generic;
using Sufni.App.Plots;
using Sufni.App.Presentation;
using Sufni.App.ViewModels.Editors;
using Sufni.App.ViewModels.SessionPages;
using Sufni.App.Views.Controls;

namespace Sufni.App.ExtensionHost.RecordedSessions;

public interface IRecordedSessionContribution
{
    string ExtensionId { get; }
    string ContributionId { get; }
    int Order { get; }
}

public sealed record RecordedSessionToolbarContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    object ViewModel) : IRecordedSessionContribution;

public sealed record RecordedSessionPageContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    PageViewModelBase Page,
    int RequestedIndex) : IRecordedSessionContribution;

public sealed record RecordedSessionMediaPaneContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    object ViewModel) : IRecordedSessionContribution;

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
    object ViewModel) : IRecordedSessionContribution;

public sealed record RecordedSessionStatisticsOverlayContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    RecordedSessionStatisticsPlotKind TargetPlotKind,
    object? ViewModel,
    RecordedSessionStatisticsPlotOverlayDescriptor? Overlay) : IRecordedSessionContribution;

public enum RecordedSessionStatisticsPlotKind
{
    FrontTravelHistogram,
    RearTravelHistogram,
    FrontVelocityHistogram,
    RearVelocityHistogram,
    CompressionBalance,
    ReboundBalance,
    FrontForkVibration,
    FrontFrameVibration,
    RearForkVibration,
    RearFrameVibration,
    SessionAnalysis,
}

public sealed record RecordedSessionStatisticsPlotOverlayDescriptor(
    IReadOnlyList<RecordedSessionPlotLineOverlay> Lines,
    IReadOnlyList<RecordedSessionPlotBandOverlay> Bands);

public sealed record RecordedSessionPlotLineOverlay(
    double X1,
    double Y1,
    double X2,
    double Y2,
    RecordedSessionPlotOverlayStyle Style,
    string? Label = null);

public sealed record RecordedSessionPlotBandOverlay(
    double X1,
    double X2,
    RecordedSessionPlotOverlayStyle Style,
    string? Label = null);

public sealed record RecordedSessionPlotOverlayStyle(
    RecordedSessionMapColor Color,
    double Width = 1.0,
    double Opacity = 1.0);

public sealed record RecordedSessionListIndicatorContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    object ViewModel) : IRecordedSessionContribution;

public sealed record RecordedSessionListActionContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    object ViewModel) : IRecordedSessionContribution;

public sealed record RecordedSessionPlotContextMenuContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    string RowId,
    TelemetryPlotContextMenuAction Action) : IRecordedSessionContribution;

public sealed record RecordedSessionPlotRowActionContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    string RowId,
    TelemetryPlotRowAction Action) : IRecordedSessionContribution;

public sealed record RecordedSessionHostedGraphRowContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    string ParentRowId,
    string RowId,
    string Title,
    SurfacePresentationState PresentationState,
    object ViewModel,
    bool IsInitiallyExpanded) : IRecordedSessionContribution
{
    public object? TitleToolTip { get; init; }
}

public sealed record RecordedSessionTimeRangeOverlayContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    string RowId,
    RecordedTimeRangeOverlaySetRegistration Registration) : IRecordedSessionContribution;
