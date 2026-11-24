using System.Collections.Immutable;

namespace Sufni.Telemetry;

public sealed record AlignedTerrainRun(TerrainProfile Profile, TerrainRunAlignment.Aligned Alignment);

public sealed record TrailAverageWindow(int SourceIndex, double StartSeconds, double EndSeconds, double? FrameLevel,
    double? LowersLevel, double? Grade, RoughnessClass? Roughness, GradeBand? Band);

public closed record TrailAverage(int RunCount)
{
    public const int MinimumRuns = 3;
    public sealed record Averaged(int RunCount, ImmutableArray<TrailAverageWindow> Windows) : TrailAverage(RunCount);
    public sealed record TooFewRuns(int RunCount) : TrailAverage(RunCount);

    public static TrailAverage Calculate(TerrainProfile source, TelemetryTimeRange range,
        IReadOnlyCollection<AlignedTerrainRun> aligned, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var count = aligned.Count + 1;
        if (count < MinimumRuns) return new TooFewRuns(count);
        var totals = new WindowTotals[source.Windows.Length];
        foreach (var run in aligned)
        {
            token.ThrowIfCancellationRequested();
            foreach (var pair in run.Alignment.Pairs)
                totals[pair.SourceIndex].Add(run.Profile.Windows[pair.MatchIndex]);
        }
        var windows = new List<TrailAverageWindow>();
        for (var i = 0; i < source.Windows.Length; i++)
        {
            var w = source.Windows[i];
            var centre = (w.StartSeconds + w.EndSeconds) / 2;
            if (centre < range.StartSeconds || centre >= range.EndSeconds) continue;
            token.ThrowIfCancellationRequested();
            if (!w.Moving)
            {
                // A stop stays in the list so the grade baseline walks past it, as it does for a single ride.
                windows.Add(new(i, w.StartSeconds, w.EndSeconds, null, null, null, null, null));
                continue;
            }
            totals[i].Add(w);
            windows.Add(totals[i].Window(i, w));
        }
        for (var i = 0; i < windows.Count; i++)
        {
            if (BaselineGrade(windows, i) is not { } baseline) continue;
            var band = TerrainProfileCalculator.ClassifyGrade(baseline);
            windows[i] = windows[i] with { Band = band, Roughness = band.IsClimb() ? null : windows[i].Roughness };
        }
        return new Averaged(count, [.. windows]);
    }

    public static double? BaselineGrade(IReadOnlyList<TrailAverageWindow> windows, int index) =>
        TerrainProfileCalculator.BaselineGrade(windows, index, static w => w.Grade, static (a, b) => a.EndSeconds == b.StartSeconds);

    private struct Mean
    {
        private double sum;
        private int count;
        public void Add(double? value)
        {
            if (value is not { } reading) return;
            sum += reading;
            count++;
        }

        public readonly double? Value => count >= MinimumRuns ? sum / count : null;
    }

    private struct WindowTotals
    {
        private Mean logFrame, logLowers, normalisedFrame, normalisedLowers, grade;

        public void Add(TerrainWindow window)
        {
            if (window.Frame is { } frame) logFrame.Add(Math.Log(frame.SpeedRemoved));
            if (window.Lowers is { } lowers) logLowers.Add(Math.Log(lowers.SpeedRemoved));
            normalisedFrame.Add(window.Frame?.Normalised);
            normalisedLowers.Add(TerrainProfileCalculator.LowersClassValue(window.Frame, window.Lowers));
            grade.Add(window.Grade);
        }

        public readonly TrailAverageWindow Window(int index, TerrainWindow source)
        {
            var frame = normalisedFrame.Value;
            var lowers = normalisedLowers.Value;
            var normalised = (frame + lowers) / 2 ?? frame ?? lowers;
            return new(index, source.StartSeconds, source.EndSeconds, Exp(logFrame.Value), Exp(logLowers.Value),
                grade.Value, normalised.HasValue ? TerrainCalibration.Classify(normalised.Value) : null, null);
        }

        private static double? Exp(double? log) => log.HasValue ? Math.Exp(log.Value) : null;
    }
}
