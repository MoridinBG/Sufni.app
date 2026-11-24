using System.Collections.Immutable;

namespace Sufni.Telemetry;

public enum ResponseCellState { WithinRepeatRange, Finding, TooShort, NoBaseline, InsufficientVariation, NoMeasurement }

public enum ResponseConfidence { Low, Full }

public sealed record ResponseCell(ResponseTarget Target, ImuLocation Predictor, ResponseCellState State, double? Ratio,
    int BaselineSessions, int BaselineWindows)
{
    /// <summary>Signed difference from the expected response: −0.16 is 16% lower.</summary>
    public double? Deviation => Ratio - 1;
}

public sealed record TerrainResponseRow(RoughnessClass? Class, double DistanceMetres, double Share, double? MeanSpeed,
    ImmutableArray<ResponseCell> Cells)
{
    public ResponseConfidence Confidence => TerrainResponse.ConfidenceAt(DistanceMetres);
}

/// <summary>A response outside its repeat range in one ground group; a null group is all non-climb ground.</summary>
public sealed record ResponseFinding(ResponseTarget Target, RoughnessClass? Group, double Deviation, ResponseConfidence Confidence);

public static class TerrainResponse
{
    /// <summary>Rows for all descent ground and each descent level inside the range, limited to the windows a filter admits.</summary>
    public static ImmutableArray<TerrainResponseRow> Describe(TerrainProfile profile, SuspensionResponseModel model,
        TelemetryTimeRange? range, Func<TerrainWindow, bool>? admits = null)
    {
        var levels = Enum.GetValues<RoughnessClass>().Where(level => !level.IsClimb()).ToArray();
        var groups = levels.Select(_ => new List<Portion>()).Append([]).ToArray();
        var all = groups[^1];
        foreach (var window in profile.Windows)
        {
            if (!window.Moving || admits?.Invoke(window) == false) continue;
            var fraction = window.CoveredFraction(range);
            if (fraction == 0) continue;
            var portion = new Portion(window, fraction);
            if (!window.Climbing) all.Add(portion);
            if (window.Roughness is { } roughness && !roughness.IsClimb()) groups[(int)roughness].Add(portion);
        }
        var total = all.Sum(p => p.Fraction);
        return [.. groups.Select((group, i) =>
        {
            var coverage = group.Sum(p => p.Fraction);
            var distance = coverage * TerrainProfileCalculator.WindowMetres;
            var seconds = group.Sum(p => p.Fraction * (p.Window.EndSeconds - p.Window.StartSeconds));
            return new TerrainResponseRow(i < levels.Length ? levels[i] : null, distance, total > 0 ? coverage / total : 0,
                seconds > 0 ? distance / seconds : null,
                [.. Enum.GetValues<ResponseTarget>().Select(target => DescribeCell(target, group, distance, model))]);
        })];
    }

    private static ResponseCell DescribeCell(ResponseTarget target, List<Portion> group, double distance, SuspensionResponseModel model)
    {
        var fit = model.Fits[target];
        ResponseCell Cell(ResponseCellState state, double? ratio = null) =>
            new(target, fit.Predictor, state, ratio, fit.Sessions, fit.Windows);
        if (distance < SuspensionResponseModel.MinimumReportMetres) return Cell(ResponseCellState.TooShort);
        if (fit is ResponseFit.Unavailable unavailable)
            return Cell(UnavailableState(unavailable.Reason));
        double sum = 0, weight = 0;
        foreach (var part in group)
            if (model.LogResidual(target, part.Window) is { } residual)
            {
                sum += part.Fraction * residual;
                weight += part.Fraction;
            }
        if (weight == 0) return Cell(ResponseCellState.NoMeasurement);
        var ratio = Math.Exp(sum / weight);
        return Cell(Assess(ratio, RepeatRange(target)), ratio);
    }

    /// <summary>The deviation from the bike baseline that repeat rides stay within.</summary>
    public static double RepeatRange(ResponseTarget target) => target == ResponseTarget.Transmission
        ? SuspensionResponseModel.BikeTransmissionThreshold : SuspensionResponseModel.BikeResponseThreshold;

    public static ImmutableArray<ResponseFinding> Findings(IEnumerable<TerrainResponseRow> rows) =>
        [.. rows.SelectMany(row => row.Cells.Where(cell => cell.State == ResponseCellState.Finding)
            .Select(cell => new ResponseFinding(cell.Target, row.Class, cell.Deviation!.Value, row.Confidence)))];

    internal static ResponseConfidence ConfidenceAt(double metres) =>
        metres < SuspensionResponseModel.ConfidentReportMetres ? ResponseConfidence.Low : ResponseConfidence.Full;

    internal static ResponseCellState UnavailableState(ResponseFitIssue reason) => reason switch
    {
        ResponseFitIssue.InsufficientVariation => ResponseCellState.InsufficientVariation,
        ResponseFitIssue.MissingSensor => ResponseCellState.NoMeasurement,
        _ => ResponseCellState.NoBaseline
    };

    public static ResponseCellState Assess(double ratio, double threshold) =>
        ratio < 1 - threshold || ratio > 1 + threshold ? ResponseCellState.Finding : ResponseCellState.WithinRepeatRange;

    private readonly record struct Portion(TerrainWindow Window, double Fraction);
}
