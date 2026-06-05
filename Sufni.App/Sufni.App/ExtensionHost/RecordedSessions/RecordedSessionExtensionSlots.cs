using System.Collections.ObjectModel;

namespace Sufni.App.ExtensionHost.RecordedSessions;

public sealed class RecordedSessionExtensionSlots
{
    public ObservableCollection<RecordedSessionToolbarContribution> GraphToolbarActions { get; } = [];
    public ObservableCollection<RecordedSessionPageContribution> Pages { get; } = [];
    public ObservableCollection<RecordedSessionMediaPaneContribution> MediaPanes { get; } = [];
    public ObservableCollection<RecordedSessionMapOverlayContribution> MapOverlays { get; } = [];
    public ObservableCollection<RecordedSessionStatisticsBannerContribution> StatisticsBanners { get; } = [];
    public ObservableCollection<RecordedSessionStatisticsOverlayContribution> StatisticsOverlays { get; } = [];
    public ObservableCollection<RecordedSessionStatisticsMetricContribution> StatisticsMetrics { get; } = [];
    public ObservableCollection<RecordedSessionListIndicatorContribution> SessionListIndicators { get; } = [];
    public ObservableCollection<RecordedSessionListActionContribution> SessionListActions { get; } = [];
    public ObservableCollection<RecordedSessionPlotContextMenuContribution> PlotContextMenuActions { get; } = [];
    public ObservableCollection<RecordedSessionPlotRowActionContribution> PlotRowHeaderActions { get; } = [];
    public ObservableCollection<RecordedSessionHostedGraphRowContribution> HostedGraphRows { get; } = [];
    public ObservableCollection<RecordedSessionTimeRangeOverlayContribution> TimeRangeOverlays { get; } = [];
}
