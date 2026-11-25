using System.Collections.Immutable;

namespace Sufni.Telemetry;

public sealed record AlignedTerrainRun(ClassifiedProfile Profile, TerrainRunAlignment.Aligned Alignment);

public sealed record TrailAverageWindow(int SourceIndex, double StartSeconds, double EndSeconds, double? FrameLevel,
    double? ForkLevel, double? RearAxleLevel, double? Grade, RoughnessClass? Roughness, GradeBand? Band);

public closed record TrailAverage(int RunCount)
{
    public const int MinimumRuns = 3;
    public sealed record Averaged(int RunCount, ImmutableArray<TrailAverageWindow> Windows) : TrailAverage(RunCount);
    public sealed record TooFewRuns(int RunCount) : TrailAverage(RunCount);

    public static TrailAverage Calculate(ClassifiedProfile source, TelemetryTimeRange range,
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
                totals[pair.SourceIndex].Add(run.Profile.Windows[pair.MatchIndex].Window);
        }
        var windows = new List<TrailAverageWindow>();
        var climbLevels = new List<RoughnessClass?>();
        for (var i = 0; i < source.Windows.Length; i++)
        {
            var w = source.Windows[i].Window;
            var centre = (w.StartSeconds + w.EndSeconds) / 2;
            if (centre < range.StartSeconds || centre >= range.EndSeconds) continue;
            token.ThrowIfCancellationRequested();
            if (!w.Moving)
            {
                // A stop stays in the list so the grade baseline walks past it, as it does for a single ride.
                windows.Add(new(i, w.StartSeconds, w.EndSeconds, null, null, null, null, null, null));
                climbLevels.Add(null);
                continue;
            }
            totals[i].Add(w);
            windows.Add(totals[i].Window(i, w));
            climbLevels.Add(totals[i].ClimbLevel);
        }
        for (var i = 0; i < windows.Count; i++)
            if (BaselineGrade(windows, i) is { } baseline)
                windows[i] = windows[i] with { Band = TerrainProfileCalculator.ClassifyGrade(baseline) };
        TerrainProfileCalculator.MergeOutliers(windows, static w => w.Band, static (w, band) => w with { Band = band }, Adjacent);
        for (var i = 0; i < windows.Count; i++)
            if (windows[i].Band?.IsClimb() == true) windows[i] = windows[i] with { Roughness = climbLevels[i] };
        TerrainProfileCalculator.MergeOutliers(windows, static w => w.Roughness, static (w, level) => w with { Roughness = level }, Adjacent);
        return new Averaged(count, [.. windows]);
    }

    public static double? BaselineGrade(IReadOnlyList<TrailAverageWindow> windows, int index) =>
        TerrainProfileCalculator.BaselineGrade(windows, index, static w => w.Grade, Adjacent);

    private static bool Adjacent(TrailAverageWindow a, TrailAverageWindow b) => a.EndSeconds == b.StartSeconds;

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
        private Mean logFrame, logFork, logRearAxle, normalisedFrame, normalisedFork, grade, logClimbFrame;

        public void Add(TerrainWindow window)
        {
            if (window.Frame is { } frame) logFrame.Add(Math.Log(frame.SpeedRemoved));
            if (window.Fork is { } fork) logFork.Add(Math.Log(fork.SpeedRemoved));
            if (window.RearAxle is { } rearAxle) logRearAxle.Add(Math.Log(rearAxle.SpeedRemoved));
            if (window.Frame is { Normalised: not null } calibrated) logClimbFrame.Add(Math.Log(calibrated.Level));
            normalisedFrame.Add(window.Frame?.Normalised);
            normalisedFork.Add(TerrainProfileCalculator.ForkClassValue(window.Frame, window.Fork));
            grade.Add(window.Grade);
        }

        public readonly TrailAverageWindow Window(int index, TerrainWindow source)
        {
            var frame = normalisedFrame.Value;
            var fork = normalisedFork.Value;
            var normalised = (frame + fork) / 2 ?? frame ?? fork;
            return new(index, source.StartSeconds, source.EndSeconds, Exp(logFrame.Value), Exp(logFork.Value), Exp(logRearAxle.Value),
                grade.Value, normalised.HasValue ? TerrainCalibration.Classify(normalised.Value) : null, null);
        }

        public readonly RoughnessClass? ClimbLevel => Exp(logClimbFrame.Value) is { } level ? TerrainCalibration.ClassifyClimb(level) : null;

        private static double? Exp(double? log) => log.HasValue ? Math.Exp(log.Value) : null;
    }
}
