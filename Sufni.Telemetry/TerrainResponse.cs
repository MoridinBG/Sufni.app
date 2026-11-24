using System.Collections.Immutable;

namespace Sufni.Telemetry;

public enum ResponseCellState { WithinRepeatRange, Finding, TooShort, NoBaseline, InsufficientVariation, NoMeasurement }

public sealed record ResponseCell(ResponseTarget Target, ImuLocation Predictor, ResponseCellState State, double? Ratio,
    int BaselineSessions, int BaselineWindows);

public sealed record TerrainResponseRow(RoughnessClass? Class, double DistanceMetres, double Share, double? MeanSpeed,
    ImmutableArray<ResponseCell> Cells);

public static class TerrainResponse
{
    public static ImmutableArray<TerrainResponseRow> Describe(TerrainProfile profile, SuspensionResponseModel model,
        TelemetryTimeRange? range)
    {
        var levels = Enum.GetValues<RoughnessClass>();
        var groups = levels.Select(_ => new List<Portion>()).Append([]).ToArray();
        var all = groups[^1];
        foreach (var window in profile.Windows)
        {
            if (!window.Moving) continue;
            var start = Math.Max(window.StartSeconds, range?.StartSeconds ?? window.StartSeconds);
            var end = Math.Min(window.EndSeconds, range?.EndSeconds ?? window.EndSeconds);
            if (end <= start) continue;
            var portion = new Portion(window, (end - start) / (window.EndSeconds - window.StartSeconds));
            if (!window.Climbing) all.Add(portion);
            if (window.Roughness is { } roughness) groups[(int)roughness].Add(portion);
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
        return Cell(Assess(ratio, target == ResponseTarget.Transmission ? SuspensionResponseModel.BikeTransmissionThreshold
            : SuspensionResponseModel.BikeResponseThreshold), ratio);
    }

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
