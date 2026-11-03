using Sufni.App.Shared.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Bikes.Models;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Segments.Coordinators;
using Sufni.App.Segments.Models;
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
public sealed partial class SegmentComparisonViewModel : ObservableObject, IDisposable, IAsyncDisposable
{
    private const double TimelinePaddingFraction = 0.05;

    private readonly Guid sessionId;
    private readonly ISegmentCoordinator segmentCoordinator;
    private readonly Action<double, double> setTimelineVisibleRange;
    private readonly Action<FailurePresentation> reportError;
    private readonly Action<string> reportNotification;
    private readonly IDisposable sessionStateSubscription;
    private SegmentComparisonSessionState state = SegmentComparisonSessionState.Empty;
    private IReadOnlyList<SegmentMatch> matches = [];
    private int pageIndex;
    private SegmentComparison? comparedMatch;
    private SegmentComparisonDetail? comparedDetail;
    private readonly HashSet<Task> pending = [];
    private readonly CancellationTokenSource shutdown = new();
    private bool disposed;
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
        IObservable<SegmentComparisonSessionState> sessionState,
        ISegmentCoordinator segmentCoordinator,
        Action<double, double> setTimelineVisibleRange,
        Action<FailurePresentation> reportError,
        Action<string> reportNotification)
    {
        this.sessionId = sessionId;
        this.segmentCoordinator = segmentCoordinator;
        this.setTimelineVisibleRange = setTimelineVisibleRange;
        this.reportError = reportError;
        this.reportNotification = reportNotification;
        sessionStateSubscription = sessionState.Subscribe(OnSessionStateChanged);
    }

    public bool HasMultiplePages => MatchCount > SegmentComparisonPage.Size;
    public bool HasAnalysisComparison => AnalysisComparison is not null;

    public string? AnalysisComparisonText => AnalysisComparison?.Comparison is { } comparison
        ? $"Comparing analysis with {comparison.Match.SessionName}{FormatLocalTime(comparison)}  match error {comparison.Match.NormalizedCostMeters:0.0}m"
        : null;

    private void OnSessionStateChanged(SegmentComparisonSessionState next)
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
            _ = TrackAsync(BuildAnalysisComparisonAsync(comparedMatch, comparedDetail, zoomTimeline: false));
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
        if (disposed) return;
        disposed = true;
        sessionStateSubscription.Dispose();
        shutdown.Cancel();
        Cancel(ref pageLoad);
        Cancel(ref analysisLoad);
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        await Task.WhenAll(pending.ToArray());
        shutdown.Dispose();
    }

    private async Task TrackAsync(Task task)
    {
        pending.Add(task);
        try { await task; }
        finally { pending.Remove(task); }
    }

    [RelayCommand(CanExecute = nameof(CanMatch))]
    private Task Match() => TrackAsync(MatchAsync());

    private async Task MatchAsync()
    {
        if (CreateMatchSource() is not { } source)
        {
            return;
        }

        var result = await segmentCoordinator.MatchAsync(source, shutdown.Token);
        if (disposed || CreateMatchSource() != source)
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
                reportError(new FailurePresentation("Segment matching needs a selected range with at least two track points."));
                break;
            case SegmentMatchResult.Failed failed:
                reportError(FailurePresentation.From(failed.Reason, failed.Details));
                break;
        }
    }

    private bool CanMatch() => !disposed && CreateMatchSource() is not null;

    [RelayCommand(CanExecute = nameof(CanMoveToPreviousPage))]
    private Task PreviousPage() => TrackAsync(MoveToPageAsync(pageIndex - 1));

    private bool CanMoveToPreviousPage() => pageIndex > 0;

    [RelayCommand(CanExecute = nameof(CanMoveToNextPage))]
    private Task NextPage() => TrackAsync(MoveToPageAsync(pageIndex + 1));

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
        using var cancellation = Replace(ref pageLoad);
        try
        {
            var result = await segmentCoordinator.LoadComparisonPageAsync(
                loadedMatches.Skip(index * SegmentComparisonPage.Size).ToArray(),
                state.Telemetry?.Metadata.Duration ?? 0,
                cancellation.Token);
            if (cancellation.IsCancellationRequested || !ReferenceEquals(loadedMatches, matches))
            {
                return;
            }

            if (result is SegmentComparisonPageResult.Failed failed)
            {
                reportError(FailurePresentation.From(failed.Reason, failed.Details));
                return;
            }

            var page = ((SegmentComparisonPageResult.Loaded)result).Page;
            matches = matches.Except(page.UnavailableMatches).ToArray();
            MatchCount = matches.Count;
            SetPageIndex(index);
            ShowRows(page.Comparisons);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(pageLoad, cancellation)) pageLoad = null;
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
        using var cancellation = Replace(ref analysisLoad);
        try
        {
            var result = await segmentCoordinator.LoadComparisonDetailAsync(comparison, cancellation.Token);
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            if (result is SegmentComparisonDetailResult.Failed failed)
            {
                reportError(FailurePresentation.From(failed.Reason, failed.Details));
                return;
            }

            if (result is SegmentComparisonDetailResult.Unavailable)
            {
                RemoveComparison(comparison);
                reportError(new FailurePresentation("The matched session telemetry is not available."));
                return;
            }

            var detail = ((SegmentComparisonDetailResult.Loaded)result).Detail;
            comparedDetail = detail;
            await BuildAnalysisComparisonAsync(comparison, detail, zoomTimeline: true);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(analysisLoad, cancellation)) analysisLoad = null;
        }
    }

    private async Task BuildAnalysisComparisonAsync(SegmentComparison comparison, SegmentComparisonDetail detail, bool zoomTimeline)
    {
        var settings = state;
        using var cancellation = Replace(ref analysisLoad);
        try
        {
            var result = await segmentCoordinator.BuildAnalysisComparisonAsync(
                comparison, detail, settings.TravelDistributionMode, settings.VelocityAverageMode,
                settings.DampingSpeedCutoffs, settings.DampingPercentages, cancellation.Token);
            if (cancellation.IsCancellationRequested || !ReferenceEquals(comparedMatch, comparison))
            {
                return;
            }

            if (result is SegmentAnalysisComparisonResult.Failed failed)
            {
                reportError(FailurePresentation.From(failed.Reason, failed.Details));
                return;
            }

            AnalysisComparison = ((SegmentAnalysisComparisonResult.Built)result).Comparison;
            if (zoomTimeline)
            {
                ZoomTimeline(comparison);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(analysisLoad, cancellation)) analysisLoad = null;
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
                item => TrackAsync(ToggleAnalysisComparisonAsync(item)),
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
