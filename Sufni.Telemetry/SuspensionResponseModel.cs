using System.Collections.Immutable;
using MathNet.Numerics.LinearAlgebra;

namespace Sufni.Telemetry;

public enum ResponseTarget { FrontRms, FrontPeakCompression, FrontPeakRebound, RearRms, RearPeakCompression, RearPeakRebound, PassedToFrame }
public enum ResponseFitIssue { InsufficientData, InsufficientVariation, MissingSensor }

public closed record ResponseFit(GroundSensors Predictors, int Sessions, int Windows)
{
    public sealed record Fitted(GroundSensors Predictors, int Sessions, int Windows, ImmutableArray<double> Coefficients)
        : ResponseFit(Predictors, Sessions, Windows);
    public sealed record Unavailable(GroundSensors Predictors, int Sessions, int Windows, ResponseFitIssue Reason)
        : ResponseFit(Predictors, Sessions, Windows);
}

public sealed record SuspensionResponseModel(ImmutableDictionary<ResponseTarget, ResponseFit> Fits, int Sessions, int Windows)
{
    public const int MinimumSessions = 5;
    public const int MinimumWindows = 1000;
    public const int MinimumReportMetres = 150;
    /// <summary>Distance from which a reported ratio carries full confidence; shorter reports are marked as low confidence.</summary>
    public const int ConfidentReportMetres = 300;
    public const double BikeResponseThreshold = 0.30;
    public const double BikePassedToFrameThreshold = 0.10;
    private const int MaximumRow = 1 + 3 * 2 + 1 + 1;
    public bool HasFit => Fits.Values.Any(fit => fit is ResponseFit.Fitted);

    public static SuspensionResponseModel Fit(TerrainProfile evaluated, IReadOnlyCollection<TerrainProfile> baseline,
        CancellationToken cancellationToken = default) => FitCore(evaluated, null, baseline, cancellationToken);

    public static SuspensionResponseModel FitPair(TerrainProfile current, TerrainProfile earlier,
        IReadOnlyCollection<TerrainProfile> baseline, CancellationToken cancellationToken = default) =>
        FitCore(current, earlier, baseline, cancellationToken);

    /// <summary>The predictor sets of a target in order of preference.</summary>
    private static ImmutableArray<GroundSensors> PredictorSets(ResponseTarget target) => target switch
    {
        ResponseTarget.PassedToFrame => [GroundSensors.ForkAndRearAxle, GroundSensors.Fork, GroundSensors.RearAxle],
        ResponseTarget.FrontRms or ResponseTarget.FrontPeakCompression or ResponseTarget.FrontPeakRebound =>
            [GroundSensors.Fork, GroundSensors.Frame],
        _ => [GroundSensors.RearAxle, GroundSensors.Frame]
    };

    // Every candidate set of every target is fitted in one pass; each target keeps its first fitted set.
    private static SuspensionResponseModel FitCore(TerrainProfile evaluated, TerrainProfile? paired,
        IReadOnlyCollection<TerrainProfile> baseline, CancellationToken cancellationToken)
    {
        var targets = Enum.GetValues<ResponseTarget>();
        var candidates = targets.SelectMany(target => PredictorSets(target)
                .Where(set => IsCandidate(target, set, evaluated, paired))
                .Select(set => new Candidate(target, set, new Regression(Columns(set)))))
            .ToArray();
        var windowUses = new List<ulong>();
        var sessionUses = new List<ulong>();
        Span<double> observation = stackalloc double[MaximumRow];
        foreach (var profile in baseline)
        {
            var compatible = candidates.Select(c => Compatible(c.Set, profile, evaluated)).ToArray();
            ulong sessionUse = 0;
            foreach (var window in profile.Windows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!window.Moving) continue;
                ulong windowUse = 0;
                for (var i = 0; i < candidates.Length; i++)
                {
                    var row = observation[..(candidates[i].Regression.Columns + 1)];
                    if (!compatible[i] || !TryObservation(window, candidates[i].Target, candidates[i].Set, row)) continue;
                    candidates[i].Regression.Add(row);
                    windowUse |= 1UL << i;
                }
                if (windowUse != 0) windowUses.Add(windowUse);
                sessionUse |= windowUse;
            }
            if (sessionUse == 0) continue;
            sessionUses.Add(sessionUse);
            for (var i = 0; i < candidates.Length; i++) if ((sessionUse & 1UL << i) != 0) candidates[i].Regression.Sessions++;
        }
        var results = ImmutableDictionary.CreateBuilder<ResponseTarget, ResponseFit>();
        ulong kept = 0;
        foreach (var target in targets)
        {
            ResponseFit fit = new ResponseFit.Unavailable(PredictorSets(target)[0], 0, 0, ResponseFitIssue.MissingSensor);
            var chosen = -1;
            for (var i = 0; i < candidates.Length; i++)
            {
                if (candidates[i].Target != target) continue;
                fit = candidates[i].Regression.Finish(candidates[i].Set);
                chosen = i;
                if (fit is ResponseFit.Fitted) break;
            }
            if (chosen >= 0) kept |= 1UL << chosen;
            results.Add(target, fit);
        }
        return new(results.ToImmutable(), sessionUses.Count(use => (use & kept) != 0), windowUses.Count(use => (use & kept) != 0));
    }

    public double? LogResidual(ResponseTarget target, TerrainWindow window)
    {
        if (!window.Moving || Fits[target] is not ResponseFit.Fitted fit) return null;
        Span<double> row = stackalloc double[Columns(fit.Predictors) + 1];
        if (!TryObservation(window, target, fit.Predictors, row)) return null;
        var residual = row[^1];
        for (var i = 0; i < fit.Coefficients.Length; i++) residual -= fit.Coefficients[i] * row[i];
        return residual;
    }

    private sealed record Candidate(ResponseTarget Target, GroundSensors Set, Regression Regression);

    /// <summary>The judged rides carry every sensor of the set; passed-to-frame also needs their frame IMU.</summary>
    private static bool IsCandidate(ResponseTarget target, GroundSensors set, TerrainProfile evaluated, TerrainProfile? paired) =>
        Sensors(set).All(sensor => Scale(evaluated, sensor) is not null && (paired is null || Scale(paired, sensor) is not null)) &&
        (target != ResponseTarget.PassedToFrame || evaluated.FrameScale is not null && paired is not { FrameScale: null });

    private static bool Compatible(GroundSensors set, TerrainProfile profile, TerrainProfile evaluated) =>
        Sensors(set).All(sensor => Scale(profile, sensor) == Scale(evaluated, sensor));

    private static int Columns(GroundSensors set) => 1 + 3 * Sensors(set).Length + 1;

    private static ImuLocation[] Sensors(GroundSensors set) => set switch
    {
        GroundSensors.ForkAndRearAxle => ForkAndRearAxle,
        GroundSensors.Fork => ForkOnly,
        GroundSensors.RearAxle => RearAxleOnly,
        _ => FrameOnly
    };

    private static readonly ImuLocation[] ForkAndRearAxle = [ImuLocation.Fork, ImuLocation.RearAxle];
    private static readonly ImuLocation[] ForkOnly = [ImuLocation.Fork];
    private static readonly ImuLocation[] RearAxleOnly = [ImuLocation.RearAxle];
    private static readonly ImuLocation[] FrameOnly = [ImuLocation.Frame];

    private static double? Scale(TerrainProfile profile, ImuLocation sensor) => sensor switch
    {
        ImuLocation.Frame => profile.FrameScale,
        ImuLocation.Fork => profile.ForkScale,
        _ => profile.RearAxleScale
    };

    private static SensorRoughness? Reading(TerrainWindow window, ImuLocation sensor) => sensor switch
    {
        ImuLocation.Frame => window.Frame,
        ImuLocation.Fork => window.Fork,
        _ => window.RearAxle
    };

    /// <summary>Fills the intercept, three log bands per sensor, log speed and the log target value.</summary>
    private static bool TryObservation(TerrainWindow window, ResponseTarget target, GroundSensors set, Span<double> row)
    {
        var value = target switch
        {
            ResponseTarget.FrontRms => window.Front?.RmsVelocity,
            ResponseTarget.FrontPeakCompression => window.Front?.PeakCompression,
            ResponseTarget.FrontPeakRebound => window.Front?.PeakRebound,
            ResponseTarget.RearRms => window.Rear?.RmsVelocity,
            ResponseTarget.RearPeakCompression => window.Rear?.PeakCompression,
            ResponseTarget.RearPeakRebound => window.Rear?.PeakRebound,
            ResponseTarget.PassedToFrame => window.Frame?.Level,
            _ => null
        };
        // Climbing response follows pedalling and rider load, not the ground law this model fits.
        if (value is not > 0 || window.Climbing) return false;
        row[0] = 1;
        var column = 1;
        foreach (var sensor in Sensors(set))
        {
            if (Reading(window, sensor) is not { } bands) return false;
            row[column++] = Math.Log(bands.LowBand);
            row[column++] = Math.Log(bands.MiddleBand);
            row[column++] = Math.Log(bands.HighBand);
        }
        row[column++] = Math.Log(window.MeanSpeed);
        row[column] = Math.Log(value.Value);
        foreach (var entry in row) if (!double.IsFinite(entry)) return false;
        return true;
    }

    // Incremental QR keeps the working matrix bounded independently of the number of rides.
    private sealed class Regression(int columns)
    {
        private readonly double[,] upper = new double[columns, columns + 1];
        public int Columns => columns;
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

        public ResponseFit Finish(GroundSensors predictors)
        {
            if (Sessions < MinimumSessions || windows < MinimumWindows)
                return new ResponseFit.Unavailable(predictors, Sessions, windows, ResponseFitIssue.InsufficientData);
            var r = Matrix<double>.Build.Dense(Columns, Columns, (i, j) => upper[i, j]);
            var svd = r.Svd(true);
            var tolerance = svd.S[0] * windows * (Math.BitIncrement(1d) - 1);
            if (svd.S[Columns - 1] <= tolerance)
                return new ResponseFit.Unavailable(predictors, Sessions, windows, ResponseFitIssue.InsufficientVariation);
            var coefficients = svd.Solve(Vector<double>.Build.Dense(Columns, i => upper[i, Columns]));
            return new ResponseFit.Fitted(predictors, Sessions, windows, [.. coefficients]);
        }
    }
}
