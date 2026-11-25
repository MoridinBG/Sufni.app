using System.Collections.Immutable;
using MathNet.Numerics.LinearAlgebra;

namespace Sufni.Telemetry;

public enum ResponseTarget { FrontRms, FrontPeakCompression, FrontPeakRebound, RearRms, RearPeakCompression, RearPeakRebound, Transmission }
public enum ResponseFitIssue { InsufficientData, InsufficientVariation, MissingSensor }

public closed record ResponseFit(ImuLocation Predictor, int Sessions, int Windows)
{
    public sealed record Fitted(ImuLocation Predictor, int Sessions, int Windows, ImmutableArray<double> Coefficients)
        : ResponseFit(Predictor, Sessions, Windows);
    public sealed record Unavailable(ImuLocation Predictor, int Sessions, int Windows, ResponseFitIssue Reason)
        : ResponseFit(Predictor, Sessions, Windows);
}

public sealed record SuspensionResponseModel(ImmutableDictionary<ResponseTarget, ResponseFit> Fits, int Sessions, int Windows)
{
    public const int MinimumSessions = 5;
    public const int MinimumWindows = 1000;
    public const int MinimumReportMetres = 150;
    /// <summary>Distance from which a reported ratio carries full confidence; shorter reports are marked as low confidence.</summary>
    public const int ConfidentReportMetres = 300;
    public const double BikeResponseThreshold = 0.30;
    public const double BikeTransmissionThreshold = 0.10;
    public bool HasFit => Fits.Values.Any(fit => fit is ResponseFit.Fitted);

    public static SuspensionResponseModel Fit(TerrainProfile evaluated, IReadOnlyCollection<TerrainProfile> baseline,
        CancellationToken cancellationToken = default) => FitCore(evaluated, null, baseline, cancellationToken);

    public static SuspensionResponseModel FitPair(TerrainProfile current, TerrainProfile earlier,
        IReadOnlyCollection<TerrainProfile> baseline, CancellationToken cancellationToken = default) =>
        FitCore(current, earlier, baseline, cancellationToken);

    private static SuspensionResponseModel FitCore(TerrainProfile evaluated, TerrainProfile? paired,
        IReadOnlyCollection<TerrainProfile> baseline, CancellationToken cancellationToken)
    {
        var targets = Enum.GetValues<ResponseTarget>();
        var predictors = targets.Select(target => SelectPredictor(target, evaluated, paired, baseline)).ToArray();
        var scales = targets.Select((target, i) => AvailableScale(target, predictors[i], evaluated, paired)).ToArray();
        var regressions = targets.Select(_ => new Regression()).ToArray();
        int sessions = 0, windows = 0;
        Span<double> observation = stackalloc double[6];
        foreach (var profile in baseline)
        {
            var compatible = scales.Select((scale, i) => scale.HasValue && Scale(profile, predictors[i]) == scale &&
                (paired is null || Scale(paired, predictors[i]) == scale)).ToArray();
            var contributed = new bool[targets.Length];
            foreach (var window in profile.Windows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!window.Moving) continue;
                var used = false;
                for (var i = 0; i < targets.Length; i++)
                {
                    if (!compatible[i] || !TryObservation(window, targets[i], predictors[i], observation)) continue;
                    regressions[i].Add(observation);
                    contributed[i] = used = true;
                }
                if (used) windows++;
            }
            if (contributed.Any(value => value)) sessions++;
            for (var i = 0; i < targets.Length; i++) if (contributed[i]) regressions[i].Sessions++;
        }
        var results = ImmutableDictionary.CreateBuilder<ResponseTarget, ResponseFit>();
        for (var i = 0; i < targets.Length; i++)
        {
            results.Add(targets[i], scales[i] is null
                ? new ResponseFit.Unavailable(predictors[i], 0, 0, ResponseFitIssue.MissingSensor)
                : regressions[i].Finish(predictors[i]));
        }
        return new(results.ToImmutable(), sessions, windows);
    }

    public double? LogResidual(ResponseTarget target, TerrainWindow window)
    {
        if (!window.Moving || Fits[target] is not ResponseFit.Fitted fit) return null;
        Span<double> row = stackalloc double[6];
        if (!TryObservation(window, target, fit.Predictor, row)) return null;
        var residual = row[5];
        for (var i = 0; i < fit.Coefficients.Length; i++) residual -= fit.Coefficients[i] * row[i];
        return residual;
    }

    private static ImuLocation SelectPredictor(ResponseTarget target, TerrainProfile evaluated, TerrainProfile? paired, IReadOnlyCollection<TerrainProfile> baseline)
    {
        if (target == ResponseTarget.Transmission) return ImuLocation.Fork;
        var front = target is ResponseTarget.FrontRms or ResponseTarget.FrontPeakCompression or ResponseTarget.FrontPeakRebound;
        var preferred = front ? ImuLocation.Fork : ImuLocation.Frame;
        if (Scale(evaluated, preferred).HasValue && (paired is null || Scale(paired, preferred).HasValue) &&
            baseline.Any(p => Scale(p, preferred).HasValue)) return preferred;
        return front ? ImuLocation.Frame : ImuLocation.Fork;
    }

    /// <summary>The predictor scale of the evaluated ride, or null when a judged ride lacks a sensor the target needs.</summary>
    private static double? AvailableScale(ResponseTarget target, ImuLocation predictor, TerrainProfile evaluated, TerrainProfile? paired) =>
        paired is not null && Scale(paired, predictor) is null ||
        target == ResponseTarget.Transmission && (evaluated.FrameScale is null || paired is { FrameScale: null })
            ? null : Scale(evaluated, predictor);

    private static double? Scale(TerrainProfile profile, ImuLocation sensor) =>
        sensor == ImuLocation.Frame ? profile.FrameScale : profile.ForkScale;

    private static bool TryObservation(TerrainWindow window, ResponseTarget target, ImuLocation predictor, Span<double> row)
    {
        var bands = predictor == ImuLocation.Frame ? window.Frame : window.Fork;
        var value = target switch
        {
            ResponseTarget.FrontRms => window.Front?.RmsVelocity,
            ResponseTarget.FrontPeakCompression => window.Front?.PeakCompression,
            ResponseTarget.FrontPeakRebound => window.Front?.PeakRebound,
            ResponseTarget.RearRms => window.Rear?.RmsVelocity,
            ResponseTarget.RearPeakCompression => window.Rear?.PeakCompression,
            ResponseTarget.RearPeakRebound => window.Rear?.PeakRebound,
            ResponseTarget.Transmission => window.Frame?.Level,
            _ => null
        };
        // Climbing response follows pedalling and rider load, not the ground law this model fits.
        if (bands is null || value is not > 0 || window.Climbing) return false;
        row[0] = 1;
        row[1] = Math.Log(bands.LowBand);
        row[2] = Math.Log(bands.MiddleBand);
        row[3] = Math.Log(bands.HighBand);
        row[4] = Math.Log(window.MeanSpeed);
        row[5] = Math.Log(value.Value);
        foreach (var entry in row) if (!double.IsFinite(entry)) return false;
        return true;
    }

    // Incremental QR keeps the working matrix bounded independently of the number of rides.
    private sealed class Regression
    {
        private const int Columns = 5;
        private readonly double[,] upper = new double[Columns, Columns + 1];
        public int Sessions { get; set; }
        private int windows;

        public void Add(Span<double> row)
        {
            windows++;
            for (var j = 0; j < Columns; j++)
            {
                if (row[j] == 0) continue;
                var length = double.Hypot(upper[j, j], row[j]);
                var c = upper[j, j] / length;
                var s = row[j] / length;
                upper[j, j] = length;
                for (var k = j + 1; k <= Columns; k++)
                {
                    var before = upper[j, k];
                    upper[j, k] = c * before + s * row[k];
                    row[k] = -s * before + c * row[k];
                }
            }
        }

        public ResponseFit Finish(ImuLocation predictor)
        {
            if (Sessions < MinimumSessions || windows < MinimumWindows)
                return new ResponseFit.Unavailable(predictor, Sessions, windows, ResponseFitIssue.InsufficientData);
            var r = Matrix<double>.Build.Dense(Columns, Columns, (i, j) => upper[i, j]);
            var svd = r.Svd(true);
            var tolerance = svd.S[0] * windows * (Math.BitIncrement(1d) - 1);
            if (svd.S[Columns - 1] <= tolerance)
                return new ResponseFit.Unavailable(predictor, Sessions, windows, ResponseFitIssue.InsufficientVariation);
            var coefficients = svd.Solve(Vector<double>.Build.Dense(Columns, i => upper[i, Columns]));
            return new ResponseFit.Fitted(predictor, Sessions, windows, [.. coefficients]);
        }
    }
}
