using System.Collections.Immutable;

namespace Sufni.Telemetry;

public static partial class TerrainProfileCalculator
{
    public const int AlgorithmVersion = 13;
    public const double ClippedShareLimit = 0.02;
    private const double SilentShareLimit = 0.5;
    private const int MinimumImuRate = 200;
    private const double ReferenceSpeed = 7;
    private const double FrameSpeedExponent = 0.7;
    private const double ForkSpeedExponent = 0.9;
    private const double RearAxleSpeedExponent = 0.9;
    private const int FullScaleCount = 32760;

    public static TerrainProfileResult Calculate(TelemetryData telemetry, IReadOnlyList<TrackSample> fixes,
        CancellationToken cancellationToken = default)
    {
        var motion = TrackMotion.FromFixes(fixes, telemetry.Metadata.Duration);
        if (motion.Spans.IsEmpty) return new TerrainProfileResult.Unavailable(TerrainUnavailableReason.NoTrack);
        var windows = CutWindows(motion, cancellationToken, out var positions, out var cells);
        if (!windows.Any(w => w.Moving)) return new TerrainProfileResult.Unavailable(TerrainUnavailableReason.NoMovement);

        var imu = telemetry.ImuData;
        ImuMetaEntry? Sensor(ImuLocation location) => imu is { SampleRate: >= MinimumImuRate } &&
            imu.ActiveLocations.Contains((byte)location)
            ? imu.Meta.FirstOrDefault(m => m.LocationId == (byte)location && m.AccelLsbPerG > 0) : null;
        var frame = Sensor(ImuLocation.Frame);
        var fork = Sensor(ImuLocation.Fork);
        var rearAxle = Sensor(ImuLocation.RearAxle);
        var frameSeries = MeasureSensor(imu, frame, windows, positions, cancellationToken);
        var forkSeries = MeasureSensor(imu, fork, windows, positions, cancellationToken);
        MeasureSensor(imu, rearAxle, windows, positions, cancellationToken);
        MeasureSuspension(telemetry.Front, telemetry.Metadata.SampleRate, windows, true, cancellationToken);
        MeasureSuspension(telemetry.Rear, telemetry.Metadata.SampleRate, windows, false, cancellationToken);
        var silent = Enum.GetValues<TerrainSensor>().Where(sensor => DropIfSilent(windows, sensor, sensor switch
        {
            TerrainSensor.FrameImu => OneCount(frame),
            TerrainSensor.ForkImu => OneCount(fork),
            TerrainSensor.RearAxleImu => OneCount(rearAxle),
            _ => 0
        })).ToImmutableArray();
        if (silent.Contains(TerrainSensor.FrameImu)) (frame, frameSeries) = (null, []);
        if (silent.Contains(TerrainSensor.ForkImu)) (fork, forkSeries) = (null, []);
        if (silent.Contains(TerrainSensor.RearAxleImu)) rearAxle = null;
        if (!silent.Contains(TerrainSensor.ShockTravel))
            MeasurePedalling(telemetry.Rear, telemetry.Metadata.SampleRate, imu, frame, windows, cancellationToken);
        var grade = MeasureGrade(motion, imu, frame, windows, cells, out var gradePieces, cancellationToken);
        var roughnessPieces = MeasureRoughnessPieces(imu, [frame, fork, rearAxle], cells, cancellationToken);
        return new TerrainProfileResult.Computed(new([.. windows], frameSeries, forkSeries, grade,
            frame?.AccelLsbPerG, fork?.AccelLsbPerG)
        {
            SilentSensors = silent,
            RoughnessPieces = roughnessPieces,
            GradePieces = gradePieces,
            RearAxleScale = rearAxle?.AccelLsbPerG,
            FrontMaxTravel = telemetry.Front is { Present: true } ? telemetry.Front.MaxTravel : null,
            RearMaxTravel = telemetry.Rear is { Present: true } ? telemetry.Rear.MaxTravel : null
        });
    }

    // A failed sensor reads zero. Its few non-zero windows would still distort classification and the bike
    // baseline, so the whole sensor is dropped for the ride.
    private static bool DropIfSilent(List<TerrainWindow> windows, TerrainSensor sensor, double floor)
    {
        double? Reading(TerrainWindow w) => sensor switch
        {
            TerrainSensor.ForkTravel => w.Front?.RmsVelocity,
            TerrainSensor.ShockTravel => w.Rear?.RmsVelocity,
            TerrainSensor.FrameImu => w.Frame?.Level,
            TerrainSensor.ForkImu => w.Fork?.Level,
            _ => w.RearAxle?.Level
        };
        int measured = 0, zero = 0;
        foreach (var window in windows)
        {
            if (!window.Moving || Reading(window) is not { } value) continue;
            measured++;
            if (value <= floor) zero++;
        }
        if (zero <= measured * SilentShareLimit) return false;
        for (var i = 0; i < windows.Count; i++)
        {
            var w = windows[i];
            windows[i] = sensor switch
            {
                TerrainSensor.ForkTravel => w with { Front = null },
                TerrainSensor.ShockTravel => w with { Rear = null },
                TerrainSensor.FrameImu => w with { Frame = null },
                TerrainSensor.ForkImu => w with { Fork = null },
                _ => w with { RearAxle = null }
            };
        }
        return true;
    }

    /// <summary>
    /// A single window between two adjacent windows of one other class takes their class. A stop, a gap or an
    /// unclassified neighbour keeps the window unchanged. Decisions use the classes before merging.
    /// </summary>
    internal static void MergeOutliers<T, TClass>(List<T> windows, Func<T, TClass?> read, Func<T, TClass, T> write,
        Func<T, T, bool> adjacent) where TClass : struct, Enum
    {
        var classes = windows.Select(read).ToArray();
        for (var i = 1; i + 1 < windows.Count; i++)
        {
            if (classes[i] is not { } own || classes[i - 1] is not { } before || !Equals(classes[i + 1], before) ||
                Equals(own, before) || !adjacent(windows[i - 1], windows[i]) || !adjacent(windows[i], windows[i + 1])) continue;
            windows[i] = write(windows[i], before);
        }
    }

    private static bool Adjacent(TerrainWindow a, TerrainWindow b) => a.EndSeconds == b.StartSeconds;

    internal static double Midpoint(TerrainWindow window) => (window.StartSeconds + window.EndSeconds) / 2;

    /// <summary>The piece of time-ordered, non-overlapping pieces that holds <paramref name="seconds"/>.</summary>
    internal static TerrainWindow? PieceAt(IReadOnlyList<TerrainWindow> pieces, double seconds)
    {
        int low = 0, high = pieces.Count;
        while (low < high)
        {
            var mid = low + (high - low) / 2;
            if (pieces[mid].EndSeconds <= seconds) low = mid + 1;
            else high = mid;
        }
        return low < pieces.Count && pieces[low].StartSeconds <= seconds ? pieces[low] : null;
    }

    // The band-pass filter carries a residue from live samples into a silent stretch; below one count is no signal.
    private static double OneCount(ImuMetaEntry? meta) => meta is null ? 0 : 1d / meta.AccelLsbPerG;

    public static TerrainSummary Summarize(TerrainProfile profile)
    {
        var moving = profile.Windows.Where(w => w.Moving).ToArray();
        return new(WindowMetres * (double)moving.Length,
            profile.GradeState == TerrainGradeState.Available ? WindowMetres * (double)moving.Count(w => w.Climbing) : null,
            Distribution(moving, w => w.Climbing ? null : w.Frame, r => r.SpeedRemoved),
            Distribution(moving, w => w.Climbing ? null : w.Fork, r => r.SpeedRemoved),
            Distribution(moving, w => w.Climbing ? null : w.RearAxle, r => r.SpeedRemoved),
            Distribution(moving, w => w.Climbing ? w.Frame : null, r => r.Level));
    }

    private static LevelDistribution? Distribution(IEnumerable<TerrainWindow> windows, Func<TerrainWindow, SensorRoughness?> reading,
        Func<SensorRoughness, double> level)
    {
        var bins = new SortedDictionary<int, double>();
        foreach (var window in windows)
        {
            if (Eligible(reading(window)) is not { } measured) continue;
            var bin = TerrainLevels.Bin(level(measured));
            bins[bin] = bins.GetValueOrDefault(bin) + WindowMetres;
        }
        return bins.Count == 0 ? null : new(bins.ToImmutableSortedDictionary());
    }

    /// <summary>The reading when it measured the ground: a level above zero and no more than the clipped share limit.</summary>
    internal static SensorRoughness? Eligible(SensorRoughness? reading) =>
        reading is { Level: > 0, ClippedShare: <= ClippedShareLimit } ? reading : null;
}
