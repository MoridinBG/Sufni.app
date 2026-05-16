namespace Sufni.Telemetry;

public record SignalChange(int Start, int End, int Change);

public static class SpikeElimination
{
    // Candidate windows must move this many raw counts from start to end.
    private const int MinimumCandidateTotalChangeCounts = 100;
    // Every adjacent step in a candidate window must meet this raw-count rate.
    private const int MinimumAdjacentStepChangeRateCountsPerSecond = 30_000;
    // Sudden-change candidates are searched only up to this duration.
    private const int MaxSuddenChangeDurationMilliseconds = 5;
    // Same-direction motion beyond the endpoint in this duration keeps the ramp.
    private const int ContinuationLookaheadDurationMilliseconds = 5;
    // Baseline-shift correction is only allowed this close to recording start.
    private const int EarlyBaselineShiftDurationMilliseconds = 100;

    public static (ushort[] fixedSignal, int anomalyCount) EliminateSpikes(
        int[] signal,
        int sampleRate)
    {
        var (fixedSignal, anomalyCount) = EliminateSpikesAsInt(signal, sampleRate);
        return (fixedSignal.Select(ClampAdcSample).ToArray(), anomalyCount);
    }

    public static (int[] fixedSignal, int anomalyCount) EliminateSpikesAsInt(
        int[] signal,
        int sampleRate)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be positive.");
        }

        var maxCandidateSamples = ScaleDurationMilliseconds(MaxSuddenChangeDurationMilliseconds, sampleRate);
        var continuationLookaheadSamples = ScaleDurationMilliseconds(ContinuationLookaheadDurationMilliseconds, sampleRate);
        var earlyBaselineShiftSampleLimit = ScaleDurationMilliseconds(EarlyBaselineShiftDurationMilliseconds, sampleRate);
        var minimumAdjacentStepChangeCounts = ScaleRateToPerSampleThreshold(MinimumAdjacentStepChangeRateCountsPerSecond, sampleRate);

        // Detecting sudden, abnormal changes in signal.
        var changes = DetectSuddenChanges(
            signal,
            maxCandidateSamples,
            MinimumCandidateTotalChangeCounts,
            minimumAdjacentStepChangeCounts,
            continuationLookaheadSamples);
        changes.Sort((a, b) => a.Start.CompareTo(b.Start));
        var anomalyCount = changes.Count;

        // If a sudden change occurs over a short time window, we make it a
        // one-step change by flattening all except one point within the window.
        foreach (var change in changes)
        {
            for (var i = change.Start + 1; i <= change.End; i++)
            {
                signal[i] = signal[change.End];
            }
        }

        // Sometimes the value reported by the sensor jumps an unreasonably large number
        // near the beginning, but measures everything correctly
        // from that baseline. We fix that jump here.
        if (changes.Count > 0 && changes[0].Start < earlyBaselineShiftSampleLimit)
        {
            var shiftStart = changes[0].End;
            var shiftDelta = changes[0].Change;
            changes.RemoveAt(0);
            for (var i = shiftStart; i < signal.Length; i++)
            {
                signal[i] -= shiftDelta;
            }
        }

        // Sometimes the value reported by the sensor dips a large amount and later
        // recovers. If the recovery never comes, treat the negative tail as sensor fault
        // and correct it through the end of the capture.
        var activeFaultDelta = 0;
        var segmentStart = 0;
        SignalChange? previousChange = null;
        foreach (var change in changes)
        {
            for (var j = segmentStart; j <= change.Start; j++)
            {
                signal[j] -= activeFaultDelta;
            }

            if (change.Change < 0)
            {
                if (previousChange is not { Change: > 0 } || Math.Abs(previousChange.Change + change.Change) >= MinimumCandidateTotalChangeCounts)
                {
                    activeFaultDelta += change.Change;
                }
            }
            else if (activeFaultDelta < 0)
            {
                activeFaultDelta = Math.Min(0, activeFaultDelta + change.Change);
            }

            segmentStart = change.Start + 1;
            previousChange = change;
        }

        for (var j = segmentStart; j < signal.Length; j++)
        {
            signal[j] -= activeFaultDelta;
        }

        return (signal, anomalyCount);
    }

    private static ushort ClampAdcSample(int value) => (ushort)Math.Clamp(value, 0, 4095);

    private static int ScaleDurationMilliseconds(int durationMilliseconds, int sampleRate)
    {
        return Math.Max(1, (int)Math.Ceiling(durationMilliseconds * sampleRate / 1000.0));
    }

    private static int ScaleRateToPerSampleThreshold(int thresholdCountsPerSecond, int sampleRate)
    {
        return Math.Max(1, (int)Math.Ceiling(thresholdCountsPerSecond / (double)sampleRate));
    }

    private static List<SignalChange> DetectSuddenChanges(
        int[] signal,
        int maxCandidateSamples,
        int minimumCandidateTotalChangeCounts,
        int minimumAdjacentStepChangeCounts,
        int continuationLookaheadSamples)
    {
        var n = signal.Length;
        var changes = new List<SignalChange>();
        var included = new bool[n]; // Track included indexes to avoid nesting

        for (var window = maxCandidateSamples; window > 0; window--)
        {
            for (var i = 0; i <= n - window - 1; i++)
            {
                // Skip if entire window is already included in previous change
                var overlap = false;
                for (var k = i; k <= i + window; k++)
                {
                    if (!included[k]) continue;
                    overlap = true;
                    break;
                }
                if (overlap)
                    continue;

                var start = signal[i];
                var end = signal[i + window];
                var totalChange = end - start;

                if (Math.Abs(totalChange) < minimumCandidateTotalChangeCounts)
                    continue;

                var allStepsBigEnough = true;
                for (var j = i; j < i + window; j++)
                {
                    var stepDiff = signal[j + 1] - signal[j];
                    if (Math.Abs(stepDiff) >= minimumAdjacentStepChangeCounts) continue;
                    allStepsBigEnough = false;
                    break;
                }

                if (!allStepsBigEnough) continue;

                var canApplyContinuationCheck = window >= 2 || maxCandidateSamples == 1;
                if (canApplyContinuationCheck &&
                    ContinuesPastEndpoint(signal, i + window, totalChange, minimumAdjacentStepChangeCounts, continuationLookaheadSamples))
                {
                    continue;
                }

                changes.Add(new SignalChange(i, i + window, totalChange));

                // Mark this region as included to avoid overlapping detections
                for (var k = i; k <= i + window; k++)
                    included[k] = true;
            }
        }

        return changes;
    }

    private static bool ContinuesPastEndpoint(
        int[] signal,
        int end,
        int change,
        int minimumContinuationCounts,
        int continuationLookaheadSamples)
    {
        var direction = Math.Sign(change);
        if (direction == 0)
        {
            return false;
        }

        var endpoint = signal[end];
        var lookaheadEnd = Math.Min(signal.Length - 1, end + continuationLookaheadSamples);
        for (var index = end + 1; index <= lookaheadEnd; index++)
        {
            var continuation = (signal[index] - endpoint) * direction;
            if (continuation >= minimumContinuationCounts)
            {
                return true;
            }
        }

        return false;
    }
}
