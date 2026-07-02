using System;
using System.Collections.Generic;
using System.Windows.Input;
using Sufni.App.ExtensionHost.Contracts.Capabilities;
using Sufni.App.ExtensionHost.Contracts.Plots;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Runtime.Presentation;

namespace Sufni.App.ExtensionHost.Contracts.RecordedSessions;

public interface IRecordedSessionContribution : IExtensionContribution;

public enum RecordedSessionToolbarZone
{
    Leading,
    Trailing,
}

public sealed record RecordedSessionToolbarCommandContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    RecordedSessionToolbarZone Zone,
    string Label,
    ToolbarIconDescriptor? Icon,
    ICommand Command,
    object? CommandParameter = null) : IRecordedSessionContribution;

public sealed record RecordedSessionToolbarViewContribution(
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

public sealed record RecordedSessionAnalysisBannerContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    IRecordedSessionAnalysisBannerContributionViewModel ViewModel) : IRecordedSessionContribution;

public sealed record RecordedSessionAnalysisTabContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    string DisplayName,
    int RequestedIndex,
    Func<IRecordedSessionAnalysisTabContributionViewModel> CreateViewModel) : IRecordedSessionContribution
{
    public RecordedSessionAnalysisTabContribution(
        string ExtensionId,
        string ContributionId,
        int Order,
        string DisplayName,
        int RequestedIndex,
        IRecordedSessionAnalysisTabContributionViewModel ViewModel)
        : this(
            ExtensionId,
            ContributionId,
            Order,
            DisplayName,
            RequestedIndex,
            () => ViewModel)
    {
    }
}

public sealed record RecordedSessionAnalysisOverlayContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    RecordedSessionAnalysisPlotTarget TargetPlot,
    IRecordedSessionAnalysisOverlayContributionViewModel? ViewModel,
    RecordedSessionAnalysisPlotOverlayDescriptor? Overlay) : IRecordedSessionContribution;

public sealed record RecordedSessionAnalysisPlotOverlayDescriptor(
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

public sealed record RecordedSessionAnalysisMetricContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    RecordedSessionAnalysisMetricTarget TargetMetric,
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

public enum RecordedSessionAnalysisMetricTarget
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

public sealed record RecordedSessionSignalPlotContextMenuContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    RecordedSessionBuiltInSignalRow TargetRow,
    TelemetryPlotContextMenuAction Action) : IRecordedSessionContribution;

public sealed record RecordedSessionSignalRowActionContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    RecordedSessionSignalRowTarget TargetRow,
    SignalRowAction Action) : IRecordedSessionContribution;

public sealed record RecordedSessionHostedSignalRowContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    RecordedSessionBuiltInSignalRow ParentRow,
    RecordedSessionSignalRowTarget RowTarget,
    string Title,
    SurfacePresentationState PresentationState,
    IRecordedSessionHostedSignalRowContributionViewModel ViewModel,
    bool IsInitiallyExpanded) : IRecordedSessionContribution
{
    public object? TitleToolTip { get; init; }
}

public sealed record RecordedSessionTimeRangeOverlayContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    RecordedSessionSignalRowTarget TargetRow,
    RecordedTimeRangeOverlaySetRegistration Registration) : IRecordedSessionContribution;
