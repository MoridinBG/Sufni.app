namespace Sufni.Telemetry;

public static partial class TerrainProfileCalculator
{
    public const int AlgorithmVersion = 1;
    public const double ClippedShareLimit = 0.02;
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
        for (var i = 0; i < windows.Count; i++)
        {
            if (!windows[i].Moving) continue;
            var (roughness, source) = Classify(windows[i].Frame, windows[i].Lowers);
            windows[i] = windows[i] with { Roughness = roughness, Source = source };
        }
        MeasureSuspension(telemetry.Front, telemetry.Metadata.SampleRate, windows, true, cancellationToken);
        MeasureSuspension(telemetry.Rear, telemetry.Metadata.SampleRate, windows, false, cancellationToken);
        var steepness = MeasureGrade(motion, imu, frame, windows, cancellationToken);
        var classified = frame != null && TerrainCalibration.For(ImuLocation.Frame, frame.AccelLsbPerG) != null ||
                         lowers != null && TerrainCalibration.For(ImuLocation.Fork, lowers.AccelLsbPerG) != null;
        var roughState = classified ? TerrainRoughnessState.Classified :
            frame != null || lowers != null ? TerrainRoughnessState.ValuesOnly : TerrainRoughnessState.NoSensor;
        return new TerrainProfileResult.Computed(new([.. windows], frameSeries, lowersSeries, roughState, steepness,
            frame?.AccelLsbPerG, lowers?.AccelLsbPerG));
    }

    internal static double? LowersClassValue(SensorRoughness? frame, SensorRoughness? lowers) =>
        frame is not null && lowers?.ClippedShare > ClippedShareLimit ? null : lowers?.Normalised;

    public static TerrainSummary Summarize(TerrainProfile profile)
    {
        var classified = profile.Windows.Where(w => w.Moving && w.Roughness.HasValue).ToArray();
        var graded = profile.Windows.Where(w => w.Moving && w.Steepness.HasValue).ToArray();
        double Share(RoughnessClass c) => classified.Length == 0 ? 0 : (double)classified.Count(w => w.Roughness == c) / classified.Length;
        double? GradeShare(SteepnessClass c) => graded.Length == 0 ? null : (double)graded.Count(w => w.Steepness == c) / graded.Length;
        return new(WindowMetres * (double)classified.Length, Share(RoughnessClass.Calm), Share(RoughnessClass.Medium),
            Share(RoughnessClass.Rough), GradeShare(SteepnessClass.Gentle), GradeShare(SteepnessClass.Moderate),
            GradeShare(SteepnessClass.Steep), classified.GroupBy(w => w.Source).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key);
    }
}
