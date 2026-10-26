using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.Segments.Coordinators;
using Sufni.App.Segments.Models;
using Sufni.App.Segments.Services;
using Sufni.Telemetry;

namespace Sufni.App.Segments.ViewModels;

/// <summary>
/// The open session's state that matching and the analysis comparison depend on.
/// </summary>
public sealed record SegmentComparisonSessionState(
    TelemetryTimeRange? AnalysisRange,
    TelemetryData? Telemetry,
    TrackTimeRange? TrackTimeline,
    TravelDistributionMode TravelDistributionMode,
    VelocityAverageMode VelocityAverageMode,
    DampingSpeedCutoffs DampingSpeedCutoffs,
    SessionDampingPercentages DampingPercentages)
{
    public static SegmentComparisonSessionState Empty { get; } = new(
        null,
        null,
        null,
        TravelDistributionMode.ActiveSuspension,
        VelocityAverageMode.SampleAveraged,
        DampingSpeedCutoffs.Default,
        SessionDampingPercentages.Empty);
}

/// <summary>
/// Segment matching of one open recorded session. It lives as long as the session
/// editor. Matches are shown a page at a time as child rows of the travel,
/// velocity, IMU, and speed rows; one match can be compared with the current
/// analysis. A change of the analysis range, telemetry, or track discards the
/// matches; a change of the analysis settings rebuilds only the comparison.
/// </summary>
public sealed partial class SegmentComparisonViewModel : ObservableObject, IDisposable
{
    private const double TimelinePaddingFraction = 0.05;

    private readonly Guid sessionId;
    private readonly ISegmentCoordinator segmentCoordinator;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly Action<double, double> setTimelineVisibleRange;
    private readonly Action<string> reportError;
    private readonly Action<string> reportNotification;
    private SegmentComparisonSessionState state = SegmentComparisonSessionState.Empty;
    private IReadOnlyList<SegmentMatch> matches = [];
    private int pageIndex;
    private SegmentComparison? comparedMatch;
    private SegmentComparisonDetail? comparedDetail;
    private CancellationTokenSource? pageLoad;
    private CancellationTokenSource? analysisLoad;

    [ObservableProperty] public partial IReadOnlyList<SegmentComparisonRowViewModel> TravelRows { get; private set; } = [];
    [ObservableProperty] public partial IReadOnlyList<SegmentComparisonRowViewModel> VelocityRows { get; private set; } = [];
    [ObservableProperty] public partial IReadOnlyList<SegmentComparisonRowViewModel> ImuRows { get; private set; } = [];
    [ObservableProperty] public partial IReadOnlyList<SegmentComparisonRowViewModel> SpeedRows { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnalysisComparison), nameof(AnalysisComparisonText))]
    public partial SegmentAnalysisComparison? AnalysisComparison { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMultiplePages))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand), nameof(NextPageCommand))]
    public partial int MatchCount { get; private set; }

    public SegmentComparisonViewModel(
        Guid sessionId,
        ISegmentCoordinator segmentCoordinator,
        IBackgroundTaskRunner backgroundTaskRunner,
        Action<double, double> setTimelineVisibleRange,
        Action<string> reportError,
        Action<string> reportNotification)
    {
        this.sessionId = sessionId;
        this.segmentCoordinator = segmentCoordinator;
        this.backgroundTaskRunner = backgroundTaskRunner;
        this.setTimelineVisibleRange = setTimelineVisibleRange;
        this.reportError = reportError;
        this.reportNotification = reportNotification;
    }

    public bool HasMultiplePages => MatchCount > SegmentComparisonPage.Size;
    public bool HasAnalysisComparison => AnalysisComparison is not null;

    public string? AnalysisComparisonText => AnalysisComparison?.Comparison is { } comparison
        ? $"Comparing analysis with {comparison.Match.SessionName}{FormatLocalTime(comparison)}  match error {comparison.Match.NormalizedCostMeters:0.0}m"
        : null;

    public void SetSessionState(SegmentComparisonSessionState next)
    {
        if (next == state)
        {
            return;
        }

        var previous = state;
        state = next;
        MatchCommand.NotifyCanExecuteChanged();
        if (previous.AnalysisRange != next.AnalysisRange ||
            !ReferenceEquals(previous.Telemetry, next.Telemetry) ||
            previous.TrackTimeline != next.TrackTimeline)
        {
            Clear();
            return;
        }

        if (comparedMatch is not null && comparedDetail is not null &&
            (previous.TravelDistributionMode != next.TravelDistributionMode ||
             previous.VelocityAverageMode != next.VelocityAverageMode ||
             previous.DampingSpeedCutoffs != next.DampingSpeedCutoffs ||
             previous.DampingPercentages != next.DampingPercentages))
        {
            _ = BuildAnalysisComparisonAsync(comparedMatch, comparedDetail, zoomTimeline: false);
        }
    }

    /// <summary>Discards the matches and the analysis comparison.</summary>
    public void Clear()
    {
        Cancel(ref pageLoad);
        ClearAnalysisComparison();
        matches = [];
        MatchCount = 0;
        pageIndex = 0;
        ShowRows([]);
    }

    public void Dispose()
    {
        Cancel(ref pageLoad);
        Cancel(ref analysisLoad);
    }

    [RelayCommand(CanExecute = nameof(CanMatch))]
    private async Task Match()
    {
        if (CreateMatchSource() is not { } source)
        {
            return;
        }

        var result = await segmentCoordinator.MatchAsync(source);
        if (CreateMatchSource() != source)
        {
            return;
        }

        switch (result)
        {
            case SegmentMatchResult.Matched matched:
                Clear();
                matches = matched.Matches;
                MatchCount = matches.Count;
                ShowRows(matched.FirstPage.Comparisons);
                reportNotification(matches.Count == 1 ? "Found 1 segment match." : $"Found {matches.Count} segment matches.");
                break;
            case SegmentMatchResult.NoTrack:
                reportError("Segment matching needs a selected range with at least two track points.");
                break;
            case SegmentMatchResult.Failed failed:
                reportError(failed.ErrorMessage);
                break;
        }
    }

    private bool CanMatch() => CreateMatchSource() is not null;

    [RelayCommand(CanExecute = nameof(CanMoveToPreviousPage))]
    private Task PreviousPage() => MoveToPageAsync(pageIndex - 1);

    private bool CanMoveToPreviousPage() => pageIndex > 0;

    [RelayCommand(CanExecute = nameof(CanMoveToNextPage))]
    private Task NextPage() => MoveToPageAsync(pageIndex + 1);

    private bool CanMoveToNextPage() => (pageIndex + 1) * SegmentComparisonPage.Size < MatchCount;

    [RelayCommand]
    private void ClearAnalysisComparison()
    {
        Cancel(ref analysisLoad);
        comparedMatch = null;
        comparedDetail = null;
        AnalysisComparison = null;
        UpdateComparedRows();
    }

    private async Task MoveToPageAsync(int index)
    {
        var loadedMatches = matches;
        var cancellation = Replace(ref pageLoad);
        try
        {
            var page = await segmentCoordinator.LoadComparisonPageAsync(
                loadedMatches.Skip(index * SegmentComparisonPage.Size).ToArray(),
                state.Telemetry?.Metadata.Duration ?? 0,
                cancellation.Token);
            if (cancellation.IsCancellationRequested || !ReferenceEquals(loadedMatches, matches))
            {
                return;
            }

            matches = matches.Except(page.UnavailableMatches).ToArray();
            MatchCount = matches.Count;
            SetPageIndex(index);
            ShowRows(page.Comparisons);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            reportError($"Comparison page could not be loaded: {e.Message}");
        }
    }

    private async Task ToggleAnalysisComparisonAsync(SegmentComparison comparison)
    {
        if (ReferenceEquals(comparedMatch, comparison))
        {
            ClearAnalysisComparison();
            return;
        }

        ClearAnalysisComparison();
        comparedMatch = comparison;
        UpdateComparedRows();
        var cancellation = Replace(ref analysisLoad);
        try
        {
            var detail = await segmentCoordinator.LoadComparisonDetailAsync(comparison, cancellation.Token);
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            if (detail is null)
            {
                RemoveComparison(comparison);
                reportError("The matched session telemetry is not available.");
                return;
            }

            comparedDetail = detail;
            await BuildAnalysisComparisonAsync(comparison, detail, zoomTimeline: true);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            reportError($"Analysis comparison could not be loaded: {e.Message}");
        }
    }

    private async Task BuildAnalysisComparisonAsync(SegmentComparison comparison, SegmentComparisonDetail detail, bool zoomTimeline)
    {
        var settings = state;
        var cancellation = Replace(ref analysisLoad);
        try
        {
            var analysis = await backgroundTaskRunner.RunAsync(
                () => SegmentAnalysisComparisonBuilder.Build(
                    comparison,
                    detail,
                    settings.TravelDistributionMode,
                    settings.VelocityAverageMode,
                    settings.DampingSpeedCutoffs,
                    settings.DampingPercentages),
                cancellation.Token);
            if (cancellation.IsCancellationRequested || !ReferenceEquals(comparedMatch, comparison))
            {
                return;
            }

            AnalysisComparison = analysis;
            if (zoomTimeline)
            {
                ZoomTimeline(comparison);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            reportError($"Analysis comparison could not be prepared: {e.Message}");
        }
    }

    private void Remove(SegmentComparisonRowViewModel row)
    {
        TravelRows = TravelRows.Where(item => item != row).ToArray();
        VelocityRows = VelocityRows.Where(item => item != row).ToArray();
        ImuRows = ImuRows.Where(item => item != row).ToArray();
        SpeedRows = SpeedRows.Where(item => item != row).ToArray();
        if (!AllRows().Any(item => ReferenceEquals(item.Comparison, row.Comparison)))
        {
            RemoveComparison(row.Comparison);
        }
    }

    private void RemoveComparison(SegmentComparison comparison)
    {
        TravelRows = TravelRows.Where(item => item.Comparison != comparison).ToArray();
        VelocityRows = VelocityRows.Where(item => item.Comparison != comparison).ToArray();
        ImuRows = ImuRows.Where(item => item.Comparison != comparison).ToArray();
        SpeedRows = SpeedRows.Where(item => item.Comparison != comparison).ToArray();
        matches = matches.Where(match => match != comparison.Match).ToArray();
        MatchCount = matches.Count;
        SetPageIndex(Math.Min(pageIndex, Math.Max(0, (MatchCount - 1) / SegmentComparisonPage.Size)));
        if (ReferenceEquals(comparedMatch, comparison))
        {
            ClearAnalysisComparison();
        }
    }

    private void ShowRows(IReadOnlyList<SegmentComparison> comparisons)
    {
        TravelRows = CreateRows(comparisons, SegmentComparisonSignal.Travel);
        VelocityRows = CreateRows(comparisons, SegmentComparisonSignal.Velocity);
        ImuRows = CreateRows(comparisons, SegmentComparisonSignal.Imu);
        SpeedRows = CreateRows(comparisons, SegmentComparisonSignal.Speed);
        UpdateComparedRows();
    }

    private SegmentComparisonRowViewModel[] CreateRows(
        IReadOnlyList<SegmentComparison> comparisons,
        SegmentComparisonSignal signal) =>
        comparisons
            .Where(comparison => comparison.GetSeries(signal).Count > 0)
            .Select(comparison => new SegmentComparisonRowViewModel(
                comparison,
                signal,
                item => _ = ToggleAnalysisComparisonAsync(item),
                Remove))
            .ToArray();

    private IEnumerable<SegmentComparisonRowViewModel> AllRows() =>
        TravelRows.Concat(VelocityRows).Concat(ImuRows).Concat(SpeedRows);

    private void UpdateComparedRows()
    {
        foreach (var row in AllRows())
        {
            row.SetAnalysisCompared(ReferenceEquals(row.Comparison, comparedMatch));
        }
    }

    private void SetPageIndex(int index)
    {
        pageIndex = index;
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
    }

    private void ZoomTimeline(SegmentComparison comparison)
    {
        if (state.AnalysisRange is not { } range || comparison.SourceDurationSeconds <= 0)
        {
            return;
        }

        var padding = range.DurationSeconds * TimelinePaddingFraction;
        setTimelineVisibleRange(
            (range.StartSeconds - padding) / comparison.SourceDurationSeconds,
            (range.EndSeconds + padding) / comparison.SourceDurationSeconds);
    }

    private SegmentMatchSource? CreateMatchSource() =>
        state is { AnalysisRange: { } range, TrackTimeline: { } trackTimeline, Telemetry.Metadata.Duration: > 0 and var duration }
            ? new SegmentMatchSource(sessionId, range, trackTimeline, duration)
            : null;

    private static string FormatLocalTime(SegmentComparison comparison) =>
        comparison.MatchedSegmentLocalTime is { } time
            ? " " + time.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)
            : "";

    private static CancellationTokenSource Replace(ref CancellationTokenSource? field)
    {
        Cancel(ref field);
        field = new CancellationTokenSource();
        return field;
    }

    private static void Cancel(ref CancellationTokenSource? field)
    {
        field?.Cancel();
        field = null;
    }
}
