using System.Collections.Immutable;
using MathNet.Numerics.Statistics;

namespace Sufni.Telemetry;

public readonly record struct ComparisonRatio(ResponseCellState State, double? Ratio = null)
{
    /// <summary>Signed difference from the matched run: −0.16 is 16% lower.</summary>
    public double? Deviation => Ratio - 1;
}

public readonly record struct PaceQuartiles(double Lower, double Median, double Upper);
public sealed record TerrainComparisonRow(RoughnessClass? Class, double DistanceMetres, PaceQuartiles? Pace,
    ImmutableDictionary<ResponseTarget, ComparisonRatio> Responses, ComparisonRatio FrontSamePosition, ComparisonRatio RearSamePosition)
{
    public ResponseConfidence Confidence => TerrainResponse.ConfidenceAt(DistanceMetres);
}

public static class TerrainRunComparison
{
    public const double ResponseThreshold = 0.20;
    public const double ReboundThreshold = 0.25;
    public const double PassedToFrameThreshold = 0.05;
    public const double SamePositionSpeedExponent = 0.9;

    public static ImmutableArray<TerrainComparisonRow> Compare(ClassifiedProfile current, ClassifiedProfile earlier,
        TerrainRunAlignment.Aligned alignment, SuspensionResponseModel model, TrailAverage.Averaged? average = null, CancellationToken token = default)
    {
        var levels = Enum.GetValues<RoughnessClass>().Where(level => !level.IsClimb()).ToArray();
        var groups = levels.Select(_ => new List<Pair>()).Append([]).ToArray();
        var averageClasses = average?.Windows.ToDictionary(w => w.SourceIndex, w => w.Roughness);
        var pairs = alignment.Pairs.Select(indices =>
        {
            var (a, b) = (current.Windows[indices.SourceIndex], earlier.Windows[indices.MatchIndex]);
            return new ClassifiedPair(new(a.Window, b.Window),
                averageClasses is null ? PairClass(a, b) : averageClasses.GetValueOrDefault(indices.SourceIndex));
        }).ToList();
        // Averaged classes arrive merged already.
        if (averageClasses is null)
            TerrainProfileCalculator.MergeOutliers(pairs, static p => p.Class, static (p, level) => p with { Class = level },
                static (a, b) => a.Pair.Current.EndSeconds == b.Pair.Current.StartSeconds);
        foreach (var (pair, roughness) in pairs)
        {
            token.ThrowIfCancellationRequested();
            groups[^1].Add(pair);
            if (roughness is { } level && !level.IsClimb()) groups[(int)level].Add(pair);
        }
        return [.. groups.Select((pairs, index) => Describe(pairs, index < levels.Length ? levels[index] : null, model, token))];
    }

    private static RoughnessClass? PairClass(ClassifiedWindow current, ClassifiedWindow earlier) =>
        (current.ClassValue + earlier.ClassValue) / 2 is { } mean
            ? TerrainCalibration.Classify(mean) : null;

    private static TerrainComparisonRow Describe(List<Pair> pairs, RoughnessClass? roughness,
        SuspensionResponseModel model, CancellationToken token)
    {
        var metres = pairs.Count * TerrainProfileCalculator.WindowMetres;
        var enough = metres >= SuspensionResponseModel.MinimumReportMetres;
        PaceQuartiles? pace = null;
        if (enough)
        {
            var speeds = pairs.Select(pair => pair.SpeedRatio).Order().ToArray();
            pace = new(SortedArrayStatistics.Quantile(speeds, 0.25), SortedArrayStatistics.Median(speeds),
                SortedArrayStatistics.Quantile(speeds, 0.75));
        }
        var responses = ImmutableDictionary.CreateBuilder<ResponseTarget, ComparisonRatio>();
        foreach (var (target, fit) in model.Fits)
        {
            token.ThrowIfCancellationRequested();
            responses[target] = !enough ? new(ResponseCellState.TooShort)
                : fit is ResponseFit.Unavailable missing ? new(TerrainResponse.UnavailableState(missing.Reason))
                : Ratio(pairs, p => model.LogResidual(target, p.Current) - model.LogResidual(target, p.Earlier),
                    RepeatRange(target));
        }
        return new(roughness, metres, pace, responses.ToImmutable(), SamePosition(true), SamePosition(false));

        ComparisonRatio SamePosition(bool front) => enough
            ? Ratio(pairs, p => p.SamePosition(front), ResponseThreshold) : new(ResponseCellState.TooShort);
    }

    /// <summary>The deviation between two runs of one trail that repeat runs stay within.</summary>
    public static double RepeatRange(ResponseTarget target) => target switch
    {
        ResponseTarget.PassedToFrame => PassedToFrameThreshold,
        ResponseTarget.FrontPeakRebound or ResponseTarget.RearPeakRebound => ReboundThreshold,
        _ => ResponseThreshold
    };

    public static ImmutableArray<ResponseFinding> Findings(IEnumerable<TerrainComparisonRow> rows) =>
        [.. rows.SelectMany(row => row.Responses.Where(response => response.Value.State == ResponseCellState.Finding)
            .OrderBy(response => response.Key)
            .Select(response => new ResponseFinding(response.Key, row.Class, response.Value.Deviation!.Value, row.Confidence)))];

    private static ComparisonRatio Ratio(List<Pair> pairs, Func<Pair, double?> residual, double threshold)
    {
        double sum = 0;
        var count = 0;
        foreach (var pair in pairs)
        {
            if (residual(pair) is not { } value) continue;
            sum += value;
            count++;
        }
        if (count == 0) return new(ResponseCellState.NoMeasurement);
        var ratio = Math.Exp(sum / count);
        return new(TerrainResponse.Assess(ratio, threshold), ratio);
    }

    private readonly record struct ClassifiedPair(Pair Pair, RoughnessClass? Class);

    private readonly record struct Pair(TerrainWindow Current, TerrainWindow Earlier)
    {
        public double SpeedRatio => Current.MeanSpeed / Earlier.MeanSpeed;
        public double? SamePosition(bool front)
        {
            var a = (front ? Current.Front : Current.Rear)?.RmsVelocity;
            var b = (front ? Earlier.Front : Earlier.Rear)?.RmsVelocity;
            return a is > 0 && b is > 0 ? Math.Log(a.Value / b.Value) - SamePositionSpeedExponent * Math.Log(SpeedRatio) : null;
        }
    }
}
