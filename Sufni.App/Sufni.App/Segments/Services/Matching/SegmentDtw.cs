using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Segments.Models;
using Sufni.App.Sessions.Models;

namespace Sufni.App.Segments.Services.Matching;

/// <summary>
/// Pure DTW computation of segment matching: builds resampled local paths from
/// track points and finds the best matching window of a candidate path.
/// </summary>
internal static class SegmentDtw
{
    private const double WebMercatorRadiusMeters = 6_378_137;
    private const int CoarseStepMeters = 25;
    private const int CoarseStrideMeters = 50;
    private const int CoarseWinnerCount = 8;
    private const int RefinementRadiusSamples = 3;
    private const int BoundsBlockSize = 64;
    private const double DirectionAgreementThreshold = 0.7;
    private const double DtwBandRatio = 0.25;
    // Share of a session's progress attributed to the coarse scan; the rest is refinement.
    private const double CoarseProgressShare = 0.5;

    internal static bool TryBuildSourcePath(
        IReadOnlyList<TrackPoint> points,
        SegmentDtwMatchOptions options,
        out TrackPath path)
    {
        path = default!;
        if (points.Count < 2 || !TryCreateReference(points, out var reference))
        {
            return false;
        }

        if (!TryBuildPath(points, reference, options.ResampleStepMeters, out path) ||
            path.DistanceMeters < options.MinSegmentLengthMeters ||
            path.Samples.Count < 2)
        {
            return false;
        }

        return true;
    }

    private static bool TryCreateReference(IReadOnlyList<TrackPoint> points, out LocalReference reference)
    {
        reference = default;
        var finite = points
            .Where(point => double.IsFinite(point.Time) && double.IsFinite(point.X) && double.IsFinite(point.Y))
            .OrderBy(point => point.Time)
            .ToList();
        if (finite.Count < 2)
        {
            return false;
        }

        var midpoint = finite[finite.Count / 2];
        var latitudeRadians = InverseWebMercatorYToLatitudeRadians(midpoint.Y);
        var scale = Math.Cos(latitudeRadians);
        if (!double.IsFinite(scale) || Math.Abs(scale) < 1e-9)
        {
            return false;
        }

        reference = new LocalReference(midpoint.X, midpoint.Y, scale);
        return true;
    }

    internal static bool TryBuildPath(
        IReadOnlyList<TrackPoint> points,
        LocalReference reference,
        double resampleStepMeters,
        out TrackPath path)
    {
        path = default!;
        var localized = points
            .Where(point => double.IsFinite(point.Time) && double.IsFinite(point.X) && double.IsFinite(point.Y))
            .OrderBy(point => point.Time)
            .Select(point => new LocalPoint(
                (point.X - reference.OriginX) * reference.Scale,
                (point.Y - reference.OriginY) * reference.Scale,
                point.Time))
            .Where(point => double.IsFinite(point.X) && double.IsFinite(point.Y) && double.IsFinite(point.Time))
            .ToList();

        if (localized.Count < 2)
        {
            return false;
        }

        var unique = new List<LocalPoint>(localized.Count);
        foreach (var point in localized)
        {
            if (unique.Count > 0 && point.Time <= unique[^1].Time)
            {
                continue;
            }

            unique.Add(point);
        }

        if (unique.Count < 2)
        {
            return false;
        }

        var distances = new double[unique.Count];
        for (var i = 1; i < unique.Count; i++)
        {
            distances[i] = distances[i - 1] + Distance(unique[i - 1], unique[i]);
        }

        var totalDistance = distances[^1];
        if (!double.IsFinite(totalDistance) || totalDistance <= 0)
        {
            return false;
        }

        var samples = Resample(unique, distances, totalDistance, resampleStepMeters);
        if (samples.Count < 2)
        {
            return false;
        }

        path = new TrackPath(reference, samples, totalDistance);
        return true;
    }

    private static List<SegmentSample> Resample(
        IReadOnlyList<LocalPoint> points,
        IReadOnlyList<double> distances,
        double totalDistance,
        double stepMeters)
    {
        var samples = new List<SegmentSample> { ToSample(points[0], 0) };
        var nextIndex = 1;

        for (var target = stepMeters; target < totalDistance; target += stepMeters)
        {
            while (nextIndex < distances.Count - 1 && distances[nextIndex] < target)
            {
                nextIndex++;
            }

            while (nextIndex < distances.Count && distances[nextIndex] <= distances[nextIndex - 1])
            {
                nextIndex++;
            }

            if (nextIndex >= distances.Count)
            {
                break;
            }

            var previousDistance = distances[nextIndex - 1];
            var nextDistance = distances[nextIndex];
            var ratio = (target - previousDistance) / (nextDistance - previousDistance);
            var previous = points[nextIndex - 1];
            var next = points[nextIndex];
            samples.Add(new SegmentSample(
                Lerp(previous.X, next.X, ratio),
                Lerp(previous.Y, next.Y, ratio),
                Lerp(previous.Time, next.Time, ratio),
                target));
        }

        samples.Add(ToSample(points[^1], totalDistance));
        return samples;
    }

    private static SegmentSample ToSample(LocalPoint point, double distanceMeters)
    {
        return new SegmentSample(point.X, point.Y, point.Time, distanceMeters);
    }

    private static double InverseWebMercatorYToLatitudeRadians(double y)
    {
        return 2 * Math.Atan(Math.Exp(y / WebMercatorRadiusMeters)) - Math.PI / 2;
    }

    private static double Lerp(double start, double end, double ratio)
    {
        return start + (end - start) * ratio;
    }

    private static double Distance(LocalPoint first, LocalPoint second)
    {
        return Hypot(second.X - first.X, second.Y - first.Y);
    }

    internal static SegmentMatch? FindBestMatch(
        TrackPath source,
        CandidateTrack candidate,
        SegmentMatchRequest request,
        SegmentDtwMatchOptions options,
        ParallelOptions parallelOptions,
        Action<double>? reportProgress)
    {
        var sourcePointCount = source.Samples.Count;
        var tolerancePoints = Math.Max(0, (int)Math.Round(sourcePointCount * options.CandidateLengthToleranceRatio));
        var minWindowCount = Math.Max(2, sourcePointCount - tolerancePoints);
        var maxWindowCount = Math.Min(candidate.Path.Samples.Count, sourcePointCount + tolerancePoints);
        if (minWindowCount > maxWindowCount)
        {
            return null;
        }

        var coarseWinners = FindCoarseWinners(source, candidate.Path, minWindowCount, maxWindowCount, options, parallelOptions, reportProgress);
        var (startRadius, countRadius) = GetRefinementRadii(options);
        var primaryWindows = BuildRefinementWindows(
            coarseWinners,
            candidate.Path.Samples.Count,
            minWindowCount,
            maxWindowCount,
            startRadius,
            countRadius);
        var fallbackWindows = BuildFallbackRefinementWindows(
            coarseWinners,
            primaryWindows,
            candidate.Path.Samples.Count,
            minWindowCount,
            maxWindowCount,
            startRadius);
        var totalRefinementUnits = primaryWindows.Count + fallbackWindows.Count;
        var refinementProgress = new RefinementProgress();

        var best = FindBestRefinementWindow(
            source,
            candidate,
            request,
            options,
            parallelOptions,
            primaryWindows,
            refinementProgress,
            totalRefinementUnits,
            reportProgress);

        if (best is null)
        {
            best = FindBestRefinementWindow(
                source,
                candidate,
                request,
                options,
                parallelOptions,
                fallbackWindows,
                refinementProgress,
                totalRefinementUnits,
                reportProgress);
        }
        else
        {
            reportProgress?.Invoke(1);
        }

        if (best is not { } bestScore)
        {
            return null;
        }

        var startSample = candidate.Path.Samples[bestScore.Window.StartIndex];
        var endSample = candidate.Path.Samples[bestScore.Window.EndIndex];
        var timeline = ResolveTimeline(candidate);
        var startSeconds = timeline.ToTelemetrySeconds(startSample.SourceTimeSeconds);
        var endSeconds = timeline.ToTelemetrySeconds(endSample.SourceTimeSeconds);

        // The search keeps scalar scores only; re-run the winning window once to
        // materialize the warping path for the time map.
        var bestPath = new List<DtwPathPoint>();
        if (ScoreSamples(
                source.Samples,
                candidate.Path.Samples,
                bestScore.Window.StartIndex,
                bestScore.Window.Count,
                options,
                new DtwScratch(),
                bestPath) is null)
        {
            return null;
        }

        var timeMap = BuildTimeMap(source, candidate, bestScore, bestPath, request, startSeconds, endSeconds);
        if (timeMap.Count < 2)
        {
            return null;
        }

        return new SegmentMatch(
            candidate.Session.Id,
            candidate.Session.Name ?? "",
            candidate.Session.Timestamp,
            startSeconds,
            endSeconds,
            bestScore.NormalizedCostMeters,
            bestScore.MedianErrorMeters,
            bestScore.P90ErrorMeters,
            source.Samples.Count,
            bestScore.Window.Count,
            timeMap,
            candidate.Session.ContentToken);
    }

    private static IReadOnlyList<CandidateWindow> FindCoarseWinners(
        TrackPath source,
        TrackPath candidate,
        int minWindowCount,
        int maxWindowCount,
        SegmentDtwMatchOptions options,
        ParallelOptions parallelOptions,
        Action<double>? reportProgress)
    {
        var sourceSimplified = SimplifyByDistance(source.Samples, CoarseStepMeters);
        var stride = Math.Max(1, (int)Math.Round(CoarseStrideMeters / options.ResampleStepMeters));
        var windowCountRange = maxWindowCount - minWindowCount + 1;
        var winners = new CandidateWindowTopK(CoarseWinnerCount);
        var scoresGate = new object();
        var completedWindowCounts = 0;

        // Window sizes scan independently; each worker keeps its own scratch
        // buffers and bounded winner list, merged once at the end.
        Parallel.For(
            0,
            windowCountRange,
            parallelOptions,
            () => new CoarseScanLocal(),
            (offset, _, local) =>
            {
                var windowCount = minWindowCount + offset;
                for (var start = 0; start + windowCount <= candidate.Samples.Count; start += stride)
                {
                    var window = new CandidateWindow(start, windowCount);
                    if (!PassesSpatialPrefilter(source, candidate, window, options))
                    {
                        continue;
                    }

                    SimplifyByDistance(candidate.Samples, CoarseStepMeters, window, local.Scratch.SimplifiedCandidate);
                    var score = ScoreSamples(
                        sourceSimplified,
                        local.Scratch.SimplifiedCandidate,
                        candidateOffset: 0,
                        local.Scratch.SimplifiedCandidate.Count,
                        options,
                        local.Scratch,
                        path: null);
                    if (score is null)
                    {
                        continue;
                    }

                    local.Winners.Add(new CandidateWindowScore(
                        window,
                        score.Value.NormalizedCostMeters,
                        score.Value.MedianErrorMeters,
                        score.Value.P90ErrorMeters));
                }

                var completed = Interlocked.Increment(ref completedWindowCounts);
                reportProgress?.Invoke(CoarseProgressShare * completed / windowCountRange);
                return local;
            },
            local =>
            {
                if (local.Winners.Count == 0)
                {
                    return;
                }

                lock (scoresGate)
                {
                    winners.AddRange(local.Winners);
                }
            });

        // Count and start index reproduce the sequential scan's tie-breaking, so
        // the winners do not depend on how the parallel partitions merged.
        return winners
            .ToSortedList()
            .Select(candidateScore => candidateScore.Window)
            .ToList();
    }

    private static (int StartRadius, int CountRadius) GetRefinementRadii(SegmentDtwMatchOptions options)
    {
        var coarseStrideSamples = Math.Max(1, (int)Math.Round(CoarseStrideMeters / options.ResampleStepMeters));
        var coarseStepSamples = Math.Max(1, (int)Math.Round(CoarseStepMeters / options.ResampleStepMeters));
        return (
            Math.Max(RefinementRadiusSamples, (int)Math.Ceiling(coarseStrideSamples / 2d)),
            Math.Max(RefinementRadiusSamples, coarseStepSamples));
    }

    private static IReadOnlyList<CandidateWindow> BuildRefinementWindows(
        IReadOnlyList<CandidateWindow> coarseWinners,
        int candidateSampleCount,
        int minWindowCount,
        int maxWindowCount,
        int startRadius,
        int countRadius)
    {
        var windows = new HashSet<CandidateWindow>();
        foreach (var winner in coarseWinners)
        {
            var firstCount = Math.Max(minWindowCount, winner.Count - countRadius);
            var lastCount = Math.Min(maxWindowCount, winner.Count + countRadius);
            AddRefinementWindows(windows, winner, candidateSampleCount, firstCount, lastCount, startRadius);
        }

        return windows
            .OrderBy(window => window.StartIndex)
            .ThenBy(window => window.Count)
            .ToList();
    }

    private static IReadOnlyList<CandidateWindow> BuildFallbackRefinementWindows(
        IReadOnlyList<CandidateWindow> coarseWinners,
        IReadOnlyList<CandidateWindow> primaryWindows,
        int candidateSampleCount,
        int minWindowCount,
        int maxWindowCount,
        int startRadius)
    {
        var primary = primaryWindows.ToHashSet();
        var windows = new HashSet<CandidateWindow>();
        foreach (var winner in coarseWinners)
        {
            AddRefinementWindows(windows, winner, candidateSampleCount, minWindowCount, maxWindowCount, startRadius);
        }

        windows.ExceptWith(primary);
        return windows
            .OrderBy(window => window.StartIndex)
            .ThenBy(window => window.Count)
            .ToList();
    }

    private static void AddRefinementWindows(
        HashSet<CandidateWindow> windows,
        CandidateWindow winner,
        int candidateSampleCount,
        int firstCount,
        int lastCount,
        int startRadius)
    {
        for (var count = firstCount; count <= lastCount; count++)
        {
            var maxStart = candidateSampleCount - count;
            if (maxStart < 0)
            {
                continue;
            }

            var firstStart = Math.Max(0, winner.StartIndex - startRadius);
            var lastStart = Math.Min(maxStart, winner.StartIndex + startRadius);
            for (var start = firstStart; start <= lastStart; start++)
            {
                windows.Add(new CandidateWindow(start, count));
            }
        }
    }

    private static CandidateWindowScore? FindBestRefinementWindow(
        TrackPath source,
        CandidateTrack candidate,
        SegmentMatchRequest request,
        SegmentDtwMatchOptions options,
        ParallelOptions parallelOptions,
        IReadOnlyList<CandidateWindow> windows,
        RefinementProgress refinementProgress,
        int totalRefinementUnits,
        Action<double>? reportProgress)
    {
        if (windows.Count == 0)
        {
            return null;
        }

        CandidateWindowScore? best = null;
        var bestGate = new object();

        Parallel.For(
            0,
            windows.Count,
            parallelOptions,
            () => new RefinementLocal(),
            (windowIndex, _, local) =>
            {
                var window = windows[windowIndex];
                if (!IsOverlappingSourceWindow(candidate, window, request))
                {
                    var score = ScoreWindow(source, candidate.Path, window, options, local.Scratch);
                    if (score is { } value && (local.Best is null || IsBetter(value, local.Best.Value)))
                    {
                        local.Best = value;
                    }
                }

                var completed = Interlocked.Increment(ref refinementProgress.Completed);
                reportProgress?.Invoke(CoarseProgressShare + (1 - CoarseProgressShare) * completed / totalRefinementUnits);
                return local;
            },
            local =>
            {
                if (local.Best is not { } localBest)
                {
                    return;
                }

                lock (bestGate)
                {
                    if (best is null || IsBetter(localBest, best.Value))
                    {
                        best = localBest;
                    }
                }
            });

        return best;
    }

    private static bool PassesSpatialPrefilter(
        TrackPath source,
        TrackPath candidate,
        CandidateWindow window,
        SegmentDtwMatchOptions options)
    {
        var sourceSamples = source.Samples;
        var candidateSamples = candidate.Samples;
        var tolerance = Math.Max(options.MaxPointDistanceMeters * 2, options.MaxNormalizedCostMeters * 2);
        var endpointTolerance = tolerance * 2;
        var endpointToleranceSquared = endpointTolerance * endpointTolerance;
        if (SquaredDistance(sourceSamples[0], candidateSamples[window.StartIndex]) > endpointToleranceSquared ||
            SquaredDistance(sourceSamples[^1], candidateSamples[window.EndIndex]) > endpointToleranceSquared)
        {
            return false;
        }

        var candidateBounds = candidate.BoundsIndex.Get(window);
        if (source.Bounds.DistanceTo(candidateBounds) > tolerance)
        {
            return false;
        }

        return Distance(source.Bounds.Center, candidateBounds.Center) <= tolerance * 4;
    }

    private static CandidateWindowScore? ScoreWindow(
        TrackPath source,
        TrackPath candidate,
        CandidateWindow window,
        SegmentDtwMatchOptions options,
        DtwScratch scratch)
    {
        if (!HasSameDirection(source, candidate, window))
        {
            return null;
        }

        var score = ScoreSamples(source.Samples, candidate.Samples, window.StartIndex, window.Count, options, scratch, path: null);
        return score is { } value
            ? new CandidateWindowScore(
                window,
                value.NormalizedCostMeters,
                value.MedianErrorMeters,
                value.P90ErrorMeters)
            : null;
    }

    private static DtwScore? ScoreSamples(
        IReadOnlyList<SegmentSample> source,
        IReadOnlyList<SegmentSample> candidate,
        int candidateOffset,
        int candidateCount,
        SegmentDtwMatchOptions options,
        DtwScratch scratch,
        List<DtwPathPoint>? path)
    {
        if (source.Count < 2 || candidateCount < 2)
        {
            return null;
        }

        var sourceCount = source.Count;
        var band = Math.Max(
            Math.Abs(sourceCount - candidateCount),
            (int)Math.Ceiling(Math.Max(sourceCount, candidateCount) * DtwBandRatio));
        var maxPointDistanceSquared = options.MaxPointDistanceMeters * options.MaxPointDistanceMeters;

        // Store one fixed-width slice per source row. Each slice contains the
        // Sakoe-Chiba band plus a one-cell predecessor border on both sides.
        // The logical start shifts with the row, so no cells outside that compact
        // slice are retained while diagonal/up/left and traceback stay addressable.
        var width = Math.Min(candidateCount + 1, band * 2 + 3);
        scratch.EnsureDtwCapacity((sourceCount + 1) * width);
        var costs = scratch.Costs;
        var steps = scratch.Steps;

        var row0End = Math.Min(candidateCount, band + 1);
        for (var j = 0; j <= row0End; j++)
        {
            costs[j] = double.PositiveInfinity;
        }

        costs[0] = 0;
        for (var i = 1; i <= sourceCount; i++)
        {
            var minJ = Math.Max(1, i - band);
            var maxJ = Math.Min(candidateCount, i + band);
            var rowStart = Math.Max(0, i - band - 1);
            var rowBase = i * width - rowStart;
            var previousRowStart = Math.Max(0, i - band - 2);
            var previousRowBase = (i - 1) * width - previousRowStart;
            var initEnd = Math.Min(candidateCount, maxJ + 1);
            for (var j = minJ - 1; j <= initEnd; j++)
            {
                costs[rowBase + j] = double.PositiveInfinity;
            }

            var sourceSample = source[i - 1];
            for (var j = minJ; j <= maxJ; j++)
            {
                var squaredDistance = SquaredDistance(sourceSample, candidate[candidateOffset + j - 1]);
                if (squaredDistance > maxPointDistanceSquared)
                {
                    continue;
                }

                var distance = Math.Sqrt(squaredDistance);
                var diagonal = costs[previousRowBase + j - 1];
                var up = costs[previousRowBase + j];
                var left = costs[rowBase + j - 1];
                var best = diagonal;
                var step = DtwStep.Diagonal;

                if (up < best)
                {
                    best = up;
                    step = DtwStep.Up;
                }

                if (left < best)
                {
                    best = left;
                    step = DtwStep.Left;
                }

                if (double.IsPositiveInfinity(best))
                {
                    continue;
                }

                costs[rowBase + j] = best + distance;
                steps[rowBase + j] = (byte)step;
            }
        }

        var finalRowBase = sourceCount * width - Math.Max(0, sourceCount - band - 1);
        var totalCost = costs[finalRowBase + candidateCount];
        if (double.IsPositiveInfinity(totalCost))
        {
            return null;
        }

        // Traceback only visits cells written this call: it starts from a finite
        // cell and every recorded step points at a finite predecessor.
        var matchedDistances = scratch.MatchedDistances;
        matchedDistances.Clear();
        path?.Clear();
        var sourceIndex = sourceCount;
        var candidateIndex = candidateCount;
        while (sourceIndex > 0 && candidateIndex > 0)
        {
            var sourceSampleIndex = sourceIndex - 1;
            var candidateSampleIndex = candidateIndex - 1;
            var distance = Distance(source[sourceSampleIndex], candidate[candidateOffset + candidateSampleIndex]);
            matchedDistances.Add(distance);
            path?.Add(new DtwPathPoint(sourceSampleIndex, candidateSampleIndex, distance));
            var tracebackRowBase = sourceIndex * width - Math.Max(0, sourceIndex - band - 1);
            switch ((DtwStep)steps[tracebackRowBase + candidateIndex])
            {
                case DtwStep.Diagonal:
                    sourceIndex--;
                    candidateIndex--;
                    break;
                case DtwStep.Up:
                    sourceIndex--;
                    break;
                case DtwStep.Left:
                    candidateIndex--;
                    break;
            }
        }

        if (matchedDistances.Count == 0)
        {
            return null;
        }

        var normalizedCost = totalCost / matchedDistances.Count;
        if (normalizedCost > options.MaxNormalizedCostMeters)
        {
            return null;
        }

        path?.Reverse();
        matchedDistances.Sort();
        return new DtwScore(
            normalizedCost,
            Percentile(matchedDistances, 0.5),
            Percentile(matchedDistances, 0.9));
    }

    private static IReadOnlyList<SegmentTimeMapPoint> BuildTimeMap(
        TrackPath source,
        CandidateTrack candidate,
        CandidateWindowScore score,
        IReadOnlyList<DtwPathPoint> path,
        SegmentMatchRequest request,
        double matchStartSeconds,
        double matchEndSeconds)
    {
        var sourceRange = request.Source.Range;
        var sourceOrigin = source.Samples[0].SourceTimeSeconds - sourceRange.StartSeconds;
        var candidateTimeline = ResolveTimeline(candidate);

        var grouped = path
            .Select(point =>
            {
                var sourceSeconds = source.Samples[point.SourceIndex].SourceTimeSeconds - sourceOrigin;
                sourceSeconds = Math.Clamp(sourceSeconds, sourceRange.StartSeconds, sourceRange.EndSeconds);

                var candidateSeconds = candidateTimeline.ToTelemetrySeconds(candidate.Path.Samples[score.Window.StartIndex + point.CandidateIndex].SourceTimeSeconds);
                candidateSeconds = Math.Clamp(
                    candidateSeconds,
                    Math.Min(matchStartSeconds, matchEndSeconds),
                    Math.Max(matchStartSeconds, matchEndSeconds));

                return new SegmentTimeMapPoint(sourceSeconds, candidateSeconds);
            })
            .GroupBy(point => point.SourceSeconds)
            .OrderBy(group => group.Key);

        var collapsed = new List<SegmentTimeMapPoint>();
        foreach (var group in grouped)
        {
            var matchSeconds = group
                .Select(point => point.MatchSeconds)
                .Order()
                .ToArray();
            if (matchSeconds.Length == 0)
            {
                continue;
            }

            var point = new SegmentTimeMapPoint(group.Key, Median(matchSeconds));
            if (collapsed.Count > 0 && point.MatchSeconds < collapsed[^1].MatchSeconds)
            {
                continue;
            }

            collapsed.Add(point);
        }

        return collapsed;
    }

    private static bool HasSameDirection(
        TrackPath source,
        TrackPath candidate,
        CandidateWindow window)
    {
        var comparisons = 0;
        var agreeing = 0;
        var maxComparisons = Math.Min(source.Samples.Count, window.Count) - 1;
        if (maxComparisons < 1)
        {
            return false;
        }

        for (var i = 0; i < maxComparisons; i++)
        {
            var sourceIndex = RelativeIndex(i, maxComparisons, source.Samples.Count - 1);
            var candidateIndex = window.StartIndex + RelativeIndex(i, maxComparisons, window.Count - 1);
            if (sourceIndex >= source.UnitDirections.Count || candidateIndex >= candidate.UnitDirections.Count)
            {
                continue;
            }

            var sourceDirection = source.UnitDirections[sourceIndex];
            var candidateDirection = candidate.UnitDirections[candidateIndex];
            if (!sourceDirection.IsValid || !candidateDirection.IsValid)
            {
                continue;
            }

            comparisons++;
            var dot = sourceDirection.X * candidateDirection.X + sourceDirection.Y * candidateDirection.Y;
            if (dot > 0)
            {
                agreeing++;
            }
        }

        return comparisons > 0 && (double)agreeing / comparisons >= DirectionAgreementThreshold;
    }

    private static IReadOnlyList<SegmentSample> SimplifyByDistance(
        IReadOnlyList<SegmentSample> samples,
        double stepMeters)
    {
        var simplified = new List<SegmentSample>();
        SimplifyByDistance(samples, stepMeters, new CandidateWindow(0, samples.Count), simplified);
        return simplified;
    }

    private static void SimplifyByDistance(
        IReadOnlyList<SegmentSample> samples,
        double stepMeters,
        CandidateWindow window,
        List<SegmentSample> output)
    {
        var start = window.StartIndex;
        var end = window.EndIndex;
        output.Clear();
        output.Add(samples[start]);
        var nextDistance = samples[start].DistanceMeters + stepMeters;

        for (var i = start + 1; i < end; i++)
        {
            if (samples[i].DistanceMeters < nextDistance)
            {
                continue;
            }

            output.Add(samples[i]);
            nextDistance = samples[i].DistanceMeters + stepMeters;
        }

        output.Add(samples[end]);
    }

    private static bool IsOverlappingSourceWindow(
        CandidateTrack candidate,
        CandidateWindow window,
        SegmentMatchRequest request)
    {
        if (request.Source.SessionId != candidate.Session.Id)
        {
            return false;
        }

        var timeline = ResolveTimeline(candidate);
        var candidateStartSeconds = timeline.ToTelemetrySeconds(candidate.Path.Samples[window.StartIndex].SourceTimeSeconds);
        var candidateEndSeconds = timeline.ToTelemetrySeconds(candidate.Path.Samples[window.EndIndex].SourceTimeSeconds);
        return candidateStartSeconds < request.Source.Range.EndSeconds &&
               request.Source.Range.StartSeconds < candidateEndSeconds;
    }

    private static SessionTrackTimeline ResolveTimeline(CandidateTrack candidate)
    {
        return candidate.Session.Timestamp is { } timestamp
            ? new SessionTrackTimeline(timestamp, candidate.Session.ContentToken.GpsOffsetSeconds)
            : new SessionTrackTimeline(candidate.Path.Samples[0].SourceTimeSeconds);
    }

    private static bool IsBetter(CandidateWindowScore left, CandidateWindowScore right)
    {
        var comparison = left.NormalizedCostMeters.CompareTo(right.NormalizedCostMeters);
        if (comparison != 0)
        {
            return comparison < 0;
        }

        comparison = left.MedianErrorMeters.CompareTo(right.MedianErrorMeters);
        if (comparison != 0)
        {
            return comparison < 0;
        }

        if (left.Window.Count != right.Window.Count)
        {
            return left.Window.Count > right.Window.Count;
        }

        // Deterministic final tie-break so the parallel merge order cannot
        // change which of two equally scored windows wins.
        return left.Window.StartIndex < right.Window.StartIndex;
    }

    private static int CompareCoarseScores(CandidateWindowScore left, CandidateWindowScore right)
    {
        var comparison = left.NormalizedCostMeters.CompareTo(right.NormalizedCostMeters);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = left.MedianErrorMeters.CompareTo(right.MedianErrorMeters);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = left.Window.Count.CompareTo(right.Window.Count);
        return comparison != 0
            ? comparison
            : left.Window.StartIndex.CompareTo(right.Window.StartIndex);
    }

    private static int RelativeIndex(int index, int count, int maxIndex)
    {
        if (count <= 1)
        {
            return 0;
        }

        return Math.Min(maxIndex, (int)Math.Round((double)index / count * maxIndex));
    }

    private static (double X, double Y) VectorBetween(SegmentSample first, SegmentSample second)
    {
        return (second.X - first.X, second.Y - first.Y);
    }

    private static IReadOnlyList<UnitDirection> BuildUnitDirections(IReadOnlyList<SegmentSample> samples)
    {
        if (samples.Count < 2)
        {
            return [];
        }

        var directions = new UnitDirection[samples.Count - 1];
        for (var i = 0; i < directions.Length; i++)
        {
            var dx = samples[i + 1].X - samples[i].X;
            var dy = samples[i + 1].Y - samples[i].Y;
            var length = Hypot(dx, dy);
            directions[i] = length > 0 && double.IsFinite(length)
                ? new UnitDirection(dx / length, dy / length, IsValid: true)
                : new UnitDirection(0, 0, IsValid: false);
        }

        return directions;
    }

    private static double Distance(SegmentSample first, SegmentSample second)
    {
        return Hypot(second.X - first.X, second.Y - first.Y);
    }

    private static double SquaredDistance(SegmentSample first, SegmentSample second)
    {
        var x = second.X - first.X;
        var y = second.Y - first.Y;
        return x * x + y * y;
    }

    private static double Distance((double X, double Y) first, (double X, double Y) second)
    {
        return Hypot(second.X - first.X, second.Y - first.Y);
    }

    private static double Hypot(double x, double y)
    {
        return Math.Sqrt(x * x + y * y);
    }

    private static double Percentile(IReadOnlyList<double> sorted, double percentile)
    {
        var index = (int)Math.Ceiling(sorted.Count * percentile) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }

    private static double Median(IReadOnlyList<double> sorted)
    {
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    internal readonly record struct LocalReference(double OriginX, double OriginY, double Scale);
    private readonly record struct LocalPoint(double X, double Y, double Time);
    internal readonly record struct SegmentSample(double X, double Y, double SourceTimeSeconds, double DistanceMeters);
    internal readonly record struct UnitDirection(double X, double Y, bool IsValid);
    internal sealed class TrackPath
    {
        public TrackPath(
            LocalReference reference,
            IReadOnlyList<SegmentSample> samples,
            double distanceMeters)
        {
            Reference = reference;
            Samples = samples;
            DistanceMeters = distanceMeters;
            Bounds = Bounds.From(samples);
            BoundsIndex = RangeBoundsIndex.Create(samples);
            UnitDirections = BuildUnitDirections(samples);
        }

        public LocalReference Reference { get; }
        public IReadOnlyList<SegmentSample> Samples { get; }
        public double DistanceMeters { get; }
        public Bounds Bounds { get; }
        public RangeBoundsIndex BoundsIndex { get; }
        public IReadOnlyList<UnitDirection> UnitDirections { get; }
    }

    internal sealed record CandidateTrack(SessionCatalogItem Session, TrackPath Path);
    internal readonly record struct CandidateWindow(int StartIndex, int Count)
    {
        public int EndIndex => StartIndex + Count - 1;
    }
    private readonly record struct CandidateWindowScore(
        CandidateWindow Window,
        double NormalizedCostMeters,
        double MedianErrorMeters,
        double P90ErrorMeters);
    private readonly record struct DtwScore(
        double NormalizedCostMeters,
        double MedianErrorMeters,
        double P90ErrorMeters);
    private readonly record struct DtwPathPoint(int SourceIndex, int CandidateIndex, double DistanceMeters);

    // Stored as bytes in the reusable step matrix to keep it compact.
    private enum DtwStep : byte
    {
        Diagonal,
        Up,
        Left,
    }

    // Per-worker reusable buffers for DTW scoring; grown on demand so a worker
    // allocates at most once per candidate instead of twice per window.
    private sealed class DtwScratch
    {
        public double[] Costs { get; private set; } = [];
        public byte[] Steps { get; private set; } = [];
        public List<double> MatchedDistances { get; } = [];
        public List<SegmentSample> SimplifiedCandidate { get; } = [];

        public void EnsureDtwCapacity(int cellCount)
        {
            if (Costs.Length >= cellCount)
            {
                return;
            }

            var capacity = Math.Max(cellCount, Costs.Length * 2);
            Costs = new double[capacity];
            Steps = new byte[capacity];
        }
    }

    private sealed class CoarseScanLocal
    {
        public DtwScratch Scratch { get; } = new();
        public CandidateWindowTopK Winners { get; } = new(CoarseWinnerCount);
    }

    private sealed class RefinementLocal
    {
        public DtwScratch Scratch { get; } = new();
        public CandidateWindowScore? Best { get; set; }
    }

    private sealed class RefinementProgress
    {
        public int Completed;
    }

    private sealed class CandidateWindowTopK
    {
        private readonly int capacity;
        private readonly List<CandidateWindowScore> scores = [];

        public CandidateWindowTopK(int capacity)
        {
            this.capacity = capacity;
        }

        public int Count => scores.Count;

        public void Add(CandidateWindowScore score)
        {
            if (capacity <= 0)
            {
                return;
            }

            var insertIndex = scores.FindIndex(existing => CompareCoarseScores(score, existing) < 0);
            if (insertIndex < 0)
            {
                if (scores.Count < capacity)
                {
                    scores.Add(score);
                }

                return;
            }

            scores.Insert(insertIndex, score);
            if (scores.Count > capacity)
            {
                scores.RemoveAt(scores.Count - 1);
            }
        }

        public void AddRange(CandidateWindowTopK other)
        {
            foreach (var score in other.scores)
            {
                Add(score);
            }
        }

        public IReadOnlyList<CandidateWindowScore> ToSortedList()
        {
            return scores.ToList();
        }
    }

    internal readonly record struct Bounds(double MinX, double MinY, double MaxX, double MaxY)
    {
        public static Bounds Empty => new(
            double.PositiveInfinity,
            double.PositiveInfinity,
            double.NegativeInfinity,
            double.NegativeInfinity);

        public (double X, double Y) Center => ((MinX + MaxX) / 2, (MinY + MaxY) / 2);
        public bool IsEmpty => MinX > MaxX || MinY > MaxY;

        public static Bounds From(IReadOnlyList<SegmentSample> samples)
        {
            return From(samples, new CandidateWindow(0, samples.Count));
        }

        public static Bounds From(IReadOnlyList<SegmentSample> samples, CandidateWindow window)
        {
            var bounds = Empty;

            for (var i = window.StartIndex; i <= window.EndIndex; i++)
            {
                bounds = bounds.Include(samples[i]);
            }

            return bounds;
        }

        public Bounds Include(SegmentSample sample)
        {
            return new Bounds(
                Math.Min(MinX, sample.X),
                Math.Min(MinY, sample.Y),
                Math.Max(MaxX, sample.X),
                Math.Max(MaxY, sample.Y));
        }

        public Bounds Include(Bounds other)
        {
            if (IsEmpty)
            {
                return other;
            }

            if (other.IsEmpty)
            {
                return this;
            }

            return new Bounds(
                Math.Min(MinX, other.MinX),
                Math.Min(MinY, other.MinY),
                Math.Max(MaxX, other.MaxX),
                Math.Max(MaxY, other.MaxY));
        }

        public double DistanceTo(Bounds other)
        {
            var x = Math.Max(0, Math.Max(other.MinX - MaxX, MinX - other.MaxX));
            var y = Math.Max(0, Math.Max(other.MinY - MaxY, MinY - other.MaxY));
            return Hypot(x, y);
        }
    }

    internal sealed class RangeBoundsIndex
    {
        private readonly IReadOnlyList<SegmentSample> samples;
        private readonly Bounds[] blockBounds;

        private RangeBoundsIndex(IReadOnlyList<SegmentSample> samples, Bounds[] blockBounds)
        {
            this.samples = samples;
            this.blockBounds = blockBounds;
        }

        public static RangeBoundsIndex Create(IReadOnlyList<SegmentSample> samples)
        {
            var fullBlockCount = samples.Count / BoundsBlockSize;
            var blockBounds = new Bounds[fullBlockCount];
            for (var block = 0; block < fullBlockCount; block++)
            {
                blockBounds[block] = Bounds.From(
                    samples,
                    new CandidateWindow(block * BoundsBlockSize, BoundsBlockSize));
            }

            return new RangeBoundsIndex(samples, blockBounds);
        }

        public Bounds Get(CandidateWindow window)
        {
            var bounds = Bounds.Empty;
            var index = window.StartIndex;
            var end = window.EndIndex;

            while (index <= end && index % BoundsBlockSize != 0)
            {
                bounds = bounds.Include(samples[index]);
                index++;
            }

            while (index + BoundsBlockSize - 1 <= end)
            {
                bounds = bounds.Include(blockBounds[index / BoundsBlockSize]);
                index += BoundsBlockSize;
            }

            while (index <= end)
            {
                bounds = bounds.Include(samples[index]);
                index++;
            }

            return bounds;
        }
    }
}
