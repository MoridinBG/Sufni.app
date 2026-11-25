using System.Collections.Immutable;
using System.Linq;
using Sufni.App.Terrain.Models;
using Sufni.Telemetry;

namespace Sufni.App.Segments.Models;

public closed record SegmentTrailAverage
{
    public sealed record Averaged(TrailAverage.Averaged Measurement, ImmutableArray<SegmentComparisonSeries> Roughness,
        ImmutableArray<SegmentComparisonSeries> Grade) : SegmentTrailAverage;
    public sealed record TooFewRuns(int RunCount) : SegmentTrailAverage;
    public sealed record Failed : SegmentTrailAverage;
    public sealed record Cancelled : SegmentTrailAverage;

    internal static SegmentTrailAverage Present(TrailAverage measurement)
    {
        if (measurement is TrailAverage.TooFewRuns missing) return new TooFewRuns(missing.RunCount);
        var average = (TrailAverage.Averaged)measurement;
        var frame = new TerrainSignalSections();
        var fork = new TerrainSignalSections();
        var rearAxle = new TerrainSignalSections();
        var grade = new TerrainSignalSections();
        for (var i = 0; i < average.Windows.Length; i++)
        {
            var w = average.Windows[i];
            frame.Add(w.StartSeconds, w.EndSeconds, w.FrameLevel);
            fork.Add(w.StartSeconds, w.EndSeconds, w.ForkLevel);
            rearAxle.Add(w.StartSeconds, w.EndSeconds, w.RearAxleLevel);
            grade.Add(w.StartSeconds, w.EndSeconds, TerrainSignalSections.Degrees(TrailAverage.BaselineGrade(average.Windows, i)));
        }
        return new Averaged(average, [.. Series(frame, "Frame", "g", SegmentComparisonSeriesRole.FrameImu),
            .. Series(fork, "Fork", "g", SegmentComparisonSeriesRole.ForkImu),
            .. Series(rearAxle, "Rear axle", "g", SegmentComparisonSeriesRole.RearAxleImu)], [.. Series(grade, "Grade", "°", SegmentComparisonSeriesRole.Grade)]);
    }

    private static ImmutableArray<SegmentComparisonSeries> Series(TerrainSignalSections sections, string label, string unit,
        SegmentComparisonSeriesRole role)
    {
        var runs = sections.Build().Select(s => new SegmentComparisonSeriesRun(s.Seconds.ToArray(), s.Values.ToArray())).ToArray();
        return runs.Length == 0 ? [] : [new(label, unit, role, [], [], Runs: runs)];
    }
}
