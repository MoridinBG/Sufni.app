namespace Sufni.Telemetry;

public record SignalChange(int Start, int End, int Change);

public static class SpikeElimination
{
    private const int SummaThreshold = 100;
    private const int StepThreshold = 30;

    public static (ushort[] fixedSignal, int anomalyCount) EliminateSpikes(int[] signal)
    {
        var (fixedSignal, anomalyCount) = EliminateSpikesAsInt(signal);
        return (fixedSignal.Select(ClampAdcSample).ToArray(), anomalyCount);
    }

    public static (int[] fixedSignal, int anomalyCount) EliminateSpikesAsInt(int[] signal)
    {
        // Detecting sudden, abnormal changes in signal.
        var changes = DetectSuddenChanges(signal, 5, SummaThreshold, StepThreshold);
        changes.Sort((a, b) => a.Start.CompareTo(b.Start));
        var anomalyCount = changes.Count;

        // If a sudden change occurs over a couple (maximum 5) measurements, we make it a
        // one-step change by flattening all except one points within the window.
        foreach (var change in changes)
        {
            for (var i = change.Start + 1; i <= change.End; i++)
            {
                signal[i] = signal[change.End];
            }
        }

        // Sometimes the value reported by the sensor jumps an unreasonably large number
        // at the beginning, after a few tenth of seconds, but measures everything correctly
        // from that baseline. We fix that jump here.
        if (changes.Count > 0 && changes[0].Start < 100)
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
                if (previousChange is not { Change: > 0 } || Math.Abs(previousChange.Change + change.Change) >= SummaThreshold)
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

    private static List<SignalChange> DetectSuddenChanges(int[] signal, int maxWindow, int summaThreshold, int stepThreshold)
    {
        var n = signal.Length;
        var changes = new List<SignalChange>();
        var included = new bool[n]; // Track included indexes to avoid nesting

        for (var window = maxWindow; window > 0; window--)
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

                if (Math.Abs(totalChange) < summaThreshold)
                    continue;

                var allStepsBigEnough = true;
                for (var j = i; j < i + window; j++)
                {
                    var stepDiff = signal[j + 1] - signal[j];
                    if (Math.Abs(stepDiff) >= stepThreshold) continue;
                    allStepsBigEnough = false;
                    break;
                }

                if (!allStepsBigEnough) continue;

                changes.Add(new SignalChange(i, i + window, totalChange));

                // Mark this region as included to avoid overlapping detections
                for (var k = i; k <= i + window; k++)
                    included[k] = true;
            }
        }

        return changes;
    }
}
