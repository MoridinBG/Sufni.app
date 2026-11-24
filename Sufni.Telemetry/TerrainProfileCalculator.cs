using System.Collections.Immutable;

namespace Sufni.Telemetry;

public static partial class TerrainProfileCalculator
{
    public const int AlgorithmVersion = 5;
    public const double ClippedShareLimit = 0.02;
    private const double SilentShareLimit = 0.5;
    private const int MinimumImuRate = 200;
    private const double ReferenceSpeed = 7;
    private const double FrameSpeedExponent = 0.7;
    private const double LowersSpeedExponent = 0.9;
    private const int FullScaleCount = 32760;

    public static TerrainProfileResult Calculate(TelemetryData telemetry, IReadOnlyList<TrackSample> fixes,
        CancellationToken cancellationToken = default)
    {
        var motion = TrackMotion.FromFixes(fixes, telemetry.Metadata.Duration);
        if (motion.Spans.IsEmpty) return new TerrainProfileResult.Unavailable(TerrainUnavailableReason.NoTrack);
        var windows = CutWindows(motion, cancellationToken, out var positions);
        if (!windows.Any(w => w.Moving)) return new TerrainProfileResult.Unavailable(TerrainUnavailableReason.NoMovement);

        var imu = telemetry.ImuData;
        ImuMetaEntry? Sensor(ImuLocation location) => imu is { SampleRate: >= MinimumImuRate } &&
            imu.ActiveLocations.Contains((byte)location)
            ? imu.Meta.FirstOrDefault(m => m.LocationId == (byte)location && m.AccelLsbPerG > 0) : null;
        var frame = Sensor(ImuLocation.Frame);
        var lowers = Sensor(ImuLocation.Fork);
        var frameSeries = MeasureSensor(imu, frame, windows, positions, cancellationToken);
        var lowersSeries = MeasureSensor(imu, lowers, windows, positions, cancellationToken);
        MeasureSuspension(telemetry.Front, telemetry.Metadata.SampleRate, windows, true, cancellationToken);
        MeasureSuspension(telemetry.Rear, telemetry.Metadata.SampleRate, windows, false, cancellationToken);
        var silent = Enum.GetValues<TerrainSensor>().Where(sensor => DropIfSilent(windows, sensor, sensor switch
        {
            TerrainSensor.Frame => OneCount(frame),
            TerrainSensor.Lowers => OneCount(lowers),
            _ => 0
        })).ToImmutableArray();
        if (silent.Contains(TerrainSensor.Frame)) (frame, frameSeries) = (null, []);
        if (silent.Contains(TerrainSensor.Lowers)) (lowers, lowersSeries) = (null, []);
        var grade = MeasureGrade(motion, imu, frame, windows, cancellationToken);
        for (var i = 0; i < windows.Count; i++)
        {
            if (!windows[i].Moving) continue;
            // Pedalling dominates climbing vibration and climbs are slow, so climbs use raw frame vibration.
            if (windows[i].Climbing)
            {
                if (windows[i].Frame is { Normalised: not null } climbFrame)
                    windows[i] = windows[i] with { Roughness = TerrainCalibration.ClassifyClimb(climbFrame.Level) };
                continue;
            }
            var (roughness, source) = Classify(windows[i].Frame, windows[i].Lowers);
            windows[i] = windows[i] with { Roughness = roughness, Source = source };
        }
        MergeOutliers(windows, static w => w.Roughness, static (w, level) => w with { Roughness = level }, Adjacent);
        var classified = frame != null && TerrainCalibration.For(ImuLocation.Frame, frame.AccelLsbPerG) != null ||
                         lowers != null && TerrainCalibration.For(ImuLocation.Fork, lowers.AccelLsbPerG) != null;
        var roughState = classified ? TerrainRoughnessState.Classified :
            frame != null || lowers != null ? TerrainRoughnessState.ValuesOnly : TerrainRoughnessState.NoSensor;
        return new TerrainProfileResult.Computed(new([.. windows], frameSeries, lowersSeries, roughState, grade,
            frame?.AccelLsbPerG, lowers?.AccelLsbPerG)
        {
            SilentSensors = silent,
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
            TerrainSensor.Front => w.Front?.RmsVelocity,
            TerrainSensor.Rear => w.Rear?.RmsVelocity,
            TerrainSensor.Frame => w.Frame?.Level,
            _ => w.Lowers?.Level
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
                TerrainSensor.Front => w with { Front = null },
                TerrainSensor.Rear => w with { Rear = null },
                TerrainSensor.Frame => w with { Frame = null },
                _ => w with { Lowers = null }
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

    // The band-pass filter carries a residue from live samples into a silent stretch; below one count is no signal.
    private static double OneCount(ImuMetaEntry? meta) => meta is null ? 0 : 1d / meta.AccelLsbPerG;

    internal static double? LowersClassValue(SensorRoughness? frame, SensorRoughness? lowers) =>
        frame is not null && lowers?.ClippedShare > ClippedShareLimit ? null : lowers?.Normalised;

    public static TerrainSummary Summarize(TerrainProfile profile)
    {
        var moving = profile.Windows.Where(w => w.Moving).ToArray();
        var classified = moving.Where(w => w.Roughness is { } level && !level.IsClimb()).ToArray();
        return new(WindowMetres * (double)moving.Length,
            profile.GradeState == TerrainGradeState.Available ? WindowMetres * (double)moving.Count(w => w.Climbing) : null,
            WindowMetres * (double)classified.Length,
            classified.Length == 0 ? 0 : (double)classified.Count(w => w.Roughness is RoughnessClass.Rough or RoughnessClass.VeryRough) / classified.Length,
            classified.GroupBy(w => w.Source).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key);
    }
}
